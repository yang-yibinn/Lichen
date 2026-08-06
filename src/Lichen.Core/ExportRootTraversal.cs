using System;
using System.Collections.Generic;
using System.Linq;

namespace Lichen.Core
{
    public static class LichenComponentIds
    {
        public static readonly Guid ExportRoot = new Guid("d7b7e6f4-8c52-4f42-a9e5-9d6f39e0c128");
    }

    public sealed class ExportRootScopeResolver
    {
        public ExportRootClosure Resolve(ContextSnapshot snapshot, string rootObjectId, int maximumNodes)
        {
            return ResolveMany(snapshot, new[] { rootObjectId }, maximumNodes);
        }

        public ExportRootClosure ResolveMany(ContextSnapshot snapshot, IEnumerable<string> rootObjectIds, int maximumNodes)
        {
            if (snapshot == null) throw new ArgumentNullException("snapshot");
            if (rootObjectIds == null) throw new ArgumentNullException("rootObjectIds");

            int maximum = maximumNodes <= 0 ? 500 : maximumNodes;
            HashSet<string> available = new HashSet<string>((snapshot.Nodes ?? new List<ContextNode>()).Select(n => n.InstanceId), StringComparer.OrdinalIgnoreCase);
            List<string> roots = rootObjectIds.Where(id => !String.IsNullOrWhiteSpace(id) && available.Contains(id))
                .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(id => id, StringComparer.OrdinalIgnoreCase).ToList();
            if (roots.Count == 0) throw new InvalidOperationException("The requested Lichen Export Root was not found in the captured graph.");

            Dictionary<string, List<string>> upstream = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (ContextEdge edge in snapshot.Edges ?? new List<ContextEdge>())
            {
                if (!available.Contains(edge.SourceNodeId) || !available.Contains(edge.TargetNodeId)) continue;
                List<string> sources;
                if (!upstream.TryGetValue(edge.TargetNodeId, out sources))
                {
                    sources = new List<string>();
                    upstream.Add(edge.TargetNodeId, sources);
                }
                if (!sources.Contains(edge.SourceNodeId, StringComparer.OrdinalIgnoreCase)) sources.Add(edge.SourceNodeId);
            }
            foreach (List<string> sources in upstream.Values) sources.Sort(StringComparer.OrdinalIgnoreCase);

            HashSet<string> rootSet = new HashSet<string>(roots, StringComparer.OrdinalIgnoreCase);
            HashSet<string> visited = new HashSet<string>(roots, StringComparer.OrdinalIgnoreCase);
            HashSet<string> included = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            Queue<string> pending = new Queue<string>(roots);
            bool truncated = false;

            while (pending.Count > 0)
            {
                string target = pending.Dequeue();
                List<string> sources;
                if (!upstream.TryGetValue(target, out sources)) continue;
                foreach (string source in sources)
                {
                    if (visited.Contains(source)) continue;
                    if (included.Count >= maximum)
                    {
                        truncated = true;
                        continue;
                    }
                    visited.Add(source);
                    if (!rootSet.Contains(source)) included.Add(source);
                    pending.Enqueue(source);
                }
            }

            HashSet<string> allowedTargets = new HashSet<string>(included, StringComparer.OrdinalIgnoreCase);
            allowedTargets.UnionWith(rootSet);
            List<ContextEdge> contributing = new List<ContextEdge>();
            HashSet<string> seenEdges = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (ContextEdge edge in (snapshot.Edges ?? new List<ContextEdge>()).OrderBy(EdgeKey, StringComparer.OrdinalIgnoreCase))
            {
                if (!included.Contains(edge.SourceNodeId) || !allowedTargets.Contains(edge.TargetNodeId)) continue;
                string key = EdgeKey(edge);
                if (!seenEdges.Add(key)) continue;
                contributing.Add(CloneEdge(edge));
            }

            return new ExportRootClosure
            {
                RootObjectIds = roots,
                IncludedObjectIds = included.OrderBy(id => id, StringComparer.OrdinalIgnoreCase).ToList(),
                ContributingEdges = contributing,
                NodeLimitReached = truncated
            };
        }

        private static ContextEdge CloneEdge(ContextEdge edge)
        {
            return new ContextEdge
            {
                SourceNodeId = edge.SourceNodeId,
                SourceParameterIndex = edge.SourceParameterIndex,
                SourceParameterName = edge.SourceParameterName,
                TargetNodeId = edge.TargetNodeId,
                TargetParameterIndex = edge.TargetParameterIndex,
                TargetParameterName = edge.TargetParameterName,
                CrossesScopeBoundary = edge.CrossesScopeBoundary,
                BoundaryStatus = edge.BoundaryStatus
            };
        }

        public static string EdgeKey(ContextEdge edge)
        {
            return edge.SourceNodeId + "|" + edge.SourceParameterIndex + "|" + edge.TargetNodeId + "|" + edge.TargetParameterIndex;
        }
    }
}
