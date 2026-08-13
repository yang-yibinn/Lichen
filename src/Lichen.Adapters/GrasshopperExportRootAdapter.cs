using System;
using System.Collections.Generic;
using System.Linq;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Special;
using Lichen.Core;

namespace Lichen.Adapters
{
    public sealed class GrasshopperExportRootScope
    {
        public GrasshopperExportRootScope()
        {
            Closure = new ExportRootClosure();
            Objects = new Dictionary<string, IGH_DocumentObject>(StringComparer.OrdinalIgnoreCase);
            Roots = new List<IGH_DocumentObject>();
            Edges = new List<GrasshopperExportRootEdge>();
        }

        public ExportRootClosure Closure { get; set; }
        public Dictionary<string, IGH_DocumentObject> Objects { get; set; }
        public List<IGH_DocumentObject> Roots { get; set; }
        public List<GrasshopperExportRootEdge> Edges { get; set; }
    }

    public sealed class GrasshopperExportRootEdge
    {
        public ContextEdge Edge { get; set; }
        public IGH_Param Source { get; set; }
        public IGH_Param Target { get; set; }
    }

    public sealed class GrasshopperExportRootAdapter
    {
        public List<ExportRootDefinition> FindRoots(GH_Document document)
        {
            if (document == null) return new List<ExportRootDefinition>();
            return document.Objects.Where(IsExportRoot).OrderBy(o => o.InstanceGuid).Select(o => new ExportRootDefinition
            {
                ObjectId = Id(o.InstanceGuid),
                Label = String.IsNullOrWhiteSpace(o.NickName) ? "Lichen" : o.NickName.Trim()
            }).ToList();
        }

