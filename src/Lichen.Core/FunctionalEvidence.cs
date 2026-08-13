using System;
using System.Collections.Generic;
using System.Linq;

namespace Lichen.Core
{
    internal enum FunctionalEvidenceProvenance
    {
        Fact,
        Claim,
        Inference
    }

    internal enum FunctionalEvidenceStrength
    {
        Insufficient,
        Possible,
        Supported
    }

    internal sealed class FunctionalEvidence
    {
        public FunctionalEvidence()
        {
            RuleId = ""; Stage = ""; Explanation = ""; ScopeId = "";
            MatchedNodeIds = new List<string>(); MatchedEdgeKeys = new List<string>(); ResultNodeIds = new List<string>();
        }

        public string RuleId { get; set; }
        public string Stage { get; set; }
        public string Explanation { get; set; }
        public string ScopeId { get; set; }
        public FunctionalEvidenceProvenance Provenance { get; set; }
        public FunctionalEvidenceStrength Strength { get; set; }
        public List<string> MatchedNodeIds { get; set; }
        public List<string> MatchedEdgeKeys { get; set; }
        public List<string> ResultNodeIds { get; set; }
        public bool ReachesCapturedOutput { get; set; }
    }

    internal sealed class FunctionalEvidenceSet
    {
        public FunctionalEvidenceSet() { Items = new List<FunctionalEvidence>(); }
        public List<FunctionalEvidence> Items { get; private set; }

        public bool Has(string ruleId)
        {
            return Items.Any(item => String.Equals(item.RuleId, ruleId, StringComparison.OrdinalIgnoreCase));
        }

        public bool HasOutputRelevant(string ruleId)
        {
            return Items.Any(item => item.ReachesCapturedOutput && String.Equals(item.RuleId, ruleId, StringComparison.OrdinalIgnoreCase));
        }

        public bool HasAuxiliary(string ruleId)
        {
            return Items.Any(item => !item.ReachesCapturedOutput && String.Equals(item.RuleId, ruleId, StringComparison.OrdinalIgnoreCase));
        }
    }

    internal static class FunctionalEvidenceAnalyzer
    {
        internal const string SurfaceSubdivisionRule = "surface.connected_subdivision";
        internal const string NumericNormalizationRule = "numeric.connected_normalization";
        internal const string ImagePanelFilteringRule = "surface.connected_image_panel_filtering";
        internal const string CurveGuidedSweepRule = "curve.connected_guided_sweep";
        internal const string CurveNetworkPreparationRule = "curve.connected_network_preparation";
        internal const string IntersectionAngleRule = "curve.connected_intersection_angle";
        internal const string AngleRemappingRule = "curve.connected_angle_remapping";
        internal const string AngleDrivenFilletRule = "curve.connected_angle_driven_fillet";
        internal const string BlockPlacementRule = "geometry.connected_block_placement";
        internal const string DitheredPanelPartitionRule = "surface.connected_dithered_panel_partition";
        internal const string ModelBlockPlacementRule = "geometry.connected_model_block_placement";
        internal const string SurfacePointCurveNetworkRule = "surface.connected_point_grid_curve_network";
        internal const string TangentCurveReconstructionRule = "curve.connected_tangent_reconstruction";
        internal const string SurfacePipeMorphRule = "surface.connected_pipe_intersection_morph";
        internal const string GeometryGroupingRule = "geometry.exact_grouping";
        internal const string DiamondPanelGenerationRule = "surface.exact_diamond_panel_generation";
        internal const string DiagridStructureGenerationRule = "surface.exact_diagrid_structure_generation";

        private static readonly HashSet<string> GenericPlumbing = Names(
            "Relay", "Data", "Geometry", "Brep", "Surface", "Curve", "Point", "Vector", "Number", "Integer", "Boolean", "Text",
            "Panel", "Number Slider", "Integer Slider", "Boolean Toggle", "Value List", "Merge", "Entwine", "Graft Tree", "Flatten Tree",
            "Shift Paths", "List Item", "Partition List", "Clean Tree", "Simplify Tree", "Trim Tree", "Construct Domain", "Construct Domain²",
            "Construct Domain2");

        private static readonly HashSet<string> ImageValueProcessing = Union(GenericPlumbing, Names(
            "Average", "Bounds", "Divide Domain", "Remap Numbers", "Graph Mapper", "Includes", "Mass Addition", "Addition", "Subtraction",
            "Multiplication", "Division"));

