using System;
using System.Collections.Generic;
using System.Linq;

namespace Lichen.Core
{
    public sealed class ThallusScopeResolver
    {
        public ThallusClosure Resolve(ContextSnapshot snapshot, IEnumerable<string> rootThallusIds, int maximumNodes)
        {
            return Resolve(snapshot, rootThallusIds, maximumNodes, true);
        }

        public ThallusClosure ResolveSelected(ContextSnapshot snapshot, IEnumerable<string> selectedThallusIds, int maximumNodes)
        {
            return Resolve(snapshot, selectedThallusIds, maximumNodes, false);
        }

        private static ThallusClosure Resolve(ContextSnapshot snapshot, IEnumerable<string> rootThallusIds, int maximumNodes, bool requireOutermost)
        {
            if (snapshot == null) throw new ArgumentNullException("snapshot");
            if (rootThallusIds == null) throw new ArgumentNullException("rootThallusIds");

            int maximum = maximumNodes <= 0 ? 500 : maximumNodes;
            Dictionary<string, ContextThallus> byId = (snapshot.Thalli ?? new List<ContextThallus>())
                .Where(t => t != null && !String.IsNullOrWhiteSpace(t.InstanceId))
                .GroupBy(t => t.InstanceId, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
            List<string> roots = new List<string>();
            HashSet<string> seenRoots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string id in rootThallusIds)
                if (!String.IsNullOrWhiteSpace(id) && seenRoots.Add(id)) roots.Add(id);
            if (roots.Count == 0) throw new InvalidOperationException(requireOutermost ? "No Thallus is connected to the Lichen T input." : "No Thallus is selected.");
            foreach (string rootId in roots)
            {
                ContextThallus root;
                if (!byId.TryGetValue(rootId, out root)) throw new InvalidOperationException("A Thallus connected to Lichen could not be found: " + rootId + ".");
                if (requireOutermost && !String.IsNullOrWhiteSpace(root.ParentThallusId)) throw new InvalidOperationException("Only an outermost Thallus can connect to Lichen.T.");
            }

            Dictionary<string, List<string>> children = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (ContextThallus thallus in byId.Values)
            {
                if (String.IsNullOrWhiteSpace(thallus.ParentThallusId)) continue;
                List<string> values;
                if (!children.TryGetValue(thallus.ParentThallusId, out values)) { values = new List<string>(); children.Add(thallus.ParentThallusId, values); }
                if (!values.Contains(thallus.InstanceId, StringComparer.OrdinalIgnoreCase)) values.Add(thallus.InstanceId);
            }
            foreach (List<string> values in children.Values) values.Sort(StringComparer.OrdinalIgnoreCase);

            HashSet<string> available = new HashSet<string>((snapshot.Nodes ?? new List<ContextNode>()).Select(n => n.InstanceId), StringComparer.OrdinalIgnoreCase);
            HashSet<string> includedThalli = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, List<string>> effective = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            HashSet<string> visiting = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string root in roots) ResolveMembers(root, byId, children, available, includedThalli, effective, visiting);

            HashSet<string> members = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string root in roots) members.UnionWith(effective[root]);
            if (members.Count > maximum)
                throw new InvalidOperationException("The connected Thalli contain " + members.Count + " objects, exceeding Lichen's " + maximum + "-object scope limit. Split the export or remove members; Lichen will not silently export a partial Thallus.");

            return new ThallusClosure
            {
                RootThallusIds = roots,
                IncludedThallusIds = includedThalli.OrderBy(id => id, StringComparer.OrdinalIgnoreCase).ToList(),
                IncludedObjectIds = members.OrderBy(id => id, StringComparer.OrdinalIgnoreCase).ToList(),
                EffectiveMemberIds = effective
            };
        }

        private static List<string> ResolveMembers(string id, Dictionary<string, ContextThallus> byId, Dictionary<string, List<string>> children,
            HashSet<string> available, HashSet<string> includedThalli, Dictionary<string, List<string>> effective, HashSet<string> visiting)
        {
            List<string> cached;
            if (effective.TryGetValue(id, out cached)) return cached;
            if (!visiting.Add(id)) throw new InvalidOperationException("The Thallus hierarchy contains a cycle involving " + id + ".");
            ContextThallus thallus;
            if (!byId.TryGetValue(id, out thallus)) throw new InvalidOperationException("The Thallus hierarchy references a missing child: " + id + ".");
            includedThalli.Add(id);
            HashSet<string> members = new HashSet<string>((thallus.DirectMemberIds ?? new List<string>()).Where(available.Contains), StringComparer.OrdinalIgnoreCase);
            List<string> childIds;
            if (children.TryGetValue(id, out childIds)) foreach (string child in childIds) members.UnionWith(ResolveMembers(child, byId, children, available, includedThalli, effective, visiting));
            visiting.Remove(id);
            List<string> result = members.OrderBy(member => member, StringComparer.OrdinalIgnoreCase).ToList();
            effective[id] = result;
            return result;
        }
    }
}
