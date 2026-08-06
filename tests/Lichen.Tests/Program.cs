using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Lichen.Core;

namespace Lichen.Tests
{
    internal static class Program
    {
        private static int passed;
        private static int failed;

        private static int Main()
        {
            Run("selected-only scope and boundaries", SelectedOnly);
            Run("immediate upstream expansion", ImmediateUpstream);
            Run("recursive upstream expansion", RecursiveUpstream);
            Run("duplicate edge prevention", DuplicatePrevention);
            Run("cycle handling", CycleHandling);
            Run("node limit", NodeLimit);
            Run("selection scopes reject an empty or unresolved selection", SelectionValidation);
            Run("empty entire documents export safely", EmptyEntireDocument);
            Run("stable node and edge ordering", StableOrdering);
            Run("exports do not mutate captured snapshots", SnapshotIsolation);
            Run("500-object exports stay bounded and deterministic", LargeDefinitionStress);
            Run("active Group components are not hidden as canvas annotations", ActiveGroupComponentPresentation);
            Run("duplicate script source is condensed only in Markdown", DuplicateScriptSourcePresentation);
            Run("markdown escaping and script fences", MarkdownEscaping);
            Run("JSON round trip", JsonRoundTrip);
            Run("component descriptions remain factual", SemanticsFallback);
            Run("detail levels are distinct", DetailLevels);
            Run("boundaries use readable names", ReadableBoundaries);
            Run("identical boundary labels are disambiguated", BoundaryLabelCollision);
            Run("workflow refinement filters passive objects", WorkflowRefinement);
            Run("runtime summaries are separated and condensed", RuntimeSummary);
            Run("repeated descriptions are normalized", DescriptionNormalization);
            Run("dependencies distinguish native and third-party", DependencyClassification);
            Run("author signals are not duplicated", AuthorSignals);
            Run("value nodes are labeled by recipients", ValueNodeLabels);
            Run("duplicate boundary labels are condensed", DuplicateBoundaryLabels);
            Run("runtime summaries use grammar and filter passive nodes", RuntimePolish);
            Run("JSON schema uses stable lower-camel fields", JsonSchemaContract);
            Run("Exact Markdown embeds the complete JSON", ExactJsonAppendix);
            Run("current script components expose source safely", CurrentScriptSource);
            Run("legacy GhPython exposes source safely", LegacyPythonSource);
            Run("classic structured C# source is reconstructed", StructuredCSharpSource);
            Run("Grasshopper expression source is captured", ExpressionSource);
            Run("unsupported script APIs fail without throwing", UnsupportedScriptSource);
            Run("bundled script and transform assemblies are native", BundledDependencyClassification);
            Run("script workflow wording stays descriptive", ScriptWorkflowWording);
            Run("arc-length script behavior is described", ArcLengthBehavior);
            Run("mesh-split script behavior is described", MeshSplitBehavior);
            Run("variable-fillet script behavior is described", VariableFilletBehavior);
            Run("unknown scripts remain explicitly unknown", UnknownScriptBehavior);
            Run("nested iterative regions are structured", NestedExecutionRegions);
            Run("stateful and solver controllers are classified", ExecutionControllerClassification);
            Run("inspected cluster graphs inform exports", InspectedClusterGraph);
            Run("protected clusters remain explicit and opaque", ProtectedClusterGraph);
            Run("duplicate cluster names and purposes are presented clearly", DuplicateClusterPresentation);
            Run("cluster-derived purpose confidence is not nested", ClusterPurposeConfidenceWording);
            Run("ordinary feedback cycles are disclosed", ExecutionCycleDisclosure);
            Run("script roles inform cautious purpose inference", ScriptRoleInference);
            Run("conditional expressions are described", ConditionalExpressionBehavior);
            Run("script evidence renders as inline code", ScriptEvidencePresentation);
            Run("large workflow summaries condense duplicate operations", WorkflowCondensation);
            Run("numeric and normalization expressions are described", ExpandedExpressionBehavior);
            Run("optimization objectives name linked components and fitness construction", OptimizationObjectiveDescription);
            Run("export root resolves a simple linear chain", ExportRootLinearChain);
            Run("export root combines multiple X sources", ExportRootMultipleSources);
            Run("export root excludes downstream side branches", ExportRootExcludesSideBranch);
            Run("export root includes diverging branches that reconnect", ExportRootIncludesReconnect);
            Run("export root handles cycles and duplicate edges", ExportRootCyclesAndDuplicates);
            Run("export root ordering is stable", ExportRootStableOrdering);
            Run("export root reports deterministic 500-object truncation", ExportRootLimit);
            Run("export root marker is absent from exported JSON", ExportRootMarkerExcluded);
            Run("multiple export roots remain independent", MultipleExportRoots);
            Run("export root traversal and export do not mutate snapshots", ExportRootSnapshotIsolation);
            Run("highlight traversal matches export-root scope", ExportRootHighlightMatchesExport);
            Console.WriteLine("Passed: " + passed + "; Failed: " + failed);
            return failed == 0 ? 0 : 1;
        }

        private static void ExportRootLinearChain()
        {
            ContextSnapshot snapshot = RootFixture("R", "Primary result", new[] { "A", "B", "C" }, Edge("A", "B"), Edge("B", "C"), Edge("C", "R"));
            ContextDocument document = new ContextGraphService().BuildDocument(snapshot, RootOptions("R"));
            Sequence(new[] { "A", "B", "C" }, document.Nodes.Select(n => n.InstanceId));
            Sequence(new[] { "A|0|B|0", "B|0|C|0" }, document.Edges.Select(ExportRootScopeResolver.EdgeKey));
            Equal(0, document.BoundaryInputs.Count); Equal(0, document.BoundaryOutputs.Count);
            Equal("Primary result", document.Scope.RootLabel); Sequence(new[] { "C" }, document.Scope.RootSourceObjectIds);
        }

        private static void ExportRootMultipleSources()
        {
            ContextSnapshot snapshot = RootFixture("R", "Combined", new[] { "A", "B" }, Edge("B", "R"), Edge("A", "R"));
            ContextDocument document = new ContextGraphService().BuildDocument(snapshot, RootOptions("R"));
            Sequence(new[] { "A", "B" }, document.Nodes.Select(n => n.InstanceId));
            Sequence(new[] { "A", "B" }, document.Scope.RootSourceObjectIds);
        }

        private static void ExportRootExcludesSideBranch()
        {
            ContextSnapshot snapshot = RootFixture("R", "Result", new[] { "A", "B", "G" }, Edge("A", "B"), Edge("B", "R"), Edge("B", "G"));
            ContextDocument document = new ContextGraphService().BuildDocument(snapshot, RootOptions("R"));
            Sequence(new[] { "A", "B" }, document.Nodes.Select(n => n.InstanceId));
            True(document.Edges.All(e => e.SourceNodeId != "G" && e.TargetNodeId != "G"), "side-branch wire leaked into root export");
            Equal(0, document.BoundaryOutputs.Count);
        }

        private static void ExportRootIncludesReconnect()
        {
            ContextSnapshot snapshot = RootFixture("R", "Result", new[] { "A", "B", "C", "G" }, Edge("A", "B"), Edge("B", "C"), Edge("B", "G"), Edge("G", "C"), Edge("C", "R"));
            ContextDocument document = new ContextGraphService().BuildDocument(snapshot, RootOptions("R"));
            Sequence(new[] { "A", "B", "C", "G" }, document.Nodes.Select(n => n.InstanceId));
            Equal(4, document.Edges.Count);
        }

        private static void ExportRootCyclesAndDuplicates()
        {
            ContextSnapshot snapshot = RootFixture("R", "Cycle", new[] { "A", "B" }, Edge("A", "B"), Edge("B", "A"), Edge("B", "R"), Edge("A", "B"));
            ExportRootClosure closure = new ExportRootScopeResolver().Resolve(snapshot, "R", 500);
            Sequence(new[] { "A", "B" }, closure.IncludedObjectIds);
            Equal(3, closure.ContributingEdges.Count);
            ContextDocument document = new ContextGraphService().BuildDocument(snapshot, RootOptions("R"));
            Equal(2, document.Edges.Count);
        }

        private static void ExportRootStableOrdering()
        {
            ContextSnapshot first = RootFixture("R", "Stable", new[] { "A", "B", "C" }, Edge("B", "C"), Edge("C", "R"), Edge("A", "C"));
            ContextSnapshot second = RootFixture("R", "Stable", new[] { "C", "B", "A" }, Edge("A", "C"), Edge("C", "R"), Edge("B", "C"));
            second.Nodes.Reverse(); second.Edges.Reverse();
            ContextExporter exporter = new ContextExporter();
            Equal(exporter.Export(first, RootOptions("R")).Json, exporter.Export(second, RootOptions("R")).Json);
        }

        private static void ExportRootLimit()
        {
            ContextSnapshot snapshot = new ContextSnapshot();
            for (int i = 0; i < 502; i++) snapshot.Nodes.Add(Node("N" + i.ToString("D3")));
            snapshot.Nodes.Add(Node("R"));
            for (int i = 0; i < 501; i++) snapshot.Edges.Add(Edge("N" + i.ToString("D3"), "N" + (i + 1).ToString("D3")));
            snapshot.Edges.Add(Edge("N501", "R")); snapshot.ExportRoots.Add(new ExportRootDefinition { ObjectId = "R", Label = "Large" });
            ContextExportOptions options = RootOptions("R"); options.MaximumNodes = 500;
            ContextDocument document = new ContextGraphService().BuildDocument(snapshot, options);
            Equal(500, document.Nodes.Count); True(document.Scope.NodeLimitReached, "root truncation was not reported");
            True(document.ExtractionNotes.Any(n => n.Contains("truncated deterministically")), "root truncation note was absent");
        }

