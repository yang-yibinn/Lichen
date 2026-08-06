using System;
using System.Collections.Generic;
using System.Linq;
using Grasshopper.Kernel;
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

            scope.Closure = new ExportRootScopeResolver().ResolveMany(snapshot, scope.Roots.Select(r => Id(r.InstanceGuid)), maximumNodes);
            HashSet<string> includedEdges = new HashSet<string>(scope.Closure.ContributingEdges.Select(ExportRootScopeResolver.EdgeKey), StringComparer.OrdinalIgnoreCase);
            scope.Edges = liveEdges.Where(e => includedEdges.Contains(e.Key)).OrderBy(e => e.Key, StringComparer.OrdinalIgnoreCase)
                .Select(e => new GrasshopperExportRootEdge { Edge = e.Edge, Source = e.Source, Target = e.Target }).ToList();
            scope.Roots = scope.Roots.OrderBy(r => r.InstanceGuid).ToList();
            return scope;
        }

        public static bool IsExportRoot(IGH_DocumentObject obj)
        {
            if (obj == null) return false;
            try { return obj.ComponentGuid == LichenComponentIds.ExportRoot; }
            catch { return false; }
        }

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
