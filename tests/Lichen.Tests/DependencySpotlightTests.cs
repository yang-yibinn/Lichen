using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using Lichen.Core;

namespace Lichen.Tests
{
    internal static partial class Program
    {
        private static void RunDependencySpotlightTests()
        {
            Run("Spotlight excludes native Grasshopper assemblies", SpotlightNative);
            Run("Spotlight excludes bundled scripting and Kangaroo from plugin highlights", SpotlightBundled);
            Run("Spotlight identifies third-party assemblies", SpotlightThirdParty);
            Run("Spotlight excludes Lichen assemblies and every component identity", SpotlightLichen);
            Run("Spotlight disabled state never enumerates candidates", SpotlightDisabled);
            Run("Spotlight order and duplicate identities are deterministic", SpotlightOrdering);
            Run("Spotlight bounds inspected objects before category filtering", SpotlightLimit);
            Run("Spotlight view state is shared per document and cleared on close", SpotlightSessions);
            Run("Spotlight panel stays anchored to the viewport upper right", SpotlightPanelPosition);
            Run("Spotlight scripts require recognized runtime identities", SpotlightScripts);
            Run("Spotlight layer and focus controls change only target presentation", SpotlightFocus);
            Run("Spotlight excludes annotations internals and invisible bounds", SpotlightEligibility);
            Run("native outer clusters are not spotlighted for internal plugin dependencies", SpotlightClusterBoundary);
            Run("Spotlight unavailable and conflicting identities fail closed", SpotlightUnavailable);
            Run("Spotlight classifier and selection are culture invariant", SpotlightCulture);
            Run("Spotlight temporary view state does not affect exports", SpotlightExportExclusion);
            Run("shared classifier preserves frozen pre-refactor export bytes and fingerprints", SpotlightExportCompatibility);
        }

