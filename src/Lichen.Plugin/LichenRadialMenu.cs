using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using Grasshopper.GUI;
using Grasshopper.GUI.Canvas;
using Grasshopper.GUI.Canvas.Interaction;

namespace Lichen.Plugin
{
    internal static class LichenRadialMenuController
    {
        internal static void Attach(GH_Canvas canvas)
        {
            if (canvas == null) return;
            canvas.MouseDown -= OnCanvasMouseDown;
            canvas.MouseDown += OnCanvasMouseDown;
        }

        internal static void Detach(GH_Canvas canvas)
        {
            if (canvas == null) return;
            canvas.MouseDown -= OnCanvasMouseDown;
        }

        private static void OnCanvasMouseDown(object sender, MouseEventArgs eventArgs)
        {
            GH_Canvas canvas = sender as GH_Canvas;
            if (canvas == null || canvas.Document == null || eventArgs == null || eventArgs.Button != MouseButtons.Middle) return;
            try
            {
                List<string> roots = LichenChainSelection.SelectedRootIds(canvas.Document);
                if (roots.Count == 0) return;
                Point controlPosition = canvas.CursorControlPosition;
                PointF canvasPosition = canvas.CursorCanvasPosition;
                canvas.BeginInvoke(new MethodInvoker(delegate { InstallCompanion(canvas, roots, controlPosition, canvasPosition); }));
            }
            catch
            {
                // A radial-menu enhancement must never interfere with Grasshopper's canvas input.
            }
        }

        private static void InstallCompanion(GH_Canvas canvas, IEnumerable<string> roots, Point controlPosition, PointF canvasPosition)
        {
            if (canvas == null || canvas.IsDisposed || canvas.Document == null) return;
            try
            {
                GH_RadialMenuInteraction nativeMenu = canvas.ActiveInteraction as GH_RadialMenuInteraction;
                if (nativeMenu == null || nativeMenu is LichenRadialMenuInteraction) return;
                GH_CanvasMouseEvent mouseEvent = new GH_CanvasMouseEvent(controlPosition, canvasPosition, MouseButtons.Middle, 1, 0);
                canvas.ActiveInteraction = new LichenRadialMenuInteraction(canvas, mouseEvent, roots);
                canvas.Invalidate();
            }
            catch
            {
                // The native menu remains usable if the companion cannot be installed.
            }
        }
    }

    internal sealed class LichenRadialMenuInteraction : GH_RadialMenuInteraction
    {
        private const int SourceIconSize = 96;
        private const float DisplayIconSize = 24F;
        private static readonly Color HoverColor = Color.FromArgb(104, 214, 83);
        private static readonly Bitmap Icon = LichenInfo.CreateSelectChainIcon(SourceIconSize);
        private static readonly Bitmap HoverIcon = LichenInfo.CreateSelectChainIcon(SourceIconSize, HoverColor);
        private static readonly Bitmap TooltipIcon = LichenInfo.CreateSelectChainIcon(24);
        private readonly List<string> rootObjectIds;
        private bool hover;
        private bool actionInvoked;
        private bool destroyed;

        internal LichenRadialMenuInteraction(GH_Canvas canvas, GH_CanvasMouseEvent eventArgs, IEnumerable<string> roots)
            : base(canvas, eventArgs)
        {
            rootObjectIds = new List<string>(roots ?? new string[0]);
            canvas.CanvasPostPaintWidgets += CanvasPostPaintWidgets;
        }

        public override bool TooltipEnabled { get { return true; } }

        public override bool IsTooltipRegion(PointF point)
        {
            return IsCompanionCanvasPoint(point) || base.IsTooltipRegion(point);
        }

        public override void SetupTooltip(PointF point, GH_TooltipDisplayEventArgs eventArgs)
        {
            Rectangle bounds = CompanionBounds(Canvas);
            if (IsCompanionCanvasPoint(point))
            {
                eventArgs.Title = "Select chain";
                eventArgs.Text = "Select chain";
                eventArgs.Description = "Select the highlighted Lichen chain and its selected Lichen marker or markers.";
                eventArgs.Icon = TooltipIcon;
                eventArgs.Region = bounds;
                return;
            }
            base.SetupTooltip(point, eventArgs);
        }

        public override GH_ObjectResponse RespondToMouseDown(GH_Canvas canvas, GH_CanvasMouseEvent eventArgs)
        {
            if (IsCompanionActivation(canvas, eventArgs)) return SelectChain(canvas);
            return base.RespondToMouseDown(canvas, eventArgs);
        }

        public override GH_ObjectResponse RespondToMouseMove(GH_Canvas canvas, GH_CanvasMouseEvent eventArgs)
        {
            GH_ObjectResponse response = base.RespondToMouseMove(canvas, eventArgs);
            bool next = CompanionBounds(canvas).Contains(eventArgs.ControlLocation);
            if (next != hover)
            {
                hover = next;
                canvas.Invalidate();
            }
            return hover ? GH_ObjectResponse.Handled : response;
        }

