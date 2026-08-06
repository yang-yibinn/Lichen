using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Special;
using Lichen.Core;

namespace Lichen.Adapters
{
    public sealed class GrasshopperGraphExtractor
    {
        private const int MaximumRuntimeMessagesPerLevel = 20;
        private const int MaximumClusterDepth = 4;

        public ContextSnapshot Capture(GH_Document document, bool includeScripts, bool includeRuntime)
        {
            return Capture(document, includeScripts, includeRuntime, 500, ScopeMode.EntireDocument);
        }

        public ContextSnapshot Capture(GH_Document document, bool includeScripts, bool includeRuntime, int maximumClusterNodes)
        {
            return Capture(document, includeScripts, includeRuntime, maximumClusterNodes, ScopeMode.EntireDocument);
        }

        public ContextSnapshot Capture(GH_Document document, bool includeScripts, bool includeRuntime, int maximumClusterNodes, ScopeMode scopeMode)
        {
            return Capture(document, includeScripts, includeRuntime, maximumClusterNodes, new ContextExportOptions { ScopeMode = scopeMode, MaximumNodes = maximumClusterNodes });
        }

        public ContextSnapshot Capture(GH_Document document, bool includeScripts, bool includeRuntime, int maximumClusterNodes, ContextExportOptions options)
        {
            if (options == null) throw new ArgumentNullException("options");
            ClusterCaptureContext context = new ClusterCaptureContext(maximumClusterNodes <= 0 ? 500 : maximumClusterNodes, options.ScopeMode, options.RootObjectId, options.RootLabel);
            return CaptureDocument(document, includeScripts, includeRuntime, context, 0, false);
        }

        private ContextSnapshot CaptureDocument(GH_Document document, bool includeScripts, bool includeRuntime, ClusterCaptureContext context, int clusterDepth, bool clusterInternal)
        {
            if (document == null) throw new ArgumentNullException("document");
            ContextSnapshot snapshot = new ContextSnapshot();
            snapshot.Name = String.IsNullOrWhiteSpace(document.DisplayName) ? "Untitled" : document.DisplayName;
            snapshot.RhinoVersion = SafeAssemblyVersion(typeof(Rhino.RhinoDoc).Assembly);
            snapshot.GrasshopperVersion = SafeAssemblyVersion(typeof(GH_Document).Assembly);

            List<IGH_DocumentObject> objects = document.Objects.OrderBy(o => Id(o.InstanceGuid), StringComparer.OrdinalIgnoreCase).ToList();
            HashSet<string> selected = new HashSet<string>(document.SelectedObjects().Select(o => Id(o.InstanceGuid)), StringComparer.OrdinalIgnoreCase);
            snapshot.SelectedObjectIds.AddRange(selected.OrderBy(s => s, StringComparer.OrdinalIgnoreCase));

            Dictionary<IGH_Param, Endpoint> outputs = new Dictionary<IGH_Param, Endpoint>(ReferenceComparer<IGH_Param>.Instance);
            Dictionary<IGH_Param, Endpoint> inputs = new Dictionary<IGH_Param, Endpoint>(ReferenceComparer<IGH_Param>.Instance);
            Dictionary<string, ContextNode> nodes = new Dictionary<string, ContextNode>(StringComparer.OrdinalIgnoreCase);

            foreach (IGH_DocumentObject obj in objects)
            {
                if (clusterInternal && context.RemainingNodes <= 0)
                {
                    snapshot.Notes.Add("The cluster-internal node limit was reached; remaining cluster objects were not inspected.");
                    break;
                }
                if (clusterInternal) context.RemainingNodes--;
                try
                {
                    ContextNode node = CaptureNode(obj, includeScripts, includeRuntime, inputs, outputs, context, clusterDepth, clusterInternal);
                    nodes[node.InstanceId] = node; snapshot.Nodes.Add(node);
                }
                catch (Exception ex)
                {
                    string id = SafeObjectId(obj);
                    snapshot.Notes.Add("Object " + id + " could not be fully inspected: " + OneLine(ex.Message));
                    if (!nodes.ContainsKey(id))
                    {
                        ContextNode fallback = new ContextNode { InstanceId = id, TypeId = SafeComponentId(obj), Name = SafeString(delegate { return obj.Name; }, "Unsupported object"), Nickname = SafeString(delegate { return obj.NickName; }, "Unsupported object"), Description = "Extraction failed; see notes." };
                        nodes[id] = fallback; snapshot.Nodes.Add(fallback);
                    }
                }
            }

            CaptureGroups(objects, nodes, snapshot);
            CaptureEdges(inputs, outputs, snapshot);
            CaptureExportRoots(objects, snapshot);
            if (!clusterInternal) CaptureIncludedRootClusters(objects, nodes, snapshot, includeScripts, includeRuntime, context);
            snapshot.Nodes = snapshot.Nodes.OrderBy(n => n.InstanceId, StringComparer.OrdinalIgnoreCase).ToList();
            snapshot.Edges = snapshot.Edges.OrderBy(EdgeKey, StringComparer.OrdinalIgnoreCase).ToList();
            return snapshot;
        }