        private static readonly HashSet<string> SweepPreparation = Union(GenericPlumbing, Names("Align Plane", "Orient", "Orient Direction"));
        private static readonly HashSet<string> FilletValueProcessing = Union(GenericPlumbing, Names("Graph Mapper"));
        private static readonly HashSet<string> BlockSources = Names("Create Block", "Block Definition", "Insert Block");
        private static readonly HashSet<string> BlockPlacementOperations = Names("Orient", "Orient Direction", "Transform", "Move", "Place Block", "Insert Block");
        private static readonly HashSet<string> ModelBlockTransformProcessing = Union(GenericPlumbing, Names("Dispatch"));
        private static readonly HashSet<string> ModelBlockResultProcessing = Union(GenericPlumbing, Names("Block Instance"));
        private static readonly HashSet<string> SurfaceGridNetworkProcessing = Union(GenericPlumbing, Names("Sub List", "Shift List", "Cull Pattern", "Line"));
        private static readonly HashSet<string> CurveSelectionProcessing = Union(GenericPlumbing, Names(
            "Cull Pattern", "Flip Curve", "Sort List", "Length", "Division", "Round", "Item Index", "Curve Proximity", "Smaller Than"));
        private static readonly HashSet<string> SurfacePipeProcessing = Union(GenericPlumbing, Names(
            "Move", "Brep Edges", "Join Curves", "Flip Curve", "Larger Than", "Cull Pattern", "Offset on Srf", "Flip Matrix"));
        private static readonly HashSet<string> PipeMorphProcessing = Union(GenericPlumbing, Names("Brep | Brep"));

        public static FunctionalEvidenceSet Analyze(ContextDocument document, IEnumerable<ContextNode> activeNodes)
        {
            FunctionalEvidenceSet result = new FunctionalEvidenceSet();
            List<ContextNode> active = (activeNodes ?? new List<ContextNode>()).Where(node => node != null).ToList();
            SemanticGraphIndex graph = new SemanticGraphIndex(document, active);

            AddDirect(result, DiamondPanelGenerationRule, "diamond-panel generation",
                "An exact LunchBox Diamond Panels component provides specialized panel-generation evidence.", ExactComponentIds(active, "Diamond Panels", "LunchBox"));
            AddDirect(result, DiagridStructureGenerationRule, "surface diagrid-structure generation",
                "An exact LunchBox Diagrid Structure component provides specialized surface-diagrid generation evidence.", ExactComponentIds(active, "Diagrid Structure", "LunchBox"));
            AddDirect(result, GeometryGroupingRule, "geometry grouping",
                "An exact native Group component provides geometry-grouping evidence.", ExactComponentIds(active, "Group", "Grasshopper"));

            AddSinglePath(result, SurfaceSubdivisionRule, "surface subdivision", "Connected surface-domain subdivision feeds surface trimming.",
                graph.FindPath(Names("Divide Domain²", "Divide Domain2"), Names("Isotrim"), 4, GenericPlumbing));

            AddSinglePath(result, NumericNormalizationRule, "numeric normalization or rescaling", "Connected numeric bounds feed value remapping.",
                graph.FindPath(Names("Bounds"), Names("Remap Numbers"), 4, GenericPlumbing));

            List<string> panelsToCull = graph.FindPath(Names("Quad Panels"), Names("Cull Pattern"), 5, GenericPlumbing);
            List<string> imageToCull = graph.FindPath(Names("Image Sampler"), Names("Cull Pattern"), 8, ImageValueProcessing);
            if (panelsToCull.Count > 0 && imageToCull.Count > 0)
                Add(result, ImagePanelFilteringRule, "image-driven filtering of quadrilateral panels using image-derived values",
                    "Connected panel geometry and processed image samples converge at pattern-based culling.", panelsToCull, imageToCull);

            AddDitheredPanelPartition(result, graph, active);
            AddSurfacePointCurveNetwork(result, graph, active);
            AddTangentCurveReconstruction(result, graph, active);
            AddSurfacePipeMorph(result, graph, active);

            List<string> projectToDivide = graph.FindPath(Names("Project"), Names("Divide Curve"), 4, GenericPlumbing);
            List<string> divideToFrames = graph.FindPath(Names("Divide Curve"), Names("Perp Frame"), 4, GenericPlumbing);
            List<string> framesToSections = graph.FindPath(Names("Perp Frame"), Names("Rectangle"), 4, SweepPreparation);
            List<string> sectionsToSweep = graph.FindPath(Names("Rectangle"), Names("Sweep1"), 4, SweepPreparation);
            if (projectToDivide.Count > 0 && divideToFrames.Count > 0 && framesToSections.Count > 0 && sectionsToSweep.Count > 0)
                Add(result, CurveGuidedSweepRule, "curve-guided sweep construction",
                    "Connected projected curves are divided into frames, used to construct sections, and passed into a sweep.",
                    projectToDivide, divideToFrames, framesToSections, sectionsToSweep);

            List<string> curveNetwork = graph.FindOrderedPath(new[]
            {
                Names("Offset Curve"), Names("Discontinuity"), Names("Shatter"), Names("Fit Curve Smooth"), Names("Join Curves")
            }, 12, GenericPlumbing);
            AddSinglePath(result, CurveNetworkPreparationRule, "curve-network offsetting, segmentation, and smoothing",
                "Connected curves are offset, segmented at discontinuities, smoothed, and rejoined.", curveNetwork);

            List<string> angleMeasurement = graph.FindOrderedPath(new[]
            {
                Names("Curve | Curve"), Names("Vector 2Pt"), Names("Angle")
            }, 8, GenericPlumbing);
            AddSinglePath(result, IntersectionAngleRule, "intersection-angle measurement",
                "Connected curve intersections are converted into vectors and measured as angles.", angleMeasurement);

            List<string> angleRemapping = graph.FindOrderedPath(new[]
            {
                Names("Curve | Curve"), Names("Vector 2Pt"), Names("Angle"), Names("Degrees"), Names("Remap Numbers")
            }, 12, GenericPlumbing);
            if (angleRemapping.Count > 0)
            {
                Add(result, AngleRemappingRule, "remapping measured angles into downstream control values",
                    "Connected intersection angles are converted to degrees and remapped into downstream control values.", angleRemapping);

                HashSet<string> filletScriptIds = new HashSet<string>(active.Where(node => node.Script != null)
                    .Where(node => (ScriptBehaviorAnalyzer.Analyze(node).PossibleRole ?? "").IndexOf("fillet", StringComparison.OrdinalIgnoreCase) >= 0)
                    .Select(node => node.InstanceId), StringComparer.OrdinalIgnoreCase);
                List<string> remapToFillet = graph.FindPathToNodeIds(new HashSet<string>(new[] { angleRemapping[angleRemapping.Count - 1] }, StringComparer.OrdinalIgnoreCase),
                    filletScriptIds, 5, FilletValueProcessing);
                if (remapToFillet.Count > 0)
                    Add(result, AngleDrivenFilletRule, "remapping measured angles into per-location fillet radii",
                        "Remapped intersection-angle values feed a script with recognized variable-fillet behavior.", angleRemapping, remapToFillet);
            }

            AddSinglePath(result, BlockPlacementRule, "block placement",
                "A connected block-definition or block-creation result feeds a placement or transformation operation.",
                graph.FindOrderedPath(new[] { BlockSources, BlockPlacementOperations }, 5, GenericPlumbing));

            AddModelBlockPlacement(result, graph, active);

            foreach (FunctionalEvidence evidence in result.Items)
                evidence.ReachesCapturedOutput = graph.CanReachCapturedOutput(evidence.ResultNodeIds);

            return result;
        }