        private static void ExportRootMarkerExcluded()
        {
            ContextSnapshot snapshot = RootFixture("R", "Private marker", new[] { "A" }, Edge("A", "R"));
            ContextExportPackage package = new ContextExporter().Export(snapshot, RootOptions("R"));
            True(package.Document.Nodes.All(n => n.InstanceId != "R"), "root marker was included as a node");
            True(package.Document.Edges.All(e => e.SourceNodeId != "R" && e.TargetNodeId != "R"), "root marker was included in an edge");
            True(!package.Json.Contains("\"instanceId\": \"R\""), "root marker leaked into JSON node inventory");
            True(!package.Json.Contains("\"R\""), "root marker ID leaked into JSON");
        }

        private static void MultipleExportRoots()
        {
            ContextSnapshot snapshot = RootFixture("R1", "First", new[] { "A", "B", "C" }, Edge("A", "B"), Edge("B", "R1"), Edge("C", "R2"));
            snapshot.Nodes.Add(Node("R2")); snapshot.ExportRoots.Add(new ExportRootDefinition { ObjectId = "R2", Label = "Second", SourceObjectIds = new List<string> { "C" } });
            ContextDocument first = new ContextGraphService().BuildDocument(snapshot, RootOptions("R1"));
            ContextDocument second = new ContextGraphService().BuildDocument(snapshot, RootOptions("R2"));
            Sequence(new[] { "A", "B" }, first.Nodes.Select(n => n.InstanceId)); Sequence(new[] { "C" }, second.Nodes.Select(n => n.InstanceId));
        }

        private static void ExportRootSnapshotIsolation()
        {
            ContextSnapshot snapshot = RootFixture("R", "Isolation", new[] { "A", "B" }, Edge("A", "B"), Edge("B", "R"));
            List<string> nodeOrder = snapshot.Nodes.Select(n => n.InstanceId).ToList(); List<string> edgeOrder = snapshot.Edges.Select(ExportRootScopeResolver.EdgeKey).ToList();
            ExportRootClosure closure = new ExportRootScopeResolver().Resolve(snapshot, "R", 500);
            new ContextExporter().Export(snapshot, RootOptions("R"));
            Sequence(nodeOrder, snapshot.Nodes.Select(n => n.InstanceId)); Sequence(edgeOrder, snapshot.Edges.Select(ExportRootScopeResolver.EdgeKey));
            True(snapshot.Edges.All(e => e.BoundaryStatus == "internal" && !e.CrossesScopeBoundary), "root export mutated source edges");
            Sequence(new[] { "A", "B" }, closure.IncludedObjectIds);
        }

        private static void ExportRootHighlightMatchesExport()
        {
            ContextSnapshot snapshot = RootFixture("R", "Visible", new[] { "A", "B", "C", "G" }, Edge("A", "B"), Edge("B", "C"), Edge("B", "G"), Edge("G", "C"), Edge("C", "R"));
            ExportRootClosure highlight = new ExportRootScopeResolver().Resolve(snapshot, "R", 500);
            ContextDocument export = new ContextGraphService().BuildDocument(snapshot, RootOptions("R"));
            Sequence(export.Scope.IncludedObjectIds, highlight.IncludedObjectIds);
            Sequence(export.Edges.Select(ExportRootScopeResolver.EdgeKey), highlight.ContributingEdges.Where(e => e.TargetNodeId != "R").Select(ExportRootScopeResolver.EdgeKey));
        }

        private static ContextSnapshot RootFixture(string rootId, string label, IEnumerable<string> nodeIds, params ContextEdge[] edges)
        {
            ContextSnapshot snapshot = new ContextSnapshot { Name = "root-fixture.gh", RhinoVersion = "8", GrasshopperVersion = "8" };
            foreach (string id in nodeIds) snapshot.Nodes.Add(Node(id));
            snapshot.Nodes.Add(Node(rootId)); snapshot.Edges.AddRange(edges);
            snapshot.ExportRoots.Add(new ExportRootDefinition { ObjectId = rootId, Label = label, SourceObjectIds = edges.Where(e => e.TargetNodeId == rootId).Select(e => e.SourceNodeId).Distinct().OrderBy(id => id).ToList() });
            return snapshot;
        }

        private static ContextExportOptions RootOptions(string rootId)
        {
            return new ContextExportOptions { ScopeMode = ScopeMode.ExportRoot, RootObjectId = rootId, DetailLevel = DetailLevel.Exact, IncludeScriptSource = true, IncludeRuntimeSummary = true, MaximumNodes = 500 };
        }

        private static void SelectedOnly()
        {
            ContextDocument d = Build(ScopeMode.SelectedOnly, "B");
            Equal(1, d.Nodes.Count); Equal("B", d.Nodes[0].InstanceId); Equal(1, d.BoundaryInputs.Count); Equal(1, d.BoundaryOutputs.Count);
            Equal("incoming", d.Edges[0].BoundaryStatus); Equal("outgoing", d.Edges[1].BoundaryStatus);
        }

        private static void ImmediateUpstream()
        {
            ContextDocument d = Build(ScopeMode.SelectedPlusImmediateUpstream, "C");
            Sequence(new[] { "B", "C" }, d.Nodes.Select(n => n.InstanceId));
            Equal(1, d.BoundaryInputs.Count); Equal("A", d.BoundaryInputs[0].ExternalNodeId);
        }

        private static void RecursiveUpstream()
        {
            ContextDocument d = Build(ScopeMode.SelectedPlusAllUpstream, "C");
            Sequence(new[] { "A", "B", "C" }, d.Nodes.Select(n => n.InstanceId)); Equal(0, d.BoundaryInputs.Count);
            True(d.Nodes.Single(n => n.InstanceId == "C").OriginallySelected, "selected marker missing");
            True(!d.Nodes.Single(n => n.InstanceId == "A").OriginallySelected, "included marker incorrect");
        }

        private static void DuplicatePrevention()
        {
            ContextSnapshot s = Fixture("B"); s.Edges.Add(Edge("A", "B"));
            ContextDocument d = new ContextGraphService().BuildDocument(s, Options(ScopeMode.SelectedOnly)); Equal(2, d.Edges.Count);
        }

        private static void CycleHandling()
        {
            ContextSnapshot s = Fixture("C"); s.Edges.Add(Edge("B", "A"));
            ContextDocument d = new ContextGraphService().BuildDocument(s, Options(ScopeMode.SelectedPlusAllUpstream));
            Equal(3, d.Nodes.Count); Equal(3, ContextGraphService.TopologicalOrder(d).Count);
        }

        private static void NodeLimit()
        {
            ContextExportOptions options = Options(ScopeMode.EntireDocument); options.MaximumNodes = 2;
            ContextDocument d = new ContextGraphService().BuildDocument(Fixture(), options); Equal(2, d.Nodes.Count); True(d.Scope.NodeLimitReached, "limit was not recorded");
        }

        private static void SelectionValidation()
        {
            ContextSnapshot emptySelection = Fixture();
            ThrowsInvalidOperation(delegate { new ContextGraphService().BuildDocument(emptySelection, Options(ScopeMode.SelectedOnly)); });
            emptySelection.SelectedObjectIds.Add("not-present");
            ThrowsInvalidOperation(delegate { new ContextGraphService().BuildDocument(emptySelection, Options(ScopeMode.SelectedPlusAllUpstream)); });
        }

        private static void EmptyEntireDocument()
        {
            ContextExportPackage package = new ContextExporter().Export(new ContextSnapshot(), Options(ScopeMode.EntireDocument));
            Equal(0, package.Document.Nodes.Count);
            True(package.Markdown.Contains("No operations were extracted."), "empty workflow was not described");
            True(package.Json.Contains("\"nodes\": ["), "empty graph JSON was not emitted");
        }

