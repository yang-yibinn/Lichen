using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Lichen.Core
{
    public sealed class ContextGraphService
    {
        public ContextDocument BuildDocument(ContextSnapshot snapshot, ContextExportOptions options)
        {
            if (snapshot == null) throw new ArgumentNullException("snapshot");
            if (options == null) throw new ArgumentNullException("options");

            List<string> notes = new List<string>(snapshot.Notes ?? new List<string>());
            HashSet<string> available = new HashSet<string>(snapshot.Nodes.Select(n => n.InstanceId), StringComparer.OrdinalIgnoreCase);
            int maximum = options.MaximumNodes <= 0 ? 500 : options.MaximumNodes;
            HashSet<string> originallySelected = new HashSet<string>((snapshot.SelectedObjectIds ?? new List<string>()).Where(available.Contains), StringComparer.OrdinalIgnoreCase);
            originallySelected.ExceptWith((snapshot.Nodes ?? new List<ContextNode>()).Where(IsLichenInfrastructure).Select(n => n.InstanceId));
            HashSet<string> selectionSeeds = new HashSet<string>(originallySelected, StringComparer.OrdinalIgnoreCase);
            ThallusClosure selectedThallusClosure = null;
            if (options.ScopeMode != ScopeMode.EntireDocument && options.ScopeMode != ScopeMode.ExportRoot)
            {
                List<string> selectedThalli = (snapshot.SelectedThallusIds ?? new List<string>()).Where(id => !String.IsNullOrWhiteSpace(id))
                    .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(id => id, StringComparer.OrdinalIgnoreCase).ToList();
                if (selectedThalli.Count > 0)
                {
                    selectedThallusClosure = new ThallusScopeResolver().ResolveSelected(snapshot, selectedThalli, maximum);
                    selectionSeeds.UnionWith(selectedThallusClosure.IncludedObjectIds);
                }
                if (selectionSeeds.Count == 0)
                    throw new InvalidOperationException("No Grasshopper objects or Thalli are selected for the requested scope.");
            }

            ExportRootClosure rootClosure = null;
            ThallusClosure thallusClosure = null;
            ExportRootDefinition rootDefinition = null;
            HashSet<string> included;
            if (options.ScopeMode == ScopeMode.ExportRoot)
            {
                rootDefinition = (snapshot.ExportRoots ?? new List<ExportRootDefinition>()).FirstOrDefault(r => String.Equals(r.ObjectId, options.RootObjectId, StringComparison.OrdinalIgnoreCase));
                List<string> xSources = rootDefinition == null ? new List<string>() : (rootDefinition.SourceObjectIds ?? new List<string>());
                List<string> thallusSources = rootDefinition == null ? new List<string>() : (rootDefinition.ThallusIds ?? new List<string>());
                bool hasInvalidThallusSource = rootDefinition != null && (rootDefinition.InvalidThallusSourceIds ?? new List<string>()).Count > 0;
                if (xSources.Count > 0 && (thallusSources.Count > 0 || hasInvalidThallusSource))
                    throw new InvalidOperationException("This Lichen root has both X and T connected. Use X for an upstream closure or T for exact Thallus membership, not both on the same root.");
                if (hasInvalidThallusSource)
                    throw new InvalidOperationException(String.IsNullOrWhiteSpace(rootDefinition.ThallusRouteError)
                        ? "Lichen.T is connected to a source that is not a valid owned Thallus identity route. Repair or disconnect that source before exporting this root."
                        : rootDefinition.ThallusRouteError);
                if (thallusSources.Count > 0)
                {
                    thallusClosure = new ThallusScopeResolver().Resolve(snapshot, thallusSources, maximum);
                    included = new HashSet<string>(thallusClosure.IncludedObjectIds, StringComparer.OrdinalIgnoreCase);
                }
                else
                {
                    rootClosure = new ExportRootScopeResolver().Resolve(snapshot, options.RootObjectId, maximum);
                    included = new HashSet<string>(rootClosure.IncludedObjectIds, StringComparer.OrdinalIgnoreCase);
                    if (rootClosure.NodeLimitReached) AddLimitNote(notes, maximum);
                }
            }
            else included = ResolveScope(snapshot, options.ScopeMode, selectionSeeds, maximum, notes);

            ContextDocument document = new ContextDocument();
            document.Name = EmptyTo(snapshot.Name, "Untitled");
            document.RhinoVersion = EmptyTo(snapshot.RhinoVersion, "unknown");
            document.GrasshopperVersion = EmptyTo(snapshot.GrasshopperVersion, "unknown");
            document.Scope.Mode = thallusClosure == null ? ScopeName(options.ScopeMode) : "thallus_root";
            document.Scope.MaximumNodes = maximum;
            document.Scope.NodeLimitReached = notes.Any(n => n.IndexOf("node limit", StringComparison.OrdinalIgnoreCase) >= 0);
            document.Scope.SelectedObjectIds = originallySelected.OrderBy(s => s, StringComparer.OrdinalIgnoreCase).ToList();
            document.Scope.SelectedThallusIds = selectedThallusClosure == null ? new List<string>() : new List<string>(selectedThallusClosure.RootThallusIds);
            document.Scope.IncludedObjectIds = included.OrderBy(s => s, StringComparer.OrdinalIgnoreCase).ToList();
            if (options.ScopeMode == ScopeMode.ExportRoot)
            {
                document.Scope.RootLabel = !String.IsNullOrWhiteSpace(options.RootLabel) ? options.RootLabel.Trim() : (rootDefinition == null ? "Lichen" : rootDefinition.Label);
                if (thallusClosure != null) document.Scope.RootThallusIds = new List<string>(thallusClosure.RootThallusIds);
                else document.Scope.RootSourceObjectIds = (rootDefinition == null ? rootClosure.ContributingEdges.Where(e => String.Equals(e.TargetNodeId, options.RootObjectId, StringComparison.OrdinalIgnoreCase)).Select(e => e.SourceNodeId) : rootDefinition.SourceObjectIds)
                    .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(id => id, StringComparer.OrdinalIgnoreCase).ToList();
                document.Scope.SelectedObjectIds.Clear();
                document.Scope.SelectedThallusIds.Clear();
            }
            document.UserContext.Purpose = options.Purpose ?? "";
            document.UserContext.RequestedTask = options.RequestedTask ?? "";
            document.UserContext.Constraints = options.Constraints ?? "";
            document.ExtractionNotes.AddRange(notes);

            foreach (ContextNode source in snapshot.Nodes.Where(n => included.Contains(n.InstanceId)).OrderBy(NodeSortKey, StringComparer.OrdinalIgnoreCase))
            {
                ContextNode node = CloneNode(source);
                node.OriginallySelected = originallySelected.Contains(node.InstanceId);
                string clusterPurpose;
                if (node.ClusterGraph != null && options.ClusterPurposeNotes != null && options.ClusterPurposeNotes.TryGetValue(node.InstanceId, out clusterPurpose) && !String.IsNullOrWhiteSpace(clusterPurpose))
                    node.ClusterGraph.UserProvidedPurpose = clusterPurpose.Trim();
                ApplyContentOptions(node, options);
                SortNode(node);
                document.Nodes.Add(node);
            }

            BuildEdgesAndBoundaries(snapshot.Edges, included, document, snapshot.Nodes, options.ScopeMode != ScopeMode.ExportRoot || thallusClosure != null);
            document.Groups = (snapshot.Groups ?? new List<ContextGroup>())
                .Where(g => g.MemberIds.Any(included.Contains)).Select(CloneGroup).OrderBy(g => g.InstanceId, StringComparer.OrdinalIgnoreCase).ToList();
            ThallusClosure organizationClosure = thallusClosure ?? selectedThallusClosure;
            if (organizationClosure != null)
            {
                HashSet<string> relevant = new HashSet<string>(organizationClosure.IncludedThallusIds, StringComparer.OrdinalIgnoreCase);
                document.Thalli = (snapshot.Thalli ?? new List<ContextThallus>()).Where(t => relevant.Contains(t.InstanceId)).Select(t => CloneThallus(t, organizationClosure))
                    .OrderBy(t => t.InstanceId, StringComparer.OrdinalIgnoreCase).ToList();
                foreach (ContextThallus thallus in document.Thalli)
                    if (thallus.MissingMemberIds.Count > 0) document.ExtractionNotes.Add("Thallus “" + EmptyTo(thallus.Name, "Thallus") + "” references " + thallus.MissingMemberIds.Count + " missing object" + (thallus.MissingMemberIds.Count == 1 ? "." : "s."));
            }
            BuildClusterBlackBoxSummaries(document);
            BuildDependencies(document);
            document.Analysis = new ComponentSemanticsService().Analyze(document);
            document.Analysis.ExecutionSemantics = new ExecutionSemanticsAnalyzer().Analyze(document);
            return document;
        }

        private static HashSet<string> ResolveScope(ContextSnapshot snapshot, ScopeMode mode, HashSet<string> selected, int maximum, List<string> notes)
        {
            HashSet<string> included = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            IEnumerable<string> initial = mode == ScopeMode.EntireDocument
                ? snapshot.Nodes.Where(n => !IsLichenInfrastructure(n)).Select(n => n.InstanceId).OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
                : selected.OrderBy(s => s, StringComparer.OrdinalIgnoreCase);
            AddBounded(initial, included, maximum, notes);
            if (mode == ScopeMode.SelectedOnly || mode == ScopeMode.EntireDocument || included.Count >= maximum) return included;

            Dictionary<string, List<string>> upstream = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (ContextEdge edge in snapshot.Edges)
            {
                List<string> sources;
                if (!upstream.TryGetValue(edge.TargetNodeId, out sources)) { sources = new List<string>(); upstream.Add(edge.TargetNodeId, sources); }
                if (!sources.Contains(edge.SourceNodeId, StringComparer.OrdinalIgnoreCase)) sources.Add(edge.SourceNodeId);
            }
            foreach (List<string> values in upstream.Values) values.Sort(StringComparer.OrdinalIgnoreCase);

            if (mode == ScopeMode.SelectedPlusImmediateUpstream)
            {
                List<string> direct = new List<string>();
                foreach (string id in selected.OrderBy(s => s, StringComparer.OrdinalIgnoreCase))
                {
                    List<string> sources;
                    if (upstream.TryGetValue(id, out sources)) direct.AddRange(sources);
                }
                AddBounded(direct.Distinct(StringComparer.OrdinalIgnoreCase), included, maximum, notes);
                return included;
            }

            Queue<string> pending = new Queue<string>(included.OrderBy(s => s, StringComparer.OrdinalIgnoreCase));
            while (pending.Count > 0 && included.Count < maximum)
            {
                string current = pending.Dequeue();
                List<string> sources;
                if (!upstream.TryGetValue(current, out sources)) continue;
                foreach (string source in sources)
                {
                    if (included.Count >= maximum) { AddLimitNote(notes, maximum); break; }
                    if (included.Add(source)) pending.Enqueue(source);
                }
            }
            if (pending.Count > 0) AddLimitNote(notes, maximum);
            return included;
        }

        private static void AddBounded(IEnumerable<string> ids, HashSet<string> included, int maximum, List<string> notes)
        {
            foreach (string id in ids)
            {
                if (included.Count >= maximum) { AddLimitNote(notes, maximum); break; }
                included.Add(id);
            }
        }

        private static bool IsLichenInfrastructure(ContextNode node)
        {
            if (node == null) return false;
            Guid typeId;
            if (!Guid.TryParse(node.TypeId, out typeId)) return false;
            return typeId == LichenComponentIds.ExportRoot || typeId == LichenComponentIds.Thallus || typeId == LichenComponentIds.ThallusEndpoint;
        }

        private static void AddLimitNote(List<string> notes, int maximum)
        {
            string note = "The configured node limit of " + maximum + " was reached; the export scope was truncated deterministically.";
            if (!notes.Contains(note)) notes.Add(note);
        }

        private static void BuildEdgesAndBoundaries(IEnumerable<ContextEdge> edges, HashSet<string> included, ContextDocument document, IEnumerable<ContextNode> allNodes, bool includeBoundaries)
        {
            Dictionary<string, ContextNode> nodes = allNodes.ToDictionary(n => n.InstanceId, StringComparer.OrdinalIgnoreCase);
            List<ContextEdge> sorted = edges.OrderBy(EdgeSortKey, StringComparer.OrdinalIgnoreCase).ToList();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (ContextEdge sourceEdge in sorted)
            {
                ContextEdge edge = CloneEdge(sourceEdge);
                bool sourceInside = included.Contains(edge.SourceNodeId);
                bool targetInside = included.Contains(edge.TargetNodeId);
                if (!sourceInside && !targetInside) continue;
                if (!includeBoundaries && (!sourceInside || !targetInside)) continue;
                string key = EdgeSortKey(edge);
                if (!seen.Add(key)) continue;
                edge.CrossesScopeBoundary = sourceInside != targetInside;
                edge.BoundaryStatus = !sourceInside ? "incoming" : (!targetInside ? "outgoing" : "internal");
                document.Edges.Add(edge);
                if (!sourceInside && targetInside)
                {
                    document.BoundaryInputs.Add(new ContextBoundaryPort { Direction = "input", ExternalNodeId = edge.SourceNodeId, ExternalNodeName = NodeDisplayName(nodes, edge.SourceNodeId), ExternalParameterName = edge.SourceParameterName, InternalNodeId = edge.TargetNodeId, InternalNodeName = NodeDisplayName(nodes, edge.TargetNodeId), InternalParameterName = edge.TargetParameterName, ParameterIndex = edge.TargetParameterIndex, ParameterName = edge.TargetParameterName });
                }
                else if (sourceInside && !targetInside)
                {
                    document.BoundaryOutputs.Add(new ContextBoundaryPort { Direction = "output", ExternalNodeId = edge.TargetNodeId, ExternalNodeName = NodeDisplayName(nodes, edge.TargetNodeId), ExternalParameterName = edge.TargetParameterName, InternalNodeId = edge.SourceNodeId, InternalNodeName = NodeDisplayName(nodes, edge.SourceNodeId), InternalParameterName = edge.SourceParameterName, ParameterIndex = edge.SourceParameterIndex, ParameterName = edge.SourceParameterName });
                }
            }
            document.BoundaryInputs = document.BoundaryInputs.OrderBy(BoundarySortKey, StringComparer.OrdinalIgnoreCase).ToList();
            document.BoundaryOutputs = document.BoundaryOutputs.OrderBy(BoundarySortKey, StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static void BuildDependencies(ContextDocument document)
        {
            Dictionary<string, ContextDependency> values = new Dictionary<string, ContextDependency>(StringComparer.OrdinalIgnoreCase);
            foreach (ContextNode node in DescendantNodes(document.Nodes))
            {
                string name = EmptyTo(node.AssemblyName, "unknown");
                string version = EmptyTo(node.AssemblyVersion, "unknown");
                string key = name + "|" + version;
                if (!values.ContainsKey(key)) values.Add(key, new ContextDependency { Name = name, Version = version, Kind = IsNativeAssembly(name) ? "grasshopper_native" : "third_party" });
            }
            document.Dependencies = values.Values.OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase).ThenBy(d => d.Version, StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static void BuildClusterBlackBoxSummaries(ContextDocument document)
        {
            Dictionary<string, ContextNode> nodes = document.Nodes.ToDictionary(n => n.InstanceId, StringComparer.OrdinalIgnoreCase);
            foreach (ContextNode cluster in document.Nodes.Where(n => n.ClusterGraph != null && !String.Equals(n.ClusterGraph.InspectionStatus, "inspected", StringComparison.OrdinalIgnoreCase)))
            {
                List<string> facts = new List<string>();
                string description = CleanOneLine(cluster.Description, 220);
                if (!String.IsNullOrWhiteSpace(description)) facts.Add("Component metadata: " + description + ".");

                List<string> inputs = cluster.Inputs.OrderBy(p => p.Index).Select(DescribePort).ToList();
                List<string> outputs = cluster.Outputs.OrderBy(p => p.Index).Select(DescribePort).ToList();
                facts.Add(inputs.Count == 0 ? "It exposes no inputs." : "Exposed inputs: " + JoinLimited(inputs, 8) + ".");
                facts.Add(outputs.Count == 0 ? "It exposes no outputs." : "Exposed outputs: " + JoinLimited(outputs, 8) + ".");

                List<string> incoming = new List<string>(); List<string> outgoing = new List<string>();
                foreach (ContextEdge edge in document.Edges.OrderBy(EdgeSortKey, StringComparer.OrdinalIgnoreCase))
                {
                    ContextNode related;
                    if (String.Equals(edge.TargetNodeId, cluster.InstanceId, StringComparison.OrdinalIgnoreCase) && nodes.TryGetValue(edge.SourceNodeId, out related))
                        incoming.Add(NodeDisplayName(nodes, related.InstanceId) + "." + EmptyTo(edge.SourceParameterName, "output") + " → " + EmptyTo(edge.TargetParameterName, "input"));
                    if (String.Equals(edge.SourceNodeId, cluster.InstanceId, StringComparison.OrdinalIgnoreCase) && nodes.TryGetValue(edge.TargetNodeId, out related))
                        outgoing.Add(EmptyTo(edge.SourceParameterName, "output") + " → " + NodeDisplayName(nodes, related.InstanceId) + "." + EmptyTo(edge.TargetParameterName, "input"));
                }
                foreach (ContextBoundaryPort boundary in document.BoundaryInputs.Where(b => String.Equals(b.InternalNodeId, cluster.InstanceId, StringComparison.OrdinalIgnoreCase)))
                    incoming.Add(EmptyTo(boundary.ExternalNodeName, boundary.ExternalNodeId) + "." + EmptyTo(boundary.ExternalParameterName, "output") + " → " + EmptyTo(boundary.InternalParameterName, "input"));
                foreach (ContextBoundaryPort boundary in document.BoundaryOutputs.Where(b => String.Equals(b.InternalNodeId, cluster.InstanceId, StringComparison.OrdinalIgnoreCase)))
                    outgoing.Add(EmptyTo(boundary.InternalParameterName, "output") + " → " + EmptyTo(boundary.ExternalNodeName, boundary.ExternalNodeId) + "." + EmptyTo(boundary.ExternalParameterName, "input"));
                if (incoming.Count > 0) facts.Add("Receives outer-graph data through " + JoinLimited(incoming.Distinct(StringComparer.OrdinalIgnoreCase).ToList(), 8) + ".");
                if (outgoing.Count > 0) facts.Add("Sends outer-graph results through " + JoinLimited(outgoing.Distinct(StringComparer.OrdinalIgnoreCase).ToList(), 8) + ".");

                List<string> runtime = cluster.Outputs.Where(p => !String.IsNullOrWhiteSpace(p.RuntimeDataSummary))
                    .OrderBy(p => p.Index).Select(p => EmptyTo(p.Nickname, EmptyTo(p.Name, "output")) + "=" + p.RuntimeDataSummary).ToList();
                if (runtime.Count > 0) facts.Add("Already-computed output summaries: " + JoinLimited(runtime, 8) + ".");
                facts.Add("These observations describe only the protected cluster's visible interface and context, not its hidden implementation.");
                cluster.ClusterGraph.BlackBoxSummary = String.Join(" ", facts.ToArray());
            }
        }

        private static string DescribePort(ContextParameter parameter)
        {
            string name = EmptyTo(parameter.Nickname, EmptyTo(parameter.Name, "unnamed"));
            List<string> details = new List<string>();
            if (!String.IsNullOrWhiteSpace(parameter.AccessMode)) details.Add(parameter.AccessMode);
            if (!String.IsNullOrWhiteSpace(parameter.TypeHint)) details.Add(parameter.TypeHint);
            if (parameter.Optional) details.Add("optional");
            return details.Count == 0 ? name : name + " (" + String.Join(", ", details.ToArray()) + ")";
        }

        private static string JoinLimited(List<string> values, int maximum)
        {
            if (values.Count <= maximum) return String.Join(", ", values.ToArray());
            return String.Join(", ", values.Take(maximum).ToArray()) + ", and " + (values.Count - maximum) + " more";
        }

        private static string CleanOneLine(string value, int maximum)
        {
            string clean = Regex.Replace((value ?? "").Replace("\r", " ").Replace("\n", " ").Trim(), @"\s+", " ");
            return clean.Length <= maximum ? clean.TrimEnd('.', ';', ':') : clean.Substring(0, maximum - 1).TrimEnd() + "…";
        }

        private static IEnumerable<ContextNode> DescendantNodes(IEnumerable<ContextNode> nodes)
        {
            foreach (ContextNode node in nodes ?? new List<ContextNode>())
            {
                yield return node;
                if (node.ClusterGraph == null) continue;
                foreach (ContextNode nested in DescendantNodes(node.ClusterGraph.Nodes)) yield return nested;
            }
        }

        public static List<ContextNode> TopologicalOrder(ContextDocument document)
        {
            Dictionary<string, ContextNode> nodes = document.Nodes.ToDictionary(n => n.InstanceId, StringComparer.OrdinalIgnoreCase);
            Dictionary<string, int> degree = nodes.Keys.ToDictionary(k => k, k => 0, StringComparer.OrdinalIgnoreCase);
            Dictionary<string, List<string>> next = nodes.Keys.ToDictionary(k => k, k => new List<string>(), StringComparer.OrdinalIgnoreCase);
            foreach (ContextEdge edge in document.Edges.Where(e => e.BoundaryStatus == "internal"))
            {
                if (!nodes.ContainsKey(edge.SourceNodeId) || !nodes.ContainsKey(edge.TargetNodeId)) continue;
                next[edge.SourceNodeId].Add(edge.TargetNodeId); degree[edge.TargetNodeId]++;
            }
            SortedSet<string> ready = new SortedSet<string>(degree.Where(p => p.Value == 0).Select(p => p.Key), StringComparer.OrdinalIgnoreCase);
            List<ContextNode> result = new List<ContextNode>();
            while (ready.Count > 0)
            {
                string id = ready.Min; ready.Remove(id); result.Add(nodes[id]);
                foreach (string target in next[id].OrderBy(s => s, StringComparer.OrdinalIgnoreCase)) { degree[target]--; if (degree[target] == 0) ready.Add(target); }
            }
            foreach (string id in nodes.Keys.Except(result.Select(n => n.InstanceId), StringComparer.OrdinalIgnoreCase).OrderBy(s => s, StringComparer.OrdinalIgnoreCase)) result.Add(nodes[id]);
            return result;
        }

        private static void SortNode(ContextNode node)
        {
            node.Inputs = node.Inputs.OrderBy(p => p.Index).ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList();
            node.Outputs = node.Outputs.OrderBy(p => p.Index).ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList();
            node.RuntimeMessages = node.RuntimeMessages.OrderBy(m => m.Level, StringComparer.OrdinalIgnoreCase).ThenBy(m => m.Message, StringComparer.OrdinalIgnoreCase).ToList();
            node.GroupIds = node.GroupIds.OrderBy(s => s, StringComparer.OrdinalIgnoreCase).ToList();
            if (node.ExecutionMetadata != null) node.ExecutionMetadata = node.ExecutionMetadata.OrderBy(m => m.Key, StringComparer.OrdinalIgnoreCase).ThenBy(m => m.Value, StringComparer.OrdinalIgnoreCase).ToList();
            if (node.ControlLinks != null) node.ControlLinks = node.ControlLinks.OrderBy(l => l.Role, StringComparer.OrdinalIgnoreCase).ThenBy(l => l.TargetNodeId, StringComparer.OrdinalIgnoreCase).ToList();
            if (node.ClusterGraph != null)
            {
                foreach (ContextNode nested in node.ClusterGraph.Nodes) SortNode(nested);
                node.ClusterGraph.Nodes = node.ClusterGraph.Nodes.OrderBy(NodeSortKey, StringComparer.OrdinalIgnoreCase).ToList();
                node.ClusterGraph.Edges = node.ClusterGraph.Edges.OrderBy(EdgeSortKey, StringComparer.OrdinalIgnoreCase).ToList();
                node.ClusterGraph.Groups = node.ClusterGraph.Groups.OrderBy(g => g.InstanceId, StringComparer.OrdinalIgnoreCase).ToList();
                node.ClusterGraph.Dependencies = node.ClusterGraph.Dependencies.OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase).ThenBy(d => d.Version, StringComparer.OrdinalIgnoreCase).ToList();
                node.ClusterGraph.ExtractionNotes = node.ClusterGraph.ExtractionNotes.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
            }
        }

        private static ContextNode CloneNode(ContextNode source)
        {
            ContextNode node = new ContextNode
            {
                InstanceId = source.InstanceId, TypeId = source.TypeId, Name = source.Name, Nickname = source.Nickname,
                Description = source.Description, Category = source.Category, Subcategory = source.Subcategory,
                AssemblyName = source.AssemblyName, AssemblyVersion = source.AssemblyVersion, PluginName = source.PluginName,
                RuntimeTypeName = source.RuntimeTypeName, OriginallySelected = source.OriginallySelected,
                CanvasBounds = source.CanvasBounds, PersistentValueSummary = source.PersistentValueSummary,
                State = source.State == null ? new ContextNodeState() : new ContextNodeState
                {
                    Enabled = source.State.Enabled, Locked = source.State.Locked, Hidden = source.State.Hidden,
                    PreviewCapable = source.State.PreviewCapable
                }
            };
            node.GroupIds = new List<string>(source.GroupIds ?? new List<string>());
            node.Inputs = (source.Inputs ?? new List<ContextParameter>()).Select(CloneParameter).ToList();
            node.Outputs = (source.Outputs ?? new List<ContextParameter>()).Select(CloneParameter).ToList();
            node.RuntimeMessages = (source.RuntimeMessages ?? new List<ContextRuntimeMessage>()).Select(m => new ContextRuntimeMessage { Level = m.Level, Message = m.Message }).ToList();
            if (source.Script != null) node.Script = new ContextScript { Language = source.Script.Language, Source = source.Script.Source, ExtractionNote = source.Script.ExtractionNote };
            if (source.ExecutionMetadata != null) node.ExecutionMetadata = source.ExecutionMetadata.Select(m => new ContextMetadataEntry { Key = m.Key, Value = m.Value }).ToList();
            if (source.ControlLinks != null) node.ControlLinks = source.ControlLinks.Select(l => new ContextControlLink { Role = l.Role, TargetNodeId = l.TargetNodeId }).ToList();
            if (source.ClusterGraph != null) node.ClusterGraph = CloneClusterGraph(source.ClusterGraph);
            return node;
        }

        private static ContextClusterGraph CloneClusterGraph(ContextClusterGraph source)
        {
            ContextClusterGraph graph = new ContextClusterGraph
            {
                InspectionStatus = source.InspectionStatus, InspectionNote = source.InspectionNote,
                UserProvidedPurpose = source.UserProvidedPurpose, BlackBoxSummary = source.BlackBoxSummary,
                DocumentId = source.DocumentId, NodeLimitReached = source.NodeLimitReached,
                Nodes = (source.Nodes ?? new List<ContextNode>()).Select(CloneNode).ToList(),
                Edges = (source.Edges ?? new List<ContextEdge>()).Select(CloneEdge).ToList(),
                Groups = (source.Groups ?? new List<ContextGroup>()).Select(CloneGroup).ToList(),
                Dependencies = (source.Dependencies ?? new List<ContextDependency>()).Select(d => new ContextDependency { Name = d.Name, Version = d.Version, Kind = d.Kind }).ToList(),
                Analysis = CloneAnalysis(source.Analysis),
                ExtractionNotes = new List<string>(source.ExtractionNotes ?? new List<string>())
            };
            return graph;
        }

        private static ContextAnalysis CloneAnalysis(ContextAnalysis source)
        {
            if (source == null) return new ContextAnalysis();
            ContextAnalysis analysis = new ContextAnalysis
            {
                InferredPurpose = source.InferredPurpose,
                DetectedOperations = new List<string>(source.DetectedOperations ?? new List<string>()),
                DetectedPatterns = new List<string>(source.DetectedPatterns ?? new List<string>()),
                Uncertainties = new List<string>(source.Uncertainties ?? new List<string>()),
                ExecutionSemantics = CloneExecutionSemantics(source.ExecutionSemantics)
            };
            return analysis;
        }

        private static ContextExecutionSemantics CloneExecutionSemantics(ContextExecutionSemantics source)
        {
            if (source == null) return new ContextExecutionSemantics();
            return new ContextExecutionSemantics
            {
                HasNonLinearBehavior = source.HasNonLinearBehavior,
                OrdinaryWireGraphHasCycle = source.OrdinaryWireGraphHasCycle,
                Regions = (source.Regions ?? new List<ContextExecutionRegion>()).Select(r => new ContextExecutionRegion
                {
                    Kind = r.Kind, Label = r.Label, StartNodeId = r.StartNodeId, EndNodeId = r.EndNodeId,
                    NestingLevel = r.NestingLevel, IterationLimit = r.IterationLimit,
                    CarriedValues = new List<string>(r.CarriedValues ?? new List<string>()),
                    NodeIds = new List<string>(r.NodeIds ?? new List<string>()), Evidence = new List<string>(r.Evidence ?? new List<string>())
                }).ToList(),
                Components = (source.Components ?? new List<ContextExecutionComponent>()).Select(c => new ContextExecutionComponent
                {
                    NodeId = c.NodeId, NodeName = c.NodeName, Kind = c.Kind, Behavior = c.Behavior,
                    Evidence = new List<string>(c.Evidence ?? new List<string>())
                }).ToList(),
                Notes = new List<string>(source.Notes ?? new List<string>())
            };
        }

        private static void ApplyContentOptions(ContextNode node, ContextExportOptions options)
        {
            if (IsDefaultPanelPrompt(node)) node.PersistentValueSummary = "";
            if (!options.IncludeRuntimeSummary)
            {
                node.RuntimeMessages.Clear();
                foreach (ContextParameter parameter in node.Inputs.Concat(node.Outputs))
                {
                    parameter.RuntimeDataSummary = "";
                    parameter.RuntimeTreeShape = "";
                }
            }
            if (!options.IncludeScriptSource && node.Script != null) node.Script.Source = "";
            if (node.ClusterGraph != null)
                foreach (ContextNode nested in node.ClusterGraph.Nodes) ApplyContentOptions(nested, options);
        }

        private static bool IsDefaultPanelPrompt(ContextNode node)
        {
            if (node == null || !String.Equals(node.Name, "Panel", StringComparison.OrdinalIgnoreCase) || String.IsNullOrWhiteSpace(node.PersistentValueSummary)) return false;
            string summary = node.PersistentValueSummary.Trim();
            if (!summary.StartsWith("text=", StringComparison.OrdinalIgnoreCase)) return false;
            string value = summary.Substring(5).Trim().Replace("…", "...");
            return Regex.IsMatch(value, @"^Double[- ]click to edit panel content\.{3}$", RegexOptions.IgnoreCase);
        }

        private static ContextParameter CloneParameter(ContextParameter source)
        {
            return new ContextParameter
            {
                Index = source.Index, Name = source.Name, Nickname = source.Nickname, Description = source.Description,
                Direction = source.Direction, AccessMode = source.AccessMode, Optional = source.Optional, TypeHint = source.TypeHint,
                SourceCount = source.SourceCount, RecipientCount = source.RecipientCount,
                PersistentDataSummary = source.PersistentDataSummary, RuntimeDataSummary = source.RuntimeDataSummary, RuntimeTreeShape = source.RuntimeTreeShape,
                Expression = source.Expression, Flatten = source.Flatten, Graft = source.Graft,
                Simplify = source.Simplify, Reverse = source.Reverse
            };
        }

        private static ContextGroup CloneGroup(ContextGroup source)
        {
            return new ContextGroup
            {
                InstanceId = source.InstanceId,
                Name = source.Name,
                MemberIds = (source.MemberIds ?? new List<string>()).OrderBy(id => id, StringComparer.OrdinalIgnoreCase).ToList()
            };
        }

        private static ContextThallus CloneThallus(ContextThallus source, ThallusClosure closure)
        {
            List<string> effective;
            closure.EffectiveMemberIds.TryGetValue(source.InstanceId, out effective);
            return new ContextThallus
            {
                InstanceId = source.InstanceId,
                Name = source.Name,
                Description = source.Description,
                ParentThallusId = !String.IsNullOrWhiteSpace(source.ParentThallusId) && closure.IncludedThallusIds.Contains(source.ParentThallusId, StringComparer.OrdinalIgnoreCase)
                    ? source.ParentThallusId : null,
                DirectMemberIds = (source.DirectMemberIds ?? new List<string>()).OrderBy(id => id, StringComparer.OrdinalIgnoreCase).ToList(),
                EffectiveMemberIds = new List<string>(effective ?? new List<string>()),
                MissingMemberIds = (source.MissingMemberIds ?? new List<string>()).OrderBy(id => id, StringComparer.OrdinalIgnoreCase).ToList(),
                Properties = (source.Properties ?? new List<ContextMetadataEntry>()).Select(p => new ContextMetadataEntry { Key = p.Key, Value = p.Value })
                    .OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase).ThenBy(p => p.Value, StringComparer.Ordinal).ToList()
            };
        }

        private static ContextEdge CloneEdge(ContextEdge source)
        {
            return new ContextEdge
            {
                SourceNodeId = source.SourceNodeId, SourceParameterIndex = source.SourceParameterIndex,
                SourceParameterName = source.SourceParameterName, TargetNodeId = source.TargetNodeId,
                TargetParameterIndex = source.TargetParameterIndex, TargetParameterName = source.TargetParameterName,
                CrossesScopeBoundary = source.CrossesScopeBoundary, BoundaryStatus = source.BoundaryStatus
            };
        }

        private static string ScopeName(ScopeMode mode)
        {
            switch (mode) { case ScopeMode.SelectedPlusImmediateUpstream: return "selected_plus_immediate_upstream"; case ScopeMode.SelectedPlusAllUpstream: return "selected_plus_all_upstream"; case ScopeMode.EntireDocument: return "entire_document"; case ScopeMode.ExportRoot: return "export_root"; default: return "selected_only"; }
        }
        private static string NodeSortKey(ContextNode node) { return node.InstanceId + "|" + node.Name; }
        private static string EdgeSortKey(ContextEdge edge) { return edge.SourceNodeId + "|" + edge.SourceParameterIndex.ToString("D6") + "|" + edge.TargetNodeId + "|" + edge.TargetParameterIndex.ToString("D6"); }
        private static string BoundarySortKey(ContextBoundaryPort port) { return port.InternalNodeId + "|" + port.ParameterIndex.ToString("D6") + "|" + port.ExternalNodeId; }
        private static string NodeDisplayName(Dictionary<string, ContextNode> nodes, string id) { ContextNode node; return nodes.TryGetValue(id, out node) ? EmptyTo(node.Nickname, EmptyTo(node.Name, id)) : id; }
        private static bool IsNativeAssembly(string name)
        {
            string[] native = {
                "Grasshopper", "CurveComponents", "FieldComponents", "IOComponents", "MathComponents", "MeshComponents",
                "SurfaceComponents", "TriangulationComponents", "VectorComponents", "XformComponents", "TransformComponents",
                "IntersectComponents", "GalapagosComponents", "RhinoCodePluginGH", "ScriptComponents", "GhPython",
                "Kangaroo2Component", "KangarooSolver"
            };
            return native.Contains(name, StringComparer.OrdinalIgnoreCase) || name.StartsWith("Grasshopper", StringComparison.OrdinalIgnoreCase);
        }
        private static string EmptyTo(string value, string fallback) { return String.IsNullOrWhiteSpace(value) ? fallback : value; }
    }

    public sealed class ComponentSemanticsService
    {
        private readonly Dictionary<string, string> descriptions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "Divide Domain²", "divides a two-dimensional domain" }, { "Divide Domain2", "divides a two-dimensional domain" },
            { "Isotrim", "extracts subsurfaces using UV subdomains" }, { "Area", "calculates area and centroid" },
            { "Evaluate Surface", "evaluates a surface at UV coordinates" }, { "Distance", "measures distance" },
            { "Bounds", "calculates a numeric domain enclosing the input values" }, { "Remap Numbers", "remaps numeric values between domains" },
            { "Amplitude", "sets vector magnitude" }, { "Move", "translates geometry by a motion vector" },
            { "Series", "creates an arithmetic sequence" }, { "Range", "creates evenly spaced values over a domain" },
            { "List Item", "selects indexed list items" }, { "Partition List", "partitions a list into chunks" },
            { "Graft Tree", "grafts data-tree paths" }, { "Flatten Tree", "flattens a data tree" },
            { "Entwine", "combines data into separate branches" }, { "Merge", "merges multiple data streams" },
            { "Loft", "creates a loft through section curves" }, { "Extrude", "extrudes geometry along a direction" },
            { "Offset", "offsets geometry" }, { "Join Curves", "joins compatible curves" }, { "Join Brep", "joins compatible Breps" },
            { "Mesh Join", "joins meshes" }, { "Unit X", "creates a unit vector along the X axis" },
            { "Unit Y", "creates a unit vector along the Y axis" }, { "Unit Z", "creates a unit vector along the Z axis" }
            ,{ "Deconstruct Brep", "deconstructs a Brep into faces, edges, and vertices" }, { "Length", "measures curve length" }
            ,{ "Division", "divides one numeric value by another" }, { "Quad Panels", "creates quadrangular panels on a surface" }
            ,{ "Divide Domain", "divides a numeric domain into equal segments" }, { "Divide Surface", "generates a UV grid of points on a surface" }
            ,{ "Shift Paths", "shifts data-tree path indices" }, { "Surface Closest Point", "finds surface UV coordinates closest to input points" }
            ,{ "Image Sampler", "samples image values at input coordinates" }, { "Average", "calculates the arithmetic mean" }
            ,{ "Graph Mapper", "maps numeric values through a user-defined graph function" }, { "Includes", "tests whether values lie inside a numeric domain" }
            ,{ "Cull Pattern", "removes list items using a repeating Boolean pattern" }
            ,{ "Button", "provides a manual Boolean trigger" }, { "Stream Freeze / Gate", "gates downstream data flow and may retain the last received value while closed" }
            ,{ "Loop Start", "starts an Anemone iterative region" }, { "Loop End", "ends an Anemone iterative region and controls repetition" }
            ,{ "Fast Loop Start", "starts a bounded Anemone fast-loop region" }, { "Fast Loop End", "ends an Anemone fast-loop region" }
        };

        private readonly HashSet<string> passiveNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Scribble", "Panel", "Number Slider", "Integer Slider", "Boolean Toggle", "Value List", "Relay",
            "Number", "Integer", "Boolean", "Text", "Data", "Geometry", "Brep", "Surface", "Curve", "Point", "Vector", "Colour"
        };

        public ContextAnalysis Analyze(ContextDocument document)
        {
            ContextAnalysis analysis = new ContextAnalysis();
            List<ContextNode> active = ContextGraphService.TopologicalOrder(document).Where(n => !passiveNames.Contains(n.Name) && !IsCanvasGroup(n)).ToList();
            FunctionalEvidenceSet functionalEvidence = FunctionalEvidenceAnalyzer.Analyze(document, active);
            HashSet<string> consumed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (ContainsAll(active, "Deconstruct Brep", "Length", "Division", "Quad Panels"))
            {
                analysis.DetectedOperations.Add("Surface boundary geometry is deconstructed and measured to derive panel-division values.");
                Consume(active, consumed, "Deconstruct Brep", "List Item", "Length", "Division");
            }
            if (Has(active, "Quad Panels"))
            {
                analysis.DetectedOperations.Add("Quad Panels creates quadrilateral panels on the input surface.");
                Consume(active, consumed, "Quad Panels");
            }
            if (ContainsAll(active, "Divide Surface", "Surface Closest Point", "Image Sampler"))
            {
                analysis.DetectedOperations.Add("Points are generated across the panel surfaces, mapped to surface UV coordinates, and evaluated with an image sampler.");
                Consume(active, consumed, "Divide Surface", "Shift Paths", "Surface Closest Point", "Image Sampler");
            }
            if (ContainsAll(active, "Remap Numbers", "Cull Pattern"))
            {
                analysis.DetectedOperations.Add("Sampled numeric values are averaged, remapped, shaped through a graph function, and converted into a panel-culling pattern.");
                Consume(active, consumed, "Average", "Divide Domain", "Remap Numbers", "Graph Mapper", "Includes", "Cull Pattern");
            }
            if (Has(active, "Area"))
            {
                analysis.DetectedOperations.Add("Area calculates area and centroid values for geometry in the selected scope.");
                Consume(active, consumed, "Area");
            }

            foreach (ContextNode node in active.Where(n => !consumed.Contains(n.InstanceId)))
                analysis.DetectedOperations.Add(OperationFor(node, analysis));

            foreach (FunctionalEvidence evidence in functionalEvidence.Items)
                analysis.DetectedPatterns.Add((evidence.ReachesCapturedOutput ? "Supported functional inference: " : "Supported auxiliary-branch inference: ") + evidence.Explanation);

            List<ScriptBehaviorSummary> scriptEvidence = active.Where(n => n.Script != null).Select(ScriptBehaviorAnalyzer.Analyze).ToList();
            List<string> scriptRoles = scriptEvidence.Select(s => s.PossibleRole).Where(r => !String.IsNullOrWhiteSpace(r))
                .Distinct(StringComparer.OrdinalIgnoreCase).Take(4).ToList();
            List<string> scriptDescriptions = scriptEvidence.Select(s => BoundedEvidence(s.AuthorDescription, 180)).Where(d => !String.IsNullOrWhiteSpace(d))
                .Distinct(StringComparer.OrdinalIgnoreCase).Take(3).ToList();
            if (scriptRoles.Count > 0) analysis.DetectedPatterns.Add("Recognized script role: " + String.Join("; ", scriptRoles.ToArray()) + ".");

            List<string> clusterPurposes = active.Where(n => n.ClusterGraph != null && String.Equals(n.ClusterGraph.InspectionStatus, "inspected", StringComparison.OrdinalIgnoreCase))
                .Select(n => n.ClusterGraph.Analysis == null ? "" : n.ClusterGraph.Analysis.InferredPurpose)
                .Where(p => !String.IsNullOrWhiteSpace(p) && p.IndexOf("cannot be determined", StringComparison.OrdinalIgnoreCase) < 0)
                .Select(NormalizeClusterPurpose).Distinct(StringComparer.OrdinalIgnoreCase).Take(3).ToList();
            bool optimization = active.Any(n => (n.RuntimeTypeName ?? "").IndexOf("Galapagos", StringComparison.OrdinalIgnoreCase) >= 0
                || (n.ControlLinks ?? new List<ContextControlLink>()).Any(l => l.Role == "genome" || l.Role == "fitness"));
            analysis.InferredPurpose = SynthesizePurpose(active, scriptDescriptions, scriptRoles, clusterPurposes, optimization, functionalEvidence);
            List<ContextThallus> thalli = (document.Thalli ?? new List<ContextThallus>()).Where(t => t != null)
                .OrderBy(t => t.InstanceId, StringComparer.OrdinalIgnoreCase).ToList();
            HashSet<string> duplicateNames = new HashSet<string>(thalli.Where(t => !String.IsNullOrWhiteSpace(t.Name))
                .GroupBy(t => t.Name.Trim(), StringComparer.OrdinalIgnoreCase).Where(group => group.Count() > 1).Select(group => group.Key), StringComparer.OrdinalIgnoreCase);
            List<string> thallusEvidence = thalli.Select(t => ThallusPurposeEvidence(t, duplicateNames))
                .Where(value => !String.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase).Take(4).ToList();
            if (thallusEvidence.Count > 0) analysis.InferredPurpose += " User-provided Thallus context adds: " + NaturalJoin(thallusEvidence) + ".";
            return analysis;
        }

        private static string ThallusPurposeEvidence(ContextThallus thallus, HashSet<string> duplicateNames)
        {
            if (thallus == null) return "";
            List<string> values = new List<string>();
            if (!String.IsNullOrWhiteSpace(thallus.Description)) values.Add(BoundedEvidence(thallus.Description, 180));
            foreach (ContextMetadataEntry property in (thallus.Properties ?? new List<ContextMetadataEntry>()).Where(p => p != null && !String.IsNullOrWhiteSpace(p.Key) && !String.IsNullOrWhiteSpace(p.Value)))
            {
                string key = property.Key.Trim();
                if (!String.Equals(key, "purpose", StringComparison.OrdinalIgnoreCase) && !String.Equals(key, "role", StringComparison.OrdinalIgnoreCase)
                    && !String.Equals(key, "stage", StringComparison.OrdinalIgnoreCase) && !String.Equals(key, "discipline", StringComparison.OrdinalIgnoreCase)) continue;
                values.Add(key + "=" + BoundedEvidence(property.Value, 100));
            }
            if (values.Count == 0) return "";
            string name = String.IsNullOrWhiteSpace(thallus.Name) ? "Thallus" : thallus.Name.Trim();
            if (String.Equals(name, "Thallus", StringComparison.OrdinalIgnoreCase) || (duplicateNames != null && duplicateNames.Contains(name)))
                name += " [" + ShortId(thallus.InstanceId) + "]";
            return name + " (“" + String.Join("; ", values.Take(3).ToArray()) + "”)";
        }

        private static string ShortId(string value)
        {
            string id = value ?? "";
            return id.Length <= 8 ? id : id.Substring(0, 8);
        }

        private static string SynthesizePurpose(List<ContextNode> active, List<string> scriptDescriptions, List<string> scriptRoles, List<string> clusterPurposes,
            bool optimization, FunctionalEvidenceSet functionalEvidence)
        {
            if (optimization && scriptRoles.Count > 0)
            {
                string result = "Possible inference: the selected workflow performs solver-controlled optimization whose fitness calculation may " + NaturalJoin(scriptRoles) + ".";
                if (scriptDescriptions.Count > 0) result += " Author-provided script descriptions add: " + NaturalJoin(scriptDescriptions) + ".";
                return result + " The broader design objective remains uncertain.";
            }

            List<string> stages = new List<string>();
            List<string> auxiliaryStages = new List<string>();
            bool iterativeCurveProcessing = (ContainsAll(active, "Loop Start", "Loop End") || ContainsAll(active, "Fast Loop Start", "Fast Loop End"))
                && active.Any(n => String.Equals(n.Name, "Discontinuity", StringComparison.OrdinalIgnoreCase)
                    || String.Equals(n.Name, "Shatter", StringComparison.OrdinalIgnoreCase)
                    || String.Equals(n.Name, "Tween Two Curves", StringComparison.OrdinalIgnoreCase)
                    || String.Equals(n.Name, "Extend Curve", StringComparison.OrdinalIgnoreCase)
                    || String.Equals(n.Name, "Trim with Region", StringComparison.OrdinalIgnoreCase));
            if (iterativeCurveProcessing)
            {
                stages.Add("iterative curve processing");
                if (ContainsAll(active, "Discontinuity", "Shatter")) stages.Add("curve segmentation at discontinuities");
                if (Has(active, "Tween Two Curves")) stages.Add("curve tween generation");
                if (Has(active, "Extend Curve")) stages.Add("curve extension");
                if (Has(active, "Trim with Region")) stages.Add("region-based curve trimming");
                if (ContainsAll(active, "Merge", "Clean Tree")) stages.Add("result accumulation and cleanup");
            }
            ClassifyStage(functionalEvidence, FunctionalEvidenceAnalyzer.SurfaceSubdivisionRule, "surface subdivision", stages, auxiliaryStages);
            ClassifyStage(functionalEvidence, FunctionalEvidenceAnalyzer.NumericNormalizationRule, "numeric normalization or rescaling", stages, auxiliaryStages);
            ClassifyStage(functionalEvidence, FunctionalEvidenceAnalyzer.DiamondPanelGenerationRule, "diamond-panel generation", stages, auxiliaryStages);
            ClassifyStage(functionalEvidence, FunctionalEvidenceAnalyzer.DiagridStructureGenerationRule, "surface diagrid-structure generation", stages, auxiliaryStages);
            ClassifyStage(functionalEvidence, FunctionalEvidenceAnalyzer.SurfacePointCurveNetworkRule,
                "graph-mapped surface point-grid curve-network construction", stages, auxiliaryStages);
            ClassifyStage(functionalEvidence, FunctionalEvidenceAnalyzer.TangentCurveReconstructionRule,
                "selective curve reconstruction with start-tangent-constrained interpolation", stages, auxiliaryStages);
            ClassifyStage(functionalEvidence, FunctionalEvidenceAnalyzer.SurfacePipeMorphRule,
                "surface splitting and branching-pipe construction followed by Brep intersection and surface morphing", stages, auxiliaryStages);
            ClassifyStage(functionalEvidence, FunctionalEvidenceAnalyzer.CurveGuidedSweepRule,
                new[] { "curve projection onto Breps", "oriented section construction along divided curves", "sweep geometry construction" }, stages, auxiliaryStages);
            ClassifyStage(functionalEvidence, FunctionalEvidenceAnalyzer.CurveNetworkPreparationRule, "curve-network offsetting, segmentation, and smoothing", stages, auxiliaryStages);
            ClassifyStage(functionalEvidence, FunctionalEvidenceAnalyzer.IntersectionAngleRule, "intersection-angle measurement", stages, auxiliaryStages);
            if (functionalEvidence.Has(FunctionalEvidenceAnalyzer.AngleDrivenFilletRule))
                ClassifyStage(functionalEvidence, FunctionalEvidenceAnalyzer.AngleDrivenFilletRule, "remapping measured angles into per-location fillet radii", stages, auxiliaryStages);
            else ClassifyStage(functionalEvidence, FunctionalEvidenceAnalyzer.AngleRemappingRule, "remapping measured angles into downstream control values", stages, auxiliaryStages);
            ClassifyStage(functionalEvidence, FunctionalEvidenceAnalyzer.DitheredPanelPartitionRule,
                "dithered image-driven partitioning of quadrilateral panels", stages, auxiliaryStages);
            ClassifyStage(functionalEvidence, FunctionalEvidenceAnalyzer.ImagePanelFilteringRule,
                "image-driven filtering of quadrilateral panels using image-derived values", stages, auxiliaryStages);
            if (!functionalEvidence.Has(FunctionalEvidenceAnalyzer.DitheredPanelPartitionRule)
                && !functionalEvidence.Has(FunctionalEvidenceAnalyzer.ImagePanelFilteringRule))
            {
                if (Has(active, "Quad Panels")) stages.Add("quadrilateral panel generation");
                if (ContainsAll(active, "Divide Surface", "Surface Closest Point", "Image Sampler")) stages.Add("image sampling across panel or surface coordinates");
            }
            ClassifyStage(functionalEvidence, FunctionalEvidenceAnalyzer.BlockPlacementRule, "block placement", stages, auxiliaryStages);
            ClassifyStage(functionalEvidence, FunctionalEvidenceAnalyzer.ModelBlockPlacementRule, "model-block instance placement", stages, auxiliaryStages);
            if (!IsRuleFullySubsumedBy(functionalEvidence, FunctionalEvidenceAnalyzer.GeometryGroupingRule, FunctionalEvidenceAnalyzer.ModelBlockPlacementRule))
                ClassifyStage(functionalEvidence, FunctionalEvidenceAnalyzer.GeometryGroupingRule, "geometry grouping", stages, auxiliaryStages);
            stages = stages.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            auxiliaryStages = auxiliaryStages.Where(stage => !stages.Contains(stage, StringComparer.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList();

            if (stages.Count > 0)
            {
                bool boundedInterpretation = iterativeCurveProcessing;
                string result = (boundedInterpretation ? "Possible functional inference from graph-wide evidence: " : "Supported functional inference from graph-wide evidence: ")
                    + "the selected workflow performs " + NaturalJoin(stages) + ".";
                if (auxiliaryStages.Count > 0)
                    result += " Additional in-scope branches contain evidence of " + NaturalJoin(auxiliaryStages) + "; the matched paths do not reach a captured output.";
                if (scriptDescriptions.Count > 0) result += " Author-provided script descriptions add: " + NaturalJoin(scriptDescriptions) + ".";
                if (scriptRoles.Count > 0) result += " Recognized source behavior may " + NaturalJoin(scriptRoles) + ".";
                if (clusterPurposes.Count > 0) result += " Inspected cluster internals also suggest " + NaturalJoin(clusterPurposes) + ".";
                if (boundedInterpretation || scriptDescriptions.Count > 0 || scriptRoles.Count > 0 || clusterPurposes.Count > 0 || auxiliaryStages.Count > 0)
                    result += " The broader design purpose remains uncertain.";
                return result;
            }
            if (auxiliaryStages.Count > 0)
            {
                string result = "Supported functional inference from graph-wide evidence: additional in-scope branches contain evidence of "
                    + NaturalJoin(auxiliaryStages) + ", but the matched paths do not reach a captured output.";
                if (scriptDescriptions.Count > 0) result += " Author-provided script descriptions add: " + NaturalJoin(scriptDescriptions) + ".";
                if (scriptRoles.Count > 0) result += " Recognized source behavior may " + NaturalJoin(scriptRoles) + ".";
                return result + " The broader design purpose remains uncertain.";
            }
            if (scriptDescriptions.Count > 0 || scriptRoles.Count > 0)
            {
                string result = scriptDescriptions.Count > 0
                    ? "Possible inference from author-provided script descriptions: the selected workflow may involve " + NaturalJoin(scriptDescriptions) + "."
                    : "Possible inference from recognized script behavior: the selected workflow may " + NaturalJoin(scriptRoles) + ".";
                if (scriptDescriptions.Count > 0 && scriptRoles.Count > 0) result += " Recognized source behavior may " + NaturalJoin(scriptRoles) + ".";
                return result + " The broader design purpose remains uncertain.";
            }
            if (clusterPurposes.Count > 0) return "Possible inference from inspected cluster internals: " + String.Join(" ", clusterPurposes.ToArray());
            return "The broader design purpose cannot be determined from the graph alone.";
        }

        private static void ClassifyStage(FunctionalEvidenceSet evidence, string ruleId, string stage, List<string> outputStages, List<string> auxiliaryStages)
        {
            ClassifyStage(evidence, ruleId, new[] { stage }, outputStages, auxiliaryStages);
        }

        private static void ClassifyStage(FunctionalEvidenceSet evidence, string ruleId, IEnumerable<string> stages, List<string> outputStages, List<string> auxiliaryStages)
        {
            if (evidence.HasOutputRelevant(ruleId)) outputStages.AddRange(stages);
            else if (evidence.HasAuxiliary(ruleId)) auxiliaryStages.AddRange(stages);
        }

        private static bool IsRuleFullySubsumedBy(FunctionalEvidenceSet evidence, string ruleId, string specializedRuleId)
        {
            List<FunctionalEvidence> candidates = evidence.Items.Where(item => String.Equals(item.RuleId, ruleId, StringComparison.OrdinalIgnoreCase)).ToList();
            if (candidates.Count == 0) return false;
            HashSet<string> specializedNodes = new HashSet<string>(evidence.Items
                .Where(item => String.Equals(item.RuleId, specializedRuleId, StringComparison.OrdinalIgnoreCase))
                .SelectMany(item => item.MatchedNodeIds ?? new List<string>()), StringComparer.OrdinalIgnoreCase);
            List<string> candidateResults = candidates.SelectMany(item => item.ResultNodeIds ?? new List<string>())
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            return specializedNodes.Count > 0 && candidateResults.Count > 0 && candidateResults.All(specializedNodes.Contains);
        }

        private static string NaturalJoin(IList<string> values)
        {
            if (values == null || values.Count == 0) return "";
            if (values.Count == 1) return values[0];
            if (values.Count == 2) return values[0] + " and " + values[1];
            return String.Join(", ", values.Take(values.Count - 1).ToArray()) + ", and " + values[values.Count - 1];
        }

        private static string BoundedEvidence(string value, int maximum)
        {
            string clean = Regex.Replace((value ?? "").Replace("\r", " ").Replace("\n", " "), @"\s+", " ").Trim();
            if (clean.Length > maximum) clean = clean.Substring(0, maximum - 1).TrimEnd() + "…";
            return TrimTerminalPunctuation(clean);
        }

        private static string NormalizeClusterPurpose(string purpose)
        {
            string value = (purpose ?? "").Trim();
            string[] prefixes = { "Supported functional inference from graph-wide evidence:", "Possible functional inference from graph-wide evidence:", "Strong inference:", "Possible inference:" };
            foreach (string prefix in prefixes)
                if (value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) { value = value.Substring(prefix.Length).Trim(); break; }
            if (value.Length > 0 && (value.Length == 1 || !Char.IsUpper(value[1]))) value = Char.ToLowerInvariant(value[0]) + value.Substring(1);
            return value;
        }

        private static bool IsCanvasGroup(ContextNode node)
        {
            return String.Equals(node.RuntimeTypeName, "Grasshopper.Kernel.Special.GH_Group", StringComparison.OrdinalIgnoreCase);
        }

        private string OperationFor(ContextNode node, ContextAnalysis analysis)
        {
            if (node.ClusterGraph != null && String.Equals(node.ClusterGraph.InspectionStatus, "inspected", StringComparison.OrdinalIgnoreCase))
            {
                List<string> operations = (node.ClusterGraph.Analysis == null ? new List<string>() : node.ClusterGraph.Analysis.DetectedOperations)
                    .Where(o => !String.IsNullOrWhiteSpace(o)).Take(3).Select(TrimTerminalPunctuation).ToList();
                string detail = operations.Count == 0
                    ? "contains " + node.ClusterGraph.Nodes.Count + " inspected internal objects"
                    : "contains an inspected internal workflow that " + String.Join("; ", operations.ToArray());
                if (node.ClusterGraph.NodeLimitReached) detail += "; the internal capture reached its configured node limit";
                string provided = String.IsNullOrWhiteSpace(node.ClusterGraph.UserProvidedPurpose) ? "" : " User-provided cluster purpose: " + TrimTerminalPunctuation(node.ClusterGraph.UserProvidedPurpose) + ".";
                return EmptyTo(node.Nickname, node.Name) + ": " + detail + "." + provided;
            }
            if (node.ClusterGraph != null && !String.IsNullOrWhiteSpace(node.ClusterGraph.BlackBoxSummary))
            {
                string provided = String.IsNullOrWhiteSpace(node.ClusterGraph.UserProvidedPurpose) ? "" : " User-provided cluster purpose: " + TrimTerminalPunctuation(node.ClusterGraph.UserProvidedPurpose) + ".";
                return EmptyTo(node.Nickname, node.Name) + ": black-box observations — " + node.ClusterGraph.BlackBoxSummary + provided;
            }
            if (node.Script != null)
            {
                string kind = (node.Script.Language ?? "").IndexOf("expression", StringComparison.OrdinalIgnoreCase) >= 0
                    ? "a Grasshopper expression"
                    : "a " + EmptyTo(node.Script.Language, "custom") + " script";
                return EmptyTo(node.Nickname, node.Name) + ": contains " + kind + "; observed source behavior is reported separately under Custom Scripts.";
            }
            string phrase;
            if (!descriptions.TryGetValue(node.Name, out phrase))
            {
                phrase = CleanDescription(node.Description);
                if (String.IsNullOrWhiteSpace(phrase))
                {
                    phrase = "uses the “" + EmptyTo(node.Name, "unknown component") + "” component; its exact operation is not described by component metadata";
                    analysis.Uncertainties.Add("No operation metadata was available for " + EmptyTo(node.Nickname, node.Name) + ".");
                }
            }
            return EmptyTo(node.Nickname, node.Name) + ": " + TrimTerminalPunctuation(phrase) + ".";
        }

        private static string CleanDescription(string value)
        {
            if (String.IsNullOrWhiteSpace(value)) return "";
            string normalized = value.Replace("\r", " ").Replace("\n", " ").Trim();
            string[] pieces = Regex.Split(normalized, @"\s{2,}");
            List<string> unique = new List<string>();
            foreach (string piece in pieces)
            {
                string clean = Regex.Replace(piece.Trim(), @"\s+", " ");
                if (clean.Length > 0 && !unique.Contains(clean, StringComparer.OrdinalIgnoreCase)) unique.Add(clean);
            }
            string result = String.Join(". ", unique.Select(TrimTerminalPunctuation).ToArray());
            return result.Length <= 240 ? result : result.Substring(0, 237).TrimEnd() + "…";
        }

        private static bool Has(IEnumerable<ContextNode> nodes, string name) { return nodes.Any(n => String.Equals(n.Name, name, StringComparison.OrdinalIgnoreCase)); }
        private static bool ContainsAll(IEnumerable<ContextNode> nodes, params string[] names) { return names.All(name => Has(nodes, name)); }
        private static void Consume(IEnumerable<ContextNode> nodes, HashSet<string> consumed, params string[] names)
        {
            HashSet<string> set = new HashSet<string>(names, StringComparer.OrdinalIgnoreCase);
            foreach (ContextNode node in nodes.Where(n => set.Contains(n.Name))) consumed.Add(node.InstanceId);
        }
        private static string TrimTerminalPunctuation(string value) { return (value ?? "").Trim().TrimEnd('.', ';', ':'); }
        private static string EmptyTo(string value, string fallback) { return String.IsNullOrWhiteSpace(value) ? fallback : value; }
    }
}