        private static void AddDitheredPanelPartition(FunctionalEvidenceSet result, SemanticGraphIndex graph, List<ContextNode> active)
        {
            HashSet<string> panelIds = new HashSet<string>(ExactComponentIds(active, "Quad Panels", "LunchBox"), StringComparer.OrdinalIgnoreCase);
            HashSet<string> samplerIds = new HashSet<string>(ExactComponentIds(active, "Image Sampler", "Grasshopper"), StringComparer.OrdinalIgnoreCase);
            HashSet<string> siftIds = new HashSet<string>(ExactComponentIds(active, "Sift Pattern", "MathComponents"), StringComparer.OrdinalIgnoreCase);
            HashSet<string> scriptIds = new HashSet<string>(active.Where(node => node.Script != null)
                .Where(node => (ScriptBehaviorAnalyzer.Analyze(node).PossibleRole ?? "").IndexOf("dither", StringComparison.OrdinalIgnoreCase) >= 0)
                .Select(node => node.InstanceId), StringComparer.OrdinalIgnoreCase);
            if (panelIds.Count == 0 || samplerIds.Count == 0 || siftIds.Count == 0 || scriptIds.Count == 0) return;

            List<string> panelsToSift = graph.FindPathToNodeIds(panelIds, siftIds, 3, GenericPlumbing);
            List<string> imageToScript = graph.FindPathToNodeIds(samplerIds, scriptIds, 8, ImageValueProcessing);
            if (panelsToSift.Count == 0 || imageToScript.Count == 0
                || !graph.PathUsesEndpointPorts(panelsToSift, "Panels", "List")
                || !graph.PathUsesEndpointPorts(imageToScript, null, "B")) return;

            HashSet<string> matchedScript = new HashSet<string>(new[] { imageToScript[imageToScript.Count - 1] }, StringComparer.OrdinalIgnoreCase);
            HashSet<string> matchedSift = new HashSet<string>(new[] { panelsToSift[panelsToSift.Count - 1] }, StringComparer.OrdinalIgnoreCase);
            List<string> scriptToSift = graph.FindPathToNodeIds(matchedScript, matchedSift, 3, GenericPlumbing);
            if (scriptToSift.Count == 0 || !graph.PathUsesEndpointPorts(scriptToSift, "Idx", "Sift Pattern")) return;

            Add(result, DitheredPanelPartitionRule, "dithered image-driven partitioning of quadrilateral panels",
                "Connected image-derived values pass through recognized dithered quantization and partition quadrilateral panels into indexed streams.",
                panelsToSift, imageToScript, scriptToSift);
        }