        public GrasshopperExportRootScope Resolve(GH_Document document, IEnumerable<string> rootObjectIds, int maximumNodes)
        {
            if (document == null) throw new ArgumentNullException("document");
            List<string> requestedRoots = (rootObjectIds ?? Enumerable.Empty<string>()).Where(id => !String.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(id => id, StringComparer.OrdinalIgnoreCase).ToList();

            ContextSnapshot snapshot = new ContextSnapshot();
            GrasshopperExportRootScope scope = new GrasshopperExportRootScope();
            Dictionary<IGH_Param, Endpoint> outputs = new Dictionary<IGH_Param, Endpoint>(ReferenceComparer<IGH_Param>.Instance);
            Dictionary<IGH_Param, Endpoint> inputs = new Dictionary<IGH_Param, Endpoint>(ReferenceComparer<IGH_Param>.Instance);
            List<LiveEdge> liveEdges = new List<LiveEdge>();

            foreach (IGH_DocumentObject obj in document.Objects.OrderBy(o => Id(o.InstanceGuid), StringComparer.OrdinalIgnoreCase))
            {
                string id = Id(obj.InstanceGuid);
                snapshot.Nodes.Add(new ContextNode { InstanceId = id });
                scope.Objects[id] = obj;
                if (requestedRoots.Contains(id, StringComparer.OrdinalIgnoreCase) && IsExportRoot(obj)) scope.Roots.Add(obj);

                IGH_Component component = obj as IGH_Component;
                if (component != null)
                {
                    for (int i = 0; i < component.Params.Input.Count; i++) inputs[component.Params.Input[i]] = new Endpoint(id, i, component.Params.Input[i].Name);
                    for (int i = 0; i < component.Params.Output.Count; i++) outputs[component.Params.Output[i]] = new Endpoint(id, i, component.Params.Output[i].Name);
                }
                else
                {
                    IGH_Param parameter = obj as IGH_Param;
                    if (parameter != null)
                    {
                        inputs[parameter] = new Endpoint(id, 0, parameter.Name);
                        outputs[parameter] = new Endpoint(id, 0, parameter.Name);
                    }
                }
            }

            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<IGH_Param, Endpoint> target in inputs.OrderBy(p => p.Value.SortKey, StringComparer.OrdinalIgnoreCase))
            {
                IList<IGH_Param> sources;
                try { sources = target.Key.Sources; }
                catch { continue; }
                if (sources == null) continue;
                foreach (IGH_Param source in sources)
                {
                    Endpoint sourceEndpoint;
                    if (!outputs.TryGetValue(source, out sourceEndpoint)) continue;
                    ContextEdge edge = new ContextEdge
                    {
                        SourceNodeId = sourceEndpoint.NodeId,
                        SourceParameterIndex = sourceEndpoint.Index,
                        SourceParameterName = sourceEndpoint.Name,
                        TargetNodeId = target.Value.NodeId,
                        TargetParameterIndex = target.Value.Index,
                        TargetParameterName = target.Value.Name
                    };
                    string key = ExportRootScopeResolver.EdgeKey(edge);
                    if (!seen.Add(key)) continue;
                    snapshot.Edges.Add(edge);
                    liveEdges.Add(new LiveEdge { Key = key, Edge = edge, Source = source, Target = target.Key });
                }
            }

            CaptureThalli(document, snapshot);
            List<string> xRoots = new List<string>();
            List<string> thallusRoots = new List<string>();
            HashSet<string> seenThallusRoots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            HashSet<string> terminalEdgeKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (IGH_DocumentObject rootObject in scope.Roots)
            {
                string rootId = Id(rootObject.InstanceGuid);
                List<ContextEdge> xEdges = snapshot.Edges.Where(e => String.Equals(e.TargetNodeId, rootId, StringComparison.OrdinalIgnoreCase) && e.TargetParameterIndex == 0).ToList();
                List<ContextEdge> tEdges = snapshot.Edges.Where(e => String.Equals(e.TargetNodeId, rootId, StringComparison.OrdinalIgnoreCase) && e.TargetParameterIndex == 1).ToList();
                if (xEdges.Count > 0 && tEdges.Count > 0) throw new InvalidOperationException("A Lichen root cannot use X and T at the same time.");
                if (tEdges.Count > 0)
                {
                    GrasshopperThallusIdentityRoute route = new GrasshopperThallusIdentityResolver().Resolve(document, rootObject, maximumNodes);
                    foreach (string thallusId in route.Resolution.OrderedThallusIds)
                        if (seenThallusRoots.Add(thallusId)) thallusRoots.Add(thallusId);
                    terminalEdgeKeys.UnionWith(route.Resolution.RouteEdgeKeys);
                }
                else xRoots.Add(rootId);
            }

            HashSet<string> included = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            HashSet<string> includedEdgeKeys = new HashSet<string>(terminalEdgeKeys, StringComparer.OrdinalIgnoreCase);
            bool truncated = false;
            if (xRoots.Count > 0)
            {
                ExportRootClosure xClosure = new ExportRootScopeResolver().ResolveMany(snapshot, xRoots, maximumNodes);
                included.UnionWith(xClosure.IncludedObjectIds);
                includedEdgeKeys.UnionWith(xClosure.ContributingEdges.Select(ExportRootScopeResolver.EdgeKey));
                truncated = xClosure.NodeLimitReached;
            }
            if (thallusRoots.Count > 0)
            {
                ThallusClosure thallusClosure = new ThallusScopeResolver().Resolve(snapshot, thallusRoots, maximumNodes);
                included.UnionWith(thallusClosure.IncludedObjectIds);
                foreach (ContextEdge edge in snapshot.Edges)
                    if (included.Contains(edge.SourceNodeId) && included.Contains(edge.TargetNodeId)) includedEdgeKeys.Add(ExportRootScopeResolver.EdgeKey(edge));
            }
            if (included.Count > (maximumNodes <= 0 ? 500 : maximumNodes))
                throw new InvalidOperationException("The combined selected Lichen scopes exceed the object limit.");
            scope.Closure = new ExportRootClosure
            {
                RootObjectIds = scope.Roots.Select(r => Id(r.InstanceGuid)).OrderBy(id => id, StringComparer.OrdinalIgnoreCase).ToList(),
                IncludedObjectIds = included.OrderBy(id => id, StringComparer.OrdinalIgnoreCase).ToList(),
                ContributingEdges = snapshot.Edges.Where(e => includedEdgeKeys.Contains(ExportRootScopeResolver.EdgeKey(e)))
                    .OrderBy(ExportRootScopeResolver.EdgeKey, StringComparer.OrdinalIgnoreCase).ToList(),
                NodeLimitReached = truncated
            };
            scope.Edges = liveEdges.Where(e => includedEdgeKeys.Contains(e.Key)).OrderBy(e => e.Key, StringComparer.OrdinalIgnoreCase)
                .Select(e => new GrasshopperExportRootEdge { Edge = e.Edge, Source = e.Source, Target = e.Target }).ToList();
            scope.Roots = scope.Roots.OrderBy(r => r.InstanceGuid).ToList();
            return scope;
        }