        private ContextNode CaptureNode(IGH_DocumentObject obj, bool includeScripts, bool includeRuntime, Dictionary<IGH_Param, Endpoint> inputs, Dictionary<IGH_Param, Endpoint> outputs, ClusterCaptureContext context, int clusterDepth, bool inspectClusters)
        {
            Type type = obj.GetType(); AssemblyName assembly = type.Assembly.GetName();
            ContextNode node = new ContextNode();
            node.InstanceId = Id(obj.InstanceGuid); node.TypeId = SafeComponentId(obj); node.Name = obj.Name ?? ""; node.Nickname = obj.NickName ?? "";
            node.Description = obj.Description ?? ""; node.Category = obj.Category ?? ""; node.Subcategory = obj.SubCategory ?? "";
            node.AssemblyName = assembly.Name ?? ""; node.AssemblyVersion = assembly.Version == null ? "" : assembly.Version.ToString(); node.PluginName = assembly.Name ?? ""; node.RuntimeTypeName = type.FullName ?? type.Name ?? "";
            node.CanvasBounds = CaptureBounds(obj.Attributes); node.State = CaptureState(obj); node.PersistentValueSummary = CaptureSpecialValue(obj);
            CaptureExecutionMetadata(obj, node);
            if (includeRuntime) CaptureRuntimeMessages(obj as IGH_ActiveObject, node.RuntimeMessages);
            if (includeScripts) node.Script = TryCaptureScript(obj);

            IGH_Component component = obj as IGH_Component;
            if (component != null)
            {
                bool isExportRoot = SafeComponentGuid(obj) == LichenComponentIds.ExportRoot;
                for (int i = 0; i < component.Params.Input.Count; i++)
                {
                    IGH_Param parameter = component.Params.Input[i]; node.Inputs.Add(CaptureParameter(parameter, i, "input", includeRuntime && !isExportRoot, !isExportRoot));
                    inputs[parameter] = new Endpoint(node.InstanceId, i, parameter.Name);
                }
                for (int i = 0; i < component.Params.Output.Count; i++)
                {
                    IGH_Param parameter = component.Params.Output[i]; node.Outputs.Add(CaptureParameter(parameter, i, "output", includeRuntime && !isExportRoot, !isExportRoot));
                    outputs[parameter] = new Endpoint(node.InstanceId, i, parameter.Name);
                }
            }
            else
            {
                IGH_Param parameter = obj as IGH_Param;
                if (parameter != null)
                {
                    ContextParameter input = CaptureParameter(parameter, 0, "input", includeRuntime, true); ContextParameter output = CaptureParameter(parameter, 0, "output", includeRuntime, true);
                    node.Inputs.Add(input); node.Outputs.Add(output);
                    inputs[parameter] = new Endpoint(node.InstanceId, 0, parameter.Name); outputs[parameter] = new Endpoint(node.InstanceId, 0, parameter.Name);
                }
            }
            GH_Cluster cluster = obj as GH_Cluster;
            if (cluster != null && inspectClusters) node.ClusterGraph = CaptureClusterGraph(cluster, includeScripts, includeRuntime, context, clusterDepth);
            return node;
        }

