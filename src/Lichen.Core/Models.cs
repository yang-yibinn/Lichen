using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace Lichen.Core
{
    public enum ScopeMode { SelectedOnly, SelectedPlusImmediateUpstream, SelectedPlusAllUpstream, EntireDocument, ExportRoot }
    public enum DetailLevel { Brief, Technical, Exact }

    [DataContract]
    public sealed class ContextDocument
    {
        public ContextDocument()
        {
            SchemaVersion = "0.6"; Name = "Untitled"; RhinoVersion = ""; GrasshopperVersion = "";
            Scope = new ContextScope(); UserContext = new ContextUserContext(); Nodes = new List<ContextNode>();
            Edges = new List<ContextEdge>(); BoundaryInputs = new List<ContextBoundaryPort>();
            BoundaryOutputs = new List<ContextBoundaryPort>(); Groups = new List<ContextGroup>();
            Dependencies = new List<ContextDependency>(); Analysis = new ContextAnalysis(); ExtractionNotes = new List<string>();
        }
        [DataMember(Name="schemaVersion", Order=1)] public string SchemaVersion { get; set; }
        [DataMember(Name="name", Order=2)] public string Name { get; set; }
        [DataMember(Name="rhinoVersion", Order=3)] public string RhinoVersion { get; set; }
        [DataMember(Name="grasshopperVersion", Order=4)] public string GrasshopperVersion { get; set; }
        [DataMember(Name="scope", Order=5)] public ContextScope Scope { get; set; }
        [DataMember(Name="userContext", Order=6)] public ContextUserContext UserContext { get; set; }
        [DataMember(Name="nodes", Order=7)] public List<ContextNode> Nodes { get; set; }
        [DataMember(Name="edges", Order=8)] public List<ContextEdge> Edges { get; set; }
        [DataMember(Name="boundaryInputs", Order=9)] public List<ContextBoundaryPort> BoundaryInputs { get; set; }
        [DataMember(Name="boundaryOutputs", Order=10)] public List<ContextBoundaryPort> BoundaryOutputs { get; set; }
        [DataMember(Name="groups", Order=11)] public List<ContextGroup> Groups { get; set; }
        [DataMember(Name="dependencies", Order=12)] public List<ContextDependency> Dependencies { get; set; }
        [DataMember(Name="analysis", Order=13)] public ContextAnalysis Analysis { get; set; }
        [DataMember(Name="extractionNotes", Order=14)] public List<string> ExtractionNotes { get; set; }
        [DataMember(Name="exportSignature", Order=15, EmitDefaultValue=false)] public ContextExportSignature ExportSignature { get; set; }
    }

    [DataContract]
    public sealed class ContextExportSignature
    {
        public ContextExportSignature() { Product = "Lichen"; ExporterVersion = ""; FingerprintAlgorithm = "sha256"; ContextFingerprint = ""; }
        [DataMember(Name="product", Order=1)] public string Product { get; set; }
        [DataMember(Name="exporterVersion", Order=2)] public string ExporterVersion { get; set; }
        [DataMember(Name="fingerprintAlgorithm", Order=3)] public string FingerprintAlgorithm { get; set; }
        [DataMember(Name="contextFingerprint", Order=4)] public string ContextFingerprint { get; set; }
    }

    [DataContract]
    public sealed class ContextScope
    {
        public ContextScope() { Mode = "selected_only"; SelectedObjectIds = new List<string>(); IncludedObjectIds = new List<string>(); MaximumNodes = 500; }
        [DataMember(Name="mode", Order=1)] public string Mode { get; set; }
        [DataMember(Name="selectedObjectIds", Order=2)] public List<string> SelectedObjectIds { get; set; }
        [DataMember(Name="includedObjectIds", Order=3)] public List<string> IncludedObjectIds { get; set; }
        [DataMember(Name="maximumNodes", Order=4)] public int MaximumNodes { get; set; }
        [DataMember(Name="nodeLimitReached", Order=5)] public bool NodeLimitReached { get; set; }
        [DataMember(Name="rootLabel", Order=6, EmitDefaultValue=false)] public string RootLabel { get; set; }
        [DataMember(Name="rootSourceObjectIds", Order=7, EmitDefaultValue=false)] public List<string> RootSourceObjectIds { get; set; }
    }

    [DataContract]
    public sealed class ContextUserContext
    {
        public ContextUserContext() { Purpose = ""; RequestedTask = ""; Constraints = ""; }
        [DataMember(Name="purpose", Order=1)] public string Purpose { get; set; }
        [DataMember(Name="requestedTask", Order=2)] public string RequestedTask { get; set; }
        [DataMember(Name="constraints", Order=3)] public string Constraints { get; set; }
    }

    [DataContract]
    public sealed class ContextNode
    {
        public ContextNode()
        {
            InstanceId = ""; TypeId = ""; Name = ""; Nickname = ""; Description = ""; Category = ""; Subcategory = "";
            AssemblyName = ""; AssemblyVersion = ""; PluginName = ""; RuntimeTypeName = ""; State = new ContextNodeState();
            Inputs = new List<ContextParameter>(); Outputs = new List<ContextParameter>(); RuntimeMessages = new List<ContextRuntimeMessage>();
            GroupIds = new List<string>(); CanvasBounds = "";
        }
        [DataMember(Name="instanceId", Order=1)] public string InstanceId { get; set; }
        [DataMember(Name="typeId", Order=2)] public string TypeId { get; set; }
        [DataMember(Name="name", Order=3)] public string Name { get; set; }
        [DataMember(Name="nickname", Order=4)] public string Nickname { get; set; }
        [DataMember(Name="description", Order=5)] public string Description { get; set; }
        [DataMember(Name="category", Order=6)] public string Category { get; set; }
        [DataMember(Name="subcategory", Order=7)] public string Subcategory { get; set; }
        [DataMember(Name="assemblyName", Order=8)] public string AssemblyName { get; set; }
        [DataMember(Name="assemblyVersion", Order=9)] public string AssemblyVersion { get; set; }
        [DataMember(Name="pluginName", Order=10)] public string PluginName { get; set; }
        [DataMember(Name="originallySelected", Order=11)] public bool OriginallySelected { get; set; }
        [DataMember(Name="state", Order=12)] public ContextNodeState State { get; set; }
        [DataMember(Name="canvasBounds", Order=13)] public string CanvasBounds { get; set; }
        [DataMember(Name="groupIds", Order=14)] public List<string> GroupIds { get; set; }
        [DataMember(Name="inputs", Order=15)] public List<ContextParameter> Inputs { get; set; }
        [DataMember(Name="outputs", Order=16)] public List<ContextParameter> Outputs { get; set; }
        [DataMember(Name="runtimeMessages", Order=17)] public List<ContextRuntimeMessage> RuntimeMessages { get; set; }
        [DataMember(Name="script", Order=18, EmitDefaultValue=false)] public ContextScript Script { get; set; }
        [DataMember(Name="persistentValueSummary", Order=19, EmitDefaultValue=false)] public string PersistentValueSummary { get; set; }
        [DataMember(Name="runtimeTypeName", Order=20)] public string RuntimeTypeName { get; set; }
        [DataMember(Name="executionMetadata", Order=21, EmitDefaultValue=false)] public List<ContextMetadataEntry> ExecutionMetadata { get; set; }
        [DataMember(Name="controlLinks", Order=22, EmitDefaultValue=false)] public List<ContextControlLink> ControlLinks { get; set; }
        [DataMember(Name="clusterGraph", Order=23, EmitDefaultValue=false)] public ContextClusterGraph ClusterGraph { get; set; }
    }

    [DataContract]
    public sealed class ContextClusterGraph
    {
        public ContextClusterGraph()
        {
            InspectionStatus = "unavailable"; InspectionNote = ""; DocumentId = "";
            Nodes = new List<ContextNode>(); Edges = new List<ContextEdge>(); Groups = new List<ContextGroup>();
            Dependencies = new List<ContextDependency>(); Analysis = new ContextAnalysis(); ExtractionNotes = new List<string>();
        }
        [DataMember(Name="inspectionStatus", Order=1)] public string InspectionStatus { get; set; }
        [DataMember(Name="inspectionNote", Order=2)] public string InspectionNote { get; set; }
        [DataMember(Name="documentId", Order=3)] public string DocumentId { get; set; }
        [DataMember(Name="userProvidedPurpose", Order=4, EmitDefaultValue=false)] public string UserProvidedPurpose { get; set; }
        [DataMember(Name="blackBoxSummary", Order=5, EmitDefaultValue=false)] public string BlackBoxSummary { get; set; }
        [DataMember(Name="nodeLimitReached", Order=6)] public bool NodeLimitReached { get; set; }
        [DataMember(Name="nodes", Order=7)] public List<ContextNode> Nodes { get; set; }
        [DataMember(Name="edges", Order=8)] public List<ContextEdge> Edges { get; set; }
        [DataMember(Name="groups", Order=9)] public List<ContextGroup> Groups { get; set; }
        [DataMember(Name="dependencies", Order=10)] public List<ContextDependency> Dependencies { get; set; }
        [DataMember(Name="analysis", Order=11)] public ContextAnalysis Analysis { get; set; }
        [DataMember(Name="extractionNotes", Order=12)] public List<string> ExtractionNotes { get; set; }
    }

    [DataContract]
    public sealed class ContextNodeState
    {
        [DataMember(Name="enabled", Order=1)] public bool Enabled { get; set; }
        [DataMember(Name="locked", Order=2)] public bool Locked { get; set; }
        [DataMember(Name="hidden", Order=3)] public bool Hidden { get; set; }
        [DataMember(Name="previewCapable", Order=4)] public bool PreviewCapable { get; set; }
    }

    [DataContract]
    public sealed class ContextParameter
    {
        public ContextParameter() { Name = ""; Nickname = ""; Description = ""; Direction = ""; AccessMode = ""; TypeHint = ""; PersistentDataSummary = ""; RuntimeDataSummary = ""; RuntimeTreeShape = ""; Expression = ""; }
        [DataMember(Name="index", Order=1)] public int Index { get; set; }
        [DataMember(Name="name", Order=2)] public string Name { get; set; }
        [DataMember(Name="nickname", Order=3)] public string Nickname { get; set; }
        [DataMember(Name="description", Order=4)] public string Description { get; set; }
        [DataMember(Name="direction", Order=5)] public string Direction { get; set; }
        [DataMember(Name="accessMode", Order=6)] public string AccessMode { get; set; }
        [DataMember(Name="optional", Order=7)] public bool Optional { get; set; }
        [DataMember(Name="typeHint", Order=8)] public string TypeHint { get; set; }
        [DataMember(Name="sourceCount", Order=9)] public int SourceCount { get; set; }
        [DataMember(Name="recipientCount", Order=10)] public int RecipientCount { get; set; }
        [DataMember(Name="persistentDataSummary", Order=11)] public string PersistentDataSummary { get; set; }
        [DataMember(Name="expression", Order=12)] public string Expression { get; set; }
        [DataMember(Name="flatten", Order=13)] public bool Flatten { get; set; }
        [DataMember(Name="graft", Order=14)] public bool Graft { get; set; }
        [DataMember(Name="simplify", Order=15)] public bool Simplify { get; set; }
        [DataMember(Name="reverse", Order=16)] public bool Reverse { get; set; }
        [DataMember(Name="runtimeDataSummary", Order=17)] public string RuntimeDataSummary { get; set; }
        [DataMember(Name="runtimeTreeShape", Order=18)] public string RuntimeTreeShape { get; set; }
    }

    [DataContract]
    public sealed class ContextEdge
    {
        public ContextEdge() { SourceNodeId = ""; SourceParameterName = ""; TargetNodeId = ""; TargetParameterName = ""; BoundaryStatus = "internal"; }
        [DataMember(Name="sourceNodeId", Order=1)] public string SourceNodeId { get; set; }
        [DataMember(Name="sourceParameterIndex", Order=2)] public int SourceParameterIndex { get; set; }
        [DataMember(Name="sourceParameterName", Order=3)] public string SourceParameterName { get; set; }
        [DataMember(Name="targetNodeId", Order=4)] public string TargetNodeId { get; set; }
        [DataMember(Name="targetParameterIndex", Order=5)] public int TargetParameterIndex { get; set; }
        [DataMember(Name="targetParameterName", Order=6)] public string TargetParameterName { get; set; }
        [DataMember(Name="crossesScopeBoundary", Order=7)] public bool CrossesScopeBoundary { get; set; }
        [DataMember(Name="boundaryStatus", Order=8)] public string BoundaryStatus { get; set; }
    }

    [DataContract]
    public sealed class ContextBoundaryPort
    {
        public ContextBoundaryPort() { Direction = ""; ExternalNodeId = ""; ExternalNodeName = ""; ExternalParameterName = ""; InternalNodeId = ""; InternalNodeName = ""; InternalParameterName = ""; ParameterName = ""; }
        [DataMember(Name="direction", Order=1)] public string Direction { get; set; }
        [DataMember(Name="externalNodeId", Order=2)] public string ExternalNodeId { get; set; }
        [DataMember(Name="internalNodeId", Order=3)] public string InternalNodeId { get; set; }
        [DataMember(Name="parameterIndex", Order=4)] public int ParameterIndex { get; set; }
        [DataMember(Name="parameterName", Order=5)] public string ParameterName { get; set; }
        [DataMember(Name="externalNodeName", Order=6)] public string ExternalNodeName { get; set; }
        [DataMember(Name="externalParameterName", Order=7)] public string ExternalParameterName { get; set; }
        [DataMember(Name="internalNodeName", Order=8)] public string InternalNodeName { get; set; }
        [DataMember(Name="internalParameterName", Order=9)] public string InternalParameterName { get; set; }
    }

    [DataContract]
    public sealed class ContextGroup
    {
        public ContextGroup() { InstanceId = ""; Name = ""; MemberIds = new List<string>(); }
        [DataMember(Name="instanceId", Order=1)] public string InstanceId { get; set; }
        [DataMember(Name="name", Order=2)] public string Name { get; set; }
        [DataMember(Name="memberIds", Order=3)] public List<string> MemberIds { get; set; }
    }

    [DataContract]
    public sealed class ContextDependency
    {
        public ContextDependency() { Name = ""; Version = ""; Kind = "third_party"; }
        [DataMember(Name="name", Order=1)] public string Name { get; set; }
        [DataMember(Name="version", Order=2)] public string Version { get; set; }
        [DataMember(Name="kind", Order=3)] public string Kind { get; set; }
    }

    [DataContract]
    public sealed class ContextRuntimeMessage
    {
        public ContextRuntimeMessage() { Level = ""; Message = ""; }
        [DataMember(Name="level", Order=1)] public string Level { get; set; }
        [DataMember(Name="message", Order=2)] public string Message { get; set; }
    }

    [DataContract]
    public sealed class ContextScript
    {
        public ContextScript() { Language = ""; Source = ""; ExtractionNote = ""; }
        [DataMember(Name="language", Order=1)] public string Language { get; set; }
        [DataMember(Name="source", Order=2)] public string Source { get; set; }
        [DataMember(Name="extractionNote", Order=3)] public string ExtractionNote { get; set; }
    }

    [DataContract]
    public sealed class ContextMetadataEntry
    {
        public ContextMetadataEntry() { Key = ""; Value = ""; }
        [DataMember(Name="key", Order=1)] public string Key { get; set; }
        [DataMember(Name="value", Order=2)] public string Value { get; set; }
    }

    [DataContract]
    public sealed class ContextControlLink
    {
        public ContextControlLink() { Role = ""; TargetNodeId = ""; }
        [DataMember(Name="role", Order=1)] public string Role { get; set; }
        [DataMember(Name="targetNodeId", Order=2)] public string TargetNodeId { get; set; }
    }

    [DataContract]
    public sealed class ContextAnalysis
    {
        public ContextAnalysis() { InferredPurpose = ""; DetectedOperations = new List<string>(); DetectedPatterns = new List<string>(); Uncertainties = new List<string>(); ExecutionSemantics = new ContextExecutionSemantics(); }
        [DataMember(Name="inferredPurpose", Order=1)] public string InferredPurpose { get; set; }
        [DataMember(Name="detectedOperations", Order=2)] public List<string> DetectedOperations { get; set; }
        [DataMember(Name="detectedPatterns", Order=3)] public List<string> DetectedPatterns { get; set; }
        [DataMember(Name="uncertainties", Order=4)] public List<string> Uncertainties { get; set; }
        [DataMember(Name="executionSemantics", Order=5)] public ContextExecutionSemantics ExecutionSemantics { get; set; }
    }

    [DataContract]
    public sealed class ContextExecutionSemantics
    {
        public ContextExecutionSemantics() { Regions = new List<ContextExecutionRegion>(); Components = new List<ContextExecutionComponent>(); Notes = new List<string>(); }
        [DataMember(Name="hasNonLinearBehavior", Order=1)] public bool HasNonLinearBehavior { get; set; }
        [DataMember(Name="ordinaryWireGraphHasCycle", Order=2)] public bool OrdinaryWireGraphHasCycle { get; set; }
        [DataMember(Name="regions", Order=3)] public List<ContextExecutionRegion> Regions { get; set; }
        [DataMember(Name="components", Order=4)] public List<ContextExecutionComponent> Components { get; set; }
        [DataMember(Name="notes", Order=5)] public List<string> Notes { get; set; }
    }

    [DataContract]
    public sealed class ContextExecutionRegion
    {
        public ContextExecutionRegion() { Kind = ""; Label = ""; StartNodeId = ""; EndNodeId = ""; IterationLimit = ""; CarriedValues = new List<string>(); NodeIds = new List<string>(); Evidence = new List<string>(); }
        [DataMember(Name="kind", Order=1)] public string Kind { get; set; }
        [DataMember(Name="label", Order=2)] public string Label { get; set; }
        [DataMember(Name="startNodeId", Order=3)] public string StartNodeId { get; set; }
        [DataMember(Name="endNodeId", Order=4)] public string EndNodeId { get; set; }
        [DataMember(Name="nestingLevel", Order=5)] public int NestingLevel { get; set; }
        [DataMember(Name="iterationLimit", Order=6)] public string IterationLimit { get; set; }
        [DataMember(Name="carriedValues", Order=7)] public List<string> CarriedValues { get; set; }
        [DataMember(Name="nodeIds", Order=8)] public List<string> NodeIds { get; set; }
        [DataMember(Name="evidence", Order=9)] public List<string> Evidence { get; set; }
    }

    [DataContract]
    public sealed class ContextExecutionComponent
    {
        public ContextExecutionComponent() { NodeId = ""; NodeName = ""; Kind = ""; Behavior = ""; Evidence = new List<string>(); }
        [DataMember(Name="nodeId", Order=1)] public string NodeId { get; set; }
        [DataMember(Name="nodeName", Order=2)] public string NodeName { get; set; }
        [DataMember(Name="kind", Order=3)] public string Kind { get; set; }
        [DataMember(Name="behavior", Order=4)] public string Behavior { get; set; }
        [DataMember(Name="evidence", Order=5)] public List<string> Evidence { get; set; }
    }

    public sealed class ContextExportOptions
    {
        public ContextExportOptions() { ScopeMode = ScopeMode.SelectedOnly; DetailLevel = DetailLevel.Technical; MaximumNodes = 500; IncludeScriptSource = true; IncludeRuntimeSummary = true; RootObjectId = ""; RootLabel = ""; ExporterVersion = "0.8.1"; ClusterPurposeNotes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); }
        public ScopeMode ScopeMode { get; set; }
        public DetailLevel DetailLevel { get; set; }
        public int MaximumNodes { get; set; }
        public bool IncludeScriptSource { get; set; }
        public bool IncludeRuntimeSummary { get; set; }
        public bool IncludeJsonAppendix { get; set; }
        public string Purpose { get; set; }
        public string RequestedTask { get; set; }
        public string Constraints { get; set; }
        public string RootObjectId { get; set; }
        public string RootLabel { get; set; }
        public string ExporterVersion { get; set; }
        public Dictionary<string, string> ClusterPurposeNotes { get; set; }
    }

    public sealed class ContextSnapshot
    {
        public ContextSnapshot() { Name = "Untitled"; RhinoVersion = ""; GrasshopperVersion = ""; Nodes = new List<ContextNode>(); Edges = new List<ContextEdge>(); SelectedObjectIds = new List<string>(); ExportRoots = new List<ExportRootDefinition>(); Groups = new List<ContextGroup>(); Notes = new List<string>(); }
        public string Name { get; set; }
        public string RhinoVersion { get; set; }
        public string GrasshopperVersion { get; set; }
        public List<ContextNode> Nodes { get; set; }
        public List<ContextEdge> Edges { get; set; }
        public List<string> SelectedObjectIds { get; set; }
        public List<ExportRootDefinition> ExportRoots { get; set; }
        public List<ContextGroup> Groups { get; set; }
        public List<string> Notes { get; set; }
    }

    public sealed class ExportRootDefinition
    {
        public ExportRootDefinition() { ObjectId = ""; Label = ""; SourceObjectIds = new List<string>(); }
        public string ObjectId { get; set; }
        public string Label { get; set; }
        public List<string> SourceObjectIds { get; set; }
    }

    public sealed class ExportRootClosure
    {
        public ExportRootClosure() { RootObjectIds = new List<string>(); IncludedObjectIds = new List<string>(); ContributingEdges = new List<ContextEdge>(); }
        public List<string> RootObjectIds { get; set; }
        public List<string> IncludedObjectIds { get; set; }
        public List<ContextEdge> ContributingEdges { get; set; }
        public bool NodeLimitReached { get; set; }
    }

    public sealed class ContextExportPackage
    {
        public ContextDocument Document { get; set; }
        public string Markdown { get; set; }
        public string Json { get; set; }
    }
}
