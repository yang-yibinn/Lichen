using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using Grasshopper.GUI;
using Grasshopper.GUI.Canvas;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Attributes;
using Lichen.Adapters;

namespace Lichen.Plugin
{
    internal sealed class LichenExportRootAttributes : GH_ComponentAttributes
    {
        internal const int ComponentBandOpacity = 112;
        internal const int OutlineOpacity = 255;
        internal const int WireOpacity = 255;
        internal const float ComponentBandSize = 10F;
        private const int MaximumNodes = 500;
        private static readonly Color LichenGreen = Color.FromArgb(64, 181, 50);
        private static readonly Color LichenEdge = Color.FromArgb(35, 126, 31);
        private static readonly Color LichenBody = Color.FromArgb(48, 108, 42);
        private static readonly Color LichenSelectedBody = Color.FromArgb(104, 214, 83);
        private static readonly Color LichenBodyEdge = Color.FromArgb(28, 78, 25);
        private static readonly Bitmap ComponentIcon = CreateWhiteIcon(LichenInfo.CreateIconCopy());
        private GrasshopperExportRootScope currentPaintScope;
        private GH_Document currentPaintDocument;

        public LichenExportRootAttributes(LichenExportRootComponent owner) : base(owner) { }

        public override GH_ObjectResponse RespondToMouseDoubleClick(GH_Canvas canvas, GH_CanvasMouseEvent eventArgs)
        {
            ((LichenExportRootComponent)Owner).ShowExportDialog();
            return GH_ObjectResponse.Handled;
        }

        protected override void Layout()
        {
            base.Layout();
            m_innerBounds = new RectangleF(m_innerBounds.Left, m_innerBounds.Top, 100F, 40F);
            LayoutInputParams(Owner, m_innerBounds);
            if (Owner.Params.Input.Count > 0 && Owner.Params.Input[0].Attributes != null)
            {
                Owner.Params.Input[0].Attributes.Pivot = new PointF(m_innerBounds.Left, m_innerBounds.Top + 20F);
                Owner.Params.Input[0].Attributes.PerformLayout();
            }
            RectangleF bounds = m_innerBounds; bounds.Inflate(6F, 2F); Bounds = bounds;
        }

        protected override void Render(GH_Canvas canvas, Graphics graphics, GH_CanvasChannel channel)
        {
            if (channel == GH_CanvasChannel.Wires)
            {
                base.Render(canvas, graphics, channel);
                currentPaintScope = ResolveSelectedScope(canvas);
                currentPaintDocument = canvas == null ? null : canvas.Document;
                if (currentPaintScope != null) DrawHighlightedWires(graphics, currentPaintScope);
                return;
            }
            if (channel == GH_CanvasChannel.Objects)
            {
                RenderLichenBody(graphics);
                return;
            }
            base.Render(canvas, graphics, channel);
            if (channel != GH_CanvasChannel.Overlay || !Selected || canvas == null || canvas.Document == null) return;
            GrasshopperExportRootScope scope = Object.ReferenceEquals(currentPaintDocument, canvas.Document) ? currentPaintScope : ResolveSelectedScope(canvas);
            if (scope != null) DrawComponentBands(graphics, scope);
        }

        private GrasshopperExportRootScope ResolveSelectedScope(GH_Canvas canvas)
        {
            if (!Selected || canvas == null || canvas.Document == null) return null;
            try
            {
                List<string> selectedRoots = canvas.Document.Objects.Where(GrasshopperExportRootAdapter.IsExportRoot)
                    .Where(o => o.Attributes != null && o.Attributes.Selected).Select(o => o.InstanceGuid.ToString("D"))
                    .OrderBy(id => id, StringComparer.OrdinalIgnoreCase).ToList();
                if (selectedRoots.Count == 0 || !String.Equals(selectedRoots[0], Owner.InstanceGuid.ToString("D"), StringComparison.OrdinalIgnoreCase)) return null;
                return new GrasshopperExportRootAdapter().Resolve(canvas.Document, selectedRoots, MaximumNodes);
            }
            catch { return null; }
        }

        private static void DrawHighlightedWires(Graphics graphics, GrasshopperExportRootScope scope)
        {
            foreach (GrasshopperExportRootEdge edge in scope.Edges)
            {
                if (edge.Source == null || edge.Target == null || edge.Source.Attributes == null || edge.Target.Attributes == null) continue;
                if (edge.Target.WireDisplay == GH_ParamWireDisplay.hidden) continue;
                GH_WireType type = SafeWireType(edge.Source);
                using (Pen wire = HighlightWirePen(type, edge.Target.WireDisplay))
                using (GraphicsPath path = GH_Painter.ConnectionPath(edge.Source.Attributes.OutputGrip, edge.Target.Attributes.InputGrip, GH_WireDirection.right, GH_WireDirection.left))
                    graphics.DrawPath(wire, path);
            }
        }

        private static GH_WireType SafeWireType(IGH_Param source)
        {
            try { return GH_Painter.DetermineWireType(source.VolatileData); }
            catch { return GH_WireType.generic; }
        }