        private void CaptureIncludedRootClusters(List<IGH_DocumentObject> objects, Dictionary<string, ContextNode> nodes, ContextSnapshot snapshot, bool includeScripts, bool includeRuntime, ClusterCaptureContext context)
        {
            ContextExportOptions scopeOptions = new ContextExportOptions
            {
                ScopeMode = context.RootScopeMode,
                DetailLevel = DetailLevel.Exact,
                IncludeScriptSource = includeScripts,
                IncludeRuntimeSummary = includeRuntime,
                MaximumNodes = context.MaximumNodes
            };
            scopeOptions.RootObjectId = context.RootObjectId;
            scopeOptions.RootLabel = context.RootLabel;
            ContextDocument scoped = new ContextGraphService().BuildDocument(snapshot, scopeOptions);
            HashSet<string> included = new HashSet<string>(scoped.Scope.IncludedObjectIds, StringComparer.OrdinalIgnoreCase);
            foreach (GH_Cluster cluster in objects.OfType<GH_Cluster>().Where(c => included.Contains(Id(c.InstanceGuid))).OrderBy(c => Id(c.InstanceGuid), StringComparer.OrdinalIgnoreCase))
            {
                ContextNode node;
                if (nodes.TryGetValue(Id(cluster.InstanceGuid), out node)) node.ClusterGraph = CaptureClusterGraph(cluster, includeScripts, includeRuntime, context, 0);
            }
        }

        private ContextClusterGraph CaptureClusterGraph(GH_Cluster cluster, bool includeScripts, bool includeRuntime, ClusterCaptureContext context, int clusterDepth)
        {
            ContextClusterGraph graph = new ContextClusterGraph();
            Guid documentId = Guid.Empty;
            try { documentId = cluster.DocumentId; } catch { }
            if (documentId != Guid.Empty) graph.DocumentId = Id(documentId);

            try
            {
                if (cluster.ProtectionLevel != GH_ClusterProtection.Unprotected)
                {
                    graph.InspectionStatus = "protected";
                    graph.InspectionNote = "The cluster is password-protected; Lichen did not request or attempt a password.";
                    return graph;
                }
            }
            catch (Exception ex)
            {
                graph.InspectionStatus = "unavailable";
                graph.InspectionNote = "Cluster protection metadata was unavailable: " + OneLine(ex.Message);
                return graph;
            }

            if (clusterDepth >= MaximumClusterDepth)
            {
                graph.InspectionStatus = "depth_limit_reached";
                graph.InspectionNote = "Nested cluster inspection stopped at the maximum depth of " + MaximumClusterDepth + ".";
                return graph;
            }
            if (context.RemainingNodes <= 0)
            {
                graph.InspectionStatus = "node_limit_reached";
                graph.NodeLimitReached = true;
                graph.InspectionNote = "The shared cluster-internal node limit was reached before this cluster could be inspected.";
                return graph;
            }
            if (documentId != Guid.Empty && !context.AncestorDocumentIds.Add(documentId))
            {
                graph.InspectionStatus = "cycle_detected";
                graph.InspectionNote = "Nested cluster inspection stopped because this cluster document is already present in the current cluster ancestry.";
                return graph;
            }

            try
            {
                GH_Document internalDocument = cluster.Document("");
                if (internalDocument == null)
                {
                    graph.InspectionStatus = "unavailable";
                    graph.InspectionNote = "Grasshopper did not provide an accessible cluster document.";
                    return graph;
                }

                ContextSnapshot snapshot = CaptureDocument(internalDocument, includeScripts, includeRuntime, context, clusterDepth + 1, true);
                ContextExportOptions options = new ContextExportOptions
                {
                    ScopeMode = ScopeMode.EntireDocument,
                    DetailLevel = DetailLevel.Exact,
                    IncludeScriptSource = includeScripts,
                    IncludeRuntimeSummary = includeRuntime,
                    MaximumNodes = Math.Max(1, snapshot.Nodes.Count)
                };
                ContextDocument document = new ContextGraphService().BuildDocument(snapshot, options);
                graph.InspectionStatus = "inspected";
                graph.NodeLimitReached = document.Scope.NodeLimitReached;
                graph.Nodes = document.Nodes;
                graph.Edges = document.Edges;
                graph.Groups = document.Groups;
                graph.Dependencies = document.Dependencies;
                graph.Analysis = document.Analysis;
                graph.ExtractionNotes = document.ExtractionNotes;
                if (graph.NodeLimitReached) graph.InspectionNote = "The cluster was inspected, but the shared cluster-internal node limit truncated its nested graph.";
                return graph;
            }
            catch (Exception ex)
            {
                graph.InspectionStatus = "unavailable";
                graph.InspectionNote = "Cluster internals could not be inspected safely: " + OneLine(ex.Message);
                return graph;
            }
            finally
            {
                if (documentId != Guid.Empty) context.AncestorDocumentIds.Remove(documentId);
            }
        }

