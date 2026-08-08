using System;
using System.Collections.Generic;
using System.Linq;
using Grasshopper;
using Grasshopper.GUI.Canvas;
using Grasshopper.Kernel;
using Lichen.Adapters;
using Lichen.Core;
using Rhino;

namespace Lichen.Plugin
{
    internal sealed class LichenChainSelectionResult
    {
        public bool Success { get; set; }
        public int SelectedCount { get; set; }
        public bool NodeLimitReached { get; set; }
    }

    internal static class LichenChainSelection
    {
        internal static List<string> SelectedRootIds(GH_Document document)
        {
            if (document == null) return new List<string>();
            try
            {
                return document.Objects.Where(GrasshopperExportRootAdapter.IsExportRoot)
                    .Where(o => o.Attributes != null && o.Attributes.Selected)
                    .Select(o => Id(o.InstanceGuid)).OrderBy(id => id, StringComparer.OrdinalIgnoreCase).ToList();
            }
            catch { return new List<string>(); }
        }

        internal static List<string> RootIdsForContext(GH_Document document, string preferredRootId)
        {
            List<string> selected = SelectedRootIds(document);
            if (String.IsNullOrWhiteSpace(preferredRootId) || selected.Contains(preferredRootId, StringComparer.OrdinalIgnoreCase)) return selected;
            IGH_DocumentObject preferred = document == null ? null : document.Objects.FirstOrDefault(o => String.Equals(Id(o.InstanceGuid), preferredRootId, StringComparison.OrdinalIgnoreCase));
            return GrasshopperExportRootAdapter.IsExportRoot(preferred) ? new List<string> { preferredRootId } : new List<string>();
        }

        internal static LichenChainSelectionResult Select(GH_Canvas canvas, IEnumerable<string> rootObjectIds)
        {
            LichenChainSelectionResult result = new LichenChainSelectionResult();
            if (canvas == null || canvas.Document == null) return result;
            List<string> rootIds = (rootObjectIds ?? Enumerable.Empty<string>()).Where(id => !String.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(id => id, StringComparer.OrdinalIgnoreCase).ToList();
            if (rootIds.Count == 0) return result;

            GH_Document document = canvas.Document;
            List<string> previousSelection;
            try { previousSelection = document.SelectedObjects().Select(o => Id(o.InstanceGuid)).ToList(); }
            catch { return result; }
            try
            {
                GrasshopperExportRootScope scope = new GrasshopperExportRootAdapter().Resolve(document, rootIds, LichenExportRootComponent.MaximumNodes);
                List<string> targetIds = new ExportRootSelectionResolver().Resolve(scope.Closure);
                List<IGH_DocumentObject> targets = new List<IGH_DocumentObject>();
                foreach (string id in targetIds)
                {
                    IGH_DocumentObject target;
                    if (!scope.Objects.TryGetValue(id, out target) || target == null || target.Attributes == null) return result;
                    targets.Add(target);
                }

                document.DeselectAll();
                foreach (IGH_DocumentObject target in targets) target.Attributes.Selected = true;
                result.Success = true;
                result.SelectedCount = targets.Count;
                result.NodeLimitReached = scope.Closure.NodeLimitReached;
            }
            catch
            {
                RestoreSelection(document, previousSelection);
            }
            finally
            {
                RedrawSelection(canvas);
            }
            return result;
        }

        private static void RestoreSelection(GH_Document document, IEnumerable<string> objectIds)
        {
            try
            {
                HashSet<string> selected = new HashSet<string>(objectIds ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);
                document.DeselectAll();
                foreach (IGH_DocumentObject obj in document.Objects)
                    if (obj.Attributes != null && selected.Contains(Id(obj.InstanceGuid))) obj.Attributes.Selected = true;
            }
            catch
            {
                // Selection restoration must not propagate into Grasshopper's UI event loop.
            }
        }

        private static void RedrawSelection(GH_Canvas canvas)
        {
            try { canvas.Invalidate(); }
            catch { }
            try
            {
                RhinoDoc document = Instances.ActiveRhinoDoc;
                if (document != null) document.Views.Redraw();
            }
            catch { }
        }

        private static string Id(Guid value) { return value.ToString("D").ToLowerInvariant(); }
    }
}
