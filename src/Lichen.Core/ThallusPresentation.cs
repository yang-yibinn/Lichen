using System;
using System.Collections.Generic;
using System.Linq;

namespace Lichen.Core
{
    internal sealed class ThallusSemanticRegion
    {
        public ThallusSemanticRegion()
        {
            ChildIds = new List<string>(); Operations = new List<string>(); ScriptLabels = new List<string>();
            ThirdPartyDependencies = new List<string>(); SharedPeerLabels = new List<string>(); InferredPurpose = ""; SemanticQualifier = "";
        }

        public ContextThallus Thallus { get; set; }
        public string Label { get; set; }
        public List<string> ChildIds { get; set; }
        public List<string> Operations { get; set; }
        public string InferredPurpose { get; set; }
        public string SemanticQualifier { get; set; }
        public List<string> ScriptLabels { get; set; }
        public int ScriptInstanceCount { get; set; }
        public List<string> ThirdPartyDependencies { get; set; }
        public List<string> SharedPeerLabels { get; set; }
        public int SharedPeerMemberCount { get; set; }
        public int IncomingBoundaryCount { get; set; }
        public int OutgoingBoundaryCount { get; set; }
        public int RuntimeParameterFactCount { get; set; }
        public int RuntimeMessageCount { get; set; }
    }

    internal sealed class ThallusFlowTransition
    {
        public string SourceId { get; set; }
        public string TargetId { get; set; }
    }

    internal sealed class ThallusFlowConvergence
    {
        public ThallusFlowConvergence() { SourceIds = new List<string>(); }
        public string TargetId { get; set; }
        public List<string> SourceIds { get; set; }
    }

    internal sealed class ThallusPresentationModel
    {
        public ThallusPresentationModel()
        {
            Regions = new List<ThallusSemanticRegion>(); ById = new Dictionary<string, ThallusSemanticRegion>(StringComparer.OrdinalIgnoreCase);
            RootIds = new List<string>(); Transitions = new List<ThallusFlowTransition>(); Convergences = new List<ThallusFlowConvergence>();
            ParallelEntryIds = new List<string>(); CycleGroups = new List<List<string>>(); DetailRegionIds = new List<string>();
            DirectRegionIdsByNode = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        }

        public List<ThallusSemanticRegion> Regions { get; set; }
        public Dictionary<string, ThallusSemanticRegion> ById { get; set; }
        public List<string> RootIds { get; set; }
        public List<ThallusFlowTransition> Transitions { get; set; }
        public List<ThallusFlowConvergence> Convergences { get; set; }
        public List<string> ParallelEntryIds { get; set; }
        public List<List<string>> CycleGroups { get; set; }
        public List<string> DetailRegionIds { get; set; }
        public Dictionary<string, List<string>> DirectRegionIdsByNode { get; set; }
        public int AmbiguousOverlapEdgeCount { get; set; }
    }

