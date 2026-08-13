using System;
using System.Collections.Generic;
using System.Linq;

namespace Lichen.Core
{
    public enum ThallusRouteComponentKind
    {
        Unsupported,
        Merge,
        Jitter,
        Relay
    }

    public sealed class ThallusRouteComponent
    {
        public ThallusRouteComponent() { ObjectId = ""; Kind = ThallusRouteComponentKind.Unsupported; }
        public string ObjectId { get; set; }
        public ThallusRouteComponentKind Kind { get; set; }
    }

    public sealed class ThallusIdentityTokenRecord
    {
        public ThallusIdentityTokenRecord() { EndpointObjectId = ""; OwnerThallusId = ""; }
        public string EndpointObjectId { get; set; }
        public string OwnerThallusId { get; set; }
        public bool IsAuthentic { get; set; }
    }

    public sealed class ThallusIdentityRouteRequest
    {
        public ThallusIdentityRouteRequest()
        {
            RootObjectId = "";
            OrderedTokens = new List<ThallusIdentityTokenRecord>();
            RoutingComponents = new List<ThallusRouteComponent>();
            Edges = new List<ContextEdge>();
            MaximumNodes = 500;
        }

        public string RootObjectId { get; set; }
        public List<ThallusIdentityTokenRecord> OrderedTokens { get; set; }
        public List<ThallusRouteComponent> RoutingComponents { get; set; }
        public List<ContextEdge> Edges { get; set; }
        public int MaximumNodes { get; set; }
    }

    public sealed class ThallusIdentityRouteResolution
    {
        public ThallusIdentityRouteResolution()
        {
            OrderedThallusIds = new List<string>();
            EndpointObjectIds = new List<string>();
            RouteEdgeKeys = new List<string>();
        }

        public List<string> OrderedThallusIds { get; set; }
        public List<string> EndpointObjectIds { get; set; }
        public List<string> RouteEdgeKeys { get; set; }
    }

