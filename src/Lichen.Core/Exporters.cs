using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Lichen.Core
{
    public sealed class ContextJsonSerializer
    {
        public string Serialize(ContextDocument document)
        {
            DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(ContextDocument));
            using (MemoryStream stream = new MemoryStream())
            {
                serializer.WriteObject(stream, document);
                return Pretty(Encoding.UTF8.GetString(stream.ToArray())).Replace("\r\n", "\n");
            }
        }

        public ContextDocument Deserialize(string json)
        {
            DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(ContextDocument));
            using (MemoryStream stream = new MemoryStream(Encoding.UTF8.GetBytes(json)))
                return (ContextDocument)serializer.ReadObject(stream);
        }

        private static string Pretty(string json)
        {
            StringBuilder output = new StringBuilder();
            bool quoted = false; bool escaped = false; int depth = 0;
            for (int i = 0; i < json.Length; i++)
            {
                char c = json[i];
                if (quoted)
                {
                    output.Append(c);
                    if (escaped) escaped = false;
                    else if (c == '\\') escaped = true;
                    else if (c == '"') quoted = false;
                    continue;
                }
                if (c == '"') { quoted = true; output.Append(c); }
                else if (c == '{' || c == '[') { output.Append(c); output.AppendLine(); depth++; Indent(output, depth); }
                else if (c == '}' || c == ']') { output.AppendLine(); depth--; Indent(output, depth); output.Append(c); }
                else if (c == ',') { output.Append(c); output.AppendLine(); Indent(output, depth); }
                else if (c == ':') output.Append(": ");
                else if (!Char.IsWhiteSpace(c)) output.Append(c);
            }
            return output.ToString();
        }

        private static void Indent(StringBuilder builder, int depth) { builder.Append(new string(' ', Math.Max(0, depth) * 2)); }
    }

    public static class RuntimeTreeShapeFormatter
    {
        public static string Format(int totalPathCount, IList<string> paths, IList<int> itemCounts)
        {
            int captured = Math.Min(totalPathCount, Math.Min(paths == null ? 0 : paths.Count, itemCounts == null ? 0 : itemCounts.Count));
            if (totalPathCount <= 0 || captured == 0) return "";

            string first, last;
            int commonCount;
            bool regular = TryRegularSequence(paths.Take(captured).ToList(), itemCounts.Take(captured).ToList(), out first, out last, out commonCount);
            string prefix = totalPathCount + (totalPathCount == 1 ? " path" : " paths");
            string result;
            if (regular)
            {
                result = prefix + (totalPathCount > captured ? "; first " + captured + ": " : ": ")
                    + first + " through " + last + " (" + Items(commonCount) + " each)";
            }
            else
            {
                List<string> samples = new List<string>();
                for (int i = 0; i < captured; i++) samples.Add(paths[i] + " (" + Items(itemCounts[i]) + ")");
                result = prefix + (totalPathCount > captured ? "; first " + captured + ": " : ": ") + String.Join(", ", samples.ToArray());
            }
            if (totalPathCount > captured) result += "; " + (totalPathCount - captured) + " additional paths not listed";
            return result;
        }

        private static bool TryRegularSequence(IList<string> paths, IList<int> counts, out string first, out string last, out int commonCount)
        {
            first = ""; last = ""; commonCount = 0;
            if (paths == null || counts == null || paths.Count < 2 || paths.Count != counts.Count) return false;
            List<int[]> indices = new List<int[]>();
            foreach (string path in paths)
            {
                Match match = Regex.Match(path ?? "", @"^\{(-?\d+(?:;-?\d+)*)\}$");
                if (!match.Success) return false;
                int[] parsed;
                try { parsed = match.Groups[1].Value.Split(';').Select(Int32.Parse).ToArray(); }
                catch { return false; }
                if (parsed.Length == 0 || indices.Count > 0 && parsed.Length != indices[0].Length) return false;
                indices.Add(parsed);
            }
            int expectedCount = counts[0];
            if (counts.Any(count => count != expectedCount)) return false;
            commonCount = expectedCount;
            for (int i = 1; i < indices.Count; i++)
            {
                for (int j = 0; j < indices[i].Length - 1; j++) if (indices[i][j] != indices[0][j]) return false;
                if (indices[i][indices[i].Length - 1] != indices[0][indices[0].Length - 1] + i) return false;
            }
            first = paths[0]; last = paths[paths.Count - 1]; return true;
        }

        private static string Items(int count) { return count + (count == 1 ? " item" : " items"); }
    }

    public sealed class MarkdownComposer
    {
        private sealed class DetailLine
        {
            public string NodeId { get; set; }
            public string Text { get; set; }
            public string RepeatSignature { get; set; }
            public string RepeatPrefix { get; set; }
            public string RepeatSuffix { get; set; }
            public int Sequence { get; set; }
        }

        public string Compose(ContextDocument document, ContextExportOptions options, string json)
        {
            StringBuilder text = new StringBuilder();
            ThallusPresentationModel thallusPresentation = ThallusPresentationAnalyzer.Analyze(document);
            text.AppendLine("# Lichen Grasshopper Context Handoff"); text.AppendLine();
            text.AppendLine("Lichen is a read-only Grasshopper context exporter that records selected graph facts and deterministic analysis without modifying the definition."); text.AppendLine();
            Section(text, "User-Provided Purpose");
            text.AppendLine(UserText(document.UserContext.Purpose, "Not provided.")); text.AppendLine();
            Section(text, "Requested Task");
            text.AppendLine(UserText(document.UserContext.RequestedTask, "Not provided."));
            if (!String.IsNullOrWhiteSpace(document.UserContext.Constraints)) text.AppendLine("\nUser-provided constraints: " + EscapeInline(document.UserContext.Constraints));
            text.AppendLine();

            Section(text, "Scope");
            text.AppendLine("- Mode: `" + document.Scope.Mode + "`");
            bool rootScope = String.Equals(document.Scope.Mode, "export_root", StringComparison.OrdinalIgnoreCase) || String.Equals(document.Scope.Mode, "thallus_root", StringComparison.OrdinalIgnoreCase);
            if (rootScope)
            {
                text.AppendLine("- Export Root: " + EscapeInline(String.IsNullOrWhiteSpace(document.Scope.RootLabel) ? "Lichen" : document.Scope.RootLabel));
                if (String.Equals(document.Scope.Mode, "thallus_root", StringComparison.OrdinalIgnoreCase))
                    text.AppendLine("- Connected outermost Thalli: " + (document.Scope.RootThallusIds == null ? 0 : document.Scope.RootThallusIds.Count));
                else text.AppendLine("- Connected X sources: " + (document.Scope.RootSourceObjectIds == null ? 0 : document.Scope.RootSourceObjectIds.Count));
            }
            if (!rootScope)
            {
                if (document.Scope.SelectedThallusIds != null && document.Scope.SelectedThallusIds.Count > 0)
                    text.AppendLine("- Selected Thalli: " + document.Scope.SelectedThallusIds.Count);
                text.AppendLine("- Originally selected objects: " + document.Scope.SelectedObjectIds.Count);
            }
            text.AppendLine("- Included objects: " + document.Nodes.Count);
            text.AppendLine("- Incoming boundary connections: " + document.BoundaryInputs.Count);
            text.AppendLine("- Outgoing boundary connections: " + document.BoundaryOutputs.Count);
            if (document.Scope.NodeLimitReached) text.AppendLine("- Warning: the configured node limit was reached.");
            text.AppendLine();

            Section(text, "Lichen Provenance Seal");
            WriteProvenanceSeal(text, document);
            text.AppendLine();

            Section(text, "Author Signals"); WriteAuthorSignals(text, document, thallusPresentation, options.DetailLevel); text.AppendLine();

            if (document.Thalli != null && document.Thalli.Count > 0)
            {
                Section(text, "Author-Defined Workflow Organization"); WriteThallusOrganization(text, document, options.DetailLevel, thallusPresentation); text.AppendLine();
            }

            Section(text, "Inferred Purpose");
            text.AppendLine(EscapeInline(document.Analysis.InferredPurpose)); text.AppendLine();

            Section(text, "Effective Inputs"); WriteBoundaries(text, document, document.BoundaryInputs, options.DetailLevel); text.AppendLine();
            Section(text, "Workflow Structure"); WriteExecutionSemantics(text, document, options.DetailLevel); text.AppendLine();
            Section(text, "Workflow Summary");
            WriteWorkflowSummary(text, document, thallusPresentation);
            text.AppendLine();
            Section(text, "Cluster Internals"); WriteClusterInternals(text, document, options.DetailLevel, options.IncludeScriptSource); text.AppendLine();
            Section(text, "Effective Outputs"); WriteEffectiveOutputs(text, document, options.DetailLevel); text.AppendLine();

            Section(text, "Data-Tree and Parameter Behavior"); WriteParameterBehavior(text, document, options.DetailLevel, thallusPresentation); text.AppendLine();
            Section(text, "Runtime Data Summary"); WriteRuntimeDataSummary(text, document, options.DetailLevel, thallusPresentation); text.AppendLine();
            Section(text, "Custom Scripts"); WriteScripts(text, document, options.IncludeScriptSource); text.AppendLine();
            Section(text, "Runtime Warnings and Errors"); WriteRuntimeMessages(text, document); text.AppendLine();
            Section(text, "Plugin Dependencies"); WriteDependencies(text, document);
            text.AppendLine();

            Section(text, "Uncertainties and Extraction Notes");
            List<string> notes = new List<string>(); notes.AddRange(document.Analysis.Uncertainties); notes.AddRange(document.ExtractionNotes);
            if (notes.Count == 0) text.AppendLine("None recorded."); else foreach (string note in notes.Distinct()) text.AppendLine("- " + EscapeInline(note));
            text.AppendLine();

            Section(text, "Component Inventory"); WriteInventory(text, document, options.DetailLevel, thallusPresentation); text.AppendLine();
            Section(text, options.DetailLevel == DetailLevel.Exact ? "Exact Connection List" : "Connection Summary"); WriteConnections(text, document, options.DetailLevel); text.AppendLine();
            Section(text, "Machine-Readable Graph");
            if (options.IncludeJsonAppendix || options.DetailLevel == DetailLevel.Exact)
            {
                string fence = FenceFor(json); text.AppendLine(fence + "json"); text.AppendLine(json); text.AppendLine(fence);
            }
            else text.AppendLine("The exact JSON appendix was excluded. Save the companion `.json` file for the machine-readable graph.");
            return text.ToString().Replace("\r\n", "\n");
        }

        private static void Section(StringBuilder text, string name) { text.AppendLine("## " + name); text.AppendLine(); }
        private static string UserText(string value, string fallback) { return String.IsNullOrWhiteSpace(value) ? fallback : "User-provided: " + EscapeInline(value); }

        private static void WriteBoundaries(StringBuilder text, ContextDocument document, List<ContextBoundaryPort> ports, DetailLevel level)
        {
            if (ports.Count == 0) { text.AppendLine("No boundary connections detected."); return; }
            List<string> lines = new List<string>();
            foreach (IGrouping<string, ContextBoundaryPort> group in ports.GroupBy(p => p.InternalNodeId + "|" + p.ParameterIndex).OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
            {
                ContextBoundaryPort first = group.First();
                string internalPort = BoundaryPortLabel(document, first.InternalNodeName, first.InternalParameterName, first.InternalNodeId, level);
                List<string> external = group.Select(p => BoundaryPortLabel(document, p.ExternalNodeName, p.ExternalParameterName, p.ExternalNodeId, level)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                if (level != DetailLevel.Exact && external.Any(label => String.Equals(label, internalPort, StringComparison.OrdinalIgnoreCase)))
                {
                    string collidedLabel = internalPort;
                    internalPort = BoundaryPortLabel(first.InternalNodeName, first.InternalParameterName, first.InternalNodeId, true);
                    external = group.Select(p =>
                    {
                        string readable = BoundaryPortLabel(p.ExternalNodeName, p.ExternalParameterName, p.ExternalNodeId, false);
                        return String.Equals(readable, collidedLabel, StringComparison.OrdinalIgnoreCase)
                            ? BoundaryPortLabel(p.ExternalNodeName, p.ExternalParameterName, p.ExternalNodeId, true)
                            : readable;
                    }).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                }
                string externalText = JoinBounded(external, 5);
                lines.Add(first.Direction == "input" ? externalText + " → " + internalPort : internalPort + " → " + externalText);
            }
            foreach (IGrouping<string, string> duplicate in lines.GroupBy(line => line, StringComparer.OrdinalIgnoreCase))
                text.AppendLine("- " + duplicate.Key + (duplicate.Count() > 1 ? " (" + duplicate.Count() + " separate connections)" : ""));
        }

        private static void WriteEffectiveOutputs(StringBuilder text, ContextDocument document, DetailLevel level)
        {
            if (!String.Equals(document.Scope.Mode, "export_root", StringComparison.OrdinalIgnoreCase))
            {
                WriteBoundaries(text, document, document.BoundaryOutputs, level);
                return;
            }

            Dictionary<string, ContextNode> nodes = document.Nodes.ToDictionary(n => n.InstanceId, StringComparer.OrdinalIgnoreCase);
            List<string> sourceIds = document.Scope.RootSourceObjectIds ?? new List<string>();
            if (sourceIds.Count == 0)
            {
                text.AppendLine("No component connected to the Export Root X input was captured.");
                return;
            }
            foreach (string sourceId in sourceIds)
            {
                ContextNode source;
                string label;
                if (!nodes.TryGetValue(sourceId, out source)) label = "object `" + sourceId + "`";
                else if (source.Outputs.Count == 1) label = NodePortLabel(document, source, DisplayName(source.Outputs[0]), "output");
                else label = DisambiguatedNodeLabel(document, source) + " (connected output)";
                text.AppendLine("- Export Root result: " + EscapeInline(label) + " → Lichen.X");
            }
        }

        private static void WriteThallusOrganization(StringBuilder text, ContextDocument document, DetailLevel level, ThallusPresentationModel presentation)
        {
            List<ContextThallus> thalli = (document.Thalli ?? new List<ContextThallus>()).OrderBy(t => t.InstanceId, StringComparer.OrdinalIgnoreCase).ToList();
            Dictionary<string, ContextThallus> byId = thalli.ToDictionary(t => t.InstanceId, StringComparer.OrdinalIgnoreCase);
            Dictionary<string, List<ContextThallus>> children = new Dictionary<string, List<ContextThallus>>(StringComparer.OrdinalIgnoreCase);
            foreach (ContextThallus thallus in thalli.Where(t => !String.IsNullOrWhiteSpace(t.ParentThallusId)))
            {
                List<ContextThallus> values;
                if (!children.TryGetValue(thallus.ParentThallusId, out values)) { values = new List<ContextThallus>(); children.Add(thallus.ParentThallusId, values); }
                values.Add(thallus);
            }
            foreach (List<ContextThallus> values in children.Values) values.Sort((a, b) => StringComparer.OrdinalIgnoreCase.Compare(presentation.ById[a.InstanceId].Label, presentation.ById[b.InstanceId].Label));

            text.AppendLine("User-provided hierarchy:");
            List<ContextThallus> roots = thalli.Where(t => String.IsNullOrWhiteSpace(t.ParentThallusId) || !byId.ContainsKey(t.ParentThallusId)).ToList();
            Dictionary<string, ContextThallus> rootsById = roots.ToDictionary(t => t.InstanceId, StringComparer.OrdinalIgnoreCase);
            HashSet<string> writtenRootIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string rootId in presentation.RootIds)
            {
                ContextThallus root;
                if (rootsById.TryGetValue(rootId, out root) && writtenRootIds.Add(rootId))
                    WriteThallusTree(text, root, children, presentation, level, 0);
            }
            foreach (ContextThallus root in roots.OrderBy(t => presentation.ById[t.InstanceId].Label, StringComparer.OrdinalIgnoreCase).ThenBy(t => t.InstanceId, StringComparer.OrdinalIgnoreCase))
                if (writtenRootIds.Add(root.InstanceId)) WriteThallusTree(text, root, children, presentation, level, 0);

            Dictionary<string, List<ContextThallus>> memberships = new Dictionary<string, List<ContextThallus>>(StringComparer.OrdinalIgnoreCase);
            foreach (ContextThallus thallus in thalli)
                foreach (string member in thallus.DirectMemberIds ?? new List<string>())
                {
                    List<ContextThallus> values;
                    if (!memberships.TryGetValue(member, out values)) { values = new List<ContextThallus>(); memberships.Add(member, values); }
                    values.Add(thallus);
                }
            List<string> shared = memberships.Where(pair => pair.Value.Count > 1).Select(pair => pair.Key).OrderBy(id => id, StringComparer.OrdinalIgnoreCase).ToList();
            if (shared.Count > 0) text.AppendLine("- Shared membership: " + shared.Count + " component" + (shared.Count == 1 ? " appears" : "s appear") + " in multiple Thalli; each component is serialized once.");

            Dictionary<string, string> uniqueOwner = memberships.Where(pair => pair.Value.Count == 1).ToDictionary(pair => pair.Key, pair => pair.Value[0].InstanceId, StringComparer.OrdinalIgnoreCase);
            HashSet<string> transitions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (ContextEdge edge in document.Edges)
            {
                string sourceOwner, targetOwner;
                if (!uniqueOwner.TryGetValue(edge.SourceNodeId, out sourceOwner) || !uniqueOwner.TryGetValue(edge.TargetNodeId, out targetOwner)
                    || String.Equals(sourceOwner, targetOwner, StringComparison.OrdinalIgnoreCase)) continue;
                transitions.Add(sourceOwner + "|" + targetOwner);
            }
            if (transitions.Count == 0) text.AppendLine("Observed cross-Thallus dataflow: none detected between uniquely owned members.");
            else
            {
                text.AppendLine("Observed cross-Thallus dataflow (organization, not literal execution order):");
                foreach (string transition in transitions.OrderBy(value => value, StringComparer.OrdinalIgnoreCase))
                {
                    string[] ids = transition.Split('|');
                    text.AppendLine("- " + EscapeInline(presentation.ById[ids[0]].Label) + " → " + EscapeInline(presentation.ById[ids[1]].Label));
                }
            }
        }

        private static void WriteThallusTree(StringBuilder text, ContextThallus thallus, Dictionary<string, List<ContextThallus>> children,
            ThallusPresentationModel presentation, DetailLevel level, int depth)
        {
            string indent = new string(' ', depth * 2);
            ThallusSemanticRegion region = presentation.ById[thallus.InstanceId];
            text.AppendLine(indent + "- " + EscapeInline(region.Label) + ": " + thallus.DirectMemberIds.Count + " direct, " + thallus.EffectiveMemberIds.Count + " effective members");
            if (!String.IsNullOrWhiteSpace(thallus.Description)) text.AppendLine(indent + "  - User-provided description: " + EscapeInline(thallus.Description));
            foreach (ContextMetadataEntry property in thallus.Properties ?? new List<ContextMetadataEntry>())
                text.AppendLine(indent + "  - User-provided property `" + EscapeInline(property.Key) + "`: " + EscapeInline(property.Value));
            if (region.ChildIds.Count > 0)
                text.AppendLine(indent + "  - Nested substages: " + EscapeInline(String.Join(", ", region.ChildIds.Select(id => presentation.ById[id].Label).ToArray())) + ".");
            WriteThallusSemanticFacts(text, region, level, indent + "  ");
            if (thallus.MissingMemberIds.Count > 0) text.AppendLine(indent + "  - Warning: " + thallus.MissingMemberIds.Count + " referenced members are missing.");
            List<ContextThallus> values;
            if (children.TryGetValue(thallus.InstanceId, out values)) foreach (ContextThallus child in values) WriteThallusTree(text, child, children, presentation, level, depth + 1);
        }

        private static void WriteThallusSemanticFacts(StringBuilder text, ThallusSemanticRegion region, DetailLevel level, string indent)
        {
            text.AppendLine(indent + "- Observed graph facts:");
            text.AppendLine(indent + "  - Thallus-boundary dataflow: " + CountPhrase(region.IncomingBoundaryCount, "incoming connection") + " and "
                + CountPhrase(region.OutgoingBoundaryCount, "outgoing connection") + ".");
            int operationLimit = level == DetailLevel.Brief ? 1 : region.Operations.Count;
            if (region.Operations.Count > 0)
            {
                text.AppendLine(indent + "  - Detected direct-member operations:");
                foreach (string operation in region.Operations.Take(operationLimit))
                    text.AppendLine(indent + "    - " + EscapeInline(BoundedOneLine(operation, level == DetailLevel.Brief ? 180 : 300)));
                if (region.Operations.Count > operationLimit)
                    text.AppendLine(indent + "    - " + (region.Operations.Count - operationLimit) + " additional direct-member operation"
                        + (region.Operations.Count - operationLimit == 1 ? " was" : "s were") + " omitted at Brief detail.");
            }
            if (level != DetailLevel.Brief && region.ScriptLabels.Count > 0)
                text.AppendLine(indent + "  - Scripts: " + CountPhrase(region.ScriptInstanceCount, "captured script") + ": " + EscapeInline(JoinBounded(region.ScriptLabels, 3)) + ".");
            if (level != DetailLevel.Brief && region.ThirdPartyDependencies.Count > 0)
                text.AppendLine(indent + "  - Third-party dependencies: " + EscapeInline(JoinBounded(region.ThirdPartyDependencies, 3)) + ".");
            if (level != DetailLevel.Brief && region.RuntimeParameterFactCount > 0)
                text.AppendLine(indent + "  - Runtime evidence: " + CountPhrase(region.RuntimeParameterFactCount, "runtime parameter fact") + ".");
            if (level != DetailLevel.Brief && region.RuntimeMessageCount > 0)
                text.AppendLine(indent + "  - Runtime messages: " + CountPhrase(region.RuntimeMessageCount, "runtime warning or error") + ".");
            if (region.SharedPeerMemberCount > 0)
                text.AppendLine(indent + "  - Shared peer membership: " + CountPhrase(region.SharedPeerMemberCount, "member")
                    + (region.SharedPeerMemberCount == 1 ? " also belongs" : " also belong") + " to "
                    + EscapeInline(JoinBounded(region.SharedPeerLabels, 3)) + "; attribution remains non-exclusive.");
            text.AppendLine(indent + "- Cautious Lichen inference: " + EscapeInline(BoundedOneLine(region.InferredPurpose, level == DetailLevel.Brief ? 220 : 420)));
        }

        private static void WriteParameterBehavior(StringBuilder text, ContextDocument document, DetailLevel level, ThallusPresentationModel presentation)
        {
            List<DetailLine> lines = new List<DetailLine>(); int sequence = 0;
            HashSet<string> emitted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, ContextNode> nodesById = document.Nodes.ToDictionary(node => node.InstanceId, StringComparer.OrdinalIgnoreCase);
            foreach (ContextNode node in document.Nodes)
            {
                if (!String.IsNullOrWhiteSpace(node.PersistentValueSummary) && !String.Equals(node.Name, "Scribble", StringComparison.OrdinalIgnoreCase)
                    && (!String.Equals(node.Name, "Panel", StringComparison.OrdinalIgnoreCase) || IsDataLikePanel(node)))
                {
                    string line = "- " + EscapeInline(ValueNodeLabel(document, node)) + ": " + EscapeInline(PersistentValueForPresentation(node));
                    if (emitted.Add(line)) lines.Add(new DetailLine { NodeId = node.InstanceId, Text = line, Sequence = sequence++ });
                }
                List<ContextParameter> parameters = node.Inputs.Concat(node.Outputs).ToList();
                List<List<ContextParameter>> treeGroups = level == DetailLevel.Brief
                    ? new List<List<ContextParameter>>()
                    : parameters.Where(p => ShouldPresentRuntimeTreeShape(document, nodesById, node, p, level)).GroupBy(p => p.RuntimeTreeShape).Select(g => g.ToList()).ToList();
                Dictionary<ContextParameter, string> mergedFacts = new Dictionary<ContextParameter, string>();
                foreach (List<ContextParameter> group in treeGroups)
                {
                    List<string> facts = group.Select(NonRuntimeParameterFacts).Distinct(StringComparer.Ordinal).ToList();
                    if (facts.Count == 1 && !String.IsNullOrWhiteSpace(facts[0])) foreach (ContextParameter parameter in group) mergedFacts[parameter] = facts[0];
                }
                foreach (ContextParameter parameter in parameters)
                {
                    if (IsStandaloneValueNode(node) && !String.IsNullOrWhiteSpace(node.PersistentValueSummary) && !HasModifier(parameter)) continue;
                    string facts = NonRuntimeParameterFacts(parameter);
                    if (String.IsNullOrWhiteSpace(facts) || mergedFacts.ContainsKey(parameter)) continue;
                    bool includeDirection = CrossDirectionFactsDiffer(node, parameter, NonRuntimeParameterFacts);
                    string label = NodePortLabel(document, node, DisplayName(parameter), parameter.Direction) + (includeDirection ? " (" + parameter.Direction + ")" : "");
                    string line = "- " + EscapeInline(label) + ": " + EscapeInline(facts);
                    if (emitted.Add(line)) lines.Add(new DetailLine { NodeId = node.InstanceId, Text = line, Sequence = sequence++ });
                }

                if (level == DetailLevel.Brief) continue;
                foreach (List<ContextParameter> grouped in treeGroups)
                {
                    List<string> ports = grouped.Select(parameter =>
                    {
                        bool includeDirection = CrossDirectionFactsDiffer(node, parameter, p => p.RuntimeTreeShape ?? "");
                        return DisplayName(parameter) + (includeDirection ? " (" + parameter.Direction + ")" : "");
                    }).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                    bool ownerCollision = grouped.Any(parameter => NodePortLabelCollides(document, node, DisplayName(parameter), parameter.Direction));
                    string owner = DisplayName(node) + (ownerCollision ? " [" + ShortId(node.InstanceId) + "]" : "");
                    string label = ports.Count == 1 ? owner + "." + ports[0] : owner + " \u2014 " + JoinBounded(ports, 6);
                    string sharedFacts;
                    bool hasSharedFacts = mergedFacts.TryGetValue(grouped[0], out sharedFacts);
                    string line = "- " + EscapeInline(label) + ": " + (hasSharedFacts ? EscapeInline(sharedFacts) + ", " : "") + "runtime tree: " + EscapeInline(grouped[0].RuntimeTreeShape);
                    if (emitted.Add(line))
                    {
                        string repeatSuffix = (ports.Count == 1 ? "." + ports[0] : " — " + JoinBounded(ports, 6)) + ": "
                            + (hasSharedFacts ? sharedFacts + ", " : "") + "runtime tree: " + grouped[0].RuntimeTreeShape;
                        lines.Add(new DetailLine
                        {
                            NodeId = node.InstanceId, Text = line, Sequence = sequence++, RepeatPrefix = DisplayName(node), RepeatSuffix = repeatSuffix,
                            RepeatSignature = RepeatComponentKey(node) + "|" + repeatSuffix
                        });
                    }
                }
            }
            WriteDetailLines(text, document, level, presentation, lines, level == DetailLevel.Brief
                ? "No noteworthy parameter modifiers or persistent data were extracted."
                : "No noteworthy parameter modifiers, persistent data, or runtime tree shapes were extracted.");
        }

        private static string NonRuntimeParameterFacts(ContextParameter parameter)
        {
            List<string> flags = new List<string>();
            if (parameter.Flatten) flags.Add("flatten"); if (parameter.Graft) flags.Add("graft"); if (parameter.Simplify) flags.Add("simplify"); if (parameter.Reverse) flags.Add("reverse");
            if (!String.IsNullOrWhiteSpace(parameter.Expression)) flags.Add("expression: " + parameter.Expression);
            if (!String.IsNullOrWhiteSpace(parameter.PersistentDataSummary)) flags.Add("persistent data: " + parameter.PersistentDataSummary);
            return String.Join(", ", flags.ToArray());
        }

        private static bool CrossDirectionFactsDiffer(ContextNode node, ContextParameter parameter, Func<ContextParameter, string> facts)
        {
            IEnumerable<ContextParameter> counterparts = String.Equals(parameter.Direction, "input", StringComparison.OrdinalIgnoreCase) ? node.Outputs : node.Inputs;
            string name = DisplayName(parameter); string value = facts(parameter) ?? "";
            return counterparts.Any(other => String.Equals(DisplayName(other), name, StringComparison.OrdinalIgnoreCase)
                && !String.Equals(facts(other) ?? "", value, StringComparison.Ordinal));
        }

        private static bool ShouldPresentRuntimeTreeShape(ContextDocument document, Dictionary<string, ContextNode> nodesById, ContextNode node, ContextParameter parameter, DetailLevel level)
        {
            if (parameter == null || String.IsNullOrWhiteSpace(parameter.RuntimeTreeShape) || level == DetailLevel.Brief) return false;
            if (level == DetailLevel.Exact || HasModifier(parameter) || IsExplicitTreeOperation(node)) return true;
            Match count = Regex.Match(parameter.RuntimeTreeShape, @"^(\d+) paths?");
            int paths;
            if (CrossDirectionFactsDiffer(node, parameter, p => p.RuntimeTreeShape ?? "")) return true;
            if (!count.Success || !Int32.TryParse(count.Groups[1].Value, out paths) || paths <= 1) return false;
            return IsRuntimeTreeTransition(document, nodesById, node, parameter);
        }

        private static bool IsRuntimeTreeTransition(ContextDocument document, Dictionary<string, ContextNode> nodesById, ContextNode node, ContextParameter parameter)
        {
            List<string> related = RelatedRuntimeTreeShapes(document, nodesById, node, parameter).Where(shape => !String.IsNullOrWhiteSpace(shape)).ToList();
            if (related.Count == 0) return true;
            if (related.Any(shape => String.Equals(shape, parameter.RuntimeTreeShape, StringComparison.Ordinal))) return false;
            string topology = TreeTopologySignature(parameter.RuntimeTreeShape);
            if (related.All(shape => !String.Equals(TreeTopologySignature(shape), topology, StringComparison.Ordinal))) return true;
            return IsIrregularTreeShape(parameter.RuntimeTreeShape);
        }

        private static IEnumerable<string> RelatedRuntimeTreeShapes(ContextDocument document, Dictionary<string, ContextNode> nodesById, ContextNode node, ContextParameter parameter)
        {
            if (String.Equals(parameter.Direction, "output", StringComparison.OrdinalIgnoreCase))
                return node.Inputs.Select(input => input.RuntimeTreeShape);

            if (document == null) return Enumerable.Empty<string>();
            List<string> shapes = new List<string>();
            foreach (ContextEdge edge in document.Edges.Where(edge => String.Equals(edge.TargetNodeId, node.InstanceId, StringComparison.OrdinalIgnoreCase)
                && ParameterMatches(edge.TargetParameterIndex, edge.TargetParameterName, parameter)))
            {
                ContextNode source;
                if (!nodesById.TryGetValue(edge.SourceNodeId, out source)) continue;
                ContextParameter sourceParameter = source.Outputs.FirstOrDefault(output => ParameterMatches(edge.SourceParameterIndex, edge.SourceParameterName, output));
                if (sourceParameter != null) shapes.Add(sourceParameter.RuntimeTreeShape);
            }
            return shapes;
        }

        private static bool ParameterMatches(int index, string name, ContextParameter parameter)
        {
            if (parameter == null) return false;
            if (!String.IsNullOrWhiteSpace(name) && (String.Equals(name, parameter.Name, StringComparison.OrdinalIgnoreCase)
                || String.Equals(name, parameter.Nickname, StringComparison.OrdinalIgnoreCase))) return true;
            return index == parameter.Index;
        }

        private static string TreeTopologySignature(string shape)
        {
            string withoutCounts = Regex.Replace(shape ?? "", @"\s*\(\d+\s+items?(?:\s+each)?\)", "", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            return Regex.Replace(withoutCounts, @"\s+", " ").Trim();
        }

        private static bool IsIrregularTreeShape(string shape)
        {
            MatchCollection counts = Regex.Matches(shape ?? "", @"\((\d+)\s+items?(?:\s+each)?\)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (counts.Cast<Match>().Select(match => match.Groups[1].Value).Distinct(StringComparer.Ordinal).Count() > 1) return true;
            return Regex.Matches(shape ?? "", @"\{[^}]+\}", RegexOptions.CultureInvariant).Count > 1
                && (shape ?? "").IndexOf(" through ", StringComparison.OrdinalIgnoreCase) < 0;
        }

        private static bool IsExplicitTreeOperation(ContextNode node)
        {
            string[] names = { "Graft Tree", "Flatten Tree", "Unflatten Tree", "Shift Paths", "Tree Branch", "Entwine", "Path Mapper", "Replace Paths", "Split Tree", "Simplify Tree", "Trim Tree", "Flip Matrix" };
            return node != null && names.Contains(node.Name, StringComparer.OrdinalIgnoreCase);
        }

        private static void WriteClusterInternals(StringBuilder text, ContextDocument document, DetailLevel level, bool includeScriptSource)
        {
            List<ContextNode> clusters = document.Nodes.Where(n => n.ClusterGraph != null)
                .OrderBy(DisplayName, StringComparer.OrdinalIgnoreCase).ThenBy(n => n.InstanceId, StringComparer.OrdinalIgnoreCase).ToList();
            if (clusters.Count == 0) { text.AppendLine("No clusters were included in this scope."); return; }
            foreach (ContextNode cluster in clusters) WriteClusterGraph(text, cluster, clusters, level, includeScriptSource, 0);
        }

        private static void WriteClusterGraph(StringBuilder text, ContextNode cluster, IList<ContextNode> siblingClusters, DetailLevel level, bool includeScriptSource, int depth)
        {
            ContextClusterGraph graph = cluster.ClusterGraph;
            text.AppendLine(new string('#', Math.Min(6, 3 + depth)) + " " + EscapeInline(ClusterDisplayLabel(cluster, siblingClusters)));
            text.AppendLine();
            text.AppendLine("- Inspection status: `" + EscapeInline(graph.InspectionStatus) + "`");
            if (!String.IsNullOrWhiteSpace(graph.InspectionNote)) text.AppendLine("- Note: " + EscapeInline(graph.InspectionNote));
            if (!String.IsNullOrWhiteSpace(graph.UserProvidedPurpose)) text.AppendLine("- User-provided purpose: " + EscapeInline(graph.UserProvidedPurpose));
            if (!String.IsNullOrWhiteSpace(graph.BlackBoxSummary)) text.AppendLine("- Black-box observations: " + EscapeInline(graph.BlackBoxSummary));
            if (!String.Equals(graph.InspectionStatus, "inspected", StringComparison.OrdinalIgnoreCase))
            {
                text.AppendLine();
                return;
            }

            text.AppendLine("- Internal graph: " + graph.Nodes.Count + " objects, " + graph.Edges.Count + " connections, and " + graph.Groups.Count + " groups" + (graph.NodeLimitReached ? " (truncated at the configured node limit)" : "") + ".");
            string purpose = graph.Analysis == null ? "" : graph.Analysis.InferredPurpose;
            if (!String.IsNullOrWhiteSpace(purpose)) text.AppendLine("- Internal purpose: " + EscapeInline(purpose));
            List<string> operations = graph.Analysis == null ? new List<string>() : graph.Analysis.DetectedOperations.Where(o => !String.IsNullOrWhiteSpace(o)).ToList();
            int operationLimit = level == DetailLevel.Brief ? 3 : 12;
            if (operations.Count > 0)
            {
                text.AppendLine("- Internal workflow:");
                foreach (string operation in operations.Take(operationLimit)) text.AppendLine("  - " + EscapeInline(operation));
                if (operations.Count > operationLimit) text.AppendLine("  - " + (operations.Count - operationLimit) + " additional operations are retained in the exact JSON.");
            }
            List<string> thirdParty = graph.Dependencies.Where(d => d.Kind == "third_party").Select(d => d.Name + " " + d.Version).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (thirdParty.Count > 0) text.AppendLine("- Internal third-party dependencies: " + EscapeInline(String.Join(", ", thirdParty.ToArray())) + ".");
            List<ContextNode> scripts = graph.Nodes.Where(n => n.Script != null).OrderBy(DisplayName, StringComparer.OrdinalIgnoreCase).ToList();
            if (scripts.Count > 0) text.AppendLine("- Internal scripts: " + EscapeInline(String.Join(", ", scripts.Select(s => DisplayName(s) + " (" + s.Script.Language + ")").ToArray())) + ".");
            text.AppendLine();
            if (scripts.Count > 0 && level != DetailLevel.Brief)
            {
                text.AppendLine(new string('#', Math.Min(6, 4 + depth)) + " Internal Script Details");
                text.AppendLine();
                ContextDocument nestedDocument = new ContextDocument { Nodes = graph.Nodes, Edges = graph.Edges, Analysis = graph.Analysis };
                WriteScripts(text, nestedDocument, includeScriptSource, Math.Min(6, 5 + depth));
            }
            List<ContextNode> nestedClusters = graph.Nodes.Where(n => n.ClusterGraph != null).OrderBy(DisplayName, StringComparer.OrdinalIgnoreCase).ThenBy(n => n.InstanceId, StringComparer.OrdinalIgnoreCase).ToList();
            foreach (ContextNode nested in nestedClusters) WriteClusterGraph(text, nested, nestedClusters, level, includeScriptSource, depth + 1);
        }

        private static void WriteRuntimeDataSummary(StringBuilder text, ContextDocument document, DetailLevel level, ThallusPresentationModel presentation)
        {
            if (level == DetailLevel.Brief) { text.AppendLine("Runtime data details are omitted at Brief detail level."); return; }
            List<DetailLine> lines = new List<DetailLine>(); int sequence = 0;
            foreach (ContextNode node in ContextGraphService.TopologicalOrder(document).Where(n => level == DetailLevel.Exact || !IsPassiveRuntimeNode(n)))
            {
                IEnumerable<ContextParameter> parameters = level == DetailLevel.Exact ? node.Inputs.Concat(node.Outputs) : node.Outputs;
                foreach (IGrouping<string, ContextParameter> group in parameters.Where(p => !String.IsNullOrWhiteSpace(p.RuntimeDataSummary) && (level == DetailLevel.Exact || IsNoteworthyRuntime(p.RuntimeDataSummary))).GroupBy(p => p.RuntimeDataSummary))
                {
                    List<ContextParameter> groupedParameters = group.ToList();
                    string names = String.Join(", ", groupedParameters.Select(DisplayName).Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
                    string owner = groupedParameters.Any(p => NodePortLabelCollides(document, node, DisplayName(p), p.Direction))
                        ? DisplayName(node) + " [" + ShortId(node.InstanceId) + "]"
                        : DisplayName(node);
                    string readable = ReadableRuntime(group.Key);
                    string line = "- " + EscapeInline(owner) + " — " + EscapeInline(names) + ": " + EscapeInline(readable);
                    string repeatSuffix = " — " + names + ": " + readable;
                    lines.Add(new DetailLine
                    {
                        NodeId = node.InstanceId, Text = line, Sequence = sequence++, RepeatPrefix = DisplayName(node), RepeatSuffix = repeatSuffix,
                        RepeatSignature = RepeatComponentKey(node) + "|" + repeatSuffix
                    });
                }
            }
            WriteDetailLines(text, document, level, presentation, lines, "No noteworthy already-computed runtime data was captured.");
        }

        private static void WriteDetailLines(StringBuilder text, ContextDocument document, DetailLevel level, ThallusPresentationModel presentation,
            List<DetailLine> lines, string emptyText)
        {
            if (lines.Count == 0) { text.AppendLine(emptyText); return; }
            if (level != DetailLevel.Technical)
            {
                foreach (DetailLine line in lines.OrderBy(value => value.Sequence)) text.AppendLine(line.Text);
                return;
            }

            Dictionary<string, int> topologicalOrder = ContextGraphService.TopologicalOrder(document).Select((node, index) => new { node.InstanceId, index })
                .ToDictionary(value => value.InstanceId, value => value.index, StringComparer.OrdinalIgnoreCase);
            if (!HasRegionDetail(presentation))
            {
                WriteCompactedDetailLines(text, lines.OrderBy(line => topologicalOrder.ContainsKey(line.NodeId) ? topologicalOrder[line.NodeId] : Int32.MaxValue)
                    .ThenBy(line => line.Sequence).ToList());
                return;
            }
            List<IGrouping<string, DetailLine>> groups = lines.GroupBy(line => DetailRegionKey(line.NodeId, presentation), StringComparer.OrdinalIgnoreCase)
                .OrderBy(group => DetailRegionOrder(group.Key, presentation)).ThenBy(group => DetailRegionLabelFromKey(group.Key, presentation), StringComparer.OrdinalIgnoreCase).ToList();
            for (int groupIndex = 0; groupIndex < groups.Count; groupIndex++)
            {
                IGrouping<string, DetailLine> group = groups[groupIndex];
                text.AppendLine("### " + EscapeHeading(DetailRegionLabelFromKey(group.Key, presentation))); text.AppendLine();
                List<DetailLine> ordered = group.OrderBy(line => topologicalOrder.ContainsKey(line.NodeId) ? topologicalOrder[line.NodeId] : Int32.MaxValue)
                    .ThenBy(line => line.Sequence).ToList();
                WriteCompactedDetailLines(text, ordered);
                if (groupIndex + 1 < groups.Count) text.AppendLine();
            }
        }

        private static void WriteCompactedDetailLines(StringBuilder text, List<DetailLine> lines)
        {
            HashSet<string> emittedSignatures = new HashSet<string>(StringComparer.Ordinal);
            foreach (DetailLine line in lines)
            {
                if (String.IsNullOrWhiteSpace(line.RepeatSignature)) { text.AppendLine(line.Text); continue; }
                if (!emittedSignatures.Add(line.RepeatSignature)) continue;
                List<DetailLine> matches = lines.Where(candidate => String.Equals(candidate.RepeatSignature, line.RepeatSignature, StringComparison.Ordinal)).ToList();
                List<string> ids = matches.Select(candidate => candidate.NodeId).Distinct(StringComparer.OrdinalIgnoreCase).Select(ShortId).ToList();
                if (ids.Count < 2) { text.AppendLine(line.Text); continue; }
                string escapedSuffix = EscapeInline(line.RepeatSuffix);
                string separator = escapedSuffix.StartsWith(".", StringComparison.Ordinal) ? "" : " ";
                text.AppendLine("- " + EscapeInline(line.RepeatPrefix) + " ×" + ids.Count + " [" + EscapeInline(String.Join(", ", ids.ToArray())) + "]"
                    + separator + escapedSuffix);
            }
        }

        private static string RepeatComponentKey(ContextNode node)
        {
            return (node == null ? "" : node.AssemblyName ?? "") + "|" + (node == null ? "" : node.AssemblyVersion ?? "") + "|"
                + (node == null ? "" : node.RuntimeTypeName ?? "") + "|" + (node == null ? "" : node.Name ?? "") + "|" + DisplayName(node);
        }

        private static IEnumerable<ContextNode> NodesInDetailOrder(ContextDocument document, ThallusPresentationModel presentation)
        {
            List<ContextNode> topological = ContextGraphService.TopologicalOrder(document).ToList();
            if (!HasRegionDetail(presentation)) return topological;
            Dictionary<string, int> order = topological.Select((node, index) => new { node.InstanceId, index })
                .ToDictionary(value => value.InstanceId, value => value.index, StringComparer.OrdinalIgnoreCase);
            return topological.OrderBy(node => DetailRegionOrder(DetailRegionKey(node.InstanceId, presentation), presentation))
                .ThenBy(node => DetailRegionLabel(node.InstanceId, presentation), StringComparer.OrdinalIgnoreCase)
                .ThenBy(node => order[node.InstanceId]);
        }

        private static bool HasRegionDetail(ThallusPresentationModel presentation)
        {
            return presentation != null && presentation.Regions != null && presentation.Regions.Count > 0;
        }

        private static string DetailRegionKey(string nodeId, ThallusPresentationModel presentation)
        {
            List<string> regionIds = SpecificDirectRegionIds(nodeId, presentation);
            if (regionIds.Count == 0) return "outside";
            return regionIds.Count == 1 ? "region:" + regionIds[0] : "shared:" + String.Join("|", regionIds.ToArray());
        }

        private static string DetailRegionLabel(string nodeId, ThallusPresentationModel presentation)
        {
            return DetailRegionLabelFromKey(DetailRegionKey(nodeId, presentation), presentation);
        }

        private static string DetailRegionLabelFromKey(string key, ThallusPresentationModel presentation)
        {
            if (key.StartsWith("region:", StringComparison.OrdinalIgnoreCase)) return RegionPathLabel(key.Substring(7), presentation);
            if (key.StartsWith("shared:", StringComparison.OrdinalIgnoreCase))
            {
                List<string> labels = key.Substring(7).Split('|').Select(id => RegionPathLabel(id, presentation)).ToList();
                return "Shared peer membership: " + String.Join("; ", labels.ToArray());
            }
            return "Outside direct Thallus membership";
        }

        private static int DetailRegionOrder(string key, ThallusPresentationModel presentation)
        {
            if (key.StartsWith("region:", StringComparison.OrdinalIgnoreCase))
            {
                int index = presentation.DetailRegionIds.FindIndex(id => String.Equals(id, key.Substring(7), StringComparison.OrdinalIgnoreCase));
                return index < 0 ? presentation.DetailRegionIds.Count : index;
            }
            return presentation.DetailRegionIds.Count + (key.StartsWith("shared:", StringComparison.OrdinalIgnoreCase) ? 1 : 2);
        }

        private static List<string> SpecificDirectRegionIds(string nodeId, ThallusPresentationModel presentation)
        {
            List<string> direct;
            if (presentation == null || presentation.DirectRegionIdsByNode == null
                || !presentation.DirectRegionIdsByNode.TryGetValue(nodeId, out direct)) return new List<string>();
            List<string> result = direct.Where(id => presentation.ById.ContainsKey(id)
                && !direct.Any(other => !String.Equals(id, other, StringComparison.OrdinalIgnoreCase) && IsRegionAncestor(id, other, presentation))).ToList();
            return result.OrderBy(id =>
            {
                int index = presentation.DetailRegionIds.FindIndex(value => String.Equals(value, id, StringComparison.OrdinalIgnoreCase));
                return index < 0 ? Int32.MaxValue : index;
            }).ThenBy(id => id, StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static bool IsRegionAncestor(string possibleAncestorId, string childId, ThallusPresentationModel presentation)
        {
            HashSet<string> visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase); string current = childId;
            while (!String.IsNullOrWhiteSpace(current) && visited.Add(current))
            {
                ThallusSemanticRegion region;
                if (!presentation.ById.TryGetValue(current, out region) || region.Thallus == null || String.IsNullOrWhiteSpace(region.Thallus.ParentThallusId)) return false;
                if (String.Equals(region.Thallus.ParentThallusId, possibleAncestorId, StringComparison.OrdinalIgnoreCase)) return true;
                current = region.Thallus.ParentThallusId;
            }
            return false;
        }

        private static string RegionPathLabel(string regionId, ThallusPresentationModel presentation)
        {
            List<string> labels = new List<string>(); HashSet<string> visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase); string current = regionId;
            while (!String.IsNullOrWhiteSpace(current) && visited.Add(current))
            {
                ThallusSemanticRegion region;
                if (!presentation.ById.TryGetValue(current, out region)) break;
                labels.Add(region.Label);
                current = region.Thallus == null ? "" : region.Thallus.ParentThallusId;
            }
            labels.Reverse();
            return labels.Count == 0 ? "Thallus [" + ShortId(regionId) + "]" : String.Join(" > ", labels.ToArray());
        }

        private static void WriteScripts(StringBuilder text, ContextDocument document, bool includeSource)
        {
            WriteScripts(text, document, includeSource, 3);
        }

        private static void WriteScripts(StringBuilder text, ContextDocument document, bool includeSource, int headingLevel)
        {
            List<ContextNode> nodes = document.Nodes.Where(n => n.Script != null).ToList();
            if (nodes.Count == 0) { text.AppendLine("No script components detected."); return; }
            Dictionary<string, string> sourceOwners = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (ContextNode node in nodes)
            {
                text.AppendLine(new string('#', Math.Max(1, Math.Min(6, headingLevel))) + " " + EscapeHeading(DisplayName(node))); text.AppendLine();
                text.AppendLine("Language: " + EscapeInline(node.Script.Language));
                if (!String.IsNullOrWhiteSpace(node.Script.ExtractionNote)) text.AppendLine("\nExtraction note: " + EscapeInline(node.Script.ExtractionNote));
                ScriptBehaviorSummary behavior = ScriptBehaviorAnalyzer.Analyze(node);
                text.AppendLine();
                if (!String.IsNullOrWhiteSpace(behavior.AuthorDescription)) text.AppendLine("Author-provided description: " + EscapeInline(behavior.AuthorDescription) + "\n");
                WriteScriptInterface(text, node);
                WriteScriptGraphRole(text, document, node);
                text.AppendLine("Observed behavior:");
                if (behavior.Observations.Count == 0)
                    text.AppendLine("- No supported deterministic behavior pattern was recognized.");
                else foreach (string observation in behavior.Observations) text.AppendLine("- " + EscapeInline(observation));
                if (!String.IsNullOrWhiteSpace(behavior.PossibleRole))
                    text.AppendLine("- Possible role in this workflow: " + EscapeInline(behavior.PossibleRole) + " (recognized source pattern; broader design intent remains uncertain).");
                if (behavior.Evidence.Count > 0)
                    text.AppendLine("- Evidence: " + String.Join(", ", behavior.Evidence.Select(CodeSpan).ToArray()) + ".");
                if (behavior.DetectedCalls.Count > 0)
                    text.AppendLine("- Additional detected calls: " + String.Join(", ", behavior.DetectedCalls.Select(CodeSpan).ToArray()) + ".");
                if (includeSource && !String.IsNullOrWhiteSpace(node.Script.Source))
                {
                    string sourceKey = (node.Script.Language ?? "") + "\n" + node.Script.Source;
                    string sourceOwner;
                    if (sourceOwners.TryGetValue(sourceKey, out sourceOwner))
                    {
                        text.AppendLine();
                        text.AppendLine("Source is identical to another " + EscapeInline(sourceOwner) + " component above and is not repeated in Markdown. The exact JSON record retains the source for this component.");
                    }
                    else
                    {
                        sourceOwners.Add(sourceKey, DisplayName(node));
                        string fence = FenceFor(node.Script.Source); text.AppendLine(); text.AppendLine(fence + LanguageTag(node.Script.Language)); text.AppendLine(node.Script.Source); text.AppendLine(fence);
                    }
                }
                else text.AppendLine("\nSource was excluded or unavailable.");
                text.AppendLine();
            }
        }

        private static void WriteScriptInterface(StringBuilder text, ContextNode node)
        {
            string inputs = node.Inputs.Count == 0 ? "none exposed" : String.Join(", ", node.Inputs.Select(p => EscapeInline(DisplayName(p)) + " (" + EscapeInline(p.AccessMode) + ")").ToArray());
            string outputs = node.Outputs.Count == 0 ? "none exposed" : String.Join(", ", node.Outputs.Select(p => EscapeInline(DisplayName(p)) + " (" + EscapeInline(p.AccessMode) + ")").ToArray());
            text.AppendLine("Inputs: " + inputs + ".");
            text.AppendLine("Outputs: " + outputs + ".");
        }

        private static void WriteScriptGraphRole(StringBuilder text, ContextDocument document, ContextNode node)
        {
            Dictionary<string, ContextNode> nodes = document.Nodes.ToDictionary(n => n.InstanceId, StringComparer.OrdinalIgnoreCase);
            List<string> upstream = new List<string>(); List<string> downstream = new List<string>();
            foreach (ContextEdge edge in document.Edges)
            {
                ContextNode related;
                if (String.Equals(edge.TargetNodeId, node.InstanceId, StringComparison.OrdinalIgnoreCase) && nodes.TryGetValue(edge.SourceNodeId, out related)) upstream.Add(DisplayName(related));
                if (String.Equals(edge.SourceNodeId, node.InstanceId, StringComparison.OrdinalIgnoreCase) && nodes.TryGetValue(edge.TargetNodeId, out related)) downstream.Add(DisplayName(related));
            }
            upstream = upstream.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
            downstream = downstream.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
            if (upstream.Count > 0) text.AppendLine("Receives data from: " + EscapeInline(JoinBounded(upstream, 6)) + ".");
            if (downstream.Count > 0) text.AppendLine("Sends results to: " + EscapeInline(JoinBounded(downstream, 6)) + ".");
            text.AppendLine();
        }

        private static void WriteExecutionSemantics(StringBuilder text, ContextDocument document, DetailLevel level)
        {
            ContextExecutionSemantics semantics = document.Analysis == null ? null : document.Analysis.ExecutionSemantics;
            if (semantics == null || !semantics.HasNonLinearBehavior)
            {
                text.AppendLine("No stateful or non-linear execution controllers were detected; the visible graph is treated as ordinary dataflow.");
                return;
            }
            foreach (ContextExecutionRegion region in semantics.Regions)
            {
                string indent = new string(' ', region.NestingLevel * 2);
                string line = indent + "- Iterative region: " + EscapeInline(region.Label) + " (" + region.NodeIds.Count + " components";
                if (!String.IsNullOrWhiteSpace(region.IterationLimit)) line += ", configured iteration limit " + EscapeInline(region.IterationLimit);
                line += ").";
                if (region.CarriedValues.Count > 0) line += " Carries: " + EscapeInline(JoinBounded(region.CarriedValues, 6)) + ".";
                if (level == DetailLevel.Exact) line += " Start `" + region.StartNodeId + "`; end `" + region.EndNodeId + "`.";
                text.AppendLine(line);
                if (level != DetailLevel.Brief)
                {
                    List<string> members = RegionMemberLabels(document, region);
                    if (members.Count > 0) text.AppendLine(indent + "  - Contains: " + EscapeInline(JoinBounded(members, 10)) + ".");
                }
            }
            IEnumerable<IGrouping<string, ContextExecutionComponent>> componentGroups = semantics.Components.GroupBy(c => ExecutionComponentLabel(document, c) + "|" + c.Kind + "|" + c.Behavior, StringComparer.OrdinalIgnoreCase);
            foreach (IGrouping<string, ContextExecutionComponent> group in componentGroups)
            {
                ContextExecutionComponent component = group.First();
                string line = "- " + EscapeInline(ExecutionComponentLabel(document, component)) + " [" + EscapeInline(component.Kind.Replace('_', ' ')) + "]: " + EscapeInline(component.Behavior);
                if (level == DetailLevel.Exact)
                    line += " Node" + (group.Count() == 1 ? " `" + component.NodeId + "`" : "s " + String.Join(", ", group.Select(c => "`" + c.NodeId + "`").ToArray())) + ".";
                else if (group.Count() > 1) line += " (" + group.Count() + " components)";
                text.AppendLine(line);
            }
            foreach (string note in semantics.Notes) text.AppendLine("- Note: " + EscapeInline(note));
        }

        private static void WriteWorkflowSummary(StringBuilder text, ContextDocument document, ThallusPresentationModel thallusPresentation)
        {
            if (thallusPresentation.RootIds.Count > 0)
            {
                WriteThallusWorkflowSummary(text, thallusPresentation);
                return;
            }
            if (document.Analysis.ExecutionSemantics != null && document.Analysis.ExecutionSemantics.HasNonLinearBehavior)
                text.AppendLine("The following is a condensed dataflow-operation summary, not literal execution order. See Workflow Structure above.\n");
            if (document.Analysis.DetectedOperations.Count == 0) { text.AppendLine("No operations were extracted."); return; }
            List<string> ordered = new List<string>(); Dictionary<string, int> counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, List<ContextNode>> collidingClusters = CollidingClusters(document);
            foreach (string rawOperation in document.Analysis.DetectedOperations)
            {
                string operation = CorrelateClusterOperation(rawOperation, collidingClusters, document);
                if (!counts.ContainsKey(operation)) { counts.Add(operation, 0); ordered.Add(operation); }
                counts[operation]++;
            }
            for (int i = 0; i < ordered.Count; i++)
            {
                string suffix = counts[ordered[i]] > 1 ? " (" + counts[ordered[i]] + " components)" : "";
                text.AppendLine((i + 1) + ". " + EscapeInline(ordered[i]) + suffix);
            }
        }

        private static void WriteThallusWorkflowSummary(StringBuilder text, ThallusPresentationModel model)
        {
            text.AppendLine("The author-defined outer regions are summarized from observed cross-Thallus dataflow; arrows are not guaranteed execution order.");
            text.AppendLine();
            if (model.RootIds.Count == 1)
                text.AppendLine("- Outermost region: " + EscapeInline(model.ById[model.RootIds[0]].Label) + ". Its nested substages and bounded semantic facts are summarized under Author-Defined Workflow Organization.");
            else text.AppendLine("- Outermost regions: " + EscapeInline(String.Join(", ", model.RootIds.Select(id => model.ById[id].Label).ToArray())) + ".");

            if (model.ParallelEntryIds.Count > 0)
                text.AppendLine("- Parallel entry branches (observed topology): " + EscapeInline(String.Join(", ", model.ParallelEntryIds.Select(id => model.ById[id].Label).ToArray())) + ".");

            if (model.Transitions.Count == 0) text.AppendLine("- Observed handoffs: none detected between uniquely owned outer-region members.");
            else
            {
                text.AppendLine("- Observed handoffs:");
                foreach (ThallusFlowTransition transition in model.Transitions)
                    text.AppendLine("  - " + EscapeInline(model.ById[transition.SourceId].Label) + " -> " + EscapeInline(model.ById[transition.TargetId].Label));
            }

            foreach (ThallusFlowConvergence convergence in model.Convergences)
                text.AppendLine("- Observed convergence: " + EscapeInline(model.ById[convergence.TargetId].Label) + " receives cross-Thallus flow from "
                    + EscapeInline(String.Join(", ", convergence.SourceIds.Select(id => model.ById[id].Label).ToArray())) + ".");
            foreach (List<string> cycle in model.CycleGroups)
                text.AppendLine("- Observed cycle: " + EscapeInline(String.Join(" <-> ", cycle.Select(id => model.ById[id].Label).ToArray())) + "; no linear order is inferred within this group.");
            if (model.AmbiguousOverlapEdgeCount > 0)
                text.AppendLine("- " + CountPhrase(model.AmbiguousOverlapEdgeCount, "cross-Thallus edge")
                    + (model.AmbiguousOverlapEdgeCount == 1 ? " remains" : " remain") + " unattributed because peer overlap prevents exclusive attribution.");
            text.AppendLine("- Component-level operations are bounded within each Thallus summary above; Exact JSON retains the complete captured graph and analysis.");
        }

        private static void WriteProvenanceSeal(StringBuilder text, ContextDocument document)
        {
            ContextExportSignature signature = document.ExportSignature;
            if (signature == null || String.IsNullOrWhiteSpace(signature.ContextFingerprint))
            {
                text.AppendLine("No provenance seal was generated.");
                return;
            }
            text.AppendLine("- Product: `" + EscapeInline(signature.Product) + "`");
            text.AppendLine("- Exporter: `" + EscapeInline(signature.ExporterVersion) + "`");
            text.AppendLine("- Schema: `" + EscapeInline(document.SchemaVersion) + "`");
            text.AppendLine("- Context seal: `LCHN-" + signature.ContextFingerprint.Substring(0, Math.Min(12, signature.ContextFingerprint.Length)).ToUpperInvariant() + "`");
            text.AppendLine("- Verification: deterministic SHA-256 content fingerprint; no timestamp, user, machine, or file-path data is included.");
        }

        private static List<string> RegionMemberLabels(ContextDocument document, ContextExecutionRegion region)
        {
            Dictionary<string, ContextNode> nodes = document.Nodes.ToDictionary(n => n.InstanceId, StringComparer.OrdinalIgnoreCase);
            List<ContextNode> clusters = document.Nodes.Where(n => n.ClusterGraph != null).ToList();
            List<string> labels = new List<string>();
            foreach (string id in region.NodeIds.Where(id => !String.Equals(id, region.StartNodeId, StringComparison.OrdinalIgnoreCase) && !String.Equals(id, region.EndNodeId, StringComparison.OrdinalIgnoreCase)))
            {
                ContextNode node;
                if (!nodes.TryGetValue(id, out node)) continue;
                labels.Add(node.ClusterGraph == null ? DisambiguatedNodeLabel(document, node) : ClusterDisplayLabel(node, clusters));
            }
            return labels.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static string ExecutionComponentLabel(ContextDocument document, ContextExecutionComponent component)
        {
            ContextNode node = document.Nodes.FirstOrDefault(n => String.Equals(n.InstanceId, component.NodeId, StringComparison.OrdinalIgnoreCase));
            if (node == null) return component.NodeName;
            if (node.ClusterGraph != null) return ClusterDisplayLabel(node, document.Nodes.Where(n => n.ClusterGraph != null).ToList());
            return component.NodeName;
        }

        private static Dictionary<string, List<ContextNode>> CollidingClusters(ContextDocument document)
        {
            return document.Nodes.Where(n => n.ClusterGraph != null)
                .GroupBy(DisplayName, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1)
                .ToDictionary(g => g.Key, g => g.OrderBy(n => n.InstanceId, StringComparer.OrdinalIgnoreCase).ToList(), StringComparer.OrdinalIgnoreCase);
        }

        private static string CorrelateClusterOperation(string operation, Dictionary<string, List<ContextNode>> clusters, ContextDocument document)
        {
            foreach (KeyValuePair<string, List<ContextNode>> pair in clusters.OrderByDescending(p => p.Key.Length))
            {
                string prefix = pair.Key + ":";
                if (!operation.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || pair.Value.Count == 0) continue;
                ContextNode node = pair.Value.FirstOrDefault(n => ClusterOperationMatches(operation, n)) ?? pair.Value[0];
                pair.Value.Remove(node);
                return ClusterDisplayLabel(node, document.Nodes.Where(n => n.ClusterGraph != null).ToList()) + operation.Substring(pair.Key.Length);
            }
            return operation;
        }

        private static bool ClusterOperationMatches(string operation, ContextNode node)
        {
            if (node.ClusterGraph == null) return false;
            if (!String.IsNullOrWhiteSpace(node.ClusterGraph.BlackBoxSummary))
                return operation.IndexOf(node.ClusterGraph.BlackBoxSummary, StringComparison.Ordinal) >= 0;
            if (node.ClusterGraph.Analysis == null) return false;
            string evidence = node.ClusterGraph.Analysis.DetectedOperations.FirstOrDefault(o => !String.IsNullOrWhiteSpace(o));
            return !String.IsNullOrWhiteSpace(evidence) && operation.IndexOf(evidence.Trim().TrimEnd('.', ';', ':'), StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static void WriteRuntimeMessages(StringBuilder text, ContextDocument document)
        {
            bool any = false;
            foreach (ContextNode node in document.Nodes)
                foreach (ContextRuntimeMessage message in node.RuntimeMessages)
                { any = true; text.AppendLine("- " + EscapeInline(message.Level) + " — " + EscapeInline(DisambiguatedNodeLabel(document, node)) + ": " + EscapeInline(message.Message)); }
            if (!any) text.AppendLine("No captured runtime warnings or errors.");
        }

        private static void WriteAuthorSignals(StringBuilder text, ContextDocument document, ThallusPresentationModel presentation, DetailLevel level)
        {
            bool any = false;
            const int groupMemberDetailLimit = 5;
            Dictionary<string, ContextNode> nodes = document.Nodes.ToDictionary(node => node.InstanceId, StringComparer.OrdinalIgnoreCase);
            foreach (ContextGroup group in document.Groups.Where(g => !String.IsNullOrWhiteSpace(g.Name) && !String.Equals(g.Name, "Group", StringComparison.OrdinalIgnoreCase)))
            {
                string line = "- Group “" + EscapeInline(group.Name) + "”: " + CountPhrase(group.MemberIds.Count, "member");
                if (level != DetailLevel.Brief && group.MemberIds.Count > 0 && group.MemberIds.Count <= groupMemberDetailLimit)
                {
                    List<string> memberLabels = group.MemberIds.OrderBy(id => id, StringComparer.OrdinalIgnoreCase).Select(id =>
                    {
                        ContextNode node;
                        return nodes.TryGetValue(id, out node)
                            ? EscapeInline(DisplayName(node)) + " [" + ShortId(node.InstanceId) + "]"
                            : "object [" + ShortId(id) + "] (not captured)";
                    }).ToList();
                    line += " — " + String.Join(", ", memberLabels.ToArray());
                    if (HasRegionDetail(presentation))
                    {
                        List<string> regions = group.MemberIds.Select(id => DetailRegionLabel(id, presentation))
                            .Where(label => !String.Equals(label, "Outside direct Thallus membership", StringComparison.OrdinalIgnoreCase))
                            .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(label => label, StringComparer.OrdinalIgnoreCase).ToList();
                        if (regions.Count == 1) line += "; region: " + EscapeInline(regions[0]);
                        else if (regions.Count > 1) line += "; regions: " + EscapeInline(JoinBounded(regions, 3));
                    }
                }
                any = true; text.AppendLine(line);
            }
            foreach (ContextNode node in document.Nodes.Where(n => (String.Equals(n.Name, "Scribble", StringComparison.OrdinalIgnoreCase)
                || String.Equals(n.Name, "Panel", StringComparison.OrdinalIgnoreCase) && !IsDataLikePanel(n)) && !String.IsNullOrWhiteSpace(n.PersistentValueSummary)))
            {
                any = true; text.AppendLine("- " + EscapeInline(ValueNodeLabel(document, node)) + ": " + EscapeInline(node.PersistentValueSummary));
            }
            List<ContextNode> purposeClusters = document.Nodes.Where(n => n.ClusterGraph != null && !String.IsNullOrWhiteSpace(n.ClusterGraph.UserProvidedPurpose))
                .OrderBy(DisplayName, StringComparer.OrdinalIgnoreCase).ThenBy(n => n.InstanceId, StringComparer.OrdinalIgnoreCase).ToList();
            foreach (IGrouping<string, ContextNode> definition in purposeClusters.GroupBy(ClusterDefinitionKey, StringComparer.OrdinalIgnoreCase).OrderBy(g => DisplayName(g.First()), StringComparer.OrdinalIgnoreCase).ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
            {
                List<string> purposes = definition.Select(n => n.ClusterGraph.UserProvidedPurpose.Trim()).Distinct(StringComparer.Ordinal).ToList();
                if (purposes.Count == 1)
                {
                    ContextNode first = definition.First(); string label = definition.Count() > 1 ? DisplayName(first) + " (" + definition.Count() + " instances)" : ClusterDisplayLabel(first, purposeClusters);
                    any = true; text.AppendLine("- User-provided purpose for cluster " + EscapeInline(label) + ": " + EscapeInline(purposes[0]));
                }
                else
                {
                    foreach (ContextNode node in definition)
                    {
                        any = true; text.AppendLine("- User-provided purpose for cluster " + EscapeInline(ClusterDisplayLabel(node, purposeClusters)) + ": " + EscapeInline(node.ClusterGraph.UserProvidedPurpose));
                    }
                }
            }
            if (!any) text.AppendLine("No named groups, scribble notes, panel text, or user-provided cluster purposes were extracted.");
        }

        private static void WriteInventory(StringBuilder text, ContextDocument document, DetailLevel level, ThallusPresentationModel presentation)
        {
            if (level == DetailLevel.Brief) { text.AppendLine("Inventory omitted at Brief detail level (" + document.Nodes.Count + " objects in scope)."); return; }
            if (level == DetailLevel.Exact)
            {
                text.AppendLine("| Instance ID | Component | Nickname | Assembly | Selected | Inputs | Outputs |");
                text.AppendLine("|---|---|---|---|---:|---:|---:|");
                foreach (ContextNode node in document.Nodes) text.AppendLine("| `" + node.InstanceId + "` | " + EscapeTable(node.Name) + " | " + EscapeTable(DisplayName(node)) + " | " + EscapeTable(node.AssemblyName) + " | " + (node.OriginallySelected ? "yes" : "no") + " | " + node.Inputs.Count + " | " + node.Outputs.Count + " |");
            }
            else
            {
                List<ContextNode> nodes = NodesInDetailOrder(document, presentation).Where(n => !IsCanvasGroup(n)).ToList();
                bool rootScope = String.Equals(document.Scope.Mode, "export_root", StringComparison.OrdinalIgnoreCase)
                    || String.Equals(document.Scope.Mode, "thallus_root", StringComparison.OrdinalIgnoreCase);
                bool includeRegion = HasRegionDetail(presentation);
                bool includeSelected = !rootScope && nodes.Select(node => node.OriginallySelected).Distinct().Count() > 1;
                if (!includeRegion) WriteTechnicalInventoryTable(text, document, nodes, includeSelected);
                else
                {
                    List<IGrouping<string, ContextNode>> groups = nodes.GroupBy(node => DetailRegionKey(node.InstanceId, presentation), StringComparer.OrdinalIgnoreCase)
                        .OrderBy(group => DetailRegionOrder(group.Key, presentation)).ThenBy(group => DetailRegionLabelFromKey(group.Key, presentation), StringComparer.OrdinalIgnoreCase).ToList();
                    for (int index = 0; index < groups.Count; index++)
                    {
                        IGrouping<string, ContextNode> group = groups[index];
                        text.AppendLine("### " + EscapeHeading(DetailRegionLabelFromKey(group.Key, presentation))); text.AppendLine();
                        WriteTechnicalInventoryTable(text, document, group.ToList(), includeSelected);
                        if (index + 1 < groups.Count) text.AppendLine();
                    }
                }
            }
        }

        private static void WriteTechnicalInventoryTable(StringBuilder text, ContextDocument document, List<ContextNode> nodes, bool includeSelected)
        {
            List<string> headers = new List<string> { "Component", "Nickname", "Assembly" };
            if (includeSelected) headers.Add("Selected");
            headers.AddRange(new[] { "Inputs", "Outputs" });
            text.AppendLine("| " + String.Join(" | ", headers.ToArray()) + " |");
            text.AppendLine("|" + String.Join("|", headers.Select(header => header == "Inputs" || header == "Outputs" || header == "Selected" ? "---:" : "---").ToArray()) + "|");
            foreach (ContextNode node in nodes)
            {
                List<string> cells = new List<string> { EscapeTable(node.Name), EscapeTable(DisambiguatedNodeLabel(document, node)), EscapeTable(node.AssemblyName) };
                if (includeSelected) cells.Add(node.OriginallySelected ? "yes" : "no");
                cells.Add(node.Inputs.Count.ToString(CultureInfo.InvariantCulture)); cells.Add(node.Outputs.Count.ToString(CultureInfo.InvariantCulture));
                text.AppendLine("| " + String.Join(" | ", cells.ToArray()) + " |");
            }
        }

        private static void WriteConnections(StringBuilder text, ContextDocument document, DetailLevel level)
        {
            if (level != DetailLevel.Exact)
            {
                text.AppendLine("Exact connections are omitted at " + level + " detail level.");
                text.AppendLine("- Internal connections: " + document.Edges.Count(e => e.BoundaryStatus == "internal"));
                text.AppendLine("- Incoming boundary connections: " + document.Edges.Count(e => e.BoundaryStatus == "incoming"));
                text.AppendLine("- Outgoing boundary connections: " + document.Edges.Count(e => e.BoundaryStatus == "outgoing"));
                return;
            }
            if (document.Edges.Count == 0) { text.AppendLine("No connections detected."); return; }
            foreach (ContextEdge edge in document.Edges)
                text.AppendLine("- `" + edge.SourceNodeId + "` [" + edge.SourceParameterIndex + "] " + EscapeInline(edge.SourceParameterName) + " → `" + edge.TargetNodeId + "` [" + edge.TargetParameterIndex + "] " + EscapeInline(edge.TargetParameterName) + " (`" + edge.BoundaryStatus + "`)");
        }

        private static void WriteDependencies(StringBuilder text, ContextDocument document)
        {
            if (document.Dependencies.Count == 0) { text.AppendLine("None detected."); return; }
            List<ContextDependency> native = document.Dependencies.Where(d => d.Kind == "grasshopper_native").ToList();
            if (native.Count > 0) text.AppendLine("- Grasshopper native components (assemblies: " + EscapeInline(String.Join(", ", native.Select(d => d.Name).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(n => n).ToArray())) + ")");
            foreach (ContextDependency dependency in document.Dependencies.Where(d => d.Kind != "grasshopper_native")) text.AppendLine("- " + EscapeInline(dependency.Name) + " " + EscapeInline(dependency.Version) + " (third-party)");
        }

        private static bool HasModifier(ContextParameter parameter) { return parameter.Flatten || parameter.Graft || parameter.Simplify || parameter.Reverse || !String.IsNullOrWhiteSpace(parameter.Expression); }
        private static bool IsDataLikePanel(ContextNode node)
        {
            if (node == null || !String.Equals(node.Name, "Panel", StringComparison.OrdinalIgnoreCase) || String.IsNullOrWhiteSpace(node.PersistentValueSummary)) return false;
            string value = node.PersistentValueSummary.Trim();
            if (value.StartsWith("text=", StringComparison.OrdinalIgnoreCase)) value = value.Substring(5).Trim();
            string[] tokens = Regex.Split(value, @"[\s,;{}\[\]\(\)]+", RegexOptions.CultureInvariant).Where(token => token.Length > 0).ToArray();
            if (tokens.Length == 0) return false;
            return tokens.All(IsInvariantNumber) || IsInvariantRange(value);
        }
        private static bool IsInvariantRange(string value)
        {
            Match match = Regex.Match(value ?? "", @"^\s*([+-]?(?:\d+(?:\.\d*)?|\.\d+)(?:[eE][+-]?\d+)?)\s+(?:to|through)\s+([+-]?(?:\d+(?:\.\d*)?|\.\d+)(?:[eE][+-]?\d+)?)\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            return match.Success && IsInvariantNumber(match.Groups[1].Value) && IsInvariantNumber(match.Groups[2].Value);
        }
        private static bool IsInvariantNumber(string value) { double parsed; return Double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed); }
        private static string PersistentValueForPresentation(ContextNode node)
        {
            string summary = node == null ? "" : node.PersistentValueSummary ?? "";
            return IsDataLikePanel(node) && summary.StartsWith("text=", StringComparison.OrdinalIgnoreCase) ? "value=" + summary.Substring(5).Trim() : summary;
        }
        private static bool IsStandaloneValueNode(ContextNode node)
        {
            string[] names = { "Number Slider", "Integer Slider", "Boolean Toggle", "Panel", "Value List", "Number", "Integer", "Boolean", "Text", "Surface", "Curve", "Point", "Brep", "Geometry" };
            return names.Contains(node.Name, StringComparer.OrdinalIgnoreCase);
        }
        private static bool IsNoteworthyRuntime(string summary)
        {
            Match match = Regex.Match(summary ?? "", @"items=(\d+), branches=(\d+)");
            if (!match.Success) return false;
            int items, branches; return Int32.TryParse(match.Groups[1].Value, out items) && Int32.TryParse(match.Groups[2].Value, out branches) && (items >= 100 || branches > 1);
        }
        private static string ReadableRuntime(string summary)
        {
            Match match = Regex.Match(summary ?? "", @"items=(\d+), branches=(\d+)");
            if (!match.Success) return summary;
            int items, branches; if (!Int32.TryParse(match.Groups[1].Value, out items) || !Int32.TryParse(match.Groups[2].Value, out branches)) return summary;
            return items.ToString("N0") + (items == 1 ? " item" : " items") + " across " + branches.ToString("N0") + (branches == 1 ? " branch" : " branches");
        }
        private static string DisplayName(ContextNode node) { return String.IsNullOrWhiteSpace(node.Nickname) ? (String.IsNullOrWhiteSpace(node.Name) ? "Unnamed object" : node.Name) : node.Nickname; }
        private static string DisplayName(ContextParameter parameter) { return String.IsNullOrWhiteSpace(parameter.Nickname) ? (String.IsNullOrWhiteSpace(parameter.Name) ? "Unnamed parameter" : parameter.Name) : parameter.Nickname; }
        private static string ClusterDefinitionKey(ContextNode node)
        {
            string documentId = node.ClusterGraph == null ? "" : node.ClusterGraph.DocumentId;
            return String.IsNullOrWhiteSpace(documentId) ? "instance:" + node.InstanceId : "document:" + documentId;
        }
        private static string ClusterDisplayLabel(ContextNode cluster, IEnumerable<ContextNode> siblingClusters)
        {
            string name = DisplayName(cluster);
            int count = (siblingClusters ?? new List<ContextNode>()).Count(n => String.Equals(DisplayName(n), name, StringComparison.OrdinalIgnoreCase));
            return count > 1 ? name + " [" + ShortId(cluster.InstanceId) + "]" : name;
        }
        private static string BoundaryPortLabel(ContextDocument document, string nodeName, string parameterName, string id, DetailLevel level)
        {
            if (level == DetailLevel.Exact)
            {
                string exact = BoundaryPortLabel(nodeName, parameterName, id, false);
                return exact + " (`" + id + "`)";
            }
            return BoundaryPortLabel(nodeName, parameterName, id, BoundaryNodeLabelCollides(document, nodeName, id));
        }

        private static string BoundaryPortLabel(string nodeName, string parameterName, string id, bool includeShortId)
        {
            string owner = String.IsNullOrWhiteSpace(nodeName) ? "Unnamed object" : nodeName;
            if (includeShortId) owner += " [" + ShortId(id) + "]";
            return EscapeInline(owner) + "." + EscapeInline(String.IsNullOrWhiteSpace(parameterName) ? "Unnamed parameter" : parameterName);
        }

        private static bool BoundaryNodeLabelCollides(ContextDocument document, string nodeName, string id)
        {
            if (document == null) return false;
            string readable = String.IsNullOrWhiteSpace(nodeName) ? "Unnamed object" : nodeName;
            if ((document.Nodes ?? new List<ContextNode>()).Any(node => !String.Equals(node.InstanceId, id, StringComparison.OrdinalIgnoreCase)
                && String.Equals(DisplayName(node), readable, StringComparison.OrdinalIgnoreCase))) return true;
            IEnumerable<ContextBoundaryPort> ports = (document.BoundaryInputs ?? new List<ContextBoundaryPort>())
                .Concat(document.BoundaryOutputs ?? new List<ContextBoundaryPort>());
            return ports.Any(port =>
                (!String.Equals(port.InternalNodeId, id, StringComparison.OrdinalIgnoreCase)
                    && String.Equals(port.InternalNodeName, readable, StringComparison.OrdinalIgnoreCase))
                || (!String.Equals(port.ExternalNodeId, id, StringComparison.OrdinalIgnoreCase)
                    && String.Equals(port.ExternalNodeName, readable, StringComparison.OrdinalIgnoreCase)));
        }
        private static string JoinBounded(List<string> values, int maximum)
        {
            if (values.Count <= maximum) return String.Join(", ", values.ToArray());
            return String.Join(", ", values.Take(maximum).ToArray()) + ", and " + (values.Count - maximum) + " more";
        }

        private static string CountPhrase(int count, string singular)
        {
            return count + " " + singular + (count == 1 ? "" : "s");
        }

        private static string BoundedOneLine(string value, int maximum)
        {
            string clean = Regex.Replace((value ?? "").Replace("\r", " ").Replace("\n", " "), @"\s+", " ").Trim();
            if (maximum > 3 && clean.Length > maximum) clean = clean.Substring(0, maximum - 3).TrimEnd() + "...";
            return clean;
        }

        private static string ValueNodeLabel(ContextDocument document, ContextNode node)
        {
            Dictionary<string, ContextNode> nodes = document.Nodes.ToDictionary(n => n.InstanceId, StringComparer.OrdinalIgnoreCase);
            List<string> targets = new List<string>();
            foreach (ContextEdge edge in document.Edges.Where(e => String.Equals(e.SourceNodeId, node.InstanceId, StringComparison.OrdinalIgnoreCase)))
            {
                ContextNode target;
                if (nodes.TryGetValue(edge.TargetNodeId, out target)) targets.Add(NodePortLabel(document, target, String.IsNullOrWhiteSpace(edge.TargetParameterName) ? "input" : edge.TargetParameterName, "input"));
                else
                {
                    ContextBoundaryPort boundary = document.BoundaryOutputs.FirstOrDefault(p => String.Equals(p.InternalNodeId, node.InstanceId, StringComparison.OrdinalIgnoreCase) && String.Equals(p.ExternalNodeId, edge.TargetNodeId, StringComparison.OrdinalIgnoreCase));
                    if (boundary != null) targets.Add(boundary.ExternalNodeName + "." + boundary.ExternalParameterName);
                }
            }
            targets = targets.Where(t => !String.IsNullOrWhiteSpace(t)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (targets.Count == 0)
                return DisambiguatedNodeLabel(document, node) + (IsDataLikePanel(node) ? " (no connected recipient captured)" : "");
            return DisambiguatedNodeLabel(document, node) + " → " + JoinBounded(targets, 3);
        }

        private static string NodePortLabel(ContextDocument document, ContextNode node, string parameterName, string direction)
        {
            string owner = DisplayName(node);
            if (NodePortLabelCollides(document, node, parameterName, direction)) owner += " [" + ShortId(node.InstanceId) + "]";
            return owner + "." + (String.IsNullOrWhiteSpace(parameterName) ? "Unnamed parameter" : parameterName);
        }

        private static string DisambiguatedNodeLabel(ContextDocument document, ContextNode node)
        {
            string label = DisplayName(node);
            if (document != null && document.Nodes.Any(other => !String.Equals(other.InstanceId, node.InstanceId, StringComparison.OrdinalIgnoreCase)
                && String.Equals(DisplayName(other), label, StringComparison.OrdinalIgnoreCase))) label += " [" + ShortId(node.InstanceId) + "]";
            return label;
        }

        private static bool NodePortLabelCollides(ContextDocument document, ContextNode node, string parameterName, string direction)
        {
            if (document == null || node == null) return false;
            string owner = DisplayName(node);
            return document.Nodes.Any(other => !String.Equals(other.InstanceId, node.InstanceId, StringComparison.OrdinalIgnoreCase)
                && String.Equals(DisplayName(other), owner, StringComparison.OrdinalIgnoreCase)
                && ParametersForDirection(other, direction).Any(p => String.Equals(DisplayName(p), parameterName, StringComparison.OrdinalIgnoreCase)
                    || String.Equals(p.Name, parameterName, StringComparison.OrdinalIgnoreCase)));
        }

        private static IEnumerable<ContextParameter> ParametersForDirection(ContextNode node, string direction)
        {
            if (String.Equals(direction, "input", StringComparison.OrdinalIgnoreCase)) return node.Inputs;
            if (String.Equals(direction, "output", StringComparison.OrdinalIgnoreCase)) return node.Outputs;
            return node.Inputs.Concat(node.Outputs);
        }

        private static bool IsPassiveRuntimeNode(ContextNode node)
        {
            string[] names = { "Scribble", "Panel", "Number Slider", "Integer Slider", "Boolean Toggle", "Value List", "Relay", "Number", "Integer", "Boolean", "Text" };
            return IsCanvasGroup(node) || names.Contains(node.Name, StringComparer.OrdinalIgnoreCase);
        }

        private static bool IsCanvasGroup(ContextNode node) { return String.Equals(node.RuntimeTypeName, "Grasshopper.Kernel.Special.GH_Group", StringComparison.OrdinalIgnoreCase); }

        private static string EscapeInline(string value)
        {
            if (String.IsNullOrEmpty(value)) return "";
            return value.Replace("\\", "\\\\").Replace("`", "\\`").Replace("*", "\\*").Replace("_", "\\_").Replace("\r", " ").Replace("\n", " ").Trim();
        }
        private static string EscapeHeading(string value) { return EscapeInline(value).Replace("#", "\\#"); }
        private static string EscapeTable(string value) { return EscapeInline(value).Replace("|", "\\|"); }
        private static string CodeSpan(string value)
        {
            string content = (value ?? "").Replace("\r", " ").Replace("\n", " ").Trim();
            int longest = 0, current = 0; foreach (char c in content) { if (c == '`') { current++; if (current > longest) longest = current; } else current = 0; }
            string fence = new string('`', Math.Max(1, longest + 1)); return fence + content + fence;
        }
        private static string ShortId(string id) { return String.IsNullOrEmpty(id) ? "unknown" : (id.Length <= 8 ? id : id.Substring(0, 8)); }
        private static string LanguageTag(string language) { string v = (language ?? "").ToLowerInvariant(); if (v.Contains("python")) return "python"; if (v.Contains("c#") || v.Contains("csharp")) return "csharp"; return "text"; }
        private static string FenceFor(string content)
        {
            int longest = 0, current = 0;
            foreach (char c in content ?? "") { if (c == '`') { current++; if (current > longest) longest = current; } else current = 0; }
            return new string('`', Math.Max(3, longest + 1));
        }
    }

    public sealed class ContextExporter
    {
        public ContextExportPackage Export(ContextSnapshot snapshot, ContextExportOptions options)
        {
            ContextDocument document = new ContextGraphService().BuildDocument(snapshot, options);
            ContextJsonSerializer jsonSerializer = new ContextJsonSerializer();
            document.ExportSignature = null;
            string unsignedJson = jsonSerializer.Serialize(document);
            document.ExportSignature = new ContextExportSignature
            {
                Product = "Lichen",
                ExporterVersion = String.IsNullOrWhiteSpace(options.ExporterVersion) ? "unknown" : options.ExporterVersion.Trim(),
                FingerprintAlgorithm = "sha256",
                ContextFingerprint = Sha256(unsignedJson)
            };
            string json = jsonSerializer.Serialize(document);
            return new ContextExportPackage { Document = document, Json = json, Markdown = new MarkdownComposer().Compose(document, options, json) };
        }

        private static string Sha256(string value)
        {
            using (SHA256 algorithm = SHA256.Create())
            {
                byte[] hash = algorithm.ComputeHash(Encoding.UTF8.GetBytes(value ?? ""));
                return String.Concat(hash.Select(b => b.ToString("x2")));
            }
        }
    }
}