        private static ContextParameter CaptureParameter(IGH_Param parameter, int index, string direction, bool includeRuntime, bool includePersistent)
        {
            ContextParameter result = new ContextParameter();
            result.Index = index; result.Name = parameter.Name ?? ""; result.Nickname = parameter.NickName ?? ""; result.Description = parameter.Description ?? "";
            result.Direction = direction; result.AccessMode = parameter.Access.ToString().ToLowerInvariant(); result.Optional = parameter.Optional; result.TypeHint = SafeParameterTypeHint(parameter);
            result.SourceCount = parameter.Sources == null ? 0 : parameter.Sources.Count; result.RecipientCount = parameter.Recipients == null ? 0 : parameter.Recipients.Count;
            result.Reverse = parameter.Reverse; result.Expression = SafePropertyString(parameter, "Expression");
            string mapping = parameter.DataMapping.ToString(); result.Flatten = mapping.IndexOf("Flatten", StringComparison.OrdinalIgnoreCase) >= 0;
            result.Graft = mapping.IndexOf("Graft", StringComparison.OrdinalIgnoreCase) >= 0; result.Simplify = mapping.IndexOf("Simplify", StringComparison.OrdinalIgnoreCase) >= 0;
            result.PersistentDataSummary = includePersistent ? CapturePersistentDataSummary(parameter) : "";
            result.RuntimeDataSummary = includeRuntime ? CaptureRuntimeDataSummary(parameter) : "";
            return result;
        }

        private static void CaptureEdges(Dictionary<IGH_Param, Endpoint> inputs, Dictionary<IGH_Param, Endpoint> outputs, ContextSnapshot snapshot)
        {
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<IGH_Param, Endpoint> target in inputs)
            {
                IList<IGH_Param> sources;
                try { sources = target.Key.Sources; } catch (Exception ex) { snapshot.Notes.Add("Sources for parameter " + target.Value.Name + " were inaccessible: " + OneLine(ex.Message)); continue; }
                if (sources == null) continue;
                foreach (IGH_Param source in sources)
                {
                    Endpoint sourceEndpoint;
                    if (!outputs.TryGetValue(source, out sourceEndpoint))
                    {
                        snapshot.Notes.Add("A source endpoint for parameter " + target.Value.Name + " could not be resolved."); continue;
                    }
                    ContextEdge edge = new ContextEdge { SourceNodeId = sourceEndpoint.NodeId, SourceParameterIndex = sourceEndpoint.Index, SourceParameterName = sourceEndpoint.Name, TargetNodeId = target.Value.NodeId, TargetParameterIndex = target.Value.Index, TargetParameterName = target.Value.Name };
                    if (seen.Add(EdgeKey(edge))) snapshot.Edges.Add(edge);
                }
            }
        }

