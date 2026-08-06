using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
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

    public sealed class MarkdownComposer
    {
        public string Compose(ContextDocument document, ContextExportOptions options, string json)
        {
            StringBuilder text = new StringBuilder();
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
            if (String.Equals(document.Scope.Mode, "export_root", StringComparison.OrdinalIgnoreCase))
            {
                text.AppendLine("- Export Root: " + EscapeInline(String.IsNullOrWhiteSpace(document.Scope.RootLabel) ? "Lichen" : document.Scope.RootLabel));
                text.AppendLine("- Connected X sources: " + (document.Scope.RootSourceObjectIds == null ? 0 : document.Scope.RootSourceObjectIds.Count));
            }
            text.AppendLine("- Originally selected objects: " + document.Scope.SelectedObjectIds.Count);
            text.AppendLine("- Included objects: " + document.Nodes.Count);
            text.AppendLine("- Incoming boundary connections: " + document.BoundaryInputs.Count);
            text.AppendLine("- Outgoing boundary connections: " + document.BoundaryOutputs.Count);
            if (document.Scope.NodeLimitReached) text.AppendLine("- Warning: the configured node limit was reached.");
            text.AppendLine();

            Section(text, "Author Signals"); WriteAuthorSignals(text, document); text.AppendLine();

            Section(text, "Inferred Purpose");
            text.AppendLine(EscapeInline(document.Analysis.InferredPurpose)); text.AppendLine();

            Section(text, "Effective Inputs"); WriteBoundaries(text, document.BoundaryInputs, options.DetailLevel); text.AppendLine();
            Section(text, "Workflow Structure"); WriteExecutionSemantics(text, document, options.DetailLevel); text.AppendLine();
            Section(text, "Workflow Summary");
            WriteWorkflowSummary(text, document);
            text.AppendLine();
            Section(text, "Cluster Internals"); WriteClusterInternals(text, document, options.DetailLevel, options.IncludeScriptSource); text.AppendLine();
            Section(text, "Effective Outputs"); WriteBoundaries(text, document.BoundaryOutputs, options.DetailLevel); text.AppendLine();

            Section(text, "Data-Tree and Parameter Behavior"); WriteParameterBehavior(text, document); text.AppendLine();
            Section(text, "Runtime Data Summary"); WriteRuntimeDataSummary(text, document, options.DetailLevel); text.AppendLine();
            Section(text, "Custom Scripts"); WriteScripts(text, document, options.IncludeScriptSource); text.AppendLine();
            Section(text, "Runtime Warnings and Errors"); WriteRuntimeMessages(text, document); text.AppendLine();
            Section(text, "Plugin Dependencies"); WriteDependencies(text, document);
            text.AppendLine();

            Section(text, "Uncertainties and Extraction Notes");
            List<string> notes = new List<string>(); notes.AddRange(document.Analysis.Uncertainties); notes.AddRange(document.ExtractionNotes);
            if (notes.Count == 0) text.AppendLine("None recorded."); else foreach (string note in notes.Distinct()) text.AppendLine("- " + EscapeInline(note));
            text.AppendLine();

            Section(text, "Component Inventory"); WriteInventory(text, document, options.DetailLevel); text.AppendLine();
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

        private static void WriteBoundaries(StringBuilder text, List<ContextBoundaryPort> ports, DetailLevel level)
        {
            if (ports.Count == 0) { text.AppendLine("No boundary connections detected."); return; }
            List<string> lines = new List<string>();
            foreach (IGrouping<string, ContextBoundaryPort> group in ports.GroupBy(p => p.InternalNodeId + "|" + p.ParameterIndex).OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
            {
                ContextBoundaryPort first = group.First();
                string internalPort = PortLabel(first.InternalNodeName, first.InternalParameterName, first.InternalNodeId, level == DetailLevel.Exact);
                List<string> external = group.Select(p => PortLabel(p.ExternalNodeName, p.ExternalParameterName, p.ExternalNodeId, level == DetailLevel.Exact)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                if (level != DetailLevel.Exact && external.Any(label => String.Equals(label, internalPort, StringComparison.OrdinalIgnoreCase)))
                {
                    string collidedLabel = internalPort;
                    internalPort = PortLabel(first.InternalNodeName, first.InternalParameterName, first.InternalNodeId, true);
                    external = group.Select(p =>
                    {
                        string readable = PortLabel(p.ExternalNodeName, p.ExternalParameterName, p.ExternalNodeId, false);
                        return String.Equals(readable, collidedLabel, StringComparison.OrdinalIgnoreCase)
                            ? PortLabel(p.ExternalNodeName, p.ExternalParameterName, p.ExternalNodeId, true)
                            : readable;
                    }).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                }
                string externalText = JoinBounded(external, 5);
                lines.Add(first.Direction == "input" ? externalText + " → " + internalPort : internalPort + " → " + externalText);
            }
            foreach (IGrouping<string, string> duplicate in lines.GroupBy(line => line, StringComparer.OrdinalIgnoreCase))
                text.AppendLine("- " + duplicate.Key + (duplicate.Count() > 1 ? " (" + duplicate.Count() + " separate connections)" : ""));
        }

        private static void WriteParameterBehavior(StringBuilder text, ContextDocument document)
        {
            bool any = false;
            foreach (ContextNode node in document.Nodes)
            {
                if (!String.IsNullOrWhiteSpace(node.PersistentValueSummary) && !String.Equals(node.Name, "Panel", StringComparison.OrdinalIgnoreCase) && !String.Equals(node.Name, "Scribble", StringComparison.OrdinalIgnoreCase))
                {
                    any = true;
                    text.AppendLine("- " + EscapeInline(ValueNodeLabel(document, node)) + ": " + EscapeInline(node.PersistentValueSummary));
                }
                foreach (ContextParameter parameter in node.Inputs.Concat(node.Outputs))
                {
                    if (IsStandaloneValueNode(node) && !String.IsNullOrWhiteSpace(node.PersistentValueSummary) && !HasModifier(parameter)) continue;
                    if (!parameter.Flatten && !parameter.Graft && !parameter.Simplify && !parameter.Reverse && String.IsNullOrWhiteSpace(parameter.Expression) && String.IsNullOrWhiteSpace(parameter.PersistentDataSummary)) continue;
                    any = true;
                    List<string> flags = new List<string>();
                    if (parameter.Flatten) flags.Add("flatten"); if (parameter.Graft) flags.Add("graft"); if (parameter.Simplify) flags.Add("simplify"); if (parameter.Reverse) flags.Add("reverse");
                    if (!String.IsNullOrWhiteSpace(parameter.Expression)) flags.Add("expression: " + parameter.Expression);
                    if (!String.IsNullOrWhiteSpace(parameter.PersistentDataSummary)) flags.Add("persistent data: " + parameter.PersistentDataSummary);
                    text.AppendLine("- " + EscapeInline(DisplayName(node)) + "." + EscapeInline(DisplayName(parameter)) + ": " + EscapeInline(String.Join(", ", flags.ToArray())));
                }
            }
            if (!any) text.AppendLine("No noteworthy parameter modifiers or persistent-data summaries were extracted.");
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

        private static void WriteRuntimeDataSummary(StringBuilder text, ContextDocument document, DetailLevel level)
        {
            if (level == DetailLevel.Brief) { text.AppendLine("Runtime data details are omitted at Brief detail level."); return; }
            bool any = false;
            foreach (ContextNode node in ContextGraphService.TopologicalOrder(document).Where(n => level == DetailLevel.Exact || !IsPassiveRuntimeNode(n)))
            {
                IEnumerable<ContextParameter> parameters = level == DetailLevel.Exact ? node.Inputs.Concat(node.Outputs) : node.Outputs;
                foreach (IGrouping<string, ContextParameter> group in parameters.Where(p => !String.IsNullOrWhiteSpace(p.RuntimeDataSummary) && (level == DetailLevel.Exact || IsNoteworthyRuntime(p.RuntimeDataSummary))).GroupBy(p => p.RuntimeDataSummary))
                {
                    any = true;
                    string names = String.Join(", ", group.Select(DisplayName).Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
                    text.AppendLine("- " + EscapeInline(DisplayName(node)) + " — " + EscapeInline(names) + ": " + EscapeInline(ReadableRuntime(group.Key)));
                }
            }
            if (!any) text.AppendLine("No noteworthy already-computed runtime data was captured.");
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
            }
            IEnumerable<IGrouping<string, ContextExecutionComponent>> componentGroups = semantics.Components.GroupBy(c => c.NodeName + "|" + c.Kind + "|" + c.Behavior, StringComparer.OrdinalIgnoreCase);
            foreach (IGrouping<string, ContextExecutionComponent> group in componentGroups)
            {
                ContextExecutionComponent component = group.First();
                string line = "- " + EscapeInline(component.NodeName) + " [" + EscapeInline(component.Kind.Replace('_', ' ')) + "]: " + EscapeInline(component.Behavior);
                if (level == DetailLevel.Exact)
                    line += " Node" + (group.Count() == 1 ? " `" + component.NodeId + "`" : "s " + String.Join(", ", group.Select(c => "`" + c.NodeId + "`").ToArray())) + ".";
                else if (group.Count() > 1) line += " (" + group.Count() + " components)";
                text.AppendLine(line);
            }
            foreach (string note in semantics.Notes) text.AppendLine("- Note: " + EscapeInline(note));
        }

        private static void WriteWorkflowSummary(StringBuilder text, ContextDocument document)
        {
            if (document.Analysis.ExecutionSemantics != null && document.Analysis.ExecutionSemantics.HasNonLinearBehavior)
                text.AppendLine("The following is a condensed dataflow-operation summary, not literal execution order. See Workflow Structure above.\n");
            if (document.Analysis.DetectedOperations.Count == 0) { text.AppendLine("No operations were extracted."); return; }
            List<string> ordered = new List<string>(); Dictionary<string, int> counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (string operation in document.Analysis.DetectedOperations)
            {
                if (!counts.ContainsKey(operation)) { counts.Add(operation, 0); ordered.Add(operation); }
                counts[operation]++;
            }
            for (int i = 0; i < ordered.Count; i++)
            {
                string suffix = counts[ordered[i]] > 1 ? " (" + counts[ordered[i]] + " components)" : "";
                text.AppendLine((i + 1) + ". " + EscapeInline(ordered[i]) + suffix);
            }
        }

        private static void WriteRuntimeMessages(StringBuilder text, ContextDocument document)
        {
            bool any = false;
            foreach (ContextNode node in document.Nodes)
                foreach (ContextRuntimeMessage message in node.RuntimeMessages)
                { any = true; text.AppendLine("- " + EscapeInline(message.Level) + " — " + EscapeInline(DisplayName(node)) + ": " + EscapeInline(message.Message)); }
            if (!any) text.AppendLine("No captured runtime warnings or errors.");
        }

        private static void WriteAuthorSignals(StringBuilder text, ContextDocument document)
        {
            bool any = false;
            foreach (ContextGroup group in document.Groups.Where(g => !String.IsNullOrWhiteSpace(g.Name) && !String.Equals(g.Name, "Group", StringComparison.OrdinalIgnoreCase)))
            {
                any = true; text.AppendLine("- Group “" + EscapeInline(group.Name) + "”: " + group.MemberIds.Count + " members");
            }
            foreach (ContextNode node in document.Nodes.Where(n => (String.Equals(n.Name, "Panel", StringComparison.OrdinalIgnoreCase) || String.Equals(n.Name, "Scribble", StringComparison.OrdinalIgnoreCase)) && !String.IsNullOrWhiteSpace(n.PersistentValueSummary)))
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

        private static void WriteInventory(StringBuilder text, ContextDocument document, DetailLevel level)
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
                text.AppendLine("| Component | Nickname | Assembly | Selected | Inputs | Outputs |");
                text.AppendLine("|---|---|---|---:|---:|---:|");
                foreach (ContextNode node in document.Nodes.Where(n => !IsCanvasGroup(n))) text.AppendLine("| " + EscapeTable(node.Name) + " | " + EscapeTable(DisplayName(node)) + " | " + EscapeTable(node.AssemblyName) + " | " + (node.OriginallySelected ? "yes" : "no") + " | " + node.Inputs.Count + " | " + node.Outputs.Count + " |");
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
        private static string PortLabel(string nodeName, string parameterName, string id, bool includeId)
        {
            string label = EscapeInline(String.IsNullOrWhiteSpace(nodeName) ? "Unnamed object" : nodeName) + "." + EscapeInline(String.IsNullOrWhiteSpace(parameterName) ? "Unnamed parameter" : parameterName);
            return includeId ? label + " (`" + id + "`)" : label;
        }
        private static string JoinBounded(List<string> values, int maximum)
        {
            if (values.Count <= maximum) return String.Join(", ", values.ToArray());
            return String.Join(", ", values.Take(maximum).ToArray()) + ", and " + (values.Count - maximum) + " more";
        }

        private static string ValueNodeLabel(ContextDocument document, ContextNode node)
        {
            Dictionary<string, ContextNode> nodes = document.Nodes.ToDictionary(n => n.InstanceId, StringComparer.OrdinalIgnoreCase);
            List<string> targets = new List<string>();
            foreach (ContextEdge edge in document.Edges.Where(e => String.Equals(e.SourceNodeId, node.InstanceId, StringComparison.OrdinalIgnoreCase)))
            {
                ContextNode target;
                if (nodes.TryGetValue(edge.TargetNodeId, out target)) targets.Add(DisplayName(target) + "." + (String.IsNullOrWhiteSpace(edge.TargetParameterName) ? "input" : edge.TargetParameterName));
                else
                {
                    ContextBoundaryPort boundary = document.BoundaryOutputs.FirstOrDefault(p => String.Equals(p.InternalNodeId, node.InstanceId, StringComparison.OrdinalIgnoreCase) && String.Equals(p.ExternalNodeId, edge.TargetNodeId, StringComparison.OrdinalIgnoreCase));
                    if (boundary != null) targets.Add(boundary.ExternalNodeName + "." + boundary.ExternalParameterName);
                }
            }
            targets = targets.Where(t => !String.IsNullOrWhiteSpace(t)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            return targets.Count == 0 ? DisplayName(node) : DisplayName(node) + " → " + JoinBounded(targets, 3);
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
            string json = jsonSerializer.Serialize(document);
            return new ContextExportPackage { Document = document, Json = json, Markdown = new MarkdownComposer().Compose(document, options, json) };
        }
    }
}