        private static void AddModelBlockPlacement(FunctionalEvidenceSet result, SemanticGraphIndex graph, List<ContextNode> active)
        {
            HashSet<string> definitionIds = new HashSet<string>(ExactComponentIds(active, "Query Model Block Definitions", "IOComponents"), StringComparer.OrdinalIgnoreCase);
            HashSet<string> modelInstanceIds = new HashSet<string>(ExactComponentIds(active, "Model Block Instance", "IOComponents"), StringComparer.OrdinalIgnoreCase);
            HashSet<string> orientIds = new HashSet<string>(ExactComponentIds(active, "Orient", "XformComponents"), StringComparer.OrdinalIgnoreCase);
            HashSet<string> groupIds = new HashSet<string>(ExactComponentIds(active, "Group", "Grasshopper"), StringComparer.OrdinalIgnoreCase);
            if (definitionIds.Count == 0 || modelInstanceIds.Count == 0 || orientIds.Count == 0 || groupIds.Count == 0) return;

            List<string> definitionPath = graph.FindPathToNodeIds(definitionIds, modelInstanceIds, 6, GenericPlumbing);
            List<string> transformPath = graph.FindPathToNodeIds(orientIds, modelInstanceIds, 4, ModelBlockTransformProcessing);
            if (definitionPath.Count == 0 || transformPath.Count == 0
                || !String.Equals(definitionPath[definitionPath.Count - 1], transformPath[transformPath.Count - 1], StringComparison.OrdinalIgnoreCase)
                || !graph.PathUsesEndpointPorts(definitionPath, "Block Definitions", "Block Definition")
                || !graph.PathUsesEndpointPorts(transformPath, "Transform", "Transform")) return;

            HashSet<string> matchedInstance = new HashSet<string>(new[] { definitionPath[definitionPath.Count - 1] }, StringComparer.OrdinalIgnoreCase);
            List<string> resultPath = graph.FindPathToNodeIds(matchedInstance, groupIds, 4, ModelBlockResultProcessing);
            if (resultPath.Count == 0 || !graph.PathUsesEndpointPorts(resultPath, "Block Instance", "Geometry")) return;

            Add(result, ModelBlockPlacementRule, "model-block instance placement",
                "Queried model-block definitions and connected orientation transforms feed model-block instances whose results are grouped.",
                definitionPath, transformPath, resultPath);
        }

        private static void AddSurfacePointCurveNetwork(FunctionalEvidenceSet result, SemanticGraphIndex graph, List<ContextNode> active)
        {
            HashSet<string> gridIds = new HashSet<string>(ExactComponentIds(active, "Parameter Point Divide Surface", "Pufferfish"), StringComparer.OrdinalIgnoreCase);
            HashSet<string> graphMapperIds = new HashSet<string>(ExactComponentIds(active, "Graph Mapper", "Grasshopper"), StringComparer.OrdinalIgnoreCase);
            HashSet<string> joinIds = new HashSet<string>(ExactComponentIds(active, "Join Curves", "CurveComponents"), StringComparer.OrdinalIgnoreCase);
            if (gridIds.Count == 0 || graphMapperIds.Count == 0 || joinIds.Count == 0) return;

            foreach (string gridId in gridIds.OrderBy(id => id, StringComparer.OrdinalIgnoreCase))
            {
                HashSet<string> matchedGrid = new HashSet<string>(new[] { gridId }, StringComparer.OrdinalIgnoreCase);
                List<string> graphToGrid = graph.FindPathToNodeIds(graphMapperIds, matchedGrid, 2, GenericPlumbing);
                List<string> gridToJoin = graph.FindPathToNodeIds(matchedGrid, joinIds, 8, SurfaceGridNetworkProcessing);
                bool mappedParameters = graph.PathUsesEndpointPorts(graphToGrid, null, "Parameters U")
                    || graph.PathUsesEndpointPorts(graphToGrid, null, "Parameters V");
                if (!mappedParameters || gridToJoin.Count == 0 || !graph.PathUsesEndpointPorts(gridToJoin, "Points", "Curves")
                    || !graph.PathContainsAll(gridToJoin, "Sub List", "Line")) continue;

                Add(result, SurfacePointCurveNetworkRule, "graph-mapped surface point-grid curve-network construction",
                    "Graph-mapped surface parameters generate a point grid whose selected points are connected and joined into a curve network.",
                    graphToGrid, gridToJoin);
                return;
            }
        }

