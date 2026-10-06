using System;
using System.Collections.Generic;
using System.Linq;

namespace Lichen.Core
{
    public enum AssemblyOrigin { Unavailable, Native, Lichen, ThirdParty }

    /// <summary>Assembly identity policy only; no host, package discovery, or source inspection.</summary>
    public static class DependencyClassification
    {
        // Preserve the export policy exactly, including its Grasshopper prefix and lack of trimming.
        public static bool IsNativeAssembly(string name)
        {
            string[] native = {
                "Grasshopper", "CurveComponents", "FieldComponents", "IOComponents", "MathComponents", "MeshComponents",
                "SurfaceComponents", "TriangulationComponents", "VectorComponents", "XformComponents", "TransformComponents",
                "IntersectComponents", "GalapagosComponents", "RhinoCodePluginGH", "ScriptComponents", "GhPython",
                "Kangaroo2Component", "KangarooSolver"
            };
            return name != null && (native.Contains(name, StringComparer.OrdinalIgnoreCase)
                || name.StartsWith("Grasshopper", StringComparison.OrdinalIgnoreCase));
        }

        public static AssemblyOrigin Classify(string assemblyName)
        {
            if (String.IsNullOrWhiteSpace(assemblyName)) return AssemblyOrigin.Unavailable;
            if (String.Equals(assemblyName, "Lichen", StringComparison.OrdinalIgnoreCase)
                || String.Equals(assemblyName, "Lichen.Core", StringComparison.OrdinalIgnoreCase)
                || String.Equals(assemblyName, "Lichen.Adapters", StringComparison.OrdinalIgnoreCase)) return AssemblyOrigin.Lichen;
            return IsNativeAssembly(assemblyName) ? AssemblyOrigin.Native : AssemblyOrigin.ThirdParty;
        }

        public static bool IsLichenComponent(Guid id)
        {
            return id == LichenComponentIds.ExportRoot || id == LichenComponentIds.Thallus
                || id == LichenComponentIds.ThallusEndpoint;
        }

        // Exact runtime type + owning assembly pairs verified against the installed public SDK.
        // Names, nicknames, categories, icons, source text, and arbitrary "script" substrings are not evidence.
        public static string ScriptLanguage(string assemblyName, string runtimeTypeName)
        {
            if (String.Equals(assemblyName, "RhinoCodePluginGH", StringComparison.OrdinalIgnoreCase))
            {
                switch (runtimeTypeName)
                {
                    case "RhinoCodePluginGH.Components.Python3Component": return "Python 3";
                    case "RhinoCodePluginGH.Components.IronPython2Component": return "Python 2";
                    case "RhinoCodePluginGH.Components.CSharpComponent": return "C#";
                    case "RhinoCodePluginGH.Components.ScriptComponent": return "Script (language unavailable)";
                }
            }
            if (String.Equals(assemblyName, "ScriptComponents", StringComparison.OrdinalIgnoreCase))
            {
                switch (runtimeTypeName)
                {
                    case "ScriptComponents.Component_CSNET_Script":
                    case "ScriptComponents.Component_CSNET_Script_OBSOLETE":
                    case "ScriptComponents.Legacy.ComponentLegacyCsScript": return "C#";
                    case "ScriptComponents.Component_VBNET_Script":
                    case "ScriptComponents.Component_VBNET_Script_OBSOLETE":
                    case "ScriptComponents.Legacy.ComponentLegacyVbScript": return "VB.NET";
                    case "ScriptComponents.Component_GenericScript_OBSOLETE": return "Script (language unavailable)";
                }
            }
            if (String.Equals(assemblyName, "GhPython", StringComparison.OrdinalIgnoreCase)
                && (runtimeTypeName == "GhPython.Component.ZuiPythonComponent"
                    || runtimeTypeName == "GhPython.Component.PythonComponent_OBSOLETE")) return "Python 2";
            return "";
        }
    }

    public sealed class SpotlightCandidate
    {
        public Guid InstanceId { get; set; }
        public Guid ComponentId { get; set; }
        public string AssemblyName { get; set; }
        public string ScriptLanguage { get; set; }
        public bool IsTopLevel { get; set; }
        public bool IsAnnotation { get; set; }
        public bool HasVisibleBounds { get; set; }
    }

    public sealed class SpotlightTarget
    {
        public string InstanceId { get; internal set; }
        public string AssemblyName { get; internal set; }
        public string ScriptLanguage { get; internal set; }
        public bool ThirdParty { get; internal set; }
        public bool Script { get; internal set; }
    }