        private static void SnapshotIsolation()
        {
            ContextSnapshot snapshot = Fixture("B");
            ContextNode selected = snapshot.Nodes.Single(n => n.InstanceId == "B");
            selected.Script = new ContextScript { Language = "C#", Source = "A = x + 1;" };
            selected.RuntimeMessages.Add(new ContextRuntimeMessage { Level = "warning", Message = "fixture warning" });
            selected.Inputs.Add(new ContextParameter { Index = 2, Name = "Later", Direction = "input", RuntimeDataSummary = "items=2, branches=1" });
            selected.Inputs.Add(new ContextParameter { Index = 1, Name = "Earlier", Direction = "input", RuntimeDataSummary = "items=1, branches=1" });
            selected.GroupIds.AddRange(new[] { "z-group", "a-group" });
            ContextGroup group = new ContextGroup { InstanceId = "group", Name = "Fixture Group" }; group.MemberIds.AddRange(new[] { "C", "B" }); snapshot.Groups.Add(group);

            ContextExportOptions excluded = Options(ScopeMode.SelectedOnly); excluded.IncludeScriptSource = false; excluded.IncludeRuntimeSummary = false;
            ContextDocument first = new ContextGraphService().BuildDocument(snapshot, excluded);
            ContextNode firstNode = first.Nodes.Single();
            Equal("", firstNode.Script.Source); Equal(0, firstNode.RuntimeMessages.Count);
            True(firstNode.Inputs.All(p => String.IsNullOrEmpty(p.RuntimeDataSummary)), "runtime summaries were not excluded");
            Sequence(new[] { "Earlier", "Later" }, firstNode.Inputs.Select(p => p.Name));

            Equal("A = x + 1;", selected.Script.Source); Equal(1, selected.RuntimeMessages.Count);
            Sequence(new[] { "Later", "Earlier" }, selected.Inputs.Select(p => p.Name));
            Sequence(new[] { "z-group", "a-group" }, selected.GroupIds);
            True(!selected.OriginallySelected, "snapshot selection marker was mutated");
            True(!snapshot.Edges[0].CrossesScopeBoundary && snapshot.Edges[0].BoundaryStatus == "internal", "snapshot edge was mutated");
            Sequence(new[] { "C", "B" }, snapshot.Groups[0].MemberIds);

            ContextDocument second = new ContextGraphService().BuildDocument(snapshot, Options(ScopeMode.SelectedOnly));
            ContextNode secondNode = second.Nodes.Single();
            Equal("A = x + 1;", secondNode.Script.Source); Equal(1, secondNode.RuntimeMessages.Count);
            True(secondNode.Inputs.Any(p => p.RuntimeDataSummary == "items=2, branches=1"), "later export lost captured runtime data");
        }

        private static void LargeDefinitionStress()
        {
            const int nodeCount = 500;
            ContextSnapshot ordered = LargeFixture(nodeCount);
            ContextExportOptions options = Options(ScopeMode.EntireDocument); options.DetailLevel = DetailLevel.Technical; options.IncludeJsonAppendix = false;
            ContextExporter exporter = new ContextExporter(); Stopwatch watch = Stopwatch.StartNew();
            ContextExportPackage first = exporter.Export(ordered, options); watch.Stop();
            True(watch.Elapsed < TimeSpan.FromSeconds(15), "500-object export exceeded the 15-second safety ceiling");
            Equal(nodeCount, first.Document.Nodes.Count); Equal(nodeCount - 1, first.Document.Edges.Count);
            True(first.Markdown.Length > 10000 && first.Json.Length > 10000, "large export was unexpectedly incomplete");

            ContextSnapshot reordered = LargeFixture(nodeCount); reordered.Nodes.Reverse(); reordered.Edges.Reverse(); reordered.Groups.Reverse();
            ContextExportPackage second = exporter.Export(reordered, options);
            Equal(first.Json, second.Json); Equal(first.Markdown, second.Markdown);
            Console.WriteLine("INFO 500-object technical export: " + watch.ElapsedMilliseconds + " ms; Markdown " + first.Markdown.Length + " chars; JSON " + first.Json.Length + " chars");
        }

        private static void ActiveGroupComponentPresentation()
        {
            ContextSnapshot snapshot = new ContextSnapshot();
            ContextNode annotation = new ContextNode { InstanceId = "annotation", Name = "Group", Description = "A group of Grasshopper objects", AssemblyName = "Grasshopper", RuntimeTypeName = "Grasshopper.Kernel.Special.GH_Group" };
            ContextNode component = new ContextNode { InstanceId = "component", Name = "Group", Nickname = "Geometry Group", Description = "Group a set of objects", AssemblyName = "Grasshopper", RuntimeTypeName = "Grasshopper.Kernel.Components.GH_GroupGeometryComponent" };
            component.Inputs.Add(new ContextParameter { Index = 0, Name = "Objects", Direction = "input" });
            component.Outputs.Add(new ContextParameter { Index = 0, Name = "Group", Direction = "output", RuntimeDataSummary = "items=100, branches=2" });
            snapshot.Nodes.Add(annotation); snapshot.Nodes.Add(component);
            ContextGroup group = new ContextGroup { InstanceId = "annotation", Name = "" }; group.MemberIds.Add("component"); snapshot.Groups.Add(group);
            ContextExportOptions options = Options(ScopeMode.EntireDocument); options.DetailLevel = DetailLevel.Technical; options.IncludeJsonAppendix = false;
            ContextExportPackage package = new ContextExporter().Export(snapshot, options);
            True(package.Document.Analysis.DetectedOperations.Any(o => o.Contains("Geometry Group") && o.Contains("Group a set of objects")), "active Group component was omitted from workflow analysis");
            True(package.Markdown.Contains("| Group | Geometry Group | Grasshopper |"), "active Group component was omitted from Technical inventory");
            True(package.Markdown.Contains("Geometry Group — Group: 100 items across 2 branches"), "active Group component runtime summary was hidden");
        }

        private static void DuplicateScriptSourcePresentation()
        {
            const string source = "// UNIQUE_DUPLICATE_SOURCE_MARKER\nA = x;";
            ContextSnapshot snapshot = new ContextSnapshot();
            ContextNode first = ScriptNode("C#", source); first.InstanceId = "S1"; first.Nickname = "Offset A";
            ContextNode second = ScriptNode("C#", source); second.InstanceId = "S2"; second.Nickname = "Offset B";
            snapshot.Nodes.Add(first); snapshot.Nodes.Add(second);
            ContextExportOptions options = Options(ScopeMode.EntireDocument); options.DetailLevel = DetailLevel.Technical; options.IncludeJsonAppendix = false;
            ContextExportPackage package = new ContextExporter().Export(snapshot, options);
            Equal(1, Count(package.Markdown, "UNIQUE_DUPLICATE_SOURCE_MARKER"));
            True(package.Markdown.Contains("is not repeated in Markdown"), "duplicate-source disclosure was omitted");
            Equal(2, Count(package.Json, "UNIQUE_DUPLICATE_SOURCE_MARKER"));
        }

        private static void StableOrdering()
        {
            ContextSnapshot s = Fixture("C"); s.Nodes.Reverse(); s.Edges.Reverse();
            ContextExporter exporter = new ContextExporter(); string first = exporter.Export(s, Options(ScopeMode.EntireDocument)).Json;
            s.Nodes.Reverse(); s.Edges.Reverse(); string second = exporter.Export(s, Options(ScopeMode.EntireDocument)).Json; Equal(first, second);
        }

        private static void MarkdownEscaping()
        {
            ContextSnapshot s = Fixture("B"); ContextNode node = s.Nodes.Single(n => n.InstanceId == "B"); node.Nickname = "B|*node*"; node.PersistentValueSummary = "value=5"; node.Script = new ContextScript { Language = "Python", Source = "print('```')" };
            ContextExportOptions options = Options(ScopeMode.SelectedOnly); options.IncludeJsonAppendix = false;
            string markdown = new ContextExporter().Export(s, options).Markdown;
            True(markdown.Contains("B\\|\\*node\\*"), "table content was not escaped"); True(markdown.Contains("````python"), "script fence was not lengthened"); True(markdown.Contains("value=5"), "special value summary missing");
        }

        private static void JsonRoundTrip()
        {
            ContextDocument before = Build(ScopeMode.EntireDocument); ContextJsonSerializer serializer = new ContextJsonSerializer();
            ContextDocument after = serializer.Deserialize(serializer.Serialize(before)); Equal(before.Nodes.Count, after.Nodes.Count); Equal(before.Edges.Count, after.Edges.Count); Equal("entire_document", after.Scope.Mode);
        }

        private static void SemanticsFallback()
        {
            ContextDocument d = Build(ScopeMode.SelectedOnly, "B"); True(d.Analysis.DetectedOperations[0].Contains("Fixture node B"), "component description missing"); True(!d.Analysis.DetectedOperations[0].Contains("interpretation uncertain"), "factual metadata was marked uncertain");
        }

        private static void DetailLevels()
        {
            ContextSnapshot s = Fixture("B"); ContextExporter exporter = new ContextExporter();
            ContextExportOptions technical = Options(ScopeMode.SelectedOnly); technical.DetailLevel = DetailLevel.Technical;
            string technicalMarkdown = exporter.Export(s, technical).Markdown;
            True(technicalMarkdown.Contains("Lichen is a read-only Grasshopper context exporter"), "Lichen introduction missing");
            True(technicalMarkdown.Contains("## Connection Summary"), "Technical connection heading is incorrect");
            True(technicalMarkdown.Contains("Exact connections are omitted at Technical detail level."), "Technical output included exact connections");
            True(!technicalMarkdown.Contains("```json"), "Technical output forced a JSON appendix");
            ContextExportOptions exact = Options(ScopeMode.SelectedOnly); exact.DetailLevel = DetailLevel.Exact;
            string exactMarkdown = exporter.Export(Fixture("B"), exact).Markdown;
            True(exactMarkdown.Contains("```json"), "Exact output did not include JSON"); True(exactMarkdown.Contains("`A` [0] out"), "Exact connection missing"); True(exactMarkdown.Contains("## Exact Connection List"), "Exact connection heading is incorrect");
        }

        private static void ReadableBoundaries()
        {
            ContextExportOptions options = Options(ScopeMode.SelectedOnly); options.DetailLevel = DetailLevel.Technical;
            string markdown = new ContextExporter().Export(Fixture("B"), options).Markdown;
            True(markdown.Contains("A.out → B.in"), "incoming boundary names missing"); True(markdown.Contains("B.out → C.in"), "outgoing boundary names missing");
        }