        private static void AddTangentCurveReconstruction(FunctionalEvidenceSet result, SemanticGraphIndex graph, List<ContextNode> active)
        {
            HashSet<string> joinIds = new HashSet<string>(ExactComponentIds(active, "Join Curves", "CurveComponents"), StringComparer.OrdinalIgnoreCase);
            HashSet<string> rebuildIds = new HashSet<string>(ExactComponentIds(active, "Rebuild Curve", "CurveComponents"), StringComparer.OrdinalIgnoreCase);
            HashSet<string> divideIds = new HashSet<string>(ExactComponentIds(active, "Divide Curve", "VectorComponents"), StringComparer.OrdinalIgnoreCase);
            HashSet<string> interpolateIds = new HashSet<string>(ExactComponentIds(active, "Interpolate (t)", "CurveComponents"), StringComparer.OrdinalIgnoreCase);
            HashSet<string> unitXIds = new HashSet<string>(ExactComponentIds(active, "Unit X", "VectorComponents"), StringComparer.OrdinalIgnoreCase);
            HashSet<string> replaceIds = new HashSet<string>(ExactComponentIds(active, "Replace Items", "MathComponents"), StringComparer.OrdinalIgnoreCase);
            HashSet<string> mergeIds = new HashSet<string>(ExactComponentIds(active, "Merge", "MathComponents"), StringComparer.OrdinalIgnoreCase);
            if (joinIds.Count == 0 || rebuildIds.Count == 0 || divideIds.Count == 0 || interpolateIds.Count == 0
                || unitXIds.Count == 0 || replaceIds.Count == 0 || mergeIds.Count == 0) return;

            foreach (string interpolateId in interpolateIds.OrderBy(id => id, StringComparer.OrdinalIgnoreCase))
            {
                HashSet<string> matchedInterpolate = new HashSet<string>(new[] { interpolateId }, StringComparer.OrdinalIgnoreCase);
                List<string> divideToInterpolate = graph.FindPathToNodeIds(divideIds, matchedInterpolate, 2, GenericPlumbing);
                List<string> tangentToInterpolate = graph.FindPathToNodeIds(unitXIds, matchedInterpolate, 2, GenericPlumbing);
                List<string> interpolateToReplace = graph.FindPathToNodeIds(matchedInterpolate, replaceIds, 2, GenericPlumbing);
                if (!graph.PathUsesEndpointPorts(divideToInterpolate, "Points", "Vertices")
                    || !graph.PathUsesEndpointPorts(tangentToInterpolate, "Unit vector", "Tangent Start")
                    || !graph.PathUsesEndpointPorts(interpolateToReplace, "Curve", "Item")) continue;

                string divideId = divideToInterpolate[0]; string replaceId = interpolateToReplace[interpolateToReplace.Count - 1];
                HashSet<string> matchedDivide = new HashSet<string>(new[] { divideId }, StringComparer.OrdinalIgnoreCase);
                HashSet<string> matchedReplace = new HashSet<string>(new[] { replaceId }, StringComparer.OrdinalIgnoreCase);
                List<string> rebuildToReplace = graph.FindPathToNodeIds(rebuildIds, matchedReplace, 2, GenericPlumbing);
                if (!graph.PathUsesEndpointPorts(rebuildToReplace, "Curve", "List")) continue;

                string rebuildId = rebuildToReplace[0];
                HashSet<string> matchedRebuild = new HashSet<string>(new[] { rebuildId }, StringComparer.OrdinalIgnoreCase);
                List<string> joinToRebuild = graph.FindPathToNodeIds(joinIds, matchedRebuild, 2, GenericPlumbing);
                List<string> rebuildToDivide = graph.FindPathToNodeIds(matchedRebuild, matchedDivide, 6, CurveSelectionProcessing);
                List<string> replaceToMerge = graph.FindPathToNodeIds(matchedReplace, mergeIds, 5, CurveSelectionProcessing);
                if (!graph.PathUsesEndpointPorts(joinToRebuild, "Curves", "Curve")
                    || !graph.PathUsesEndpointPorts(rebuildToDivide, "Curve", "Curve")
                    || !graph.PathUsesEndpointPorts(replaceToMerge, "List", null)) continue;

                Add(result, TangentCurveReconstructionRule, "selective curve reconstruction with start-tangent-constrained interpolation",
                    "Joined curves are rebuilt, selected and divided, reconstructed with an explicit start-tangent input, and replaced in the curve set.",
                    joinToRebuild, rebuildToDivide, divideToInterpolate, tangentToInterpolate, interpolateToReplace, rebuildToReplace, replaceToMerge);
                return;
            }
        }