    public sealed class SpotlightIdSelection
    {
        public SpotlightIdSelection() { Ids = new List<string>(); }
        public List<string> Ids { get; private set; }
        public bool LimitReached { get; internal set; }
    }

    public sealed class SpotlightSelection
    {
        public SpotlightSelection() { Targets = new List<SpotlightTarget>(); AvailableTargets = new List<SpotlightTarget>(); }
        public List<SpotlightTarget> Targets { get; private set; }
        public List<SpotlightTarget> AvailableTargets { get; private set; }
        public int InspectedCount { get; internal set; }
        public int UnavailableCount { get; set; }
        public bool LimitReached { get; set; }
    }

    public static class DependencySpotlightSelection
    {
        public const int MaximumObjects = 500;
        public static string NormalizeId(Guid id) { return id.ToString("D").ToLowerInvariant(); }

        // Enumerate outer identities, but retain at most 500; panning and enumeration order cannot change the sample.
        public static SpotlightIdSelection BoundIds(IEnumerable<Guid> ids, int maximum)
        {
            int limit = maximum <= 0 ? MaximumObjects : Math.Min(maximum, MaximumObjects);
            SortedSet<string> selected = new SortedSet<string>(StringComparer.Ordinal);
            SpotlightIdSelection result = new SpotlightIdSelection();
            foreach (Guid id in ids ?? Enumerable.Empty<Guid>())
            {
                if (id == Guid.Empty) continue;
                selected.Add(NormalizeId(id));
                if (selected.Count > limit) { selected.Remove(selected.Max); result.LimitReached = true; }
            }
            result.Ids.AddRange(selected);
            return result;
        }

        public static SpotlightSelection Resolve(IEnumerable<SpotlightCandidate> candidates, bool enabled,
            bool showThirdParty, bool showScripts, string assemblyFocus, string scriptFocus, int maximum)
        {
            SpotlightSelection result = new SpotlightSelection();
            if (!enabled) return result;
            List<SpotlightCandidate> eligible = (candidates ?? Enumerable.Empty<SpotlightCandidate>())
                .Where(c => c != null && c.InstanceId != Guid.Empty && c.IsTopLevel && !c.IsAnnotation
                    && c.HasVisibleBounds && !DependencyClassification.IsLichenComponent(c.ComponentId)).ToList();
            SpotlightIdSelection bound = BoundIds(eligible.Select(c => c.InstanceId), maximum);
            result.LimitReached = bound.LimitReached;
            // Conflicting duplicate identity records fail closed, independent of enumeration order.
            Dictionary<string, List<SpotlightCandidate>> byId = eligible.GroupBy(c => NormalizeId(c.InstanceId))
                .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);
            foreach (string id in bound.Ids)
            {
                result.InspectedCount++;
                List<SpotlightCandidate> same = byId[id];
                SpotlightCandidate candidate = same[0];
                if (same.Any(c => c.ComponentId != candidate.ComponentId
                    || !String.Equals(c.AssemblyName, candidate.AssemblyName, StringComparison.Ordinal)
                    || !String.Equals(c.ScriptLanguage, candidate.ScriptLanguage, StringComparison.Ordinal)))
                { result.UnavailableCount++; continue; }
                AssemblyOrigin origin = DependencyClassification.Classify(candidate.AssemblyName);
                if (origin == AssemblyOrigin.Unavailable) { result.UnavailableCount++; continue; }
                if (origin == AssemblyOrigin.Lichen) continue;
                bool thirdParty = origin == AssemblyOrigin.ThirdParty;
                bool script = !String.IsNullOrEmpty(candidate.ScriptLanguage);
                if (!thirdParty && !script) continue;
                SpotlightTarget target = new SpotlightTarget {
                    InstanceId = id, AssemblyName = candidate.AssemblyName, ScriptLanguage = candidate.ScriptLanguage ?? "",
                    ThirdParty = thirdParty, Script = script
                };
                result.AvailableTargets.Add(target);
                bool assemblyMatch = String.IsNullOrEmpty(assemblyFocus)
                    || String.Equals(assemblyFocus, candidate.AssemblyName, StringComparison.OrdinalIgnoreCase);
                bool languageMatch = String.IsNullOrEmpty(scriptFocus)
                    || String.Equals(scriptFocus, candidate.ScriptLanguage, StringComparison.Ordinal);
                if ((showThirdParty && thirdParty && assemblyMatch) || (showScripts && script && languageMatch)) result.Targets.Add(target);
            }
            return result;
        }
    }
}
