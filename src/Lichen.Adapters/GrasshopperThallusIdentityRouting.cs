using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Special;
using Grasshopper.Kernel.Types;
using Lichen.Core;

namespace Lichen.Adapters
{
    internal sealed class LichenThallusIdentityPayload
    {
        internal LichenThallusIdentityPayload(string endpointObjectId, string ownerThallusId)
        {
            EndpointObjectId = endpointObjectId ?? "";
            OwnerThallusId = ownerThallusId ?? "";
        }

        internal string EndpointObjectId { get; private set; }
        internal string OwnerThallusId { get; private set; }
        public override string ToString() { return "Lichen Thallus identity"; }
    }

    internal sealed class LichenThallusIdentityGoo : GH_Goo<LichenThallusIdentityPayload>
    {
        internal LichenThallusIdentityGoo(string endpointObjectId, string ownerThallusId)
            : base(new LichenThallusIdentityPayload(endpointObjectId, ownerThallusId)) { }

        private LichenThallusIdentityGoo(LichenThallusIdentityGoo other) : base(other) { }

        public override IGH_Goo Duplicate() { return new LichenThallusIdentityGoo(this); }
        public override bool IsValid { get { return Value != null && !String.IsNullOrWhiteSpace(Value.EndpointObjectId) && !String.IsNullOrWhiteSpace(Value.OwnerThallusId); } }
        public override string TypeName { get { return "Lichen Thallus identity"; } }
        public override string TypeDescription { get { return "Opaque live identity emitted by an owned Lichen Thallus endpoint."; } }
        public override string ToString() { return "Lichen Thallus identity"; }

        internal ThallusIdentityTokenRecord ToRecord()
        {
            return new ThallusIdentityTokenRecord
            {
                EndpointObjectId = Value == null ? "" : Value.EndpointObjectId,
                OwnerThallusId = Value == null ? "" : Value.OwnerThallusId,
                IsAuthentic = IsValid
            };
        }
    }

    public sealed class GrasshopperThallusIdentityRoute
    {
        public GrasshopperThallusIdentityRoute()
        {
            Resolution = new ThallusIdentityRouteResolution();
            Edges = new List<GrasshopperExportRootEdge>();
        }

        public ThallusIdentityRouteResolution Resolution { get; set; }
        public List<GrasshopperExportRootEdge> Edges { get; set; }
    }

    public sealed class GrasshopperThallusIdentityResolver
    {
        private static readonly Guid MergeComponentId = new Guid("3cadddef-1e2b-4c09-9390-0e8f78f7609f");
        private static readonly Guid JitterComponentId = new Guid("f02a20f6-bb49-4e3d-b155-8ed5d3c6b000");
        private static readonly Guid RelayComponentId = new Guid("b6236720-8d88-4289-93c3-ac4c99f9b97b");

        public GrasshopperThallusIdentityRoute Resolve(GH_Document document, IGH_DocumentObject rootObject, int maximumNodes)
        {
            if (document == null) throw new ArgumentNullException("document");
            if (rootObject == null || SafeComponentGuid(rootObject) != LichenComponentIds.ExportRoot)
                throw new InvalidOperationException("The Lichen.T routing target is missing or invalid.");
            IGH_Component root = rootObject as IGH_Component;
            if (root == null || root.Params.Input.Count < 2) throw new InvalidOperationException("The Lichen root does not expose its T input.");
            IGH_Param target = root.Params.Input[1];
            if (target.Sources == null || target.Sources.Count == 0) throw new InvalidOperationException("No Thallus identity route reaches Lichen.T.");

            int maximum = maximumNodes <= 0 ? 500 : maximumNodes;
            ContextSnapshot snapshot = new ContextSnapshot();
            ThallusIdentityRouteRequest request = new ThallusIdentityRouteRequest
            {
                RootObjectId = Id(rootObject.InstanceGuid),
                MaximumNodes = maximum
            };
            AddNode(snapshot, rootObject);
            CaptureThalli(document, snapshot);

            Dictionary<string, GrasshopperExportRootEdge> liveEdges = new Dictionary<string, GrasshopperExportRootEdge>(StringComparer.OrdinalIgnoreCase);
            HashSet<string> expanded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (IGH_Param source in target.Sources)
                CaptureSource(source, target, snapshot, request, liveEdges, expanded, maximum);
            request.OrderedTokens = CaptureTokens(target, maximum);

            ThallusIdentityRouteResolution resolution = new ThallusIdentityRouteResolver().Resolve(snapshot, request);
            GrasshopperThallusIdentityRoute result = new GrasshopperThallusIdentityRoute { Resolution = resolution };
            foreach (string key in resolution.RouteEdgeKeys)
            {
                GrasshopperExportRootEdge edge;
                if (liveEdges.TryGetValue(key, out edge)) result.Edges.Add(edge);
            }
            return result;
        }