        private static SpotlightCandidate SpotlightObject(int index, string assembly)
        {
            return new SpotlightCandidate { InstanceId = new Guid("00000000-0000-0000-0000-" + index.ToString("D12", CultureInfo.InvariantCulture)),
                AssemblyName = assembly, IsTopLevel = true, HasVisibleBounds = true };
        }
        private static SpotlightSelection SpotlightResolve(IEnumerable<SpotlightCandidate> candidates)
        {
            return DependencySpotlightSelection.Resolve(candidates, true, true, false, "", "", 500);
        }
        private static void SpotlightNative()
        {
            string[] names = { "Grasshopper", "CurveComponents", "FieldComponents", "IOComponents", "MathComponents", "MeshComponents",
                "SurfaceComponents", "TriangulationComponents", "VectorComponents", "XformComponents", "TransformComponents", "IntersectComponents", "GalapagosComponents", "GrasshopperExtra" };
            Equal(0, SpotlightResolve(names.Select((name, i) => SpotlightObject(i + 1, name))).Targets.Count);
        }
        private static void SpotlightBundled()
        {
            string[] names = { "RhinoCodePluginGH", "ScriptComponents", "GhPython", "Kangaroo2Component", "KangarooSolver" };
            Equal(0, SpotlightResolve(names.Select((name, i) => SpotlightObject(i + 1, name))).Targets.Count);
        }
        private static void SpotlightThirdParty()
        {
            string[] names = { "LunchBox", "Pufferfish", "Treesloth", "LichenLookalike" };
            SpotlightSelection result = SpotlightResolve(names.Select((name, i) => SpotlightObject(i + 1, name)));
            Sequence(names, result.Targets.Select(t => t.AssemblyName));
        }
        private static void SpotlightLichen()
        {
            List<SpotlightCandidate> objects = new List<SpotlightCandidate>();
            foreach (string name in new[] { "Lichen", "Lichen.Core", "Lichen.Adapters", "LICHEN" }) objects.Add(SpotlightObject(objects.Count + 1, name));
            foreach (Guid id in new[] { LichenComponentIds.ExportRoot, LichenComponentIds.Thallus, LichenComponentIds.ThallusEndpoint })
            {
                SpotlightCandidate obj = SpotlightObject(objects.Count + 1, "CustomAssembly"); obj.ComponentId = id; obj.ScriptLanguage = "Python 3"; objects.Add(obj);
            }
            Equal(0, DependencySpotlightSelection.Resolve(objects, true, true, true, "", "", 500).Targets.Count);
        }
        private static IEnumerable<SpotlightCandidate> UnreadableSpotlightObjects()
        {
            yield return SpotlightObject(1, "Pufferfish");
            throw new Exception("Disabled inspection enumerated its source");
        }
        private static void SpotlightDisabled()
        {
            SpotlightSelection result = DependencySpotlightSelection.Resolve(UnreadableSpotlightObjects(), false, true, true, "", "", 500);
            Equal(0, result.Targets.Count); Equal(0, result.InspectedCount); Equal(false, result.LimitReached);
        }
        private static void SpotlightOrdering()
        {
            SpotlightCandidate first = SpotlightObject(1, "Pufferfish"), second = SpotlightObject(2, "Pufferfish");
            SpotlightCandidate[] objects = { second, first, first, second };
            SpotlightSelection result = SpotlightResolve(objects);
            Equal(2, result.Targets.Count);
            Sequence(result.Targets.Select(t => t.InstanceId), SpotlightResolve(objects.Reverse()).Targets.Select(t => t.InstanceId));
            Equal(2, result.Targets.Select(t => t.InstanceId).Distinct().Count());
        }
        private static void SpotlightLimit()
        {
            List<SpotlightCandidate> objects = Enumerable.Range(1, 501).Select(i => SpotlightObject(i, i <= 500 ? "Grasshopper" : "Pufferfish")).ToList();
            SpotlightSelection first = SpotlightResolve(objects), reversed = SpotlightResolve(objects.AsEnumerable().Reverse());
            Equal(500, first.InspectedCount); Equal(true, first.LimitReached); Equal(0, first.Targets.Count);
            Equal(first.InspectedCount, reversed.InspectedCount); Equal(first.LimitReached, reversed.LimitReached);
            Equal(false, SpotlightResolve(objects.Take(500)).LimitReached);
            SpotlightIdSelection ids = DependencySpotlightSelection.BoundIds(objects.Select(c => c.InstanceId), 9000);
            Equal(500, ids.Ids.Count); Equal(true, ids.LimitReached);
            Sequence(ids.Ids, DependencySpotlightSelection.BoundIds(objects.Select(c => c.InstanceId).Reverse(), 500).Ids);
        }
        private static void SpotlightSessions()
        {
            SpotlightSessions<object> sessions = new SpotlightSessions<object>();
            object a = new object(), b = new object();
            SpotlightViewState first = sessions.For(a);
            Equal(false, first.Enabled); first.Toggle(); first.ShowScripts = true; first.AssemblyFocus = "Pufferfish";
            True(Object.ReferenceEquals(first, sessions.For(a)), "same document acquired competing state");
            Equal(true, sessions.For(a).Enabled); Equal(false, sessions.For(b).Enabled);
            first.Toggle(); Equal(false, first.Enabled); Equal(false, first.ShowScripts); Equal("", first.AssemblyFocus);
            first.Toggle(); first.Collapsed = true; sessions.Forget(a);
            True(!Object.ReferenceEquals(first, sessions.For(a)), "closed document retained old view");
            Equal(false, sessions.For(a).Enabled); Equal(false, sessions.For(a).Collapsed);
        }
        private static void SpotlightPanelPosition()
        {
            SpotlightPanelBounds a = SpotlightPanelLayout.Place(1000, 700, 1, 220);
            Equal(628, a.Left); Equal(12, a.Top); Equal(360, a.Width); Equal(220, a.Height);
            SpotlightPanelBounds collapsed = SpotlightPanelLayout.Place(1000, 700, 1, 60);
            Equal(a.Left, collapsed.Left); Equal(a.Top, collapsed.Top);
            SpotlightPanelBounds resized = SpotlightPanelLayout.Place(1200, 800, 1, 220);
            Equal(a.Left + 200, resized.Left); Equal(a.Top, resized.Top);
            SpotlightPanelBounds scaled = SpotlightPanelLayout.Place(2000, 1400, 2, 440);
            Equal(a.Left * 2, scaled.Left); Equal(a.Top * 2, scaled.Top);
            SpotlightPanelBounds small = SpotlightPanelLayout.Place(100, 70, 1, 220);
            True(small.Left >= 0 && small.Top >= 0 && small.Left + small.Width <= 100 && small.Top + small.Height <= 70, "small canvas overflow");
        }
        private static void SpotlightScripts()
        {
            Equal("Python 3", Lichen.Core.DependencyClassification.ScriptLanguage("RhinoCodePluginGH", "RhinoCodePluginGH.Components.Python3Component"));
            Equal("C#", Lichen.Core.DependencyClassification.ScriptLanguage("RhinoCodePluginGH", "RhinoCodePluginGH.Components.CSharpComponent"));
            Equal("Python 2", Lichen.Core.DependencyClassification.ScriptLanguage("GhPython", "GhPython.Component.ZuiPythonComponent"));
            Equal("C#", Lichen.Core.DependencyClassification.ScriptLanguage("ScriptComponents", "ScriptComponents.Component_CSNET_Script_OBSOLETE"));
            Equal("VB.NET", Lichen.Core.DependencyClassification.ScriptLanguage("ScriptComponents", "ScriptComponents.Component_VBNET_Script"));
            Equal("", Lichen.Core.DependencyClassification.ScriptLanguage("UnknownPlugin", "RhinoCodePluginGH.Components.Python3Component"));
            Equal("", Lichen.Core.DependencyClassification.ScriptLanguage("RhinoCodePluginGH", "Fake.Python3Component"));
            Equal("", Lichen.Core.DependencyClassification.ScriptLanguage("Grasshopper", "Grasshopper.Kernel.Special.GH_Cluster"));
            SpotlightCandidate script = SpotlightObject(1, "RhinoCodePluginGH"); script.ScriptLanguage = "Python 3";
            Equal(0, SpotlightResolve(new[] { script }).Targets.Count);
            Equal(1, DependencySpotlightSelection.Resolve(new[] { script }, true, false, true, "", "", 500).Targets.Count);
        }
        private static void SpotlightFocus()
        {
            SpotlightCandidate a = SpotlightObject(1, "LunchBox"), b = SpotlightObject(2, "Pufferfish"), c = SpotlightObject(3, "RhinoCodePluginGH");
            c.ScriptLanguage = "Python 3";
            SpotlightCandidate[] objects = { a, b, c };
            SpotlightSelection focus = DependencySpotlightSelection.Resolve(objects, true, true, false, "pufferfish", "", 500);
            Equal(1, focus.Targets.Count); Equal("Pufferfish", focus.Targets[0].AssemblyName); Equal(3, focus.AvailableTargets.Count);
            Equal(2, DependencySpotlightSelection.Resolve(objects, true, true, true, "LunchBox", "Python 3", 500).Targets.Count);
            Equal(0, DependencySpotlightSelection.Resolve(objects, true, false, true, "", "C#", 500).Targets.Count);
            Equal(0, DependencySpotlightSelection.Resolve(objects, true, false, false, "", "", 500).Targets.Count);
            b.ScriptLanguage = "Python 3";
            Equal(3, DependencySpotlightSelection.Resolve(objects, true, true, true, "", "", 500).Targets.Count);
            Equal("LunchBox", a.AssemblyName); Equal("Python 3", c.ScriptLanguage);
        }
        private static void SpotlightEligibility()
        {
            SpotlightCandidate annotation = SpotlightObject(1, "Plugin"), internalObject = SpotlightObject(2, "Plugin"), hidden = SpotlightObject(3, "Plugin");
            annotation.IsAnnotation = true; internalObject.IsTopLevel = false; hidden.HasVisibleBounds = false;
            Equal(0, SpotlightResolve(new[] { annotation, internalObject, hidden, SpotlightObject(4, "Grasshopper") }).Targets.Count);
        }
        private static void SpotlightUnavailable()
        {
            SpotlightCandidate first = SpotlightObject(1, "Pufferfish"), conflict = SpotlightObject(1, "LunchBox"), unknown = SpotlightObject(2, null);
            SpotlightCandidate[] objects = { first, conflict, unknown };
            SpotlightSelection selection = SpotlightResolve(objects);
            Equal(0, selection.Targets.Count); Equal(2, selection.UnavailableCount);
            Equal(selection.UnavailableCount, SpotlightResolve(objects.Reverse()).UnavailableCount);
        }
        private static void SpotlightClusterBoundary()
        {
            ContextSnapshot snapshot = new ContextSnapshot();
            ContextNode cluster = Node("cluster"); cluster.AssemblyName = "Grasshopper";
            cluster.ClusterGraph = new ContextClusterGraph { InspectionStatus = "inspected" };
            ContextNode internalNode = Node("internal"); internalNode.AssemblyName = "Pufferfish";
            cluster.ClusterGraph.Nodes.Add(internalNode); snapshot.Nodes.Add(cluster);
            ContextDocument export = new ContextGraphService().BuildDocument(snapshot, Options(ScopeMode.EntireDocument));
            True(export.Dependencies.Any(d => d.Name == "Pufferfish" && d.Kind == "third_party"), "export lost internal dependencies");
            Equal(0, SpotlightResolve(new[] { SpotlightObject(1, cluster.AssemblyName) }).Targets.Count);
        }
        private static void SpotlightCulture()
        {
            CultureInfo previous = Thread.CurrentThread.CurrentCulture, previousUi = Thread.CurrentThread.CurrentUICulture;
            try
            {
                string expected = null;
                foreach (string culture in new[] { "en-US", "tr-TR", "ar-SA", "fr-FR" })
                {
                    Thread.CurrentThread.CurrentCulture = new CultureInfo(culture); Thread.CurrentThread.CurrentUICulture = new CultureInfo(culture);
                    SpotlightNative(); SpotlightBundled(); SpotlightSessions(); SpotlightPanelPosition();
                    string ids = String.Join("|", SpotlightResolve(new[] { SpotlightObject(10, "Pufferfish"), SpotlightObject(2, "LunchBox") }).Targets.Select(t => t.InstanceId));
                    if (expected == null) expected = ids; else Equal(expected, ids);
                }
            }
            finally { Thread.CurrentThread.CurrentCulture = previous; Thread.CurrentThread.CurrentUICulture = previousUi; }
        }
        private static void SpotlightExportExclusion()
        {
            ContextSnapshot snapshot = Fixture();
            ContextExportOptions options = Options(ScopeMode.EntireDocument);
            ContextExportPackage before = new ContextExporter().Export(snapshot, options);
            SpotlightSessions<ContextSnapshot> sessions = new SpotlightSessions<ContextSnapshot>();
            SpotlightViewState view = sessions.For(snapshot);
            view.Toggle(); view.ShowScripts = true; view.AssemblyFocus = "Pufferfish"; view.Collapsed = true;
            ContextExportPackage after = new ContextExporter().Export(snapshot, options);
            Equal(before.Json, after.Json); Equal(before.Markdown, after.Markdown);
            sessions.Forget(snapshot);
            Equal(before.Json, new ContextExporter().Export(snapshot, options).Json);
        }