        private static Pen HighlightWirePen(GH_WireType type, GH_ParamWireDisplay display)
        {
            int alpha = display == GH_ParamWireDisplay.faint ? 92 : WireOpacity;
            bool multiple = type == GH_WireType.list || type == GH_WireType.tree;
            float width = display == GH_ParamWireDisplay.faint ? 1.5F : (multiple ? 4F : 2F);
            Pen pen = new Pen(Color.FromArgb(alpha, LichenGreen), width); pen.StartCap = LineCap.Round; pen.EndCap = LineCap.Round;
            if (multiple && display != GH_ParamWireDisplay.faint) pen.CompoundArray = new[] { 0F, 0.24F, 0.76F, 1F };
            if (type == GH_WireType.tree && display != GH_ParamWireDisplay.faint) pen.DashStyle = DashStyle.Dash;
            return pen;
        }

        private static void DrawComponentBands(Graphics graphics, GrasshopperExportRootScope scope)
        {
            using (Brush band = new SolidBrush(Color.FromArgb(ComponentBandOpacity, LichenGreen)))
            using (Pen outline = new Pen(Color.FromArgb(OutlineOpacity, LichenEdge), 1.5F))
            {
                foreach (string id in scope.Closure.IncludedObjectIds)
                {
                    IGH_DocumentObject obj;
                    if (!scope.Objects.TryGetValue(id, out obj) || obj.Attributes == null) continue;
                    DrawComponentBand(graphics, obj.Attributes.Bounds, band, outline);
                }
            }

            if (scope.Closure.NodeLimitReached) foreach (IGH_DocumentObject root in scope.Roots) DrawTruncationWarning(graphics, root);
        }

        private void RenderLichenBody(Graphics graphics)
        {
            RectangleF body = m_innerBounds;
            PointF grip = new PointF(body.Left, body.Top + 20F);
            GH_PaletteStyle style = new GH_PaletteStyle(Selected ? LichenSelectedBody : LichenBody, LichenBodyEdge, Color.White);
            using (GH_Capsule capsule = GH_Capsule.CreateCapsule(body, GH_Palette.Normal, 3, 6))
            using (Brush textBrush = new SolidBrush(Color.White))
            {
                capsule.SetJaggedEdges(false, true);
                capsule.AddInputGrip(grip);
                capsule.Render(graphics, style);
                RectangleF iconBox = new RectangleF(body.Right - 36F, body.Top + 8F, 24F, 24F);
                RectangleF inputBox = new RectangleF(body.Left + 8F, body.Top, iconBox.Left - body.Left - 10F, body.Height);
                using (Font inputFont = new Font(SystemFonts.MessageBoxFont.FontFamily, 9F, FontStyle.Bold))
                using (StringFormat format = new StringFormat { Alignment = StringAlignment.Near, LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter })
                    graphics.DrawString("X", inputFont, textBrush, inputBox, format);
                if (ComponentIcon != null) graphics.DrawImage(ComponentIcon, iconBox);
            }
        }

        private static Bitmap CreateWhiteIcon(Bitmap source)
        {
            if (source == null) return null;
            Bitmap result = new Bitmap(source.Width, source.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            for (int y = 0; y < source.Height; y++)
            {
                for (int x = 0; x < source.Width; x++)
                {
                    int alpha = source.GetPixel(x, y).A;
                    result.SetPixel(x, y, Color.FromArgb(alpha, 255, 255, 255));
                }
            }
            source.Dispose();
            return result;
        }

        private static void DrawComponentBand(Graphics graphics, RectangleF bounds, Brush band, Pen outline)
        {
            if (bounds.Width <= 0F || bounds.Height <= 0F) return;
            RectangleF outer = bounds; outer.Inflate(ComponentBandSize, ComponentBandSize);
            using (GraphicsPath outerPath = RoundedRectangle(outer, 7F))
            using (GraphicsPath innerPath = RoundedRectangle(bounds, 5F))
            using (Region ring = new Region(outerPath))
            {
                ring.Exclude(innerPath);
                graphics.FillRegion(band, ring);
                graphics.DrawPath(outline, outerPath);
            }
        }

        private static GraphicsPath RoundedRectangle(RectangleF bounds, float radius)
        {
            float diameter = Math.Min(radius * 2F, Math.Min(bounds.Width, bounds.Height));
            GraphicsPath path = new GraphicsPath();
            if (diameter <= 0F) { path.AddRectangle(bounds); return path; }
            path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180F, 90F);
            path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270F, 90F);
            path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0F, 90F);
            path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90F, 90F);
            path.CloseFigure(); return path;
        }

        private static void DrawTruncationWarning(Graphics graphics, IGH_DocumentObject root)
        {
            if (root == null || root.Attributes == null) return;
            const string message = "Lichen highlight truncated at 500 objects";
            using (Font font = new Font(SystemFonts.MessageBoxFont.FontFamily, 7.5F, FontStyle.Bold))
            using (Brush background = new SolidBrush(Color.FromArgb(225, 240, 247, 238)))
            using (Brush foreground = new SolidBrush(LichenGreen))
            using (Pen border = new Pen(Color.FromArgb(OutlineOpacity, LichenGreen), 1F))
            {
                SizeF size = graphics.MeasureString(message, font);
                RectangleF bounds = new RectangleF(root.Attributes.Bounds.Left, root.Attributes.Bounds.Bottom + 5F, size.Width + 10F, size.Height + 5F);
                graphics.FillRectangle(background, bounds); graphics.DrawRectangle(border, bounds.X, bounds.Y, bounds.Width, bounds.Height);
                graphics.DrawString(message, font, foreground, bounds.Left + 5F, bounds.Top + 2F);
            }
        }
    }
}
