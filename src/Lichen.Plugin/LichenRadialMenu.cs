using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using Grasshopper.GUI;
using Grasshopper.GUI.Canvas;
using Grasshopper.GUI.Canvas.Interaction;
using Grasshopper.Kernel;

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
                bool canCreate = LichenThallusCommands.CanCreate(canvas.Document);
                if (roots.Count == 0 && !canCreate) return;
                Point controlPosition = canvas.CursorControlPosition;
                PointF canvasPosition = canvas.CursorCanvasPosition;
                canvas.BeginInvoke(new MethodInvoker(delegate { InstallCompanion(canvas, roots, canCreate, controlPosition, canvasPosition); }));
            }
            catch
            {
                // A radial-menu enhancement must never interfere with Grasshopper's canvas input.
            }
        }

        private static void InstallCompanion(GH_Canvas canvas, IEnumerable<string> roots, bool canCreate, Point controlPosition, PointF canvasPosition)
        {
            if (canvas == null || canvas.IsDisposed || canvas.Document == null) return;
            try
            {
                GH_RadialMenuInteraction nativeMenu = canvas.ActiveInteraction as GH_RadialMenuInteraction;
                if (nativeMenu == null || nativeMenu is LichenRadialMenuInteraction) return;
                GH_CanvasMouseEvent mouseEvent = new GH_CanvasMouseEvent(controlPosition, canvasPosition, MouseButtons.Middle, 1, 0);
                canvas.ActiveInteraction = new LichenRadialMenuInteraction(canvas, mouseEvent, roots, canCreate);
                canvas.Invalidate();
            }
            catch
            {
                // The native menu remains usable if the companions cannot be installed.
            }
        }
    }

    internal sealed class LichenRadialMenuInteraction : GH_RadialMenuInteraction
    {
        private enum CompanionAction { None, SelectChain, CreateThallus }

        private const int SourceIconSize = 96;
        private const float DisplayIconSize = 24F;
        private static readonly Color HoverColor = Color.FromArgb(104, 214, 83);
        private static readonly Bitmap SelectIcon = LichenInfo.CreateSelectChainIcon(SourceIconSize);
        private static readonly Bitmap SelectHoverIcon = LichenInfo.CreateSelectChainIcon(SourceIconSize, HoverColor);
        private static readonly Bitmap SelectTooltipIcon = LichenInfo.CreateSelectChainIcon(24);
        private static readonly Bitmap ThallusIcon = LichenInfo.CreateThallusIcon(SourceIconSize);
        private static readonly Bitmap ThallusHoverIcon = LichenInfo.CreateThallusIcon(SourceIconSize, HoverColor);
        private static readonly Bitmap ThallusTooltipIcon = LichenInfo.CreateThallusIcon(24);
        private readonly List<string> rootObjectIds;
        private readonly bool showSelectChain;
        private readonly bool showCreateThallus;
        private CompanionAction hoverAction;
        private bool actionInvoked;
        private bool destroyed;

        internal LichenRadialMenuInteraction(GH_Canvas canvas, GH_CanvasMouseEvent eventArgs, IEnumerable<string> roots, bool canCreate)
            : base(canvas, eventArgs)
        {
            rootObjectIds = new List<string>(roots ?? new string[0]);
            showSelectChain = rootObjectIds.Count > 0;
            showCreateThallus = canCreate;
            canvas.CanvasPostPaintWidgets += CanvasPostPaintWidgets;
        }

        public override bool TooltipEnabled { get { return true; } }

        public override bool IsTooltipRegion(PointF point)
        {
            return ActionAtCanvasPoint(point) != CompanionAction.None || base.IsTooltipRegion(point);
        }

        public override void SetupTooltip(PointF point, GH_TooltipDisplayEventArgs eventArgs)
        {
            CompanionAction action = ActionAtCanvasPoint(point);
            if (action == CompanionAction.SelectChain)
            {
                eventArgs.Title = "Select chain";
                eventArgs.Text = "Select chain";
                eventArgs.Description = "Select the highlighted Lichen chain and its selected Lichen marker or markers.";
                eventArgs.Icon = SelectTooltipIcon;
                eventArgs.Region = CompanionBounds(Canvas, action);
                return;
            }
            if (action == CompanionAction.CreateThallus)
            {
                eventArgs.Title = "Create Thallus";
                eventArgs.Text = "Create Thallus";
                eventArgs.Description = "Create a Lichen workflow group from the selected Grasshopper components.";
                eventArgs.Icon = ThallusTooltipIcon;
                eventArgs.Region = CompanionBounds(Canvas, action);
                return;
            }
            base.SetupTooltip(point, eventArgs);
        }

        public override GH_ObjectResponse RespondToMouseDown(GH_Canvas canvas, GH_CanvasMouseEvent eventArgs)
        {
            CompanionAction action = ActionAtControlPoint(canvas, eventArgs);
            if (action != CompanionAction.None) return InvokeAction(canvas, action);
            return base.RespondToMouseDown(canvas, eventArgs);
        }

        public override GH_ObjectResponse RespondToMouseMove(GH_Canvas canvas, GH_CanvasMouseEvent eventArgs)
        {
            GH_ObjectResponse response = base.RespondToMouseMove(canvas, eventArgs);
            CompanionAction next = ActionAtControlPoint(canvas, eventArgs);
            if (next != hoverAction)
            {
                hoverAction = next;
                canvas.Invalidate();
            }
            return hoverAction == CompanionAction.None ? response : GH_ObjectResponse.Handled;
        }

        public override GH_ObjectResponse RespondToMouseUp(GH_Canvas canvas, GH_CanvasMouseEvent eventArgs)
        {
            CompanionAction action = ActionAtControlPoint(canvas, eventArgs);
            if (action != CompanionAction.None) return InvokeAction(canvas, action);
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

        private CompanionAction ActionAtControlPoint(GH_Canvas canvas, GH_CanvasMouseEvent eventArgs)
        {
            if (actionInvoked || canvas == null || eventArgs == null || eventArgs.Button == MouseButtons.Right) return CompanionAction.None;
            if (showSelectChain && CompanionBounds(canvas, CompanionAction.SelectChain).Contains(eventArgs.ControlLocation)) return CompanionAction.SelectChain;
            if (showCreateThallus && CompanionBounds(canvas, CompanionAction.CreateThallus).Contains(eventArgs.ControlLocation)) return CompanionAction.CreateThallus;
            return CompanionAction.None;
        }

        private CompanionAction ActionAtCanvasPoint(PointF point)
        {
            try
            {
                if (Canvas == null) return CompanionAction.None;
                Point controlPoint = Point.Round(Canvas.Viewport.ProjectPoint(point));
                if (showSelectChain && CompanionBounds(Canvas, CompanionAction.SelectChain).Contains(controlPoint)) return CompanionAction.SelectChain;
                if (showCreateThallus && CompanionBounds(Canvas, CompanionAction.CreateThallus).Contains(controlPoint)) return CompanionAction.CreateThallus;
            }
            catch { }
            return CompanionAction.None;
        }

        private GH_ObjectResponse InvokeAction(GH_Canvas canvas, CompanionAction action)
        {
            actionInvoked = true;
            if (action == CompanionAction.SelectChain) LichenChainSelection.Select(canvas, rootObjectIds);
            else if (action == CompanionAction.CreateThallus) LichenThallusCommands.CreateFromSelection(canvas);
            return GH_ObjectResponse.Release;
        }

        private void CanvasPostPaintWidgets(GH_Canvas canvas)
        {
            if (destroyed || !IsActive || canvas == null) return;
            try
            {
                if (showSelectChain) DrawCompanion(canvas, CompanionAction.SelectChain, SelectIcon, SelectHoverIcon);
                if (showCreateThallus) DrawCompanion(canvas, CompanionAction.CreateThallus, ThallusIcon, ThallusHoverIcon);
            }
            catch
            {
                // Companion drawing must never interrupt Grasshopper's canvas paint.
            }
        }

        private void DrawCompanion(GH_Canvas canvas, CompanionAction action, Bitmap icon, Bitmap hoverIcon)
        {
            Graphics graphics = canvas.Graphics;
            if (graphics == null) return;
            Rectangle bounds = CompanionBounds(canvas, action);
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
                    Bitmap visible = hoverAction == action && hoverIcon != null ? hoverIcon : icon;
                    if (visible != null)
                    {
                        Rectangle iconBounds = new Rectangle((int)Math.Round(center.X - iconSize * 0.5F), (int)Math.Round(center.Y - iconSize * 0.5F), iconSize, iconSize);
                        graphics.DrawImage(visible, iconBounds);
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

        private Rectangle CompanionBounds(GH_Canvas canvas, CompanionAction action)
        {
            float scale = Math.Max(1F, GH_GraphicsUtil.UiScale);
            int size = Math.Max(30, (int)Math.Round(34F * scale));
            int offsetX = (int)Math.Round((action == CompanionAction.CreateThallus ? -94F : -65F) * scale);
            int offsetY = (int)Math.Round((action == CompanionAction.CreateThallus ? 0F : -63F) * scale);
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