        public static bool IsSupportedImmediateRecipient(IGH_Param recipient)
        {
            if (recipient == null) return false;
            IGH_DocumentObject top = TopObject(recipient);
            if (top == null) return false;
            int inputIndex = InputIndex(top, recipient);
            if (SafeComponentGuid(top) == LichenComponentIds.ExportRoot) return inputIndex == 1;
            ThallusRouteComponentKind kind = RouteKind(top);
            if (kind == ThallusRouteComponentKind.Merge) return inputIndex >= 0;
            if (kind == ThallusRouteComponentKind.Jitter || kind == ThallusRouteComponentKind.Relay) return inputIndex == 0;
            return false;
        }

        private static void CaptureSource(IGH_Param source, IGH_Param target, ContextSnapshot snapshot, ThallusIdentityRouteRequest request,
            Dictionary<string, GrasshopperExportRootEdge> liveEdges, HashSet<string> expanded, int maximum)
        {
            if (source == null || target == null) throw new InvalidOperationException("A Thallus identity route contains a missing parameter.");
            IGH_DocumentObject sourceObject = TopObject(source);
            IGH_DocumentObject targetObject = TopObject(target);
            if (sourceObject == null || targetObject == null) throw new InvalidOperationException("A Thallus identity route contains a detached parameter.");
            AddNode(snapshot, sourceObject); AddNode(snapshot, targetObject);

            ContextEdge edge = new ContextEdge
            {
                SourceNodeId = Id(sourceObject.InstanceGuid),
                SourceParameterIndex = OutputIndex(sourceObject, source),
                SourceParameterName = source.Name ?? "",
                TargetNodeId = Id(targetObject.InstanceGuid),
                TargetParameterIndex = InputIndex(targetObject, target),
                TargetParameterName = target.Name ?? ""
            };
            string edgeKey = ExportRootScopeResolver.EdgeKey(edge);
            if (!liveEdges.ContainsKey(edgeKey))
            {
                request.Edges.Add(edge);
                liveEdges.Add(edgeKey, new GrasshopperExportRootEdge { Edge = edge, Source = source, Target = target });
            }

            if (SafeComponentGuid(sourceObject) == LichenComponentIds.ThallusEndpoint) return;
            ThallusRouteComponentKind kind = RouteKind(sourceObject);
            if (kind == ThallusRouteComponentKind.Unsupported) return;
            if (!request.RoutingComponents.Any(value => String.Equals(value.ObjectId, edge.SourceNodeId, StringComparison.OrdinalIgnoreCase)))
                request.RoutingComponents.Add(new ThallusRouteComponent { ObjectId = edge.SourceNodeId, Kind = kind });

            string state = edge.SourceNodeId + "|" + edge.SourceParameterIndex;
            if (!expanded.Add(state)) return;
            if (expanded.Count > maximum) throw new InvalidOperationException("The Thallus identity route exceeds Lichen's bounded routing limit of " + maximum + " objects.");

            IEnumerable<IGH_Param> inputs = RoutingInputs(sourceObject, kind);
            foreach (IGH_Param input in inputs)
            {
                IList<IGH_Param> sources;
                try { sources = input.Sources; }
                catch { throw new InvalidOperationException("A routing component's Thallus identity sources are unavailable."); }
                if (sources == null) continue;
                foreach (IGH_Param upstream in sources) CaptureSource(upstream, input, snapshot, request, liveEdges, expanded, maximum);
            }
        }

        private static IEnumerable<IGH_Param> RoutingInputs(IGH_DocumentObject obj, ThallusRouteComponentKind kind)
        {
            IGH_Component component = obj as IGH_Component;
            if (component != null)
            {
                if (kind == ThallusRouteComponentKind.Merge) return component.Params.Input.Cast<IGH_Param>().ToList();
                if (component.Params.Input.Count > 0) return new[] { component.Params.Input[0] };
                return Enumerable.Empty<IGH_Param>();
            }
            IGH_Param parameter = obj as IGH_Param;
            return parameter == null ? Enumerable.Empty<IGH_Param>() : new[] { parameter };
        }

        private static List<ThallusIdentityTokenRecord> CaptureTokens(IGH_Param target, int maximum)
        {
            List<ThallusIdentityTokenRecord> result = new List<ThallusIdentityTokenRecord>();
            try
            {
                IGH_Structure data = target.VolatileData;
                if (data == null || data.DataCount == 0) return result;
                IEnumerator values = ((IEnumerable)data.AllData(true)).GetEnumerator();
                while (values.MoveNext())
                {
                    if (result.Count >= maximum + 1) break;
                    ThallusIdentityTokenRecord identity = UnwrapIdentity(values.Current);
                    result.Add(identity ?? new ThallusIdentityTokenRecord { IsAuthentic = false });
                }
            }
            catch { result.Add(new ThallusIdentityTokenRecord { IsAuthentic = false }); }
            return result;
        }

        private static ThallusIdentityTokenRecord UnwrapIdentity(object value)
        {
            object current = value;
            for (int depth = 0; depth < 3 && current != null; depth++)
            {
                LichenThallusIdentityGoo identity = current as LichenThallusIdentityGoo;
                if (identity != null) return identity.ToRecord();
                LichenThallusIdentityPayload payload = current as LichenThallusIdentityPayload;
                if (payload != null)
                    return new ThallusIdentityTokenRecord { EndpointObjectId = payload.EndpointObjectId, OwnerThallusId = payload.OwnerThallusId, IsAuthentic = true };
                GH_ObjectWrapper wrapper = current as GH_ObjectWrapper;
                if (wrapper == null) break;
                current = wrapper.Value;
            }
            return null;
        }