    public sealed class ThallusIdentityRouteResolver
    {
        public ThallusIdentityRouteResolution Resolve(ContextSnapshot snapshot, ThallusIdentityRouteRequest request)
        {
            if (snapshot == null) throw new ArgumentNullException("snapshot");
            if (request == null) throw new ArgumentNullException("request");
            if (String.IsNullOrWhiteSpace(request.RootObjectId)) throw new InvalidOperationException("The Lichen.T routing target is missing.");

            int maximum = request.MaximumNodes <= 0 ? 500 : request.MaximumNodes;
            List<ContextEdge> edges = (request.Edges ?? new List<ContextEdge>()).Where(edge => edge != null)
                .OrderBy(ExportRootScopeResolver.EdgeKey, StringComparer.OrdinalIgnoreCase).ToList();
            List<ContextEdge> terminalEdges = edges.Where(edge => String.Equals(edge.TargetNodeId, request.RootObjectId, StringComparison.OrdinalIgnoreCase)
                && edge.TargetParameterIndex == 1).ToList();
            if (terminalEdges.Count == 0) throw new InvalidOperationException("No Thallus identity route reaches Lichen.T.");

            Dictionary<string, ContextNode> nodes = UniqueNodes(snapshot.Nodes);
            Dictionary<string, ContextThallus> thalli = UniqueThalli(snapshot.Thalli);
            Dictionary<string, ContextThallus> ownersByEndpoint = OwnersByEndpoint(thalli.Values);
            Dictionary<string, ThallusRouteComponentKind> routing = RoutingKinds(request.RoutingComponents);
            Dictionary<string, List<ContextEdge>> incoming = IncomingEdges(edges);
            List<string> endpointOccurrences = new List<string>();
            HashSet<string> routeEdgeKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            HashSet<string> visiting = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int steps = 0;

            foreach (ContextEdge edge in terminalEdges)
                Follow(edge, nodes, routing, incoming, endpointOccurrences, routeEdgeKeys, visiting, maximum, ref steps);

            if (endpointOccurrences.Count == 0) throw new InvalidOperationException("Lichen.T does not receive a live Thallus endpoint through the accepted route.");
            string repeatedEndpoint = endpointOccurrences.GroupBy(id => id, StringComparer.OrdinalIgnoreCase).Where(group => group.Count() > 1)
                .Select(group => group.Key).OrderBy(id => id, StringComparer.OrdinalIgnoreCase).FirstOrDefault();
            if (!String.IsNullOrWhiteSpace(repeatedEndpoint))
                throw new InvalidOperationException("A Thallus endpoint reaches Lichen.T more than once. Duplicate routed identities are not accepted.");

            List<ThallusIdentityTokenRecord> tokens = request.OrderedTokens ?? new List<ThallusIdentityTokenRecord>();
            if (tokens.Count == 0) throw new InvalidOperationException("Lichen.T has no current Thallus identity data. Recompute the routing chain and try again.");
            if (tokens.Count > maximum) throw new InvalidOperationException("The routed Thallus identity list exceeds Lichen's bounded routing limit of " + maximum + ".");
            if (tokens.Any(token => token == null || !token.IsAuthentic))
                throw new InvalidOperationException("Lichen.T received generic or malformed data instead of an opaque Thallus identity.");

            string duplicateToken = tokens.GroupBy(token => token.EndpointObjectId ?? "", StringComparer.OrdinalIgnoreCase)
                .Where(group => String.IsNullOrWhiteSpace(group.Key) || group.Count() > 1).Select(group => group.Key).FirstOrDefault();
            if (duplicateToken != null)
                throw new InvalidOperationException("Lichen.T received a missing or duplicate Thallus identity. Each outermost Thallus may appear exactly once.");
            if (tokens.GroupBy(token => token.OwnerThallusId ?? "", StringComparer.OrdinalIgnoreCase)
                .Any(group => String.IsNullOrWhiteSpace(group.Key) || group.Count() > 1))
                throw new InvalidOperationException("Lichen.T received duplicate identities for one Thallus. Each outermost Thallus may appear exactly once.");

            HashSet<string> expectedEndpoints = new HashSet<string>(endpointOccurrences, StringComparer.OrdinalIgnoreCase);
            HashSet<string> actualEndpoints = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            List<string> orderedThallusIds = new List<string>();
            foreach (ThallusIdentityTokenRecord token in tokens)
            {
                ContextNode endpointNode;
                if (!nodes.TryGetValue(token.EndpointObjectId, out endpointNode) || !IsThallusEndpoint(endpointNode))
                    throw new InvalidOperationException("A routed Thallus identity refers to a deleted, missing, or stale endpoint.");
                ContextThallus owner;
                if (!ownersByEndpoint.TryGetValue(token.EndpointObjectId, out owner))
                    throw new InvalidOperationException("A routed Thallus identity no longer has a live owning Thallus.");
                if (!String.Equals(owner.InstanceId, token.OwnerThallusId, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("A routed Thallus identity does not match its live owner.");
                if (!String.IsNullOrWhiteSpace(owner.ParentThallusId))
                    throw new InvalidOperationException("Only an outermost Thallus can reach Lichen.T.");
                actualEndpoints.Add(token.EndpointObjectId);
                orderedThallusIds.Add(owner.InstanceId);
            }

            if (!expectedEndpoints.SetEquals(actualEndpoints))
                throw new InvalidOperationException("The routed Thallus identities do not exactly match the live endpoints contributing to Lichen.T.");

            return new ThallusIdentityRouteResolution
            {
                OrderedThallusIds = orderedThallusIds,
                EndpointObjectIds = tokens.Select(token => token.EndpointObjectId).ToList(),
                RouteEdgeKeys = routeEdgeKeys.OrderBy(key => key, StringComparer.OrdinalIgnoreCase).ToList()
            };
        }

        private static void Follow(ContextEdge edge, Dictionary<string, ContextNode> nodes,
            Dictionary<string, ThallusRouteComponentKind> routing, Dictionary<string, List<ContextEdge>> incoming,
            List<string> endpointOccurrences, HashSet<string> routeEdgeKeys, HashSet<string> visiting,
            int maximum, ref int steps)
        {
            if (++steps > maximum * 8) throw new InvalidOperationException("The Thallus identity route exceeds Lichen's bounded traversal limit.");
            routeEdgeKeys.Add(ExportRootScopeResolver.EdgeKey(edge));
            ContextNode sourceNode;
            if (!nodes.TryGetValue(edge.SourceNodeId, out sourceNode))
                throw new InvalidOperationException("A Thallus identity route references a missing source object.");
            if (IsThallusEndpoint(sourceNode))
            {
                if (edge.SourceParameterIndex != 0) throw new InvalidOperationException("A Thallus identity route uses an invalid endpoint output.");
                endpointOccurrences.Add(sourceNode.InstanceId);
                return;
            }

            ThallusRouteComponentKind kind;
            if (!routing.TryGetValue(sourceNode.InstanceId, out kind) || kind == ThallusRouteComponentKind.Unsupported)
                throw new InvalidOperationException("Lichen.T accepts only direct Thallus endpoints or the supported Merge, Jitter Values, and Relay routing path.");
            if (edge.SourceParameterIndex != 0)
                throw new InvalidOperationException(kind == ThallusRouteComponentKind.Jitter
                    ? "Lichen.T must use Jitter's Values output, not its Indices output."
                    : "A Thallus identity route uses an unsupported routing output.");

            string state = sourceNode.InstanceId + "|" + edge.SourceParameterIndex;
            if (!visiting.Add(state)) throw new InvalidOperationException("The Thallus identity route contains a cycle.");
            try
            {
                List<ContextEdge> sourceEdges;
                if (!incoming.TryGetValue(sourceNode.InstanceId, out sourceEdges)) sourceEdges = new List<ContextEdge>();
                IEnumerable<ContextEdge> followed = kind == ThallusRouteComponentKind.Merge
                    ? sourceEdges
                    : sourceEdges.Where(value => value.TargetParameterIndex == 0);
                List<ContextEdge> relevant = followed.OrderBy(value => value.TargetParameterIndex)
                    .ThenBy(ExportRootScopeResolver.EdgeKey, StringComparer.OrdinalIgnoreCase).ToList();
                if (relevant.Count == 0) throw new InvalidOperationException("A supported Thallus routing component has no identity input.");
                foreach (ContextEdge upstream in relevant)
                    Follow(upstream, nodes, routing, incoming, endpointOccurrences, routeEdgeKeys, visiting, maximum, ref steps);
            }
            finally { visiting.Remove(state); }
        }

        private static Dictionary<string, ContextNode> UniqueNodes(IEnumerable<ContextNode> values)
        {
            Dictionary<string, ContextNode> result = new Dictionary<string, ContextNode>(StringComparer.OrdinalIgnoreCase);
            foreach (IGrouping<string, ContextNode> group in (values ?? new List<ContextNode>()).Where(value => value != null && !String.IsNullOrWhiteSpace(value.InstanceId))
                .GroupBy(value => value.InstanceId, StringComparer.OrdinalIgnoreCase))
            {
                if (group.Count() > 1) throw new InvalidOperationException("The Thallus identity route contains duplicate object identities.");
                result.Add(group.Key, group.First());
            }
            return result;
        }

        private static Dictionary<string, ContextThallus> UniqueThalli(IEnumerable<ContextThallus> values)
        {
            Dictionary<string, ContextThallus> result = new Dictionary<string, ContextThallus>(StringComparer.OrdinalIgnoreCase);
            foreach (IGrouping<string, ContextThallus> group in (values ?? new List<ContextThallus>()).Where(value => value != null && !String.IsNullOrWhiteSpace(value.InstanceId))
                .GroupBy(value => value.InstanceId, StringComparer.OrdinalIgnoreCase))
            {
                if (group.Count() > 1) throw new InvalidOperationException("The document contains duplicate Thallus owner identities.");
                result.Add(group.Key, group.First());
            }
            return result;
        }

        private static Dictionary<string, ContextThallus> OwnersByEndpoint(IEnumerable<ContextThallus> thalli)
        {
            Dictionary<string, ContextThallus> result = new Dictionary<string, ContextThallus>(StringComparer.OrdinalIgnoreCase);
            foreach (ContextThallus thallus in thalli.Where(value => !String.IsNullOrWhiteSpace(value.EndpointObjectId)))
            {
                if (result.ContainsKey(thallus.EndpointObjectId)) throw new InvalidOperationException("Multiple Thalli claim the same endpoint identity.");
                result.Add(thallus.EndpointObjectId, thallus);
            }
            return result;
        }

        private static Dictionary<string, ThallusRouteComponentKind> RoutingKinds(IEnumerable<ThallusRouteComponent> values)
        {
            Dictionary<string, ThallusRouteComponentKind> result = new Dictionary<string, ThallusRouteComponentKind>(StringComparer.OrdinalIgnoreCase);
            foreach (ThallusRouteComponent value in values ?? new List<ThallusRouteComponent>())
            {
                if (value == null || String.IsNullOrWhiteSpace(value.ObjectId)) continue;
                ThallusRouteComponentKind existing;
                if (result.TryGetValue(value.ObjectId, out existing) && existing != value.Kind)
                    throw new InvalidOperationException("A routing object has conflicting identities.");
                result[value.ObjectId] = value.Kind;
            }
            return result;
        }

        private static Dictionary<string, List<ContextEdge>> IncomingEdges(IEnumerable<ContextEdge> edges)
        {
            Dictionary<string, List<ContextEdge>> result = new Dictionary<string, List<ContextEdge>>(StringComparer.OrdinalIgnoreCase);
            foreach (ContextEdge edge in edges)
            {
                List<ContextEdge> values;
                if (!result.TryGetValue(edge.TargetNodeId, out values)) { values = new List<ContextEdge>(); result.Add(edge.TargetNodeId, values); }
                values.Add(edge);
            }
            return result;
        }

        private static bool IsThallusEndpoint(ContextNode node)
        {
            Guid typeId;
            return node != null && Guid.TryParse(node.TypeId, out typeId) && typeId == LichenComponentIds.ThallusEndpoint;
        }
    }
}