    internal static class ThallusPresentationAnalyzer
    {
        public static ThallusPresentationModel Analyze(ContextDocument document)
        {
            ThallusPresentationModel model = new ThallusPresentationModel();
            if (document == null || document.Thalli == null || document.Thalli.Count == 0) return model;

            List<ContextThallus> thalli = document.Thalli.Where(t => t != null && !String.IsNullOrWhiteSpace(t.InstanceId))
                .OrderBy(t => t.InstanceId, StringComparer.OrdinalIgnoreCase).ToList();
            Dictionary<string, ContextThallus> byId = thalli.ToDictionary(t => t.InstanceId, StringComparer.OrdinalIgnoreCase);
            Dictionary<string, string> labels = thalli.ToDictionary(t => t.InstanceId, t => Label(t, thalli), StringComparer.OrdinalIgnoreCase);
            HashSet<string> thirdPartyNames = new HashSet<string>((document.Dependencies ?? new List<ContextDependency>())
                .Where(d => d != null && !String.Equals(d.Kind, "grasshopper_native", StringComparison.OrdinalIgnoreCase))
                .Select(d => d.Name), StringComparer.OrdinalIgnoreCase);

            foreach (ContextThallus thallus in thalli)
            {
                HashSet<string> effectiveMembers = new HashSet<string>(thallus.EffectiveMemberIds ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
                HashSet<string> directMembers = new HashSet<string>(thallus.DirectMemberIds ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
                List<ContextNode> nodes = (document.Nodes ?? new List<ContextNode>()).Where(n => directMembers.Contains(n.InstanceId))
                    .OrderBy(n => n.InstanceId, StringComparer.OrdinalIgnoreCase).ToList();
                ContextDocument subgraph = new ContextDocument
                {
                    Nodes = nodes,
                    Edges = (document.Edges ?? new List<ContextEdge>()).Where(e => directMembers.Contains(e.SourceNodeId) && directMembers.Contains(e.TargetNodeId))
                        .OrderBy(ExportRootScopeResolver.EdgeKey, StringComparer.OrdinalIgnoreCase).ToList(),
                    Thalli = new List<ContextThallus>()
                };
                ContextAnalysis analysis = new ComponentSemanticsService().Analyze(subgraph);
                FunctionalEvidenceSet functionalEvidence = FunctionalEvidenceAnalyzer.Analyze(subgraph, nodes);
                ThallusSemanticRegion region = new ThallusSemanticRegion
                {
                    Thallus = thallus,
                    Label = labels[thallus.InstanceId],
                    Operations = analysis.DetectedOperations.Where(value => !String.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                    InferredPurpose = analysis.InferredPurpose ?? "",
                    ChildIds = thalli.Where(child => String.Equals(child.ParentThallusId, thallus.InstanceId, StringComparison.OrdinalIgnoreCase))
                        .OrderBy(child => labels[child.InstanceId], StringComparer.OrdinalIgnoreCase).ThenBy(child => child.InstanceId, StringComparer.OrdinalIgnoreCase)
                        .Select(child => child.InstanceId).ToList(),
                    ScriptInstanceCount = nodes.Count(n => n.Script != null),
                    ScriptLabels = nodes.Where(n => n.Script != null).Select(n => NodeLabel(n) + " (" + EmptyTo(n.Script.Language, "unknown language") + ")")
                        .GroupBy(value => value, StringComparer.OrdinalIgnoreCase).OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
                        .Select(group => group.Key + (group.Count() > 1 ? " \u00d7" + group.Count() : "")).ToList(),
                    ThirdPartyDependencies = DependenciesFor(nodes, thirdPartyNames),
                    RuntimeParameterFactCount = nodes.SelectMany(n => n.Inputs.Concat(n.Outputs)).Count(p => !String.IsNullOrWhiteSpace(p.RuntimeDataSummary) || !String.IsNullOrWhiteSpace(p.RuntimeTreeShape)),
                    RuntimeMessageCount = nodes.Sum(n => (n.RuntimeMessages ?? new List<ContextRuntimeMessage>()).Count(IsWarningOrError))
                };
                region.SemanticQualifier = SemanticQualifier(nodes, functionalEvidence, thirdPartyNames);
                if (IsDefaultName(thallus.Name) && !String.IsNullOrWhiteSpace(region.SemanticQualifier))
                    region.Label += " (" + region.SemanticQualifier + ")";
                CountBoundaries(document.Edges, effectiveMembers, region);
                model.Regions.Add(region); model.ById.Add(thallus.InstanceId, region);
            }

            AddPeerOverlap(thalli, byId, model);
            List<string> orderedRootIds = new List<string>();
            HashSet<string> seenRootIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (document.Scope != null && String.Equals(document.Scope.Mode, "thallus_root", StringComparison.OrdinalIgnoreCase))
                foreach (string id in document.Scope.RootThallusIds ?? new List<string>())
                {
                    ContextThallus routed;
                    if (byId.TryGetValue(id, out routed) && (String.IsNullOrWhiteSpace(routed.ParentThallusId) || !byId.ContainsKey(routed.ParentThallusId)) && seenRootIds.Add(id))
                        orderedRootIds.Add(id);
                }
            foreach (ContextThallus root in thalli.Where(t => String.IsNullOrWhiteSpace(t.ParentThallusId) || !byId.ContainsKey(t.ParentThallusId))
                .OrderBy(t => labels[t.InstanceId], StringComparer.OrdinalIgnoreCase).ThenBy(t => t.InstanceId, StringComparer.OrdinalIgnoreCase))
                if (seenRootIds.Add(root.InstanceId)) orderedRootIds.Add(root.InstanceId);
            model.RootIds = orderedRootIds;
            AddDetailMembership(thalli, model);
            BuildRootFlow(document, model);
            return model;
        }

        private static string SemanticQualifier(List<ContextNode> nodes, FunctionalEvidenceSet evidence, HashSet<string> thirdPartyNames)
        {
            FunctionalEvidence supported = (evidence == null ? new List<FunctionalEvidence>() : evidence.Items)
                .Where(item => item.Strength == FunctionalEvidenceStrength.Supported && !String.IsNullOrWhiteSpace(item.Stage))
                .OrderBy(item => item.RuleId, StringComparer.OrdinalIgnoreCase).FirstOrDefault();
            if (supported != null) return "supported function: " + CompactFunctionalStage(supported);

            HashSet<string> generic = new HashSet<string>(new[]
            {
                "Relay", "Merge", "Entwine", "Shift Paths", "Graft Tree", "Flatten Tree", "Simplify Tree", "Clean Tree", "Trim Tree",
                "List Item", "Partition List", "Panel", "Number Slider", "Integer Slider", "Boolean Toggle", "Value List"
            }, StringComparer.OrdinalIgnoreCase);
            List<string> specialized = nodes.Where(node => thirdPartyNames.Contains(node.AssemblyName) && !generic.Contains(node.Name))
                .Select(NodeLabel).Where(value => !String.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase).Take(2).ToList();
            return specialized.Count == 0 ? "" : "observed component" + (specialized.Count == 1 ? ": " : "s: ") + String.Join(" / ", specialized.ToArray());
        }

        private static string CompactFunctionalStage(FunctionalEvidence evidence)
        {
            if (evidence == null) return "";
            if (String.Equals(evidence.RuleId, FunctionalEvidenceAnalyzer.SurfacePipeMorphRule, StringComparison.OrdinalIgnoreCase)) return "surface pipe/intersection/morph";
            if (String.Equals(evidence.RuleId, FunctionalEvidenceAnalyzer.SurfacePointCurveNetworkRule, StringComparison.OrdinalIgnoreCase)) return "surface point-grid curves";
            if (String.Equals(evidence.RuleId, FunctionalEvidenceAnalyzer.TangentCurveReconstructionRule, StringComparison.OrdinalIgnoreCase)) return "tangent-constrained curve reconstruction";
            if (String.Equals(evidence.RuleId, FunctionalEvidenceAnalyzer.CurveGuidedSweepRule, StringComparison.OrdinalIgnoreCase)) return "curve-guided sweep";
            if (String.Equals(evidence.RuleId, FunctionalEvidenceAnalyzer.CurveNetworkPreparationRule, StringComparison.OrdinalIgnoreCase)) return "curve-network preparation";
            if (String.Equals(evidence.RuleId, FunctionalEvidenceAnalyzer.IntersectionAngleRule, StringComparison.OrdinalIgnoreCase)) return "intersection-angle analysis";
            if (String.Equals(evidence.RuleId, FunctionalEvidenceAnalyzer.AngleDrivenFilletRule, StringComparison.OrdinalIgnoreCase)) return "angle-driven fillet control";
            if (String.Equals(evidence.RuleId, FunctionalEvidenceAnalyzer.AngleRemappingRule, StringComparison.OrdinalIgnoreCase)) return "angle-remapped controls";
            if (String.Equals(evidence.RuleId, FunctionalEvidenceAnalyzer.DitheredPanelPartitionRule, StringComparison.OrdinalIgnoreCase)) return "dithered panel partitioning";
            if (String.Equals(evidence.RuleId, FunctionalEvidenceAnalyzer.ImagePanelFilteringRule, StringComparison.OrdinalIgnoreCase)) return "image-driven panel filtering";
            if (String.Equals(evidence.RuleId, FunctionalEvidenceAnalyzer.ModelBlockPlacementRule, StringComparison.OrdinalIgnoreCase)) return "model-block placement";
            if (String.Equals(evidence.RuleId, FunctionalEvidenceAnalyzer.NumericNormalizationRule, StringComparison.OrdinalIgnoreCase)) return "numeric normalization";
            return evidence.Stage;
        }

        private static bool IsDefaultName(string value)
        {
            return String.IsNullOrWhiteSpace(value) || String.Equals(value.Trim(), "Thallus", StringComparison.OrdinalIgnoreCase);
        }

        private static void AddDetailMembership(List<ContextThallus> thalli, ThallusPresentationModel model)
        {
            foreach (ContextThallus thallus in thalli)
                foreach (string nodeId in thallus.DirectMemberIds ?? new List<string>())
                {
                    List<string> regionIds;
                    if (!model.DirectRegionIdsByNode.TryGetValue(nodeId, out regionIds))
                    {
                        regionIds = new List<string>(); model.DirectRegionIdsByNode.Add(nodeId, regionIds);
                    }
                    if (!regionIds.Contains(thallus.InstanceId, StringComparer.OrdinalIgnoreCase)) regionIds.Add(thallus.InstanceId);
                }

            HashSet<string> visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string rootId in model.RootIds) AddDetailRegion(rootId, model, visited);
            foreach (ThallusSemanticRegion region in model.Regions.OrderBy(value => value.Label, StringComparer.OrdinalIgnoreCase).ThenBy(value => value.Thallus.InstanceId, StringComparer.OrdinalIgnoreCase))
                AddDetailRegion(region.Thallus.InstanceId, model, visited);
            Dictionary<string, int> order = model.DetailRegionIds.Select((id, index) => new { id, index })
                .ToDictionary(value => value.id, value => value.index, StringComparer.OrdinalIgnoreCase);
            foreach (List<string> regionIds in model.DirectRegionIdsByNode.Values)
                regionIds.Sort((left, right) => order[left].CompareTo(order[right]));
        }

        private static void AddDetailRegion(string regionId, ThallusPresentationModel model, HashSet<string> visited)
        {
            ThallusSemanticRegion region;
            if (!visited.Add(regionId) || !model.ById.TryGetValue(regionId, out region)) return;
            model.DetailRegionIds.Add(regionId);
            foreach (string childId in region.ChildIds) AddDetailRegion(childId, model, visited);
        }

        private static void CountBoundaries(IEnumerable<ContextEdge> edges, HashSet<string> members, ThallusSemanticRegion region)
        {
            foreach (ContextEdge edge in edges ?? new List<ContextEdge>())
            {
                bool source = members.Contains(edge.SourceNodeId); bool target = members.Contains(edge.TargetNodeId);
                if (!source && target) region.IncomingBoundaryCount++;
                else if (source && !target) region.OutgoingBoundaryCount++;
            }
        }

        private static bool IsWarningOrError(ContextRuntimeMessage message)
        {
            string level = message == null ? "" : message.Level ?? "";
            return level.IndexOf("warning", StringComparison.OrdinalIgnoreCase) >= 0 || level.IndexOf("error", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static List<string> DependenciesFor(IEnumerable<ContextNode> nodes, HashSet<string> thirdPartyNames)
        {
            HashSet<string> values = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (ContextNode node in nodes)
            {
                if (thirdPartyNames.Contains(node.AssemblyName)) values.Add(node.AssemblyName);
                AddClusterDependencies(node.ClusterGraph, values);
            }
            return values.OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static void AddClusterDependencies(ContextClusterGraph graph, HashSet<string> values)
        {
            if (graph == null) return;
            foreach (ContextDependency dependency in graph.Dependencies ?? new List<ContextDependency>())
                if (dependency != null && !String.Equals(dependency.Kind, "grasshopper_native", StringComparison.OrdinalIgnoreCase) && !String.IsNullOrWhiteSpace(dependency.Name))
                    values.Add(dependency.Name);
            foreach (ContextNode node in graph.Nodes ?? new List<ContextNode>()) AddClusterDependencies(node.ClusterGraph, values);
        }

        private static void AddPeerOverlap(List<ContextThallus> thalli, Dictionary<string, ContextThallus> byId, ThallusPresentationModel model)
        {
            for (int i = 0; i < thalli.Count; i++)
            {
                ContextThallus first = thalli[i]; HashSet<string> shared = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                List<string> peers = new List<string>();
                for (int j = 0; j < thalli.Count; j++)
                {
                    if (i == j || IsAncestor(first.InstanceId, thalli[j].InstanceId, byId) || IsAncestor(thalli[j].InstanceId, first.InstanceId, byId)) continue;
                    HashSet<string> intersection = new HashSet<string>(first.EffectiveMemberIds ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
                    intersection.IntersectWith(thalli[j].EffectiveMemberIds ?? new List<string>());
                    if (intersection.Count == 0) continue;
                    shared.UnionWith(intersection); peers.Add(model.ById[thalli[j].InstanceId].Label);
                }
                ThallusSemanticRegion region = model.ById[first.InstanceId];
                region.SharedPeerMemberCount = shared.Count;
                region.SharedPeerLabels = peers.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToList();
            }
        }

        private static bool IsAncestor(string possibleAncestor, string childId, Dictionary<string, ContextThallus> byId)
        {
            HashSet<string> visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase); string current = childId;
            while (!String.IsNullOrWhiteSpace(current) && visited.Add(current))
            {
                ContextThallus child;
                if (!byId.TryGetValue(current, out child) || String.IsNullOrWhiteSpace(child.ParentThallusId)) return false;
                if (String.Equals(child.ParentThallusId, possibleAncestor, StringComparison.OrdinalIgnoreCase)) return true;
                current = child.ParentThallusId;
            }
            return false;
        }

        private static void BuildRootFlow(ContextDocument document, ThallusPresentationModel model)
        {
            Dictionary<string, List<string>> owners = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (string rootId in model.RootIds)
                foreach (string member in model.ById[rootId].Thallus.EffectiveMemberIds ?? new List<string>())
                {
                    List<string> values;
                    if (!owners.TryGetValue(member, out values)) { values = new List<string>(); owners.Add(member, values); }
                    if (!values.Contains(rootId, StringComparer.OrdinalIgnoreCase)) values.Add(rootId);
                }
            foreach (List<string> values in owners.Values) values.Sort(StringComparer.OrdinalIgnoreCase);

            HashSet<string> transitions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (ContextEdge edge in document.Edges ?? new List<ContextEdge>())
            {
                List<string> sourceOwners, targetOwners;
                if (!owners.TryGetValue(edge.SourceNodeId, out sourceOwners) || !owners.TryGetValue(edge.TargetNodeId, out targetOwners)) continue;
                if (sourceOwners.Count == 1 && targetOwners.Count == 1)
                {
                    if (!String.Equals(sourceOwners[0], targetOwners[0], StringComparison.OrdinalIgnoreCase)) transitions.Add(sourceOwners[0] + "|" + targetOwners[0]);
                }
                else if (!new HashSet<string>(sourceOwners, StringComparer.OrdinalIgnoreCase).SetEquals(targetOwners)) model.AmbiguousOverlapEdgeCount++;
            }
            model.Transitions = transitions.Select(value => value.Split('|')).Select(ids => new ThallusFlowTransition { SourceId = ids[0], TargetId = ids[1] })
                .OrderBy(t => model.ById[t.SourceId].Label, StringComparer.OrdinalIgnoreCase).ThenBy(t => t.SourceId, StringComparer.OrdinalIgnoreCase)
                .ThenBy(t => model.ById[t.TargetId].Label, StringComparer.OrdinalIgnoreCase).ThenBy(t => t.TargetId, StringComparer.OrdinalIgnoreCase).ToList();

            model.Convergences = model.Transitions.GroupBy(t => t.TargetId, StringComparer.OrdinalIgnoreCase)
                .Select(group => new ThallusFlowConvergence
                {
                    TargetId = group.Key,
                    SourceIds = group.Select(t => t.SourceId).Distinct(StringComparer.OrdinalIgnoreCase)
                        .OrderBy(id => model.ById[id].Label, StringComparer.OrdinalIgnoreCase).ThenBy(id => id, StringComparer.OrdinalIgnoreCase).ToList()
                }).Where(value => value.SourceIds.Count > 1)
                .OrderBy(value => model.ById[value.TargetId].Label, StringComparer.OrdinalIgnoreCase).ThenBy(value => value.TargetId, StringComparer.OrdinalIgnoreCase).ToList();

            HashSet<string> targets = new HashSet<string>(model.Transitions.Select(t => t.TargetId), StringComparer.OrdinalIgnoreCase);
            model.ParallelEntryIds = model.RootIds.Where(id => !targets.Contains(id))
                .OrderBy(id => model.ById[id].Label, StringComparer.OrdinalIgnoreCase).ThenBy(id => id, StringComparer.OrdinalIgnoreCase).ToList();
            if (model.ParallelEntryIds.Count < 2) model.ParallelEntryIds.Clear();
            model.CycleGroups = FindCycles(model);
        }

        private static List<List<string>> FindCycles(ThallusPresentationModel model)
        {
            Dictionary<string, List<string>> adjacency = model.RootIds.ToDictionary(id => id, id => new List<string>(), StringComparer.OrdinalIgnoreCase);
            foreach (ThallusFlowTransition transition in model.Transitions) adjacency[transition.SourceId].Add(transition.TargetId);
            List<List<string>> groups = new List<List<string>>(); HashSet<string> assigned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string root in model.RootIds)
            {
                if (assigned.Contains(root)) continue;
                List<string> group = model.RootIds.Where(other => !String.Equals(root, other, StringComparison.OrdinalIgnoreCase)
                    && CanReach(root, other, adjacency) && CanReach(other, root, adjacency)).ToList();
                if (group.Count == 0) continue;
                group.Add(root); group = group.Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(id => model.ById[id].Label, StringComparer.OrdinalIgnoreCase).ThenBy(id => id, StringComparer.OrdinalIgnoreCase).ToList();
                foreach (string id in group) assigned.Add(id);
                groups.Add(group);
            }
            return groups.OrderBy(group => model.ById[group[0]].Label, StringComparer.OrdinalIgnoreCase).ThenBy(group => group[0], StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static bool CanReach(string start, string target, Dictionary<string, List<string>> adjacency)
        {
            Queue<string> pending = new Queue<string>(); HashSet<string> visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase); pending.Enqueue(start);
            while (pending.Count > 0)
            {
                string current = pending.Dequeue(); if (!visited.Add(current)) continue;
                foreach (string next in adjacency[current]) { if (String.Equals(next, target, StringComparison.OrdinalIgnoreCase)) return true; pending.Enqueue(next); }
            }
            return false;
        }

        private static string Label(ContextThallus thallus, IList<ContextThallus> all)
        {
            string name = String.IsNullOrWhiteSpace(thallus.Name) ? "" : thallus.Name.Trim();
            if (String.IsNullOrWhiteSpace(name) || String.Equals(name, "Thallus", StringComparison.OrdinalIgnoreCase)) return "Thallus [" + ShortId(thallus.InstanceId) + "]";
            int duplicates = all.Count(value => String.Equals(String.IsNullOrWhiteSpace(value.Name) ? "" : value.Name.Trim(), name, StringComparison.OrdinalIgnoreCase));
            return duplicates > 1 ? name + " [" + ShortId(thallus.InstanceId) + "]" : name;
        }

        private static string NodeLabel(ContextNode node) { return EmptyTo(node.Nickname, EmptyTo(node.Name, "Unnamed component")); }
        private static string EmptyTo(string value, string fallback) { return String.IsNullOrWhiteSpace(value) ? fallback : value.Trim(); }
        private static string ShortId(string id) { return String.IsNullOrEmpty(id) ? "unknown" : (id.Length <= 8 ? id : id.Substring(0, 8)); }
    }
}