        private static void BoundaryLabelCollision()
        {
            ContextSnapshot snapshot = new ContextSnapshot(); snapshot.SelectedObjectIds.Add("inside");
            snapshot.Nodes.Add(new ContextNode { InstanceId = "outside", Name = "Isotrim", Nickname = "Isotrim", AssemblyName = "SurfaceComponents" });
            snapshot.Nodes.Add(new ContextNode { InstanceId = "inside", Name = "Isotrim", Nickname = "Isotrim", AssemblyName = "SurfaceComponents" });
            snapshot.Edges.Add(new ContextEdge { SourceNodeId = "outside", SourceParameterName = "Surface", TargetNodeId = "inside", TargetParameterName = "Surface" });
            ContextExportOptions options = Options(ScopeMode.SelectedOnly); options.DetailLevel = DetailLevel.Technical; options.IncludeJsonAppendix = false;
            string markdown = new ContextExporter().Export(snapshot, options).Markdown;
            True(markdown.Contains("Isotrim.Surface (`outside`) → Isotrim.Surface (`inside`)"), "identical boundary labels remained ambiguous");
        }

        private static void WorkflowRefinement()
        {
            ContextSnapshot s = new ContextSnapshot();
            string[] names = { "Number Slider", "Deconstruct Brep", "Length", "Division", "Quad Panels", "Divide Surface", "Surface Closest Point", "Image Sampler", "Average", "Remap Numbers", "Graph Mapper", "Includes", "Cull Pattern", "Area" };
            for (int i = 0; i < names.Length; i++) s.Nodes.Add(new ContextNode { InstanceId = "N" + i, Name = names[i], Nickname = names[i], AssemblyName = names[i] == "Quad Panels" ? "LunchBox" : "Grasshopper", AssemblyVersion = "1" });
            ContextDocument d = new ContextGraphService().BuildDocument(s, Options(ScopeMode.EntireDocument));
            Equal(5, d.Analysis.DetectedOperations.Count); True(!d.Analysis.DetectedOperations.Any(o => o.StartsWith("Number Slider")), "passive slider became a workflow step"); True(d.Analysis.InferredPurpose.Contains("image-derived values"), "recognized purpose missing");
        }

        private static void RuntimeSummary()
        {
            ContextSnapshot s = Fixture("B"); ContextNode node = s.Nodes.Single(n => n.InstanceId == "B"); node.Outputs.Add(new ContextParameter { Index = 0, Name = "Result", Nickname = "Result", Direction = "output", RuntimeDataSummary = "items=15252, branches=5" });
            ContextExportOptions options = Options(ScopeMode.SelectedOnly); options.DetailLevel = DetailLevel.Technical;
            string markdown = new ContextExporter().Export(s, options).Markdown;
            True(markdown.Contains("15,252 items across 5 branches"), "runtime counts were not formatted"); True(!markdown.Contains("internalized/available"), "obsolete runtime label remains");
        }

        private static void DescriptionNormalization()
        {
            ContextSnapshot s = new ContextSnapshot(); s.Nodes.Add(new ContextNode { InstanceId = "X", Name = "Custom Mapper", Nickname = "Custom Mapper", Description = "Bezier curve evaluator  Bezier curve evaluator  Bezier curve evaluator", AssemblyName = "Custom", AssemblyVersion = "1" });
            ContextDocument d = new ContextGraphService().BuildDocument(s, Options(ScopeMode.EntireDocument)); Equal(1, Count(d.Analysis.DetectedOperations[0], "Bezier curve evaluator"));
        }

        private static void DependencyClassification()
        {
            ContextSnapshot s = Fixture(); s.Nodes[0].AssemblyName = "MathComponents"; s.Nodes[1].AssemblyName = "LunchBox";
            ContextExportOptions options = Options(ScopeMode.EntireDocument); options.DetailLevel = DetailLevel.Technical;
            string markdown = new ContextExporter().Export(s, options).Markdown;
            True(markdown.Contains("Grasshopper native components"), "native dependency group missing"); True(markdown.Contains("LunchBox 1.0 (third-party)"), "third-party dependency missing"); True(!markdown.Contains("MathComponents 1.0 (third-party)"), "native assembly labeled third-party");
        }

        private static void AuthorSignals()
        {
            ContextSnapshot s = Fixture(); s.Nodes[0].Name = "Panel"; s.Nodes[0].Nickname = ""; s.Nodes[0].PersistentValueSummary = "text=Facade zones";
            ContextExportOptions options = Options(ScopeMode.EntireDocument); options.DetailLevel = DetailLevel.Technical;
            string markdown = new ContextExporter().Export(s, options).Markdown; Equal(1, Count(markdown, "text=Facade zones"));
        }

        private static void ValueNodeLabels()
        {
            ContextSnapshot s = Fixture(); s.Nodes[0].Name = "Number Slider"; s.Nodes[0].Nickname = "Number Slider"; s.Nodes[0].PersistentValueSummary = "value=5";
            ContextExportOptions options = Options(ScopeMode.EntireDocument); options.DetailLevel = DetailLevel.Technical;
            string markdown = new ContextExporter().Export(s, options).Markdown; True(markdown.Contains("Number Slider → B.in: value=5"), "value node recipient label missing");
        }

        private static void DuplicateBoundaryLabels()
        {
            ContextSnapshot s = new ContextSnapshot();
            s.Nodes.Add(new ContextNode { InstanceId = "A1", Name = "Length", Nickname = "Length", AssemblyName = "CurveComponents" });
            s.Nodes.Add(new ContextNode { InstanceId = "A2", Name = "Length", Nickname = "Length", AssemblyName = "CurveComponents" });
            s.Nodes.Add(new ContextNode { InstanceId = "X1", Name = "Division", Nickname = "Division", AssemblyName = "MathComponents" });
            s.Nodes.Add(new ContextNode { InstanceId = "X2", Name = "Division", Nickname = "Division", AssemblyName = "MathComponents" });
            s.SelectedObjectIds.Add("A1"); s.SelectedObjectIds.Add("A2"); s.Edges.Add(Edge("A1", "X1")); s.Edges.Add(Edge("A2", "X2"));
            ContextExportOptions options = Options(ScopeMode.SelectedOnly); options.DetailLevel = DetailLevel.Technical;
            string markdown = new ContextExporter().Export(s, options).Markdown; True(markdown.Contains("(2 separate connections)"), "duplicate boundary lines were not condensed");
        }

        private static void RuntimePolish()
        {
            ContextSnapshot s = Fixture(); ContextNode passive = s.Nodes[0]; passive.Name = "Number"; passive.Nickname = "Number"; passive.Outputs.Add(new ContextParameter { Name = "Number", Direction = "output", RuntimeDataSummary = "items=1000, branches=10" });
            ContextNode active = s.Nodes[1]; active.Outputs.Add(new ContextParameter { Name = "Result", Direction = "output", RuntimeDataSummary = "items=100, branches=1" });
            ContextExportOptions technical = Options(ScopeMode.EntireDocument); technical.DetailLevel = DetailLevel.Technical; string markdown = new ContextExporter().Export(s, technical).Markdown;
            True(!markdown.Contains("1,000 items across 10 branches"), "passive runtime node was included"); True(markdown.Contains("100 items across 1 branch"), "singular branch grammar is incorrect");
            ContextExportOptions exact = Options(ScopeMode.EntireDocument); exact.DetailLevel = DetailLevel.Exact; string exactMarkdown = new ContextExporter().Export(FixtureWithRuntime(), exact).Markdown;
            True(exactMarkdown.Contains("1,000 items across 10 branches"), "Exact output omitted passive runtime data");
        }

        private static void JsonSchemaContract()
        {
            string json = new ContextExporter().Export(Fixture("B"), Options(ScopeMode.SelectedOnly)).Json;
            True(json.Contains("\"schemaVersion\": \"0.5\""), "schema version missing");
            True(json.Contains("\"selectedObjectIds\""), "scope field is not lower camel case");
            True(json.Contains("\"sourceNodeId\""), "edge field is not lower camel case");
            True(json.Contains("\"internalNodeName\""), "boundary display name missing");
            True(!json.Contains("\"SchemaVersion\""), "Pascal-case field leaked into JSON");
            True(json.Contains("\"executionSemantics\""), "execution semantics contract missing");
            True(json.Contains("\"runtimeTypeName\""), "runtime type contract missing");
        }

        private static void ExactJsonAppendix()
        {
            ContextExportOptions options = Options(ScopeMode.SelectedOnly); options.DetailLevel = DetailLevel.Exact; options.IncludeJsonAppendix = false;
            ContextSnapshot snapshot = Fixture("B"); snapshot.Nodes.Single(n => n.InstanceId == "B").Outputs.Add(new ContextParameter { Name = "Result", Direction = "output", RuntimeDataSummary = "items=10, branches=1" });
            ContextExportPackage package = new ContextExporter().Export(snapshot, options);
            True(package.Markdown.Contains(package.Json), "Exact Markdown appendix differs from saved JSON");
            True(package.Markdown.Contains("\"runtimeDataSummary\""), "complete parameter contract missing from Exact JSON");
        }

        private static void CurrentScriptSource()
        {
            ScriptSourceReadResult result = SafeScriptSourceReader.Read(new Python3ComponentFixture("print('lichen')"));
            True(result.Recognized, "current Python component was not recognized");
            Equal("Python 3", result.Language);
            Equal("print('lichen')", result.Source);
        }