        private static ContextSnapshot SpotlightMixedDependencyFixture()
        {
            ContextSnapshot snapshot = Fixture(); snapshot.Nodes.Clear(); snapshot.Edges.Clear();
            string[] names = { "Grasshopper", "GrasshopperExtra", "CurveComponents", "FieldComponents", "IOComponents", "MathComponents", "MeshComponents",
                "SurfaceComponents", "TriangulationComponents", "VectorComponents", "XformComponents", "TransformComponents", "IntersectComponents", "GalapagosComponents",
                "RhinoCodePluginGH", "ScriptComponents", "GhPython", "Kangaroo2Component", "KangarooSolver", "LunchBox", "Pufferfish", "Treesloth",
                "Lichen", "Lichen.Core", "Lichen.Adapters", "", "unknown", " Grasshopper", "lunchbox" };
            for (int i = 0; i < names.Length; i++) snapshot.Nodes.Add(new ContextNode { InstanceId = "node-" + i.ToString("D2", CultureInfo.InvariantCulture),
                Name = "Object", Nickname = "Object", AssemblyName = names[i], AssemblyVersion = "1.0" });
            return snapshot;
        }
        private static void SpotlightExportCompatibility()
        {
            Dictionary<string, Func<ContextSnapshot>> fixtures = new Dictionary<string, Func<ContextSnapshot>> {
                { "Fixture", () => Fixture() }, { "FixtureWithRuntime", FixtureWithRuntime }, { "ThallusFixture", ThallusFixture },
                { "RoutedThallusFixture", RoutedThallusFixture }, { "LargeFixture", () => LargeFixture(500) }, { "MixedDependencies", SpotlightMixedDependencyFixture }
            };
            CultureInfo previous = Thread.CurrentThread.CurrentCulture;
            try
            {
                foreach (string frozen in SpotlightFrozenExports)
                {
                        string[] parts = frozen.Split('|');
                        Thread.CurrentThread.CurrentCulture = new CultureInfo(parts[0]);
                        // The frozen baseline includes its original exporter version in JSON/provenance.
                        ContextExportOptions options = new ContextExportOptions { ExporterVersion = "0.8.1", ScopeMode = ScopeMode.EntireDocument, DetailLevel = (DetailLevel)Enum.Parse(typeof(DetailLevel), parts[2]) };
                        ContextSnapshot snapshot = fixtures[parts[1]]();
                        ContextExportPackage package = new ContextExporter().Export(snapshot, options);
                        Equal(parts[3], Sha256(package.Json)); Equal(parts[4], Sha256(package.Markdown)); Equal(parts[5], package.Document.ExportSignature.ContextFingerprint);
                        snapshot.Nodes.Reverse(); snapshot.Edges.Reverse();
                        ContextExportPackage reversed = new ContextExporter().Export(snapshot, options);
                        Equal(package.Json, reversed.Json); Equal(package.Markdown, reversed.Markdown);
                }
            }
            finally { Thread.CurrentThread.CurrentCulture = previous; }
        }
        // SHA-256 of UTF-8 bytes from the isolated, unchanged cbb2d59 dirty-worktree baseline, before refactoring.
        private static readonly string[] SpotlightFrozenExports = {
            "en-US|Fixture|Brief|e10e4c4a7ecdc1bb440725718b81f6a76e2dcb41f98d65b11b97a8641aed41aa|932fd05b3ba525f7a31176542c8686bebc4595f4d63d1718544a0963c7dfc2ef|f5653146a079f8bba5f5673907242c8702c53bfdd0ca370bf7dd090fe0f57a60",
            "en-US|Fixture|Technical|e10e4c4a7ecdc1bb440725718b81f6a76e2dcb41f98d65b11b97a8641aed41aa|29c06aaf7e1c09de6bad66cd55bbbea9e72b5f093bb240fc611a1a8671e49f82|f5653146a079f8bba5f5673907242c8702c53bfdd0ca370bf7dd090fe0f57a60",
            "en-US|Fixture|Exact|e10e4c4a7ecdc1bb440725718b81f6a76e2dcb41f98d65b11b97a8641aed41aa|c9850153b8309b4a9d6911f46111ab448f2f8f0935df1d501cc4220e25bbb196|f5653146a079f8bba5f5673907242c8702c53bfdd0ca370bf7dd090fe0f57a60",
            "en-US|FixtureWithRuntime|Brief|86e03730ab7d4fdc0f531320cab31c22d31a2e15b85102e1aa2964b2c36baec0|ae2ec50a34ed481956806ce0034dbb4a72659c706f9d2f06ac59eee0555c48aa|b819c2e00de515d0a104d5b4a1e7faba5d0e76008c6653fdeb433687ba382c86",
            "en-US|FixtureWithRuntime|Technical|86e03730ab7d4fdc0f531320cab31c22d31a2e15b85102e1aa2964b2c36baec0|0731e3eb876e2a1e1081649c8824238c8dcc918129f6d0928687268959ae6037|b819c2e00de515d0a104d5b4a1e7faba5d0e76008c6653fdeb433687ba382c86",
            "en-US|FixtureWithRuntime|Exact|86e03730ab7d4fdc0f531320cab31c22d31a2e15b85102e1aa2964b2c36baec0|15768411f5bffae990131b95a3ab0affc208279413b670ed692b5413e6c10033|b819c2e00de515d0a104d5b4a1e7faba5d0e76008c6653fdeb433687ba382c86",
            "en-US|ThallusFixture|Brief|565f563a136abef53532f1d8e4a781bc01a514f5031fd6341d58fff41622e0a5|d46d7b1b7cc01e6ae94387da6ba68a9fb0bded6c731ad8cbe8bb45fd5a12b029|98ef1bea2324600ed215d7ca779814ba1b7f8834dccd059af0765d58c85bb08e",
            "en-US|ThallusFixture|Technical|565f563a136abef53532f1d8e4a781bc01a514f5031fd6341d58fff41622e0a5|785c1fbc87a437f58a9bbb99d7b9487e5e83f9fb7743743e3d455b45aa84feb1|98ef1bea2324600ed215d7ca779814ba1b7f8834dccd059af0765d58c85bb08e",
            "en-US|ThallusFixture|Exact|565f563a136abef53532f1d8e4a781bc01a514f5031fd6341d58fff41622e0a5|1c5c468fa03db8a3f086638edbb9804415fbc8906a01054e1165a36b566c4c50|98ef1bea2324600ed215d7ca779814ba1b7f8834dccd059af0765d58c85bb08e",
            "en-US|RoutedThallusFixture|Brief|3fcb12e3829576a4a68dd3af6f2771e3e745db8cf8deb462dd8063d73ef5af02|0039e03c7d3b96e2e8fd85372c5583f3a6d28fdd6455465e88e80a5c0bdb4d55|5549342664461a0c009e71288da00ec0909a2d9f52d5ad196150f633c78be06f",
            "en-US|RoutedThallusFixture|Technical|3fcb12e3829576a4a68dd3af6f2771e3e745db8cf8deb462dd8063d73ef5af02|548c6fbd13af49c73bbff923d32ed8c58e2fb3ce2bc0f9ef1d1765e2113c4ab2|5549342664461a0c009e71288da00ec0909a2d9f52d5ad196150f633c78be06f",
            "en-US|RoutedThallusFixture|Exact|3fcb12e3829576a4a68dd3af6f2771e3e745db8cf8deb462dd8063d73ef5af02|718e8452d245bb77e49ecb904bf1e0c52b97c310d6d33c567c929f7727936570|5549342664461a0c009e71288da00ec0909a2d9f52d5ad196150f633c78be06f",
            "en-US|LargeFixture|Brief|87d936512e508cca913729fcdb261bf36d779be9bb40d52e5a1863eb784f4bcd|c9fd4ffae982f85655524632dad9ded0efd063d10085ffb64e023dc2218692f5|a32e7cadd68b2e5c0748ee1977bc815dd9d3b1391491af6a5ef3febbb5db2372",
            "en-US|LargeFixture|Technical|87d936512e508cca913729fcdb261bf36d779be9bb40d52e5a1863eb784f4bcd|506fbc6c65e6e5adb031d7c9652fb6f0d4a8dadf582ae7f91a5ae36a16c9762b|a32e7cadd68b2e5c0748ee1977bc815dd9d3b1391491af6a5ef3febbb5db2372",
            "en-US|LargeFixture|Exact|87d936512e508cca913729fcdb261bf36d779be9bb40d52e5a1863eb784f4bcd|16c863119e019f6d4db3c0305863ee37d0e8a553a8c11860733d1f71e5cfdab9|a32e7cadd68b2e5c0748ee1977bc815dd9d3b1391491af6a5ef3febbb5db2372",
            "en-US|MixedDependencies|Brief|5dd81e32008be6f09d0feec992230cd1bcfc65fcf345f6138983c2e4c491b53a|d356f94efa2434db644e93a14c42032a382b80dbc2286425c89825f41a76107c|a512bab8cde7a2ebf691a470db27d9d1e3164f287ccb79859418d33341f57192",
            "en-US|MixedDependencies|Technical|5dd81e32008be6f09d0feec992230cd1bcfc65fcf345f6138983c2e4c491b53a|ec701be74be5fef68b3736c35f315b985cd58f508a50bb3506c33d1bb7530f48|a512bab8cde7a2ebf691a470db27d9d1e3164f287ccb79859418d33341f57192",
            "en-US|MixedDependencies|Exact|5dd81e32008be6f09d0feec992230cd1bcfc65fcf345f6138983c2e4c491b53a|39dc1e890282ead9b139fc45be47079562055ef1a9dac5c17af750c28f6d559d|a512bab8cde7a2ebf691a470db27d9d1e3164f287ccb79859418d33341f57192",
            "tr-TR|Fixture|Brief|e10e4c4a7ecdc1bb440725718b81f6a76e2dcb41f98d65b11b97a8641aed41aa|932fd05b3ba525f7a31176542c8686bebc4595f4d63d1718544a0963c7dfc2ef|f5653146a079f8bba5f5673907242c8702c53bfdd0ca370bf7dd090fe0f57a60",
            "tr-TR|Fixture|Technical|e10e4c4a7ecdc1bb440725718b81f6a76e2dcb41f98d65b11b97a8641aed41aa|29c06aaf7e1c09de6bad66cd55bbbea9e72b5f093bb240fc611a1a8671e49f82|f5653146a079f8bba5f5673907242c8702c53bfdd0ca370bf7dd090fe0f57a60",
            "tr-TR|Fixture|Exact|e10e4c4a7ecdc1bb440725718b81f6a76e2dcb41f98d65b11b97a8641aed41aa|c9850153b8309b4a9d6911f46111ab448f2f8f0935df1d501cc4220e25bbb196|f5653146a079f8bba5f5673907242c8702c53bfdd0ca370bf7dd090fe0f57a60",
            "tr-TR|FixtureWithRuntime|Brief|86e03730ab7d4fdc0f531320cab31c22d31a2e15b85102e1aa2964b2c36baec0|ae2ec50a34ed481956806ce0034dbb4a72659c706f9d2f06ac59eee0555c48aa|b819c2e00de515d0a104d5b4a1e7faba5d0e76008c6653fdeb433687ba382c86",
            "tr-TR|FixtureWithRuntime|Technical|86e03730ab7d4fdc0f531320cab31c22d31a2e15b85102e1aa2964b2c36baec0|0731e3eb876e2a1e1081649c8824238c8dcc918129f6d0928687268959ae6037|b819c2e00de515d0a104d5b4a1e7faba5d0e76008c6653fdeb433687ba382c86",
            "tr-TR|FixtureWithRuntime|Exact|86e03730ab7d4fdc0f531320cab31c22d31a2e15b85102e1aa2964b2c36baec0|a2b6665b3f18ecfd5baeabfba3d40aa6e244749ee18a2d953f92ba1c4032abf8|b819c2e00de515d0a104d5b4a1e7faba5d0e76008c6653fdeb433687ba382c86",
            "tr-TR|ThallusFixture|Brief|565f563a136abef53532f1d8e4a781bc01a514f5031fd6341d58fff41622e0a5|d46d7b1b7cc01e6ae94387da6ba68a9fb0bded6c731ad8cbe8bb45fd5a12b029|98ef1bea2324600ed215d7ca779814ba1b7f8834dccd059af0765d58c85bb08e",
            "tr-TR|ThallusFixture|Technical|565f563a136abef53532f1d8e4a781bc01a514f5031fd6341d58fff41622e0a5|785c1fbc87a437f58a9bbb99d7b9487e5e83f9fb7743743e3d455b45aa84feb1|98ef1bea2324600ed215d7ca779814ba1b7f8834dccd059af0765d58c85bb08e",
            "tr-TR|ThallusFixture|Exact|565f563a136abef53532f1d8e4a781bc01a514f5031fd6341d58fff41622e0a5|1c5c468fa03db8a3f086638edbb9804415fbc8906a01054e1165a36b566c4c50|98ef1bea2324600ed215d7ca779814ba1b7f8834dccd059af0765d58c85bb08e",
            "tr-TR|RoutedThallusFixture|Brief|3fcb12e3829576a4a68dd3af6f2771e3e745db8cf8deb462dd8063d73ef5af02|0039e03c7d3b96e2e8fd85372c5583f3a6d28fdd6455465e88e80a5c0bdb4d55|5549342664461a0c009e71288da00ec0909a2d9f52d5ad196150f633c78be06f",
            "tr-TR|RoutedThallusFixture|Technical|3fcb12e3829576a4a68dd3af6f2771e3e745db8cf8deb462dd8063d73ef5af02|548c6fbd13af49c73bbff923d32ed8c58e2fb3ce2bc0f9ef1d1765e2113c4ab2|5549342664461a0c009e71288da00ec0909a2d9f52d5ad196150f633c78be06f",
            "tr-TR|RoutedThallusFixture|Exact|3fcb12e3829576a4a68dd3af6f2771e3e745db8cf8deb462dd8063d73ef5af02|718e8452d245bb77e49ecb904bf1e0c52b97c310d6d33c567c929f7727936570|5549342664461a0c009e71288da00ec0909a2d9f52d5ad196150f633c78be06f",
            "tr-TR|LargeFixture|Brief|87d936512e508cca913729fcdb261bf36d779be9bb40d52e5a1863eb784f4bcd|c9fd4ffae982f85655524632dad9ded0efd063d10085ffb64e023dc2218692f5|a32e7cadd68b2e5c0748ee1977bc815dd9d3b1391491af6a5ef3febbb5db2372",
            "tr-TR|LargeFixture|Technical|87d936512e508cca913729fcdb261bf36d779be9bb40d52e5a1863eb784f4bcd|506fbc6c65e6e5adb031d7c9652fb6f0d4a8dadf582ae7f91a5ae36a16c9762b|a32e7cadd68b2e5c0748ee1977bc815dd9d3b1391491af6a5ef3febbb5db2372",
            "tr-TR|LargeFixture|Exact|87d936512e508cca913729fcdb261bf36d779be9bb40d52e5a1863eb784f4bcd|16c863119e019f6d4db3c0305863ee37d0e8a553a8c11860733d1f71e5cfdab9|a32e7cadd68b2e5c0748ee1977bc815dd9d3b1391491af6a5ef3febbb5db2372",
            "tr-TR|MixedDependencies|Brief|5dd81e32008be6f09d0feec992230cd1bcfc65fcf345f6138983c2e4c491b53a|d356f94efa2434db644e93a14c42032a382b80dbc2286425c89825f41a76107c|a512bab8cde7a2ebf691a470db27d9d1e3164f287ccb79859418d33341f57192",
            "tr-TR|MixedDependencies|Technical|5dd81e32008be6f09d0feec992230cd1bcfc65fcf345f6138983c2e4c491b53a|ec701be74be5fef68b3736c35f315b985cd58f508a50bb3506c33d1bb7530f48|a512bab8cde7a2ebf691a470db27d9d1e3164f287ccb79859418d33341f57192",
            "tr-TR|MixedDependencies|Exact|5dd81e32008be6f09d0feec992230cd1bcfc65fcf345f6138983c2e4c491b53a|39dc1e890282ead9b139fc45be47079562055ef1a9dac5c17af750c28f6d559d|a512bab8cde7a2ebf691a470db27d9d1e3164f287ccb79859418d33341f57192",
        };
    }
}