        private static void CaptureThalli(GH_Document document, ContextSnapshot snapshot)
        {
            List<GH_Group> groups = document.Objects.OfType<GH_Group>().Where(group => SafeComponentGuid(group) == LichenComponentIds.Thallus)
                .OrderBy(group => group.InstanceGuid).ToList();
            Dictionary<Guid, IGH_DocumentObject> objects = document.Objects.GroupBy(value => value.InstanceGuid).ToDictionary(group => group.Key, group => group.First());
            Dictionary<Guid, ContextThallus> captured = new Dictionary<Guid, ContextThallus>();
            foreach (GH_Group group in groups)
            {
                List<Guid> endpoints = group.ObjectIDs.Where(id =>
                {
                    IGH_DocumentObject member;
                    return objects.TryGetValue(id, out member) && SafeComponentGuid(member) == LichenComponentIds.ThallusEndpoint;
                }).Distinct().OrderBy(id => id).ToList();
                if (endpoints.Count > 1) throw new InvalidOperationException("A Thallus owns multiple endpoint identities. Repair or recreate that Thallus before routing it.");
                ContextThallus thallus = new ContextThallus
                {
                    InstanceId = Id(group.InstanceGuid),
                    Name = group.NickName ?? "Thallus",
                    EndpointObjectId = endpoints.Count == 0 ? "" : Id(endpoints[0])
                };
                captured.Add(group.InstanceGuid, thallus); snapshot.Thalli.Add(thallus);
            }
            foreach (GH_Group parent in groups)
                foreach (Guid memberId in parent.ObjectIDs)
                {
                    ContextThallus child;
                    if (!captured.TryGetValue(memberId, out child) || memberId == parent.InstanceGuid) continue;
                    string parentId = Id(parent.InstanceGuid);
                    if (!String.IsNullOrWhiteSpace(child.ParentThallusId) && !String.Equals(child.ParentThallusId, parentId, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("A routed Thallus belongs to more than one parent Thallus.");
                    child.ParentThallusId = parentId;
                }
        }

        private static void AddNode(ContextSnapshot snapshot, IGH_DocumentObject obj)
        {
            string id = Id(obj.InstanceGuid);
            if (snapshot.Nodes.Any(value => String.Equals(value.InstanceId, id, StringComparison.OrdinalIgnoreCase))) return;
            snapshot.Nodes.Add(new ContextNode { InstanceId = id, TypeId = SafeComponentGuid(obj).ToString("D") });
        }

        private static ThallusRouteComponentKind RouteKind(IGH_DocumentObject obj)
        {
            if (obj == null) return ThallusRouteComponentKind.Unsupported;
            Guid id = SafeComponentGuid(obj);
            Type type = obj.GetType();
            string typeName = type.FullName ?? "";
            string assembly = type.Assembly.GetName().Name ?? "";
            if (id == MergeComponentId && String.Equals(assembly, "MathComponents", StringComparison.Ordinal)
                && String.Equals(typeName, "MathComponents.MergeComponents.Component_MergeVariable", StringComparison.Ordinal)) return ThallusRouteComponentKind.Merge;
            if (id == JitterComponentId && String.Equals(assembly, "MathComponents", StringComparison.Ordinal)
                && String.Equals(typeName, "MathComponents.FunctionComponents.Component_Jitter", StringComparison.Ordinal)) return ThallusRouteComponentKind.Jitter;
            if (id == RelayComponentId && String.Equals(assembly, "Grasshopper", StringComparison.Ordinal)
                && String.Equals(typeName, "Grasshopper.Kernel.Special.GH_Relay", StringComparison.Ordinal)) return ThallusRouteComponentKind.Relay;
            return ThallusRouteComponentKind.Unsupported;
        }

        private static IGH_DocumentObject TopObject(IGH_Param parameter)
        {
            try
            {
                if (parameter == null || parameter.Attributes == null || parameter.Attributes.GetTopLevel == null) return parameter as IGH_DocumentObject;
                return parameter.Attributes.GetTopLevel.DocObject;
            }
            catch { return null; }
        }

        private static int InputIndex(IGH_DocumentObject obj, IGH_Param parameter)
        {
            IGH_Component component = obj as IGH_Component;
            return component == null ? (Object.ReferenceEquals(obj, parameter) ? 0 : -1) : component.Params.Input.IndexOf(parameter);
        }

        private static int OutputIndex(IGH_DocumentObject obj, IGH_Param parameter)
        {
            IGH_Component component = obj as IGH_Component;
            return component == null ? (Object.ReferenceEquals(obj, parameter) ? 0 : -1) : component.Params.Output.IndexOf(parameter);
        }

        private static Guid SafeComponentGuid(IGH_DocumentObject obj) { try { return obj == null ? Guid.Empty : obj.ComponentGuid; } catch { return Guid.Empty; } }
        private static string Id(Guid value) { return value.ToString("D").ToLowerInvariant(); }
    }
}