        private static void LegacyPythonSource()
        {
            ScriptSourceReadResult result = SafeScriptSourceReader.Read(new ZuiPythonComponentFixture { Code = "a = x + 1" });
            True(result.Recognized, "legacy GhPython component was not recognized");
            Equal("Python 2 (IronPython)", result.Language);
            Equal("a = x + 1", result.Source);
        }

        private static void StructuredCSharpSource()
        {
            Component_CSNET_ScriptFixture fixture = new Component_CSNET_ScriptFixture();
            fixture.ScriptSource = new ScriptSourceFixture { UsingCode = "using Rhino;", ScriptCode = "A = x + 1;", AdditionalCode = "private int helper = 1;" };
            ScriptSourceReadResult result = SafeScriptSourceReader.Read(fixture);
            Equal("C#", result.Language);
            True(result.Source.Contains("using Rhino;"), "using section missing");
            True(result.Source.Contains("A = x + 1;"), "script body missing");
            True(result.Source.Contains("private int helper = 1;"), "additional section missing");
        }

        private static void ExpressionSource()
        {
            ScriptSourceReadResult result = SafeScriptSourceReader.Read(new Component_ExpressionFixture { Expression = "x^2 + y" });
            Equal("Grasshopper expression", result.Language);
            Equal("x^2 + y", result.Source);
        }

        private static void UnsupportedScriptSource()
        {
            ScriptSourceReadResult result = SafeScriptSourceReader.Read(new ThrowingScriptFixture());
            True(result.Recognized, "script-shaped component was not recognized");
            Equal("", result.Source);
            True(result.ExtractionNote.Contains("no safely readable source"), "missing safe failure note");
        }

        private static void BundledDependencyClassification()
        {
            ContextSnapshot s = Fixture();
            s.Nodes[0].AssemblyName = "RhinoCodePluginGH";
            s.Nodes[1].AssemblyName = "ScriptComponents";
            s.Nodes[2].AssemblyName = "XformComponents";
            s.Nodes.Add(new ContextNode { InstanceId = "K", Name = "Kangaroo Solver", Nickname = "Kangaroo Solver", AssemblyName = "Kangaroo2Component", AssemblyVersion = "2.0" });
            string markdown = new ContextExporter().Export(s, Options(ScopeMode.EntireDocument)).Markdown;
            True(!markdown.Contains("RhinoCodePluginGH 1.0 (third-party)"), "current script assembly labeled third-party");
            True(!markdown.Contains("ScriptComponents 1.0 (third-party)"), "classic script assembly labeled third-party");
            True(!markdown.Contains("XformComponents 1.0 (third-party)"), "native transform assembly labeled third-party");
            True(!markdown.Contains("Kangaroo2Component 2.0 (third-party)"), "bundled Kangaroo assembly labeled third-party");
        }

        private static void ScriptWorkflowWording()
        {
            ContextSnapshot s = Fixture("B");
            ContextNode node = s.Nodes.Single(n => n.InstanceId == "B");
            node.Name = "C# Script"; node.Nickname = "Corner Tool"; node.Description = "A C#.NET scriptable component.";
            node.Script = new ContextScript { Language = "C#", Source = "A = x;" };
            ContextDocument d = new ContextGraphService().BuildDocument(s, Options(ScopeMode.SelectedOnly));
            string operation = d.Analysis.DetectedOperations.Single();
            True(operation.Contains("contains a C# script"), "script workflow did not point to observed behavior");
            True(!operation.Contains("A C#.NET scriptable component"), "generic component metadata was presented as behavior");
        }

        private static void ArcLengthBehavior()
        {
            ContextNode node = ScriptNode("Python 3", "L = crv.GetLength()\nok, p = crv.LengthParameter(d, Tol)\nsub = crv.Trim(p0, p1)\nclosed = crv.IsClosed\nd0 = d0 % L");
            ScriptBehaviorSummary summary = ScriptBehaviorAnalyzer.Analyze(node);
            True(summary.Observations.Any(o => o.Contains("distances along that curve")), "arc-length inputs not described");
            True(summary.Evidence.Contains("Curve.LengthParameter"), "arc-length evidence missing");
        }

        private static void MeshSplitBehavior()
        {
            string source = "Mesh.CreateBooleanSplit(a,b); pieces[0].SplitDisjointPieces(); cutter.IsPointInside(p,t,false); boolContaminated=true; work.Split(cutter); StripCutterFaces(m,c,b,out n); work.Faces.CullDegenerateFaces();";
            ScriptBehaviorSummary summary = ScriptBehaviorAnalyzer.Analyze(ScriptNode("C#", source));
            True(summary.Observations.Any(o => o.Contains("outside and inside")), "mesh classification not described");
            True(summary.Evidence.Contains("Mesh.CreateBooleanSplit"), "mesh-split evidence missing");
        }

        private static void VariableFilletBehavior()
        {
            string source = "Curve.GetFilletPoints(a,b,radii[0],t1,t2,out x,out y,out p); curve.DuplicateSegments(); Curve.JoinCurves(parts); overlapping fillet; trimparams;";
            ScriptBehaviorSummary summary = ScriptBehaviorAnalyzer.Analyze(ScriptNode("C#", source));
            True(summary.Observations.Any(o => o.Contains("fillet arcs")), "fillet construction not described");
            True(summary.Observations.Any(o => o.Contains("overlapping fillets")), "overlap handling not described");
        }

        private static void UnknownScriptBehavior()
        {
            ContextNode node = ScriptNode("C#", "A = x + y;");
            ScriptBehaviorSummary summary = ScriptBehaviorAnalyzer.Analyze(node);
            Equal(0, summary.Observations.Count);
            ContextSnapshot s = new ContextSnapshot(); s.Nodes.Add(node);
            string markdown = new ContextExporter().Export(s, Options(ScopeMode.EntireDocument)).Markdown;
            True(markdown.Contains("No supported deterministic behavior pattern was recognized"), "unknown behavior was not disclosed");
        }

        private static ContextNode ScriptNode(string language, string source)
        {
            return new ContextNode { InstanceId = "script", TypeId = "script-type", Name = language.IndexOf("Python", StringComparison.OrdinalIgnoreCase) >= 0 ? "Python 3 Script" : "C# Script", Nickname = "Script", AssemblyName = "ScriptComponents", AssemblyVersion = "1.0", Script = new ContextScript { Language = language, Source = source } };
        }

        private static void NestedExecutionRegions()
        {
            ContextSnapshot s = new ContextSnapshot();
            ContextNode outerStart = ControlNode("OS", "Loop Start", true); ContextNode outerEnd = ControlNode("OE", "Loop End", false);
            ContextNode innerStart = ControlNode("IS", "Fast Loop Start", true); ContextNode innerEnd = ControlNode("IE", "Fast Loop End", false);
            innerStart.Inputs.Insert(0, new ContextParameter { Index = 0, Name = "Iterations", Direction = "input" });
            for (int i = 0; i < innerStart.Inputs.Count; i++) innerStart.Inputs[i].Index = i;
            ContextNode slider = new ContextNode { InstanceId = "N", Name = "Number Slider", Nickname = "Number Slider", PersistentValueSummary = "value=30", AssemblyName = "Grasshopper" };
            ContextNode work = new ContextNode { InstanceId = "W", Name = "Move", Nickname = "Move", Description = "Move geometry", AssemblyName = "XformComponents" };
            s.Nodes.AddRange(new[] { outerStart, outerEnd, innerStart, innerEnd, slider, work });
            s.Edges.Add(ControlEdge(outerStart, outerEnd)); s.Edges.Add(ControlEdge(innerStart, innerEnd));
            s.Edges.Add(new ContextEdge { SourceNodeId = "OS", SourceParameterIndex = 2, SourceParameterName = "Data", TargetNodeId = "IS", TargetParameterIndex = 1, TargetParameterName = "Data" });
            s.Edges.Add(new ContextEdge { SourceNodeId = "IS", SourceParameterIndex = 2, SourceParameterName = "Data", TargetNodeId = "W", TargetParameterIndex = 0, TargetParameterName = "Geometry" });
            s.Edges.Add(new ContextEdge { SourceNodeId = "W", SourceParameterIndex = 0, SourceParameterName = "Geometry", TargetNodeId = "IE", TargetParameterIndex = 2, TargetParameterName = "Data" });
            s.Edges.Add(new ContextEdge { SourceNodeId = "IE", SourceParameterIndex = 0, SourceParameterName = "Data", TargetNodeId = "OE", TargetParameterIndex = 2, TargetParameterName = "Data" });
            s.Edges.Add(new ContextEdge { SourceNodeId = "N", SourceParameterIndex = 0, SourceParameterName = "Number", TargetNodeId = "IS", TargetParameterIndex = 0, TargetParameterName = "Iterations" });
            ContextDocument d = new ContextGraphService().BuildDocument(s, Options(ScopeMode.EntireDocument));
            Equal(2, d.Analysis.ExecutionSemantics.Regions.Count);
            ContextExecutionRegion inner = d.Analysis.ExecutionSemantics.Regions.Single(r => r.StartNodeId == "IS");
            Equal(1, inner.NestingLevel); Equal("30", inner.IterationLimit);
            True(!d.Analysis.ExecutionSemantics.OrdinaryWireGraphHasCycle, "control region created a false wire cycle");
        }

