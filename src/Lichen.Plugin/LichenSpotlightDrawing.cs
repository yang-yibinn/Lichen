using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Linq;
using Grasshopper.GUI;
using Grasshopper.GUI.Canvas;
using Grasshopper.Kernel;

using Lichen.Core;

namespace Lichen.Plugin
{
    internal static class LichenSpotlightDrawing
    {
        private static readonly Color AssemblyColor = Color.FromArgb(118, 84, 196);
        private static readonly Color ScriptColor = Color.FromArgb(31, 111, 180);

        private static bool IsCandidate(IGH_DocumentObject obj)
        {
            // Passive groups, Scribbles, markup, and other annotations are not executable canvas objects.
            if (!(obj is IGH_Component) && !(obj is IGH_Param)) return false;
            if (DependencyClassification.IsLichenComponent(obj.ComponentGuid)) return false;
            IGH_Attributes attributes = obj.Attributes;
            return attributes != null && attributes.IsTopLevel && ValidBounds(attributes.Bounds);
        }

        private static bool ValidBounds(RectangleF bounds)
        {
            return bounds.Width > 0F && bounds.Height > 0F && Finite(bounds.X) && Finite(bounds.Y)
                && Finite(bounds.Right) && Finite(bounds.Bottom);
        }
        private static bool Finite(float value) { return !Single.IsNaN(value) && !Single.IsInfinity(value); }

        internal static SpotlightSelection Inspect(GH_Document document, SpotlightViewState state)
        {
            if (document == null || document.Nested || !state.Enabled) return new SpotlightSelection();
            List<Guid> identities = new List<Guid>();
            int unreadable = 0;
            foreach (IGH_DocumentObject obj in document.Objects)
            {
                try { if (IsCandidate(obj)) identities.Add(obj.InstanceGuid); }
                catch { unreadable++; }
            }
            SpotlightIdSelection bound = DependencySpotlightSelection.BoundIds(identities, DependencySpotlightSelection.MaximumObjects);
            List<SpotlightCandidate> candidates = new List<SpotlightCandidate>();
            foreach (string id in bound.Ids)
            {
                SpotlightCandidate candidate = new SpotlightCandidate { InstanceId = new Guid(id), IsTopLevel = true, HasVisibleBounds = true };
                candidates.Add(candidate);
                try
                {
                    IGH_DocumentObject obj = document.FindObject(candidate.InstanceId, false);
                    if (obj == null || !IsCandidate(obj)) continue;
                    candidate.ComponentId = obj.ComponentGuid;
                    Type type = obj.GetType();
                    candidate.AssemblyName = type.Assembly.GetName().Name;
                    // Walk only type metadata, never script properties or source. Known base types cover derived containers.
                    for (int depth = 0; type != null && depth < 16; depth++, type = type.BaseType)
                    {
                        string language = DependencyClassification.ScriptLanguage(type.Assembly.GetName().Name, type.FullName);
                        if (language.Length != 0) { candidate.ScriptLanguage = language; break; }
                    }
                }
                catch { candidate.AssemblyName = null; candidate.ScriptLanguage = null; }
            }
            SpotlightSelection result = DependencySpotlightSelection.Resolve(candidates, true, state.ShowThirdParty, state.ShowScripts,
                state.AssemblyFocus, state.ScriptFocus, DependencySpotlightSelection.MaximumObjects);
            result.LimitReached |= bound.LimitReached;
            // Failed eligibility reads are disclosed separately; never silently claim complete inspection.
            result.UnavailableCount += unreadable;
            return result;
        }

        internal static void DrawOverlay(GH_Canvas canvas, SpotlightViewState state, SpotlightSelection selection)
        {
            Graphics graphics = canvas.Graphics;
            if (graphics == null) return;
            GraphicsState saved = graphics.Save();
            try
            {
                graphics.ResetTransform();
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                float scale = Math.Max(1F, GH_GraphicsUtil.UiScale);
                List<RectangleF> occupied = new List<RectangleF>();
                foreach (IGH_DocumentObject obj in canvas.Document.Objects)
                {
                    try
                    {
                        if (!(obj is IGH_Component) && !(obj is IGH_Param)) continue;
                        if (obj.Attributes == null || !obj.Attributes.IsTopLevel || !ValidBounds(obj.Attributes.Bounds)) continue;
                        RectangleF face = canvas.Viewport.ProjectRectangle(obj.Attributes.Bounds);
                        if (face.IntersectsWith(canvas.ClientRectangle)) occupied.Add(face);
                    }
                    catch { }
                }
                // Even tightly packed neighbors retain unobscured faces, ports, and native warning content.
                GraphicsState targetState = graphics.Save();
                try
                {
                    foreach (RectangleF face in occupied) graphics.SetClip(face, CombineMode.Exclude);
                    foreach (SpotlightTarget target in selection.Targets)
                    {
                        try
                        {
                            IGH_DocumentObject obj = canvas.Document.FindObject(new Guid(target.InstanceId), false);
                            if (obj == null || !IsCandidate(obj)) continue;
                            RectangleF bounds = canvas.Viewport.ProjectRectangle(obj.Attributes.Bounds);
                            bounds.Inflate(5F * scale, 5F * scale);
                            if (!bounds.IntersectsWith(canvas.ClientRectangle)) continue;
                            bool thirdParty = state.ShowThirdParty && target.ThirdParty
                                && (state.AssemblyFocus.Length == 0 || String.Equals(state.AssemblyFocus, target.AssemblyName, StringComparison.OrdinalIgnoreCase));
                            // One outline per target. A third-party script uses brackets; its script identity remains in the legend.
                            DrawOutline(graphics, bounds, thirdParty, scale);
                        }
                        catch { }
                    }
                }
                finally { graphics.Restore(targetState); }

            }
            finally { graphics.Restore(saved); }
        }

        private static void DrawOutline(Graphics graphics, RectangleF bounds, bool thirdParty, float scale)
        {
            Color color = thirdParty ? AssemblyColor : ScriptColor;

            using (Pen pen = new Pen(color, 3F * scale))
            using (GraphicsPath path = new GraphicsPath())
            {
                if (thirdParty)
                {
                    float length = Math.Min(16F * scale, Math.Min(bounds.Width, bounds.Height) * 0.3F);
                    AddCorner(path, bounds.Left, bounds.Top, length, length);
                    AddCorner(path, bounds.Right, bounds.Top, -length, length);
                    AddCorner(path, bounds.Left, bounds.Bottom, length, -length);
                    AddCorner(path, bounds.Right, bounds.Bottom, -length, -length);
                }
                else { path.AddRectangle(bounds); pen.DashStyle = DashStyle.Dash; }
                graphics.DrawPath(pen, path);
            }
        }
        private static void AddCorner(GraphicsPath path, float x, float y, float dx, float dy)
        {
            path.StartFigure(); path.AddLines(new[] { new PointF(x + dx, y), new PointF(x, y), new PointF(x, y + dy) });
        }

    }
}