        private static void AddSurfacePipeMorph(FunctionalEvidenceSet result, SemanticGraphIndex graph, List<ContextNode> active)
        {
            HashSet<string> splitIds = new HashSet<string>(ExactComponentIds(active, "Surface Split", "SurfaceComponents"), StringComparer.OrdinalIgnoreCase);
            HashSet<string> pipeIds = new HashSet<string>(ExactComponentIds(active, "MultiPipe", "Kangaroo2Component"), StringComparer.OrdinalIgnoreCase);
            HashSet<string> morphIds = new HashSet<string>(ExactComponentIds(active, "Surface Morph", "XformComponents"), StringComparer.OrdinalIgnoreCase);
            if (splitIds.Count == 0 || pipeIds.Count == 0 || morphIds.Count == 0) return;

            List<string> splitToPipe = graph.FindPathToNodeIds(splitIds, pipeIds, 12, SurfacePipeProcessing);
            if (!graph.PathUsesEndpointPorts(splitToPipe, "Fragments", "Curves")
                || !graph.PathContainsAll(splitToPipe, "Offset on Srf", "Flip Matrix")) return;
            HashSet<string> matchedPipe = new HashSet<string>(new[] { splitToPipe[splitToPipe.Count - 1] }, StringComparer.OrdinalIgnoreCase);
            List<string> pipeToMorph = graph.FindPathToNodeIds(matchedPipe, morphIds, 5, PipeMorphProcessing);
            if (!graph.PathUsesEndpointPorts(pipeToMorph, "Pipe", "Geometry") || !graph.PathContainsAll(pipeToMorph, "Brep | Brep")) return;

            Add(result, SurfacePipeMorphRule, "surface splitting and branching-pipe construction followed by Brep intersection and surface morphing",
                "Split-surface geometry is converted into an offset curve network for branching-pipe construction; the pipe is intersected with Brep geometry and the resulting curves are surface-morphed.",
                splitToPipe, pipeToMorph);
        }