        private static void ExecutionControllerClassification()
        {
            ContextSnapshot s = new ContextSnapshot();
            ContextNode timer = ExecutionNode("T", "Timer", "Grasshopper.Kernel.Special.GH_Timer"); timer.ExecutionMetadata = Meta("timerInterval", "100 ms"); timer.ControlLinks = new List<ContextControlLink> { new ContextControlLink { Role = "scheduled_target", TargetNodeId = "X" } };
            ContextNode dam = ExecutionNode("D", "Data Dam", "Grasshopper.Kernel.Components.GH_DataDamComponent"); dam.ExecutionMetadata = Meta("delayMilliseconds", "500");
            ContextNode recorder = ExecutionNode("R", "Data Recorder", "Grasshopper.Kernel.Special.GH_DataRecorder"); recorder.ExecutionMetadata = Meta("dataLimit", "100");
            ContextNode cluster = ExecutionNode("C", "Cluster", "Grasshopper.Kernel.Special.GH_Cluster"); cluster.ExecutionMetadata = Meta("storage", "embedded");
            ContextNode galapagos = ExecutionNode("G", "Galapagos", "GalapagosComponents.GalapagosObject"); galapagos.ControlLinks = new List<ContextControlLink> { new ContextControlLink { Role = "genome", TargetNodeId = "X" }, new ContextControlLink { Role = "fitness", TargetNodeId = "F" } };
            ContextNode gate = ExecutionNode("Q", "Stream Freeze / Gate", "Plugin.Gate"); gate.Description = "Gate switch controls whether streaming data passes and retains the last received data.";
            ContextNode button = ExecutionNode("B", "Button", "Grasshopper.Kernel.Special.GH_ButtonObject");
            ContextNode solver = ExecutionNode("K", "Kangaroo Solver", "Kangaroo2Component.KangarooGH2");
            ContextNode target = ExecutionNode("X", "Number Slider", "Grasshopper.Kernel.Special.GH_NumberSlider"); ContextNode fitness = ExecutionNode("F", "Fitness", "Grasshopper.Kernel.Parameters.Param_Number");
            s.Nodes.AddRange(new[] { timer, dam, recorder, cluster, galapagos, gate, button, solver, target, fitness });
            ContextExecutionSemantics semantics = new ContextGraphService().BuildDocument(s, Options(ScopeMode.EntireDocument)).Analysis.ExecutionSemantics;
            string[] kinds = semantics.Components.Select(c => c.Kind).ToArray();
            True(kinds.Contains("scheduler"), "timer not classified"); True(kinds.Contains("deferred_dataflow"), "dam not classified");
            True(kinds.Contains("stateful_recorder"), "recorder not classified"); True(kinds.Contains("reusable_subgraph"), "cluster not classified");
            True(kinds.Contains("optimization_solver"), "Galapagos not classified"); True(kinds.Contains("stateful_gate"), "gate not classified");
            True(kinds.Contains("manual_trigger"), "button not classified"); True(kinds.Contains("iterative_solver"), "iterative solver not classified");
            True(semantics.Components.Single(c => c.NodeId == "G").Behavior.Contains("fitness value Fitness"), "solver control links omitted");
        }

        private static void InspectedClusterGraph()
        {
            ContextSnapshot snapshot = new ContextSnapshot();
            ContextNode cluster = ExecutionNode("C", "Cluster", "Grasshopper.Kernel.Special.GH_Cluster");
            cluster.Nickname = "Panel Generator"; cluster.ExecutionMetadata = Meta("storage", "embedded");
            ContextNode move = ExecutionNode("I1", "Move", "Grasshopper.Kernel.Components.Transform.Move");
            move.AssemblyName = "Grasshopper"; move.Outputs.Add(new ContextParameter { Name = "Geometry", RuntimeDataSummary = "items=4, branches=1" });
            ContextNode area = ExecutionNode("I2", "Area", "Grasshopper.Kernel.Components.Analysis.Area"); area.AssemblyName = "Grasshopper";
            ContextNode script = ExecutionNode("I3", "Python 3 Script", "RhinoCodePlatform.GH.Components.Python3Component");
            script.AssemblyName = "InternalToolkit"; script.AssemblyVersion = "2.0"; script.Script = new ContextScript { Language = "Python 3", Source = "result = geometry" };
            ContextClusterGraph graph = new ContextClusterGraph { InspectionStatus = "inspected", DocumentId = "cluster-document" };
            graph.Nodes.AddRange(new[] { move, area, script });
            graph.Edges.Add(new ContextEdge { SourceNodeId = "I1", SourceParameterName = "Geometry", TargetNodeId = "I2", TargetParameterName = "Geometry" });
            graph.Dependencies.Add(new ContextDependency { Name = "InternalToolkit", Version = "2.0", Kind = "third_party" });
            graph.Analysis.DetectedOperations.Add("Move translates geometry before Area measures it.");
            graph.Analysis.InferredPurpose = "Possible inference: the cluster transforms geometry and measures the result.";
            cluster.ClusterGraph = graph; snapshot.Nodes.Add(cluster);

            ContextExportOptions options = Options(ScopeMode.EntireDocument); options.DetailLevel = DetailLevel.Exact; options.ClusterPurposeNotes["C"] = "Produces panel geometry for downstream evaluation.";
            ContextExportPackage package = new ContextExporter().Export(snapshot, options);
            ContextNode exported = package.Document.Nodes.Single();
            Equal(3, exported.ClusterGraph.Nodes.Count);
            Equal("Produces panel geometry for downstream evaluation.", exported.ClusterGraph.UserProvidedPurpose);
            True(package.Document.Dependencies.Any(d => d.Name == "InternalToolkit"), "internal dependency was not promoted to the outer export");
            True(package.Document.Analysis.DetectedOperations.Any(o => o.Contains("inspected internal workflow")), "cluster operations did not inform the outer workflow");
            True(package.Document.Analysis.ExecutionSemantics.Components.Single().Behavior.Contains("3 internal objects"), "cluster execution summary omitted internal counts");
            True(!package.Document.Analysis.ExecutionSemantics.Notes.Any(n => n.Contains("internals are opaque")), "inspected cluster was still described as opaque");
            True(package.Markdown.Contains("## Cluster Internals") && package.Markdown.Contains("Panel Generator") && package.Markdown.Contains("Move translates geometry"), "Markdown omitted inspected cluster behavior");
            True(package.Markdown.Contains("Internal Script Details") && package.Markdown.Contains("result = geometry"), "Markdown omitted enabled internal script source");
            True(package.Json.Contains("\"clusterGraph\"") && package.Json.Contains("\"inspectionStatus\": \"inspected\""), "exact JSON omitted the nested cluster graph");
            ContextDocument roundTrip = new ContextJsonSerializer().Deserialize(package.Json);
            Equal(3, roundTrip.Nodes.Single().ClusterGraph.Nodes.Count);

            ContextExportOptions excluded = Options(ScopeMode.EntireDocument); excluded.IncludeScriptSource = false; excluded.IncludeRuntimeSummary = false;
            ContextDocument filtered = new ContextGraphService().BuildDocument(snapshot, excluded);
            Equal("", filtered.Nodes.Single().ClusterGraph.Nodes.Single(n => n.InstanceId == "I3").Script.Source);
            Equal("", filtered.Nodes.Single().ClusterGraph.Nodes.Single(n => n.InstanceId == "I1").Outputs.Single().RuntimeDataSummary);
            Equal("result = geometry", script.Script.Source);
            Equal("items=4, branches=1", move.Outputs.Single().RuntimeDataSummary);
        }

        private static void ProtectedClusterGraph()
        {
            ContextSnapshot snapshot = new ContextSnapshot();
            ContextNode cluster = ExecutionNode("C", "Cluster", "Grasshopper.Kernel.Special.GH_Cluster");
            cluster.Nickname = "Facade Module"; cluster.Description = "Generates facade module results from base geometry and density controls.";
            cluster.Inputs.Add(new ContextParameter { Index = 0, Name = "Geometry", Nickname = "Base", AccessMode = "item", TypeHint = "Geometry" });
            cluster.Inputs.Add(new ContextParameter { Index = 1, Name = "Density", AccessMode = "item", TypeHint = "Number", Optional = true });
            cluster.Outputs.Add(new ContextParameter { Index = 0, Name = "Modules", AccessMode = "list", TypeHint = "Geometry", RuntimeDataSummary = "items=24, branches=1" });
            cluster.ClusterGraph = new ContextClusterGraph { InspectionStatus = "protected", InspectionNote = "The cluster is password-protected; Lichen did not request or attempt a password." };
            ContextNode source = ExecutionNode("S", "Surface", "Grasshopper.Kernel.Parameters.Param_Surface"); source.Nickname = "Envelope";
            ContextNode target = ExecutionNode("T", "Area", "Grasshopper.Kernel.Components.Analysis.Area"); target.Nickname = "Module Areas";
            snapshot.Nodes.AddRange(new[] { source, cluster, target });
            snapshot.SelectedObjectIds.Add("C");
            snapshot.Edges.Add(new ContextEdge { SourceNodeId = "S", SourceParameterName = "Surface", TargetNodeId = "C", TargetParameterIndex = 0, TargetParameterName = "Geometry" });
            snapshot.Edges.Add(new ContextEdge { SourceNodeId = "C", SourceParameterIndex = 0, SourceParameterName = "Modules", TargetNodeId = "T", TargetParameterName = "Geometry" });
            ContextExportOptions options = Options(ScopeMode.SelectedOnly); options.ClusterPurposeNotes["C"] = "Creates repeatable facade modules for downstream area checks.";
            ContextExportPackage package = new ContextExporter().Export(snapshot, options);
            ContextClusterGraph exported = package.Document.Nodes.Single(n => n.InstanceId == "C").ClusterGraph;
            True(package.Markdown.Contains("password-protected"), "protected-cluster explanation missing");
            True(exported.BlackBoxSummary.Contains("Exposed inputs: Base") && exported.BlackBoxSummary.Contains("Exposed outputs: Modules"), "protected cluster interface was not summarized");
            True(exported.BlackBoxSummary.Contains("Envelope.Surface") && exported.BlackBoxSummary.Contains("Module Areas.Geometry"), "protected cluster neighbors were not summarized");
            True(exported.BlackBoxSummary.Contains("items=24, branches=1"), "protected cluster runtime summary was omitted");
            Equal("Creates repeatable facade modules for downstream area checks.", exported.UserProvidedPurpose);
            True(package.Markdown.Contains("User-provided purpose for cluster Facade Module") && package.Markdown.Contains("Black-box observations"), "protected cluster purpose or black-box summary missing from Markdown");
            True(package.Json.Contains("\"userProvidedPurpose\"") && package.Json.Contains("\"blackBoxSummary\""), "protected cluster context missing from Exact JSON");
            True(package.Document.Analysis.ExecutionSemantics.Notes.Any(n => n.Contains("remain opaque")), "protected cluster opacity was not disclosed");
            True(package.Document.Analysis.ExecutionSemantics.Components.Single().Behavior.Contains("Internal inspection unavailable"), "protected cluster execution summary was ambiguous");
            True(package.Document.Analysis.DetectedOperations.Any(o => o.Contains("black-box observations")), "protected cluster black-box facts did not inform workflow analysis");
            True(cluster.ClusterGraph.UserProvidedPurpose == null && cluster.ClusterGraph.BlackBoxSummary == null, "export mutated the protected-cluster snapshot");
        }