        private static void CaptureExportRoots(IEnumerable<IGH_DocumentObject> objects, ContextSnapshot snapshot)
        {
            foreach (IGH_DocumentObject obj in objects.Where(o => SafeComponentGuid(o) == LichenComponentIds.ExportRoot).OrderBy(o => Id(o.InstanceGuid), StringComparer.OrdinalIgnoreCase))
            {
                string objectId = Id(obj.InstanceGuid);
                ExportRootDefinition root = new ExportRootDefinition { ObjectId = objectId, Label = String.IsNullOrWhiteSpace(obj.NickName) ? "Lichen" : obj.NickName.Trim() };
                root.SourceObjectIds = snapshot.Edges.Where(e => String.Equals(e.TargetNodeId, objectId, StringComparison.OrdinalIgnoreCase))
                    .Select(e => e.SourceNodeId).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(id => id, StringComparer.OrdinalIgnoreCase).ToList();
                snapshot.ExportRoots.Add(root);
            }
        }

        private static void CaptureGroups(IList<IGH_DocumentObject> objects, Dictionary<string, ContextNode> nodes, ContextSnapshot snapshot)
        {
            foreach (GH_Group group in objects.OfType<GH_Group>())
            {
                ContextGroup value = new ContextGroup { InstanceId = Id(group.InstanceGuid), Name = group.NickName ?? group.Name ?? "Group" };
                foreach (Guid member in group.ObjectIDs.OrderBy(g => g))
                {
                    string id = Id(member); value.MemberIds.Add(id);
                    ContextNode node; if (nodes.TryGetValue(id, out node) && !node.GroupIds.Contains(value.InstanceId)) node.GroupIds.Add(value.InstanceId);
                }
                snapshot.Groups.Add(value);
            }
            snapshot.Groups = snapshot.Groups.OrderBy(g => g.InstanceId, StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static ContextNodeState CaptureState(IGH_DocumentObject obj)
        {
            IGH_ActiveObject active = obj as IGH_ActiveObject; IGH_PreviewObject preview = obj as IGH_PreviewObject;
            bool locked = active != null && active.Locked;
            return new ContextNodeState { Enabled = !locked, Locked = locked, Hidden = preview != null && preview.Hidden, PreviewCapable = preview != null };
        }

        private static void CaptureRuntimeMessages(IGH_ActiveObject active, List<ContextRuntimeMessage> destination)
        {
            if (active == null) return;
            AddMessages(active, GH_RuntimeMessageLevel.Error, "error", destination);
            AddMessages(active, GH_RuntimeMessageLevel.Warning, "warning", destination);
            AddMessages(active, GH_RuntimeMessageLevel.Remark, "remark", destination);
        }

        private static void AddMessages(IGH_ActiveObject active, GH_RuntimeMessageLevel level, string name, List<ContextRuntimeMessage> destination)
        {
            try
            {
                int count = 0; foreach (string message in active.RuntimeMessages(level)) { if (count++ >= MaximumRuntimeMessagesPerLevel) break; destination.Add(new ContextRuntimeMessage { Level = name, Message = message ?? "" }); }
            }
            catch (Exception ex) { destination.Add(new ContextRuntimeMessage { Level = "extraction_note", Message = "Runtime messages unavailable: " + OneLine(ex.Message) }); }
        }

        private static string CapturePersistentDataSummary(IGH_Param parameter)
        {
            try
            {
                object persistent = SafeProperty(parameter, "PersistentData"); IGH_Structure structure = persistent as IGH_Structure;
                if (structure != null && structure.DataCount > 0)
                {
                    List<string> values = new List<string>(); bool simple = structure.DataCount <= 8;
                    if (simple)
                    {
                        IEnumerator data = (IEnumerator)structure.AllData(true);
                        while (data.MoveNext())
                        {
                            object item = data.Current;
                            if (!IsSimpleGoo(item)) { simple = false; break; }
                            values.Add(Bounded(Convert.ToString(item, CultureInfo.InvariantCulture), 128));
                        }
                    }
                    if (simple && values.Count == structure.DataCount)
                        return values.Count == 1 ? "value=" + values[0] : "values=[" + String.Join(", ", values.ToArray()) + "]";
                    return "persistent items=" + structure.DataCount + ", branches=" + structure.PathCount;
                }
            }
            catch { }
            return "";
        }

        private static string CaptureRuntimeDataSummary(IGH_Param parameter)
        {
            try
            {
                IGH_Structure data = parameter.VolatileData;
                if (data != null && data.DataCount > 0) return "items=" + data.DataCount + ", branches=" + data.PathCount;
            }
            catch { }
            return "";
        }

        private static string CaptureSpecialValue(IGH_DocumentObject obj)
        {
            string typeName = obj.GetType().FullName ?? "";
            try
            {
                if (typeName.IndexOf("GH_NumberSlider", StringComparison.OrdinalIgnoreCase) >= 0) return "value=" + Convert.ToString(SafeProperty(obj, "CurrentValue"), CultureInfo.InvariantCulture);
                if (typeName.IndexOf("GH_BooleanToggle", StringComparison.OrdinalIgnoreCase) >= 0) return "value=" + Convert.ToString(SafeProperty(obj, "Value"), CultureInfo.InvariantCulture);
                if (typeName.IndexOf("GH_Panel", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    object value = SafeProperty(obj, "UserText") ?? SafeProperty(obj, "Text"); return value == null ? "panel text unavailable" : "text=" + Bounded(Convert.ToString(value, CultureInfo.InvariantCulture), 512);
                }
                if (typeName.IndexOf("GH_Scribble", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    object value = SafeProperty(obj, "Text") ?? SafeProperty(obj, "UserText"); return value == null ? "note text unavailable" : "note=" + Bounded(Convert.ToString(value, CultureInfo.InvariantCulture), 512);
                }
                if (typeName.IndexOf("GH_ValueList", StringComparison.OrdinalIgnoreCase) >= 0) return "value list; selected values are summarized through volatile data";
            }
            catch (Exception ex) { return "value unavailable: " + OneLine(ex.Message); }
            return null;
        }

        private static bool IsSimpleGoo(object value)
        {
            if (value == null) return true;
            string name = value.GetType().Name;
            string[] safe = { "GH_Number", "GH_Integer", "GH_Boolean", "GH_String", "GH_Interval", "GH_Complex", "GH_Colour", "GH_Time" };
            return safe.Contains(name, StringComparer.OrdinalIgnoreCase);
        }

        private static void CaptureExecutionMetadata(IGH_DocumentObject obj, ContextNode node)
        {
            string typeName = obj.GetType().FullName ?? "";
            string lower = typeName.ToLowerInvariant();

            if (lower == "grasshopper.kernel.special.gh_timer")
            {
                AddMetadata(node, "timerIntervalMilliseconds", SafeProperty(obj, "Interval"));
                AddMetadata(node, "timerInterval", SafeProperty(obj, "IntervalString"));
                AddMetadata(node, "manual", SafeProperty(obj, "Manual"));
                AddMetadata(node, "targetCount", SafeProperty(obj, "TargetCount"));
                AddGuidLinks(node, "scheduled_target", SafeProperty(obj, "Targets"));
            }
            else if (lower == "grasshopper.kernel.components.gh_datadamcomponent")
            {
                AddMetadata(node, "mode", SafeProperty(obj, "Mode"));
                object delay = SafeProperty(obj, "Delay");
                TimeSpan? span = delay is TimeSpan ? (TimeSpan?)delay : null;
                if (span.HasValue) AddMetadata(node, "delayMilliseconds", span.Value.TotalMilliseconds);
                AddMetadata(node, "transferPossible", SafeProperty(obj, "TransferPossible"));
            }
            else if (lower == "grasshopper.kernel.special.gh_datarecorder")
            {
                AddMetadata(node, "recording", SafeProperty(obj, "RecordData"));
                AddMetadata(node, "dataLimit", SafeProperty(obj, "DataLimit"));
            }
            else if (lower == "grasshopper.kernel.special.gh_cluster")
            {
                object filePath = SafeProperty(obj, "FilePath");
                AddMetadata(node, "storage", String.IsNullOrWhiteSpace(filePath as string) ? "embedded" : "linked_file");
                AddMetadata(node, "synchronisation", SafeProperty(obj, "Synchronisation"));
                AddMetadata(node, "caching", SafeProperty(obj, "EnableCaching"));
                AddMetadata(node, "protection", SafeProperty(obj, "ProtectionLevel"));
            }
            else if (lower == "galapagoscomponents.galapagosobject")
            {
                AddGuidLinks(node, "genome", SafeProperty(obj, "Inputs"));
                AddGuidLink(node, "fitness", SafeProperty(obj, "Output"));
                object settings = SafeProperty(obj, "Settings");
                if (settings != null)
                {
                    AddMetadata(node, "solver", SafeProperty(settings, "Solver"));
                    AddMetadata(node, "runtimeLimitEnabled", SafeProperty(settings, "UseRuntimeLimit"));
                    object limit = SafeProperty(settings, "RuntimeLimit");
                    TimeSpan? runtime = limit is TimeSpan ? (TimeSpan?)limit : null;
                    if (runtime.HasValue) AddMetadata(node, "runtimeLimit", runtime.Value.ToString());
                    AddMetadata(node, "infiniteTarget", SafeProperty(settings, "InfiniteTarget"));
                    AddMetadata(node, "target", SafeProperty(settings, "Target"));
                    AddMetadata(node, "threshold", SafeProperty(settings, "Threshold"));
                }
            }
        }

        private static void AddMetadata(ContextNode node, string key, object value)
        {
            if (value == null) return;
            if (node.ExecutionMetadata == null) node.ExecutionMetadata = new List<ContextMetadataEntry>();
            node.ExecutionMetadata.Add(new ContextMetadataEntry { Key = key, Value = Convert.ToString(value, CultureInfo.InvariantCulture) ?? "" });
        }

        private static void AddGuidLinks(ContextNode node, string role, object values)
        {
            IEnumerable enumerable = values as IEnumerable;
            if (enumerable == null) return;
            foreach (object value in enumerable) AddGuidLink(node, role, value);
        }

        private static void AddGuidLink(ContextNode node, string role, object value)
        {
            Guid id;
            if (value is Guid) id = (Guid)value;
            else if (!Guid.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), out id)) return;
            if (id == Guid.Empty) return;
            if (node.ControlLinks == null) node.ControlLinks = new List<ContextControlLink>();
            node.ControlLinks.Add(new ContextControlLink { Role = role, TargetNodeId = Id(id) });
        }

        private static ContextScript TryCaptureScript(IGH_DocumentObject obj)
        {
            ScriptSourceReadResult capture = SafeScriptSourceReader.Read(obj);
            if (!capture.Recognized) return null;
            return new ContextScript { Language = capture.Language, Source = capture.Source, ExtractionNote = capture.ExtractionNote };
        }

        private static string CaptureBounds(IGH_Attributes attributes)
        {
            if (attributes == null) return ""; RectangleF b = attributes.Bounds;
            return String.Format(CultureInfo.InvariantCulture, "x={0:0.###}, y={1:0.###}, width={2:0.###}, height={3:0.###}", b.X, b.Y, b.Width, b.Height);
        }

        private static object SafeProperty(object target, string name)
        {
            if (target == null) return null;
            try { PropertyInfo property = target.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance); return property == null || property.GetIndexParameters().Length > 0 ? null : property.GetValue(target, null); }
            catch { return null; }
        }
        private static string SafeParameterTypeHint(IGH_Param parameter)
        {
            // IGH_Param.TypeName is intentionally not used here. GH_Param<T>.TypeName may
            // instantiate T, which raises a Grasshopper breakpoint for interface or abstract T.
            Type concrete = parameter.GetType();
            Type current = concrete;
            while (current != null)
            {
                if (current.IsGenericType)
                {
                    Type[] arguments = current.GetGenericArguments();
                    if (arguments.Length == 1)
                    {
                        Type argument = arguments[0];
                        return String.IsNullOrWhiteSpace(argument.FullName) ? argument.Name : argument.FullName;
                    }
                }
                current = current.BaseType;
            }
            return String.IsNullOrWhiteSpace(concrete.FullName) ? concrete.Name : concrete.FullName;
        }
        private static string SafePropertyString(object target, string name) { object value = SafeProperty(target, name); return value == null ? "" : Convert.ToString(value, CultureInfo.InvariantCulture); }
        private static string SafeObjectId(IGH_DocumentObject obj) { return SafeString(delegate { return Id(obj.InstanceGuid); }, "unknown"); }
        private static string SafeComponentId(IGH_DocumentObject obj) { return SafeString(delegate { return Id(obj.ComponentGuid); }, "unknown"); }
        private static Guid SafeComponentGuid(IGH_DocumentObject obj) { try { return obj.ComponentGuid; } catch { return Guid.Empty; } }
        private static string SafeString(Func<string> read, string fallback) { try { string value = read(); return String.IsNullOrEmpty(value) ? fallback : value; } catch { return fallback; } }
        private static string SafeAssemblyVersion(Assembly assembly) { try { return assembly.GetName().Version.ToString(); } catch { return "unknown"; } }
        private static string Id(Guid value) { return value.ToString("D").ToLowerInvariant(); }
        private static string EdgeKey(ContextEdge edge) { return edge.SourceNodeId + "|" + edge.SourceParameterIndex + "|" + edge.TargetNodeId + "|" + edge.TargetParameterIndex; }
        private static string OneLine(string value) { return (value ?? "").Replace("\r", " ").Replace("\n", " ").Trim(); }
        private static string Bounded(string value, int maximum) { if (String.IsNullOrEmpty(value) || value.Length <= maximum) return value ?? ""; return value.Substring(0, maximum) + "…"; }

        private sealed class ClusterCaptureContext
        {
            public ClusterCaptureContext(int maximumNodes, ScopeMode rootScopeMode, string rootObjectId, string rootLabel) { MaximumNodes = maximumNodes; RemainingNodes = maximumNodes; RootScopeMode = rootScopeMode; RootObjectId = rootObjectId ?? ""; RootLabel = rootLabel ?? ""; AncestorDocumentIds = new HashSet<Guid>(); }
            public int MaximumNodes;
            public int RemainingNodes;
            public ScopeMode RootScopeMode;
            public string RootObjectId;
            public string RootLabel;
            public HashSet<Guid> AncestorDocumentIds;
        }

        private sealed class Endpoint
        {
            public Endpoint(string nodeId, int index, string name) { NodeId = nodeId; Index = index; Name = name ?? ""; }
            public string NodeId; public int Index; public string Name;
        }

        private sealed class ReferenceComparer<T> : IEqualityComparer<T> where T : class
        {
            public static readonly ReferenceComparer<T> Instance = new ReferenceComparer<T>();
            public bool Equals(T x, T y) { return Object.ReferenceEquals(x, y); }
            public int GetHashCode(T obj) { return RuntimeHelpers.GetHashCode(obj); }
        }
    }
}