        public override GH_ObjectResponse RespondToMouseUp(GH_Canvas canvas, GH_CanvasMouseEvent eventArgs)
        {
            if (IsCompanionActivation(canvas, eventArgs)) return SelectChain(canvas);
            return base.RespondToMouseUp(canvas, eventArgs);
        }

        public override void Destroy()
        {
            if (!destroyed)
            {
                destroyed = true;
                if (Canvas != null) Canvas.CanvasPostPaintWidgets -= CanvasPostPaintWidgets;
            }
            base.Destroy();
        }

        private bool IsCompanionActivation(GH_Canvas canvas, GH_CanvasMouseEvent eventArgs)
        {
            return !actionInvoked && canvas != null && eventArgs != null && eventArgs.Button != MouseButtons.Right
                && CompanionBounds(canvas).Contains(eventArgs.ControlLocation);
        }

        private GH_ObjectResponse SelectChain(GH_Canvas canvas)
        {
            actionInvoked = true;
            LichenChainSelection.Select(canvas, rootObjectIds);
            return GH_ObjectResponse.Release;
        }

        private void CanvasPostPaintWidgets(GH_Canvas canvas)
        {
            if (destroyed || !IsActive || canvas == null) return;
            try
            {
                Graphics graphics = canvas.Graphics;
                if (graphics == null) return;
                Rectangle bounds = CompanionBounds(canvas);
                PointF center = new PointF(bounds.Left + bounds.Width * 0.5F, bounds.Top + bounds.Height * 0.5F);
                float scale = Math.Max(1F, GH_GraphicsUtil.UiScale);
                int iconSize = Math.Max(18, (int)Math.Round(DisplayIconSize * scale));
                float iconRadius = iconSize * 0.5F;
                Point radialCenter = ControlPointDown;
                float dx = center.X - radialCenter.X, dy = center.Y - radialCenter.Y;
                float length = (float)Math.Sqrt(dx * dx + dy * dy);
                SmoothingMode previous = graphics.SmoothingMode;
                InterpolationMode previousInterpolation = graphics.InterpolationMode;
                using (Matrix previousTransform = graphics.Transform)
                {
                    graphics.ResetTransform();
                    graphics.SmoothingMode = SmoothingMode.AntiAlias;
                    graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    try
                    {
                        if (length > 0F)
                        {
                            float ux = dx / length, uy = dy / length;
                            PointF start = new PointF(radialCenter.X + ux * 76F * scale, radialCenter.Y + uy * 76F * scale);
                            PointF end = new PointF(center.X - ux * iconRadius, center.Y - uy * iconRadius);
                            using (Pen spoke = new Pen(Color.FromArgb(105, 92, 92, 92), Math.Max(1F, scale)))
                            {
                                spoke.DashStyle = DashStyle.Dot;
                                graphics.DrawLine(spoke, start, end);
                            }
                        }

                        Bitmap visibleIcon = hover && HoverIcon != null ? HoverIcon : Icon;
                        if (visibleIcon != null)
                        {
                            Rectangle iconBounds = new Rectangle((int)Math.Round(center.X - iconSize * 0.5F), (int)Math.Round(center.Y - iconSize * 0.5F), iconSize, iconSize);
                            graphics.DrawImage(visibleIcon, iconBounds);
                        }
                    }
                    finally
                    {
                        graphics.Transform = previousTransform;
                        graphics.SmoothingMode = previous;
                        graphics.InterpolationMode = previousInterpolation;
                    }
                }
            }
            catch
            {
                // Companion drawing must never interrupt Grasshopper's canvas paint.
            }
        }

        private bool IsCompanionCanvasPoint(PointF point)
        {
            try
            {
                if (Canvas == null) return false;
                PointF controlPoint = Canvas.Viewport.ProjectPoint(point);
                return CompanionBounds(Canvas).Contains(Point.Round(controlPoint));
            }
            catch { return false; }
        }

        private Rectangle CompanionBounds(GH_Canvas canvas)
        {
            float scale = Math.Max(1F, GH_GraphicsUtil.UiScale);
            int size = Math.Max(30, (int)Math.Round(34F * scale));
            int offsetX = (int)Math.Round(-65F * scale);
            int offsetY = (int)Math.Round(-63F * scale);
            int x = ControlPointDown.X + offsetX - size / 2;
            int y = ControlPointDown.Y + offsetY - size / 2;
            if (canvas != null)
            {
                const int margin = 4;
                x = Math.Max(margin, Math.Min(x, canvas.ClientSize.Width - size - margin));
                y = Math.Max(margin, Math.Min(y, canvas.ClientSize.Height - size - margin));
            }
            return new Rectangle(x, y, size, size);
        }
    }
}