        private static void DuplicateClusterPresentation()
        {
            ContextSnapshot snapshot = new ContextSnapshot();
            ContextNode first = ExecutionNode("aaaaaaaa-1111-1111-1111-111111111111", "Cluster", "Grasshopper.Kernel.Special.GH_Cluster"); first.Nickname = "Flow";
            first.ClusterGraph = new ContextClusterGraph { InspectionStatus = "protected", InspectionNote = "Protected.", DocumentId = "dddddddd-4444-4444-4444-444444444444" };
            ContextNode second = ExecutionNode("bbbbbbbb-2222-2222-2222-222222222222", "Cluster", "Grasshopper.Kernel.Special.GH_Cluster"); second.Nickname = "Flow";
            second.ClusterGraph = new ContextClusterGraph { InspectionStatus = "protected", InspectionNote = "Protected.", DocumentId = "dddddddd-4444-4444-4444-444444444444" };
            snapshot.Nodes.AddRange(new[] { first, second }); snapshot.SelectedObjectIds.Add(first.InstanceId); snapshot.SelectedObjectIds.Add(second.InstanceId);
            ContextExportOptions options = Options(ScopeMode.SelectedOnly); options.DetailLevel = DetailLevel.Technical;
            options.ClusterPurposeNotes[first.InstanceId] = "Re-aligns geometry between guide curves."; options.ClusterPurposeNotes[second.InstanceId] = "Re-aligns geometry between guide curves.";
            string markdown = new ContextExporter().Export(snapshot, options).Markdown;
            True(markdown.Contains("### Flow [aaaaaaaa]") && markdown.Contains("### Flow [bbbbbbbb]"), "duplicate cluster headings were not disambiguated");
            True(markdown.Contains("User-provided purpose for cluster Flow (2 instances): Re-aligns geometry between guide curves."), "shared duplicate-cluster purpose was not consolidated");
            Equal(1, Count(markdown, "User-provided purpose for cluster Flow (2 instances):"));
        }

        private static void ClusterPurposeConfidenceWording()
        {
            ContextSnapshot snapshot = new ContextSnapshot();
            ContextNode cluster = ExecutionNode("cluster", "Cluster", "Grasshopper.Kernel.Special.GH_Cluster"); cluster.Nickname = "Normalize";
            cluster.ClusterGraph = new ContextClusterGraph { InspectionStatus = "inspected" };
            cluster.ClusterGraph.Analysis.InferredPurpose = "Strong inference: the component sequence normalizes or rescales numeric values.";
            snapshot.Nodes.Add(cluster); snapshot.SelectedObjectIds.Add(cluster.InstanceId);
            string purpose = new ContextGraphService().BuildDocument(snapshot, Options(ScopeMode.SelectedOnly)).Analysis.InferredPurpose;
            Equal("Possible inference from inspected cluster internals: the component sequence normalizes or rescales numeric values.", purpose);
            True(!purpose.Contains(": Strong inference:"), "cluster-derived purpose retained nested confidence wording");
        }

        private static void ExecutionCycleDisclosure()
        {
            ContextSnapshot s = Fixture(); s.Edges.Add(Edge("C", "A"));
            ContextExecutionSemantics semantics = new ContextGraphService().BuildDocument(s, Options(ScopeMode.EntireDocument)).Analysis.ExecutionSemantics;
            True(semantics.OrdinaryWireGraphHasCycle, "wire cycle was not detected"); True(semantics.HasNonLinearBehavior, "cycle did not flag non-linear behavior");
        }

        private static void ScriptRoleInference()
        {
            ContextSnapshot s = new ContextSnapshot();
            ContextNode node = ScriptNode("Python 3", "L = crv.GetLength()\nok, p = crv.LengthParameter(d, Tol)\nsub = crv.Trim(p0, p1)"); s.Nodes.Add(node);
            ContextDocument d = new ContextGraphService().BuildDocument(s, Options(ScopeMode.EntireDocument));
            True(d.Analysis.InferredPurpose.Contains("Possible inference from recognized script behavior"), "script role did not inform cautious purpose inference");
            True(d.Analysis.InferredPurpose.Contains("broader design purpose remains uncertain"), "purpose uncertainty was omitted");
        }

        private static void ConditionalExpressionBehavior()
        {
            ContextNode node = ScriptNode("Grasshopper expression", "if(x>y, y, x)");
            ScriptBehaviorSummary summary = ScriptBehaviorAnalyzer.Analyze(node);
            True(summary.Observations.Single().Contains("Returns y when x > y; otherwise returns x"), "conditional expression was merely repeated");
        }

        private static void ScriptEvidencePresentation()
        {
            ContextSnapshot s = new ContextSnapshot();
            s.Nodes.Add(ScriptNode("C#", "Curve.GetFilletPoints(a,b,radii[0],t1,t2,out x,out y,out p); curve.DuplicateSegments(); Curve.JoinCurves(parts);"));
            string markdown = new ContextExporter().Export(s, Options(ScopeMode.EntireDocument)).Markdown;
            True(markdown.Contains("`Curve.GetFilletPoints`"), "evidence did not render as inline code");
            True(!markdown.Contains("\\`Curve.GetFilletPoints\\`"), "inline code was backslash escaped");
        }

        private static void WorkflowCondensation()
        {
            ContextSnapshot s = new ContextSnapshot();
            for (int i = 0; i < 3; i++) s.Nodes.Add(new ContextNode { InstanceId = "M" + i, Name = "Move", Nickname = "Move", Description = "Move geometry", AssemblyName = "XformComponents" });
            string markdown = new ContextExporter().Export(s, Options(ScopeMode.EntireDocument)).Markdown;
            True(markdown.Contains("(3 components)"), "duplicate workflow operations were not condensed");
        }

        private static void ExpandedExpressionBehavior()
        {
            ScriptBehaviorSummary conditional = ScriptBehaviorAnalyzer.Analyze(ScriptNode("Grasshopper expression", "if(x>1,1,0)"));
            True(conditional.Observations.Single().Contains("Returns 1 when x > 1; otherwise returns 0"), "numeric conditional was not described");
            Equal("produce a binary threshold flag", conditional.PossibleRole);
            ScriptBehaviorSummary normalized = ScriptBehaviorAnalyzer.Analyze(ScriptNode("Grasshopper expression", "(x-y)/(z-y)"));
            True(normalized.Observations.Single().Contains("producing a normalized ratio"), "normalization expression was not described");
            Equal("normalize a value between two reference bounds", normalized.PossibleRole);
            True(ScriptBehaviorAnalyzer.Analyze(ScriptNode("Grasshopper expression", "x/2")).Observations.Single().Contains("Divides x by 2"), "basic arithmetic was not described");
        }

