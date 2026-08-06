using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Lichen.Core
{
    public sealed class ExecutionSemanticsAnalyzer
    {
        public ContextExecutionSemantics Analyze(ContextDocument document)
        {
            ContextExecutionSemantics result = new ContextExecutionSemantics();
            if (document == null || document.Nodes == null) return result;

            Dictionary<string, ContextNode> nodes = document.Nodes.ToDictionary(n => n.InstanceId, StringComparer.OrdinalIgnoreCase);
            Dictionary<string, List<string>> next = NewGraph(nodes.Keys);
            Dictionary<string, List<string>> previous = NewGraph(nodes.Keys);
            foreach (ContextEdge edge in document.Edges.Where(e => e.BoundaryStatus == "internal"))
            {
                if (!nodes.ContainsKey(edge.SourceNodeId) || !nodes.ContainsKey(edge.TargetNodeId)) continue;
                AddDistinct(next[edge.SourceNodeId], edge.TargetNodeId);
                AddDistinct(previous[edge.TargetNodeId], edge.SourceNodeId);
            }

            result.OrdinaryWireGraphHasCycle = HasCycle(nodes.Keys, next);
            BuildRegions(document, nodes, next, previous, result);

            HashSet<string> regionEndpoints = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (ContextExecutionRegion region in result.Regions) { regionEndpoints.Add(region.StartNodeId); regionEndpoints.Add(region.EndNodeId); }
            foreach (ContextNode node in document.Nodes)
            {
                ContextExecutionComponent component = Classify(node, nodes, document);
                if (component == null || regionEndpoints.Contains(node.InstanceId) && component.Kind == "iterative_controller") continue;
                result.Components.Add(component);
            }

            result.Components = result.Components.OrderBy(c => c.Kind, StringComparer.OrdinalIgnoreCase).ThenBy(c => c.NodeId, StringComparer.OrdinalIgnoreCase).ToList();
            result.HasNonLinearBehavior = result.OrdinaryWireGraphHasCycle || result.Regions.Count > 0 || result.Components.Count > 0;
            if (result.OrdinaryWireGraphHasCycle)
                result.Notes.Add("The ordinary wire graph contains at least one feedback cycle, so a topological component order cannot represent execution completely.");
            if (result.Regions.Count > 0)
                result.Notes.Add("Paired control boundaries define repeated regions even when the ordinary wire graph itself is acyclic.");
            if (result.Components.Any(c => c.Kind == "optimization_solver" || c.Kind == "iterative_solver"))
                result.Notes.Add("Solver history and repeated intermediate solutions are not captured; this export records the current graph and already-available state only.");
            List<ContextNode> clusters = document.Nodes.Where(IsCluster).ToList();
            if (clusters.Any(c => c.ClusterGraph != null && String.Equals(c.ClusterGraph.InspectionStatus, "inspected", StringComparison.OrdinalIgnoreCase)))
                result.Notes.Add("Unprotected cluster internals were inspected as bounded nested graphs; exposed ports and outer-graph connections remain distinct from internal wiring.");
            if (clusters.Any(c => c.ClusterGraph == null || !String.Equals(c.ClusterGraph.InspectionStatus, "inspected", StringComparison.OrdinalIgnoreCase)))
                result.Notes.Add("Some cluster internals remain opaque because they were protected, unavailable, cyclic, or beyond an inspection limit; exposed ports and outer-graph connections remain available.");
            if (result.HasNonLinearBehavior)
                result.Notes.Add("The operation list is a dataflow summary and should not be read as literal execution order.");
            return result;
        }

        private static void BuildRegions(ContextDocument document, Dictionary<string, ContextNode> nodes, Dictionary<string, List<string>> next, Dictionary<string, List<string>> previous, ContextExecutionSemantics result)
        {
            foreach (ContextEdge edge in document.Edges.Where(e => e.BoundaryStatus == "internal"))
            {
                ContextNode start, end;
                if (!nodes.TryGetValue(edge.SourceNodeId, out start) || !nodes.TryGetValue(edge.TargetNodeId, out end)) continue;
                if (!IsControlBoundary(start, end, edge)) continue;

                HashSet<string> fromStart = Reachable(start.InstanceId, next);
                HashSet<string> toEnd = Reachable(end.InstanceId, previous);
                List<string> members = fromStart.Intersect(toEnd, StringComparer.OrdinalIgnoreCase).OrderBy(s => s, StringComparer.OrdinalIgnoreCase).ToList();
                ContextExecutionRegion region = new ContextExecutionRegion();
                region.Kind = "iterative_region";
                region.StartNodeId = start.InstanceId;
                region.EndNodeId = end.InstanceId;
                region.Label = DisplayName(start) + " to " + DisplayName(end);
                region.NodeIds = members;
                region.IterationLimit = FindIterationLimit(document, start);
                region.CarriedValues = start.Outputs.Where(p => !IsControlPort(p.Name)).Select(DisplayName)
                    .Concat(end.Inputs.Where(p => !IsControlPort(p.Name)).Select(DisplayName))
                    .Where(n => !String.IsNullOrWhiteSpace(n)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
                region.Evidence.Add(DisplayName(start) + "." + edge.SourceParameterName + " connects to " + DisplayName(end) + "." + edge.TargetParameterName);
                if (!String.IsNullOrWhiteSpace(region.IterationLimit)) region.Evidence.Add("iteration limit=" + region.IterationLimit);
                result.Regions.Add(region);
            }

            foreach (ContextExecutionRegion region in result.Regions)
                region.NestingLevel = result.Regions.Count(other => !Object.ReferenceEquals(other, region)
                    && other.NodeIds.Contains(region.StartNodeId, StringComparer.OrdinalIgnoreCase)
                    && other.NodeIds.Contains(region.EndNodeId, StringComparer.OrdinalIgnoreCase)
                    && other.NodeIds.Count > region.NodeIds.Count);
            result.Regions = result.Regions.OrderBy(r => r.NestingLevel).ThenBy(r => r.StartNodeId, StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static bool IsControlBoundary(ContextNode start, ContextNode end, ContextEdge edge)
        {
            string startText = Combined(start);
            string endText = Combined(end);
            bool namesPair = (ContainsAny(startText, "loop start", "loop begin", "iteration start", "repeat start") && ContainsAny(endText, "loop end", "loop finish", "iteration end", "repeat end"))
                || (ContainsAny(start.Name.ToLowerInvariant(), "start", "begin") && ContainsAny(end.Name.ToLowerInvariant(), "end", "finish"));
            bool portsPair = (edge.SourceParameterName == ">" && edge.TargetParameterName == "<")
                || (ContainsAny((edge.SourceParameterName ?? "").ToLowerInvariant(), "repeat", "iterate", "loop") && ContainsAny((edge.TargetParameterName ?? "").ToLowerInvariant(), "exit", "return", "loop"));
            return namesPair && portsPair;
        }

        private static string FindIterationLimit(ContextDocument document, ContextNode start)
        {
            ContextParameter parameter = start.Inputs.FirstOrDefault(p => ContainsAny((p.Name ?? "").ToLowerInvariant(), "iterations", "iteration count", "maximum iterations", "max iterations"));
            if (parameter == null) return "";
            ContextEdge incoming = document.Edges.FirstOrDefault(e => e.BoundaryStatus == "internal" && String.Equals(e.TargetNodeId, start.InstanceId, StringComparison.OrdinalIgnoreCase) && e.TargetParameterIndex == parameter.Index);
            if (incoming == null) return ExtractValue(parameter.PersistentDataSummary);
            ContextNode source = document.Nodes.FirstOrDefault(n => String.Equals(n.InstanceId, incoming.SourceNodeId, StringComparison.OrdinalIgnoreCase));
            return source == null ? "" : ExtractValue(source.PersistentValueSummary);
        }

        private static string ExtractValue(string summary)
        {
            if (String.IsNullOrWhiteSpace(summary)) return "";
            int equals = summary.IndexOf('=');
            return equals < 0 ? summary.Trim() : summary.Substring(equals + 1).Trim();
        }

        private static ContextExecutionComponent Classify(ContextNode node, Dictionary<string, ContextNode> nodes, ContextDocument document)
        {
            string text = Combined(node);
            string runtime = (node.RuntimeTypeName ?? "").ToLowerInvariant();
            List<ContextControlLink> links = node.ControlLinks ?? new List<ContextControlLink>();

            if (runtime == "galapagoscomponents.galapagosobject" || ContainsAny(text, "galapagos", "evolutionary solver") || links.Any(l => l.Role == "genome" || l.Role == "fitness"))
            {
                List<ContextControlLink> genomeLinks = links.Where(l => l.Role == "genome").ToList();
                ContextControlLink fitnessLink = links.FirstOrDefault(l => l.Role == "fitness");
                string behavior = "Controls repeated whole-definition evaluations for optimization";
                if (genomeLinks.Count > 0) behavior += " using " + genomeLinks.Count + " linked genome" + (genomeLinks.Count == 1 ? "" : "s") + " (" + DescribeTargets(genomeLinks, nodes) + ")";
                if (fitnessLink != null) behavior += " and fitness value " + TargetName(fitnessLink, nodes);
                behavior += ".";
                string solver = Metadata(node, "solver");
                if (!String.IsNullOrWhiteSpace(solver)) behavior += " Solver mode: " + solver + ".";
                if (String.Equals(Metadata(node, "runtimeLimitEnabled"), "True", StringComparison.OrdinalIgnoreCase) && !String.IsNullOrWhiteSpace(Metadata(node, "runtimeLimit"))) behavior += " Runtime limit: " + Metadata(node, "runtimeLimit") + ".";
                if (fitnessLink != null)
                {
                    string construction = DescribeFitnessConstruction(fitnessLink.TargetNodeId, document, nodes);
                    if (!String.IsNullOrWhiteSpace(construction)) behavior += " " + construction;
                }
                behavior += " The export represents the current state, not the optimization history.";
                return Item(node, "optimization_solver", behavior, "solver/controller metadata", ControlEvidence(node, nodes));
            }
            if (runtime == "grasshopper.kernel.special.gh_timer" || String.Equals(node.Name, "Timer", StringComparison.OrdinalIgnoreCase))
            {
                string interval = Metadata(node, "timerInterval");
                string behavior = "Schedules repeated Grasshopper solutions" + (String.IsNullOrWhiteSpace(interval) ? "" : " at interval " + interval) + ".";
                return Item(node, "scheduler", behavior, "timer metadata", ControlEvidence(node, nodes));
            }
            if (runtime == "grasshopper.kernel.components.gh_datadamcomponent" || ContainsAny(text, "data dam"))
            {
                string delay = Metadata(node, "delayMilliseconds");
                string behavior = "Holds incoming data and releases it according to its configured mode" + (String.IsNullOrWhiteSpace(delay) ? "" : " after approximately " + delay + " ms") + ".";
                return Item(node, "deferred_dataflow", behavior, "data-dam metadata", null);
            }
            if (runtime == "grasshopper.kernel.special.gh_datarecorder" || ContainsAny(text, "data recorder"))
            {
                string limit = Metadata(node, "dataLimit");
                string behavior = "Accumulates data across solutions" + (String.IsNullOrWhiteSpace(limit) ? "" : " with configured limit " + limit) + ".";
                return Item(node, "stateful_recorder", behavior, "recorder metadata", null);
            }
            if (runtime == "grasshopper.kernel.special.gh_cluster" || runtime == "grasshopper.kernel.special.gh_cluster_obsolete" || String.Equals(node.Name, "Cluster", StringComparison.OrdinalIgnoreCase))
            {
                string storage = Metadata(node, "storage");
                string behavior = "Encapsulates a reusable Grasshopper subgraph behind exposed inputs and outputs" + (String.IsNullOrWhiteSpace(storage) ? "" : " (" + storage.Replace('_', ' ') + ")") + ".";
                if (node.ClusterGraph != null && String.Equals(node.ClusterGraph.InspectionStatus, "inspected", StringComparison.OrdinalIgnoreCase))
                {
                    behavior += " Lichen inspected " + node.ClusterGraph.Nodes.Count + " internal object" + (node.ClusterGraph.Nodes.Count == 1 ? "" : "s")
                        + " and " + node.ClusterGraph.Edges.Count + " internal connection" + (node.ClusterGraph.Edges.Count == 1 ? "" : "s") + ".";
                    List<string> operations = node.ClusterGraph.Analysis == null ? new List<string>() : node.ClusterGraph.Analysis.DetectedOperations.Where(o => !String.IsNullOrWhiteSpace(o)).Take(2).ToList();
                    if (operations.Count > 0) behavior += " Internal workflow: " + String.Join(" ", operations.ToArray());
                    if (node.ClusterGraph.NodeLimitReached) behavior += " The internal graph was truncated at the configured node limit.";
                }
                else if (node.ClusterGraph != null && !String.IsNullOrWhiteSpace(node.ClusterGraph.InspectionNote))
                    behavior += " Internal inspection unavailable: " + node.ClusterGraph.InspectionNote;
                if (node.ClusterGraph != null && !String.IsNullOrWhiteSpace(node.ClusterGraph.BlackBoxSummary))
                    behavior += " Black-box observations: " + node.ClusterGraph.BlackBoxSummary;
                if (node.ClusterGraph != null && !String.IsNullOrWhiteSpace(node.ClusterGraph.UserProvidedPurpose))
                    behavior += " User-provided cluster purpose: " + node.ClusterGraph.UserProvidedPurpose.Trim().TrimEnd('.', ';', ':') + ".";
                return Item(node, "reusable_subgraph", behavior, "cluster runtime type", null);
            }
            if (runtime == "grasshopper.kernel.special.gh_buttonobject" || String.Equals(node.Name, "Button", StringComparison.OrdinalIgnoreCase) || String.Equals(node.Name, "Trigger", StringComparison.OrdinalIgnoreCase))
                return Item(node, "manual_trigger", "Provides a user-triggered event or pulse that may initiate downstream work.", "component type/name", null);
            if (ContainsAny(text, "stream freeze", "stream gate", "gate switch", "data gate"))
                return Item(node, "stateful_gate", "Controls whether data is allowed downstream and may retain the last received value while closed.", "component name/description", null);
            if (ContainsAny(text, "stream filter"))
                return Item(node, "dataflow_selector", "Selects which input stream is passed downstream.", "component name/description", null);
            if (ContainsAny(text, "loop start", "loop end", "iteration start", "iteration end"))
                return Item(node, "iterative_controller", "Marks an iterative control boundary whose repetition is component-defined.", "component name/description", null);
            if (ContainsAny(runtime, "kangaroo2component.kangaroogh", "kangaroo2component.stepsolver", "kangaroo2component.kangarooZombie") || ContainsAny(text, "iterative solver", "physics solver", "optimization solver"))
                return Item(node, "iterative_solver", "Runs an internal iterative solver; intermediate states and solver history are not represented by ordinary wires.", "solver runtime type or description", null);
            if (ContainsAny(text, "timer", "recompute periodically", "scheduled solution"))
                return Item(node, "scheduler", "May schedule or trigger repeated Grasshopper solutions.", "component description", null);
            if (ContainsAny(text, "defer", "delay data", "pause data", "retain the last", "cache state", "accumulate across"))
                return Item(node, "stateful_or_deferred_component", "May defer computation or retain state across solutions; exact execution remains component-defined.", "component description", null);
            if (ContainsAny(text, "iterate", "iteration", "repeat until", "repeatedly solve"))
                return Item(node, "internally_iterative_component", "Performs repeated work internally; the visible wire graph represents its inputs and final outputs rather than each iteration.", "component description", null);
            return null;
        }

        private static ContextExecutionComponent Item(ContextNode node, string kind, string behavior, string evidence, IEnumerable<string> extraEvidence)
        {
            ContextExecutionComponent item = new ContextExecutionComponent { NodeId = node.InstanceId, NodeName = DisplayName(node), Kind = kind, Behavior = behavior };
            if (!String.IsNullOrWhiteSpace(evidence)) item.Evidence.Add(evidence);
            if (extraEvidence != null) item.Evidence.AddRange(extraEvidence.Where(e => !String.IsNullOrWhiteSpace(e)));
            return item;
        }

        private static IEnumerable<string> ControlEvidence(ContextNode node, Dictionary<string, ContextNode> nodes)
        {
            foreach (ContextControlLink link in node.ControlLinks ?? new List<ContextControlLink>())
            {
                ContextNode target;
                string name = nodes.TryGetValue(link.TargetNodeId, out target) ? DisplayName(target) : link.TargetNodeId;
                yield return link.Role + " link to " + name;
            }
        }

        private static string DescribeTargets(IEnumerable<ContextControlLink> links, Dictionary<string, ContextNode> nodes)
        {
            List<string> names = links.Select(link => TargetName(link, nodes)).ToList();
            return String.Join(", ", names.GroupBy(n => n, StringComparer.OrdinalIgnoreCase).OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.Count() == 1 ? g.Key : g.Key + " (" + g.Count() + " components)").ToArray());
        }

        private static string TargetName(ContextControlLink link, Dictionary<string, ContextNode> nodes)
        {
            ContextNode target; return nodes.TryGetValue(link.TargetNodeId, out target) ? DisplayName(target) : link.TargetNodeId;
        }

        private static string DescribeFitnessConstruction(string fitnessNodeId, ContextDocument document, Dictionary<string, ContextNode> nodes)
        {
            ContextNode fitness;
            if (!nodes.TryGetValue(fitnessNodeId, out fitness)) return "";
            List<string> inputs = new List<string>();
            foreach (ContextEdge edge in document.Edges.Where(e => e.BoundaryStatus == "internal" && String.Equals(e.TargetNodeId, fitnessNodeId, StringComparison.OrdinalIgnoreCase)))
            {
                ContextNode source;
                if (!nodes.TryGetValue(edge.SourceNodeId, out source)) continue;
                ScriptBehaviorSummary script = ScriptBehaviorAnalyzer.Analyze(source);
                string description = DisplayName(source);
                if (!String.IsNullOrWhiteSpace(script.PossibleRole)) description += " — may " + script.PossibleRole;
                if (!inputs.Contains(description, StringComparer.OrdinalIgnoreCase)) inputs.Add(description);
            }
            if (inputs.Count == 0) return "";
            return "The fitness component " + DisplayName(fitness) + " directly receives " + String.Join("; ", inputs.ToArray()) + ".";
        }

        private static string Metadata(ContextNode node, string key)
        {
            ContextMetadataEntry entry = (node.ExecutionMetadata ?? new List<ContextMetadataEntry>()).FirstOrDefault(e => String.Equals(e.Key, key, StringComparison.OrdinalIgnoreCase));
            return entry == null ? "" : entry.Value;
        }

        private static Dictionary<string, List<string>> NewGraph(IEnumerable<string> ids)
        {
            return ids.ToDictionary(id => id, id => new List<string>(), StringComparer.OrdinalIgnoreCase);
        }

        private static void AddDistinct(List<string> values, string value)
        {
            if (!values.Contains(value, StringComparer.OrdinalIgnoreCase)) values.Add(value);
        }

        private static HashSet<string> Reachable(string start, Dictionary<string, List<string>> graph)
        {
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            Queue<string> queue = new Queue<string>(); queue.Enqueue(start); seen.Add(start);
            while (queue.Count > 0)
            {
                string current = queue.Dequeue();
                foreach (string next in graph[current]) if (seen.Add(next)) queue.Enqueue(next);
            }
            return seen;
        }

        private static bool HasCycle(IEnumerable<string> ids, Dictionary<string, List<string>> next)
        {
            Dictionary<string, int> degree = ids.ToDictionary(id => id, id => 0, StringComparer.OrdinalIgnoreCase);
            foreach (List<string> targets in next.Values) foreach (string target in targets) degree[target]++;
            Queue<string> queue = new Queue<string>(degree.Where(p => p.Value == 0).Select(p => p.Key)); int visited = 0;
            while (queue.Count > 0) { string id = queue.Dequeue(); visited++; foreach (string target in next[id]) { degree[target]--; if (degree[target] == 0) queue.Enqueue(target); } }
            return visited != degree.Count;
        }

        private static bool IsControlPort(string name)
        {
            string value = (name ?? "").Trim().ToLowerInvariant();
            return value == ">" || value == "<" || value == "counter" || value == "exit" || value == "repeat" || value == "trigger" || value == "iterations";
        }

        private static string Combined(ContextNode node)
        {
            return ((node.Name ?? "") + " " + (node.Nickname ?? "") + " " + (node.Description ?? "") + " " + (node.Category ?? "") + " " + (node.Subcategory ?? "") + " " + (node.RuntimeTypeName ?? "")).ToLowerInvariant();
        }

        private static bool IsCluster(ContextNode node)
        {
            string runtime = (node.RuntimeTypeName ?? "").ToLowerInvariant();
            return runtime == "grasshopper.kernel.special.gh_cluster" || runtime == "grasshopper.kernel.special.gh_cluster_obsolete" || String.Equals(node.Name, "Cluster", StringComparison.OrdinalIgnoreCase);
        }

        private static bool ContainsAny(string value, params string[] terms)
        {
            foreach (string term in terms) if ((value ?? "").IndexOf(term.ToLowerInvariant(), StringComparison.Ordinal) >= 0) return true;
            return false;
        }

        private static string DisplayName(ContextNode node) { return String.IsNullOrWhiteSpace(node.Nickname) ? (String.IsNullOrWhiteSpace(node.Name) ? "Unnamed object" : node.Name) : node.Nickname; }
        private static string DisplayName(ContextParameter parameter) { return String.IsNullOrWhiteSpace(parameter.Nickname) ? (String.IsNullOrWhiteSpace(parameter.Name) ? "Unnamed parameter" : parameter.Name) : parameter.Nickname; }
    }
}