        private static List<string> ExactComponentIds(IEnumerable<ContextNode> nodes, string componentName, string assemblyName)
        {
            return (nodes ?? new List<ContextNode>()).Where(node => node != null
                && String.Equals(node.Name, componentName, StringComparison.OrdinalIgnoreCase)
                && String.Equals(node.AssemblyName, assemblyName, StringComparison.OrdinalIgnoreCase))
                .Select(node => node.InstanceId).OrderBy(id => id, StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static void AddDirect(FunctionalEvidenceSet result, string ruleId, string stage, string explanation, List<string> nodeIds)
        {
            if (nodeIds == null || nodeIds.Count == 0) return;
            result.Items.Add(new FunctionalEvidence
            {
                RuleId = ruleId,
                Stage = stage,
                Explanation = explanation,
                Provenance = FunctionalEvidenceProvenance.Inference,
                Strength = FunctionalEvidenceStrength.Supported,
                MatchedNodeIds = nodeIds.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(id => id, StringComparer.OrdinalIgnoreCase).ToList(),
                MatchedEdgeKeys = new List<string>(),
                ResultNodeIds = nodeIds.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(id => id, StringComparer.OrdinalIgnoreCase).ToList()
            });
        }

        private static void AddSinglePath(FunctionalEvidenceSet result, string ruleId, string stage, string explanation, List<string> path)
        {
            if (path.Count > 0) Add(result, ruleId, stage, explanation, path);
        }

        private static void Add(FunctionalEvidenceSet result, string ruleId, string stage, string explanation, params List<string>[] paths)
        {
            FunctionalEvidence evidence = new FunctionalEvidence
            {
                RuleId = ruleId,
                Stage = stage,
                Explanation = explanation,
                Provenance = FunctionalEvidenceProvenance.Inference,
                Strength = FunctionalEvidenceStrength.Supported,
                MatchedNodeIds = paths.SelectMany(path => path).Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(id => id, StringComparer.OrdinalIgnoreCase).ToList(),
                MatchedEdgeKeys = paths.SelectMany(EdgeKeys).Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(key => key, StringComparer.OrdinalIgnoreCase).ToList(),
                ResultNodeIds = paths.Where(path => path != null && path.Count > 0).Select(path => path[path.Count - 1])
                    .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(id => id, StringComparer.OrdinalIgnoreCase).ToList()
            };
            result.Items.Add(evidence);
        }

        private static IEnumerable<string> EdgeKeys(IList<string> path)
        {
            for (int i = 1; i < path.Count; i++) yield return path[i - 1] + "|" + path[i];
        }

        private static HashSet<string> Names(params string[] values)
        {
            return new HashSet<string>(values, StringComparer.OrdinalIgnoreCase);
        }

        private static HashSet<string> Union(HashSet<string> first, HashSet<string> second)
        {
            HashSet<string> result = new HashSet<string>(first, StringComparer.OrdinalIgnoreCase); result.UnionWith(second); return result;
        }
    }

    internal sealed class SemanticGraphIndex
    {
        private sealed class OrderedPathState
        {
            public List<string> Path { get; set; }
            public int NextStageIndex { get; set; }
        }

        private readonly Dictionary<string, ContextNode> nodes;
        private readonly Dictionary<string, List<string>> next;
        private readonly HashSet<string> activeNodeIds;
        private readonly List<ContextEdge> internalEdges;
        private readonly HashSet<string> outputReachableNodeIds;
        private readonly bool outputFilteringEnabled;

        public SemanticGraphIndex(ContextDocument document, IEnumerable<ContextNode> activeNodes)
        {
            activeNodeIds = new HashSet<string>((activeNodes ?? new List<ContextNode>()).Where(node => node != null).Select(node => node.InstanceId), StringComparer.OrdinalIgnoreCase);
            nodes = (document == null ? new List<ContextNode>() : document.Nodes ?? new List<ContextNode>()).Where(node => node != null && !String.IsNullOrWhiteSpace(node.InstanceId))
                .GroupBy(node => node.InstanceId, StringComparer.OrdinalIgnoreCase).ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
            next = nodes.Keys.ToDictionary(id => id, id => new List<string>(), StringComparer.OrdinalIgnoreCase);
            internalEdges = (document == null ? new List<ContextEdge>() : document.Edges ?? new List<ContextEdge>())
                .Where(edge => edge != null && nodes.ContainsKey(edge.SourceNodeId) && nodes.ContainsKey(edge.TargetNodeId)
                    && (String.IsNullOrWhiteSpace(edge.BoundaryStatus) || String.Equals(edge.BoundaryStatus, "internal", StringComparison.OrdinalIgnoreCase)))
                .OrderBy(ExportRootScopeResolver.EdgeKey, StringComparer.OrdinalIgnoreCase).ToList();
            foreach (ContextEdge edge in internalEdges)
            {
                if (!next[edge.SourceNodeId].Contains(edge.TargetNodeId, StringComparer.OrdinalIgnoreCase)) next[edge.SourceNodeId].Add(edge.TargetNodeId);
            }
            foreach (List<string> targets in next.Values) targets.Sort(StringComparer.OrdinalIgnoreCase);

            HashSet<string> outputIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (document != null && document.Scope != null)
                outputIds.UnionWith((document.Scope.RootSourceObjectIds ?? new List<string>()).Where(nodes.ContainsKey));
            if (document != null)
                outputIds.UnionWith((document.BoundaryOutputs ?? new List<ContextBoundaryPort>()).Where(port => port != null)
                    .Select(port => port.InternalNodeId).Where(nodes.ContainsKey));
            outputFilteringEnabled = outputIds.Count > 0;
            outputReachableNodeIds = OutputReachableNodes(outputIds);
        }

        public bool CanReachCapturedOutput(IEnumerable<string> nodeIds)
        {
            if (!outputFilteringEnabled) return true;
            return (nodeIds ?? new List<string>()).Any(outputReachableNodeIds.Contains);
        }

        public bool PathUsesEndpointPorts(IList<string> path, string sourceParameterName, string targetParameterName)
        {
            if (path == null || path.Count < 2) return false;
            if (path.Count == 2)
                return EdgeMatches(path[0], path[1], sourceParameterName, targetParameterName);
            return EdgeMatches(path[0], path[1], sourceParameterName, null)
                && EdgeMatches(path[path.Count - 2], path[path.Count - 1], null, targetParameterName);
        }

        public bool PathContainsAll(IList<string> path, params string[] requiredNames)
        {
            if (path == null || requiredNames == null) return false;
            HashSet<string> namesInPath = new HashSet<string>(path.Where(nodes.ContainsKey).Select(id => nodes[id].Name), StringComparer.OrdinalIgnoreCase);
            return requiredNames.All(namesInPath.Contains);
        }

        public List<string> FindPath(HashSet<string> sourceNames, HashSet<string> targetNames, int maximumEdges, HashSet<string> allowedIntermediates)
        {
            if (maximumEdges <= 0) return new List<string>();
            foreach (ContextNode source in nodes.Values.Where(node => activeNodeIds.Contains(node.InstanceId) && sourceNames.Contains(node.Name))
                .OrderBy(node => node.InstanceId, StringComparer.OrdinalIgnoreCase))
            {
                Queue<List<string>> pending = new Queue<List<string>>();
                HashSet<string> visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase); visited.Add(source.InstanceId);
                pending.Enqueue(new List<string> { source.InstanceId });
                while (pending.Count > 0)
                {
                    List<string> path = pending.Dequeue(); string current = path[path.Count - 1]; int depth = path.Count - 1;
                    if (depth >= maximumEdges) continue;
                    foreach (string targetId in next[current])
                    {
                        ContextNode target = nodes[targetId];
                        List<string> candidate = new List<string>(path); candidate.Add(targetId);
                        if (activeNodeIds.Contains(targetId) && targetNames.Contains(target.Name)) return candidate;
                        if (!allowedIntermediates.Contains(target.Name) || !visited.Add(targetId)) continue;
                        pending.Enqueue(candidate);
                    }
                }
            }
            return new List<string>();
        }

        public List<string> FindOrderedPath(IList<HashSet<string>> stages, int maximumEdges, HashSet<string> allowedIntermediates)
        {
            if (stages == null || stages.Count < 2 || maximumEdges <= 0) return new List<string>();
            foreach (ContextNode source in nodes.Values.Where(node => activeNodeIds.Contains(node.InstanceId) && stages[0].Contains(node.Name))
                .OrderBy(node => node.InstanceId, StringComparer.OrdinalIgnoreCase))
            {
                Queue<OrderedPathState> pending = new Queue<OrderedPathState>();
                HashSet<string> visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                pending.Enqueue(new OrderedPathState { Path = new List<string> { source.InstanceId }, NextStageIndex = 1 });
                visited.Add(source.InstanceId + "|1");
                while (pending.Count > 0)
                {
                    OrderedPathState state = pending.Dequeue(); string current = state.Path[state.Path.Count - 1];
                    if (state.Path.Count - 1 >= maximumEdges) continue;
                    foreach (string targetId in next[current])
                    {
                        ContextNode target = nodes[targetId]; int nextStageIndex = state.NextStageIndex;
                        if (stages[nextStageIndex].Contains(target.Name)) nextStageIndex++;
                        else if (!allowedIntermediates.Contains(target.Name)) continue;
                        List<string> candidate = new List<string>(state.Path); candidate.Add(targetId);
                        if (nextStageIndex >= stages.Count) return candidate;
                        string visitKey = targetId + "|" + nextStageIndex;
                        if (!visited.Add(visitKey)) continue;
                        pending.Enqueue(new OrderedPathState { Path = candidate, NextStageIndex = nextStageIndex });
                    }
                }
            }
            return new List<string>();
        }

        public List<string> FindPathToNodeIds(HashSet<string> sourceNodeIds, HashSet<string> targetNodeIds, int maximumEdges, HashSet<string> allowedIntermediates)
        {
            if (sourceNodeIds == null || targetNodeIds == null || sourceNodeIds.Count == 0 || targetNodeIds.Count == 0 || maximumEdges <= 0)
                return new List<string>();
            foreach (string sourceId in sourceNodeIds.Where(id => activeNodeIds.Contains(id) && nodes.ContainsKey(id)).OrderBy(id => id, StringComparer.OrdinalIgnoreCase))
            {
                Queue<List<string>> pending = new Queue<List<string>>(); HashSet<string> visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                pending.Enqueue(new List<string> { sourceId }); visited.Add(sourceId);
                while (pending.Count > 0)
                {
                    List<string> path = pending.Dequeue(); string current = path[path.Count - 1];
                    if (path.Count - 1 >= maximumEdges) continue;
                    foreach (string targetId in next[current])
                    {
                        List<string> candidate = new List<string>(path); candidate.Add(targetId);
                        if (activeNodeIds.Contains(targetId) && targetNodeIds.Contains(targetId)) return candidate;
                        if (!allowedIntermediates.Contains(nodes[targetId].Name) || !visited.Add(targetId)) continue;
                        pending.Enqueue(candidate);
                    }
                }
            }
            return new List<string>();
        }

        private bool EdgeMatches(string sourceId, string targetId, string sourceParameterName, string targetParameterName)
        {
            return internalEdges.Any(edge => String.Equals(edge.SourceNodeId, sourceId, StringComparison.OrdinalIgnoreCase)
                && String.Equals(edge.TargetNodeId, targetId, StringComparison.OrdinalIgnoreCase)
                && (String.IsNullOrWhiteSpace(sourceParameterName) || String.Equals(edge.SourceParameterName, sourceParameterName, StringComparison.OrdinalIgnoreCase))
                && (String.IsNullOrWhiteSpace(targetParameterName) || String.Equals(edge.TargetParameterName, targetParameterName, StringComparison.OrdinalIgnoreCase)));
        }

        private HashSet<string> OutputReachableNodes(HashSet<string> outputIds)
        {
            HashSet<string> reachable = new HashSet<string>(outputIds ?? new HashSet<string>(), StringComparer.OrdinalIgnoreCase);
            if (reachable.Count == 0) return reachable;
            Dictionary<string, List<string>> previous = nodes.Keys.ToDictionary(id => id, id => new List<string>(), StringComparer.OrdinalIgnoreCase);
            foreach (ContextEdge edge in internalEdges)
                if (!previous[edge.TargetNodeId].Contains(edge.SourceNodeId, StringComparer.OrdinalIgnoreCase)) previous[edge.TargetNodeId].Add(edge.SourceNodeId);
            foreach (List<string> sources in previous.Values) sources.Sort(StringComparer.OrdinalIgnoreCase);
            Queue<string> pending = new Queue<string>(reachable.OrderBy(id => id, StringComparer.OrdinalIgnoreCase));
            while (pending.Count > 0)
            {
                string current = pending.Dequeue();
                foreach (string sourceId in previous[current]) if (reachable.Add(sourceId)) pending.Enqueue(sourceId);
            }
            return reachable;
        }
    }
}