        private static void CaptureThalli(GH_Document document, ContextSnapshot snapshot)
        {
            List<GH_Group> groups = document.Objects.OfType<GH_Group>().Where(group => SafeComponentGuid(group) == LichenComponentIds.Thallus)
                .OrderBy(group => group.InstanceGuid).ToList();
            Dictionary<Guid, IGH_DocumentObject> objects = document.Objects.GroupBy(o => o.InstanceGuid).ToDictionary(g => g.Key, g => g.First());
            Dictionary<Guid, ContextThallus> byGroupId = new Dictionary<Guid, ContextThallus>();
            foreach (GH_Group group in groups)
            {
                ContextThallus thallus = new ContextThallus { InstanceId = Id(group.InstanceGuid), Name = group.NickName ?? "Thallus" };
                foreach (Guid id in group.ObjectIDs.OrderBy(value => value))
                {
                    IGH_DocumentObject member;
                    if (!objects.TryGetValue(id, out member)) { thallus.MissingMemberIds.Add(Id(id)); continue; }
                    Guid componentId = SafeComponentGuid(member);
                    if (componentId == LichenComponentIds.ThallusEndpoint) { thallus.EndpointObjectId = Id(id); continue; }
                    if (componentId != LichenComponentIds.Thallus && !(member is GH_Group)) thallus.DirectMemberIds.Add(Id(id));
                }
                byGroupId[group.InstanceGuid] = thallus; snapshot.Thalli.Add(thallus);
            }
            foreach (GH_Group parent in groups)
                foreach (Guid id in parent.ObjectIDs.OrderBy(value => value))
                {
                    ContextThallus child;
                    if (!byGroupId.TryGetValue(id, out child)) continue;
                    string parentId = Id(parent.InstanceGuid);
                    if (String.IsNullOrWhiteSpace(child.ParentThallusId) || StringComparer.OrdinalIgnoreCase.Compare(parentId, child.ParentThallusId) < 0) child.ParentThallusId = parentId;
                }
        }

        public static bool IsExportRoot(IGH_DocumentObject obj)
        {
            if (obj == null) return false;
            try { return obj.ComponentGuid == LichenComponentIds.ExportRoot; }
            catch { return false; }
        }

        private static Guid SafeComponentGuid(IGH_DocumentObject obj) { try { return obj.ComponentGuid; } catch { return Guid.Empty; } }

        private static string Id(Guid value) { return value.ToString("D").ToLowerInvariant(); }

        private sealed class Endpoint
        {
            public Endpoint(string nodeId, int index, string name) { NodeId = nodeId; Index = index; Name = name ?? ""; }
            public string NodeId;
            public int Index;
            public string Name;
            public string SortKey { get { return NodeId + "|" + Index; } }
        }

        private sealed class LiveEdge
        {
            public string Key;
            public ContextEdge Edge;
            public IGH_Param Source;
            public IGH_Param Target;
        }

        private sealed class ReferenceComparer<T> : IEqualityComparer<T> where T : class
        {
            public static readonly ReferenceComparer<T> Instance = new ReferenceComparer<T>();
            public bool Equals(T x, T y) { return Object.ReferenceEquals(x, y); }
            public int GetHashCode(T obj) { return System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj); }
        }
    }
}