        private static void OptimizationObjectiveDescription()
        {
            ContextSnapshot s = new ContextSnapshot();
            ContextNode genome1 = ExecutionNode("S1", "Number Slider", "Grasshopper.Kernel.Special.GH_NumberSlider");
            ContextNode genome2 = ExecutionNode("S2", "Number Slider", "Grasshopper.Kernel.Special.GH_NumberSlider");
            ContextNode normalized = ScriptNode("Grasshopper expression", "(x-y)/(z-y)"); normalized.InstanceId = "E1"; normalized.Nickname = "Normalized Area";
            ContextNode penalty = ScriptNode("Grasshopper expression", "if(x>1,1,0)"); penalty.InstanceId = "E2"; penalty.Nickname = "Intersection Penalty";
            ContextNode fitness = ExecutionNode("F", "Addition", "MathComponents.Addition"); fitness.Nickname = "Fitness Score";
            ContextNode galapagos = ExecutionNode("G", "Galapagos", "GalapagosComponents.GalapagosObject");
            galapagos.ExecutionMetadata = Meta("solver", "Evolutionary");
            galapagos.ControlLinks = new List<ContextControlLink> { new ContextControlLink { Role = "genome", TargetNodeId = "S1" }, new ContextControlLink { Role = "genome", TargetNodeId = "S2" }, new ContextControlLink { Role = "fitness", TargetNodeId = "F" } };
            s.Nodes.AddRange(new[] { genome1, genome2, normalized, penalty, fitness, galapagos });
            s.Edges.Add(new ContextEdge { SourceNodeId = "E1", SourceParameterName = "Result", TargetNodeId = "F", TargetParameterName = "A" });
            s.Edges.Add(new ContextEdge { SourceNodeId = "E2", SourceParameterName = "Result", TargetNodeId = "F", TargetParameterName = "B" });
            ContextDocument d = new ContextGraphService().BuildDocument(s, Options(ScopeMode.EntireDocument));
            string behavior = d.Analysis.ExecutionSemantics.Components.Single(c => c.NodeId == "G").Behavior;
            True(behavior.Contains("2 linked genomes (Number Slider (2 components))"), "genome components were not named");
            True(behavior.Contains("fitness value Fitness Score"), "fitness component was not named");
            True(behavior.Contains("Solver mode: Evolutionary"), "solver mode was omitted");
            True(behavior.Contains("normalize a value") && behavior.Contains("binary threshold flag"), "fitness construction was not described");
            True(d.Analysis.InferredPurpose.Contains("solver-controlled optimization"), "optimization did not inform cautious purpose inference");
        }

        private static ContextNode ControlNode(string id, string name, bool start)
        {
            ContextNode node = ExecutionNode(id, name, "Plugin." + name.Replace(" ", ""));
            if (start)
            {
                node.Inputs.Add(new ContextParameter { Index = 0, Name = "Data", Direction = "input" });
                node.Outputs.Add(new ContextParameter { Index = 0, Name = ">", Direction = "output" }); node.Outputs.Add(new ContextParameter { Index = 1, Name = "Counter", Direction = "output" }); node.Outputs.Add(new ContextParameter { Index = 2, Name = "Data", Direction = "output" });
            }
            else
            {
                node.Inputs.Add(new ContextParameter { Index = 0, Name = "<", Direction = "input" }); node.Inputs.Add(new ContextParameter { Index = 1, Name = "Exit", Direction = "input" }); node.Inputs.Add(new ContextParameter { Index = 2, Name = "Data", Direction = "input" });
                node.Outputs.Add(new ContextParameter { Index = 0, Name = "Data", Direction = "output" });
            }
            return node;
        }

        private static ContextEdge ControlEdge(ContextNode start, ContextNode end) { return new ContextEdge { SourceNodeId = start.InstanceId, SourceParameterIndex = 0, SourceParameterName = ">", TargetNodeId = end.InstanceId, TargetParameterIndex = 0, TargetParameterName = "<" }; }
        private static ContextNode ExecutionNode(string id, string name, string runtimeType) { return new ContextNode { InstanceId = id, TypeId = "type-" + id, Name = name, Nickname = name, Description = "", AssemblyName = "Fixture", AssemblyVersion = "1.0", RuntimeTypeName = runtimeType }; }
        private static List<ContextMetadataEntry> Meta(string key, string value) { return new List<ContextMetadataEntry> { new ContextMetadataEntry { Key = key, Value = value } }; }

        private static ContextSnapshot FixtureWithRuntime()
        {
            ContextSnapshot s = Fixture(); ContextNode passive = s.Nodes[0]; passive.Name = "Number"; passive.Nickname = "Number";
            passive.Outputs.Add(new ContextParameter { Name = "Number", Direction = "output", RuntimeDataSummary = "items=1000, branches=10" }); return s;
        }

        private static ContextDocument Build(ScopeMode mode, params string[] selected) { return new ContextGraphService().BuildDocument(Fixture(selected), Options(mode)); }
        private static ContextExportOptions Options(ScopeMode mode) { return new ContextExportOptions { ScopeMode = mode, DetailLevel = DetailLevel.Exact, IncludeScriptSource = true, IncludeRuntimeSummary = true, MaximumNodes = 500 }; }
        private static ContextSnapshot Fixture(params string[] selected)
        {
            ContextSnapshot s = new ContextSnapshot { Name = "fixture.gh", RhinoVersion = "8", GrasshopperVersion = "8" };
            s.Nodes.Add(Node("A")); s.Nodes.Add(Node("B")); s.Nodes.Add(Node("C")); s.SelectedObjectIds.AddRange(selected);
            s.Edges.Add(Edge("A", "B")); s.Edges.Add(Edge("B", "C")); return s;
        }

        private static ContextSnapshot LargeFixture(int count)
        {
            ContextSnapshot snapshot = new ContextSnapshot { Name = "large-fixture.gh", RhinoVersion = "8", GrasshopperVersion = "8" };
            string[] names = { "Move", "Area", "Remap Numbers", "Cull Pattern", "Panel", "Number Slider", "Merge", "Bounds" };
            for (int i = 0; i < count; i++)
            {
                string id = "N" + i.ToString("D4");
                ContextNode node = new ContextNode
                {
                    InstanceId = id, TypeId = "large-type-" + (i % names.Length), Name = names[i % names.Length],
                    Nickname = names[i % names.Length] + " " + i, Description = "Large deterministic fixture component " + i,
                    AssemblyName = i % 11 == 0 ? "FixturePlugin" : "Grasshopper", AssemblyVersion = "1.0.0.0",
                    RuntimeTypeName = "Fixture." + names[i % names.Length].Replace(" ", "")
                };
                node.Inputs.Add(new ContextParameter { Index = 0, Name = "Input", Direction = "input", RuntimeDataSummary = "items=" + (i + 1) + ", branches=" + ((i % 7) + 1) });
                node.Outputs.Add(new ContextParameter { Index = 0, Name = "Output", Direction = "output", RuntimeDataSummary = "items=" + (i + 2) + ", branches=" + ((i % 7) + 1) });
                if (i % 25 == 0) node.PersistentValueSummary = "value=" + i;
                if (i % 100 == 0) node.RuntimeMessages.Add(new ContextRuntimeMessage { Level = "remark", Message = "Fixture remark " + i });
                snapshot.Nodes.Add(node);
                if (i > 0) snapshot.Edges.Add(new ContextEdge { SourceNodeId = "N" + (i - 1).ToString("D4"), SourceParameterIndex = 0, SourceParameterName = "Output", TargetNodeId = id, TargetParameterIndex = 0, TargetParameterName = "Input" });
            }
            for (int groupIndex = 0; groupIndex < 10; groupIndex++)
            {
                ContextGroup group = new ContextGroup { InstanceId = "G" + groupIndex.ToString("D2"), Name = "Stage " + groupIndex };
                int start = groupIndex * 50; for (int i = start; i < start + 50; i++) group.MemberIds.Add("N" + i.ToString("D4"));
                snapshot.Groups.Add(group);
            }
            return snapshot;
        }
        private static ContextNode Node(string id) { return new ContextNode { InstanceId = id, TypeId = "type-" + id, Name = "Unknown " + id, Nickname = id, Description = "Fixture node " + id, AssemblyName = "Fixture", AssemblyVersion = "1.0" }; }
        private static ContextEdge Edge(string source, string target) { return new ContextEdge { SourceNodeId = source, SourceParameterIndex = 0, SourceParameterName = "out", TargetNodeId = target, TargetParameterIndex = 0, TargetParameterName = "in" }; }

        private static void Run(string name, Action test)
        {
            try { test(); passed++; Console.WriteLine("PASS " + name); }
            catch (Exception ex) { failed++; Console.WriteLine("FAIL " + name + ": " + ex.Message); }
        }
        private static void Equal<T>(T expected, T actual) { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception("Expected " + expected + " but received " + actual + "."); }
        private static void True(bool condition, string message) { if (!condition) throw new Exception(message); }
        private static void ThrowsInvalidOperation(Action action) { try { action(); } catch (InvalidOperationException) { return; } throw new Exception("Expected InvalidOperationException."); }
        private static void Sequence(IEnumerable<string> expected, IEnumerable<string> actual) { string a = String.Join(",", expected.ToArray()); string b = String.Join(",", actual.ToArray()); if (a != b) throw new Exception("Expected [" + a + "] but received [" + b + "]."); }
        private static int Count(string value, string token) { int count = 0, index = 0; while ((index = value.IndexOf(token, index, StringComparison.Ordinal)) >= 0) { count++; index += token.Length; } return count; }

        private sealed class Python3ComponentFixture
        {
            private readonly string source;
            public Python3ComponentFixture(string source) { this.source = source; }
            public bool TryGetSource(out string value) { value = source; return true; }
        }

        private sealed class ZuiPythonComponentFixture
        {
            public string Code { get; set; }
        }

        private sealed class Component_CSNET_ScriptFixture
        {
            public ScriptSourceFixture ScriptSource { get; set; }
        }

        private sealed class ScriptSourceFixture
        {
            public string UsingCode { get; set; }
            public string ScriptCode { get; set; }
            public string AdditionalCode { get; set; }
        }

        private sealed class Component_ExpressionFixture
        {
            public string Expression { get; set; }
        }

        private sealed class ThrowingScriptFixture
        {
            public string Code { get { throw new InvalidOperationException("fixture getter failed"); } }
        }
    }
}
