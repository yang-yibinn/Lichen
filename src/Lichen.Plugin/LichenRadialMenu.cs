using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using Grasshopper.GUI;
using Grasshopper.GUI.Canvas;
using Grasshopper.GUI.Canvas.Interaction;
using Grasshopper.Kernel;
using Lichen.Core;

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
                if (roots.Count == 0 && !canCreate && canvas.Document.Nested) return;
                Point controlPosition = canvas.CursorControlPosition;
                canvas.BeginInvoke(new MethodInvoker(delegate { InstallCompanion(canvas, roots, canCreate, controlPosition); }));
            }
            catch
            {
                // A radial-menu enhancement must never interfere with Grasshopper's canvas input.
            }
        }

        private static void InstallCompanion(GH_Canvas canvas, IEnumerable<string> roots, bool canCreate, Point controlPosition)
        {
            if (canvas == null || canvas.IsDisposed || canvas.Document == null) return;
            try
            {
                GH_RadialMenuInteraction nativeMenu = canvas.ActiveInteraction as GH_RadialMenuInteraction;
                // Do not replace another extension's custom radial interaction.
                if (nativeMenu == null || nativeMenu.GetType() != typeof(GH_RadialMenuInteraction)) return;
                RadialMenuLayout layout = RadialMenuLayout.Place(canvas.ClientSize.Width, canvas.ClientSize.Height,
                    controlPosition.X, controlPosition.Y, GH_GraphicsUtil.UiScale);
                if (!layout.Fits) return; // Keep the native menu on very small canvases.
                Point center = new Point(layout.CenterX, layout.CenterY);
                GH_CanvasMouseEvent mouseEvent = new GH_CanvasMouseEvent(center, canvas.Viewport.UnprojectPoint(center), MouseButtons.Middle, 1, 0);
                canvas.ActiveInteraction = new LichenRadialMenuInteraction(canvas, mouseEvent, roots, canCreate, layout);
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
        private enum CompanionAction { None, SelectChain, CreateThallus, Spotlight }

        private const int SourceIconSize = 96;
        private const float DisplayIconSize = 24F;
        private static readonly Color HoverColor = Color.FromArgb(104, 214, 83);
        private static readonly Bitmap SelectIcon = LichenInfo.CreateSelectChainIcon(SourceIconSize);
        private static readonly Bitmap SelectHoverIcon = LichenInfo.CreateSelectChainIcon(SourceIconSize, HoverColor);
        private static readonly Bitmap SelectTooltipIcon = LichenInfo.CreateSelectChainIcon(24);
        private static readonly Bitmap ThallusIcon = LichenInfo.CreateThallusIcon(SourceIconSize);
        private static readonly Bitmap ThallusHoverIcon = LichenInfo.CreateThallusIcon(SourceIconSize, HoverColor);
        private static readonly Bitmap ThallusTooltipIcon = LichenInfo.CreateThallusIcon(24);
        private static readonly Bitmap SpotlightIcon = LichenInfo.CreateSpotlightIcon(SourceIconSize);
        private static readonly Bitmap SpotlightHoverIcon = LichenInfo.CreateSpotlightIcon(SourceIconSize, HoverColor);
        private static readonly Bitmap SpotlightTooltipIcon = LichenInfo.CreateSpotlightIcon(24);
        private readonly List<string> rootObjectIds;
        private readonly bool showSelectChain;
        private readonly bool showCreateThallus;
        private CompanionAction hoverAction;
        private bool actionInvoked;
        private bool destroyed;
        private readonly RadialMenuLayout layout;
        private bool openingReleasePending = true;

        internal LichenRadialMenuInteraction(GH_Canvas canvas, GH_CanvasMouseEvent eventArgs, IEnumerable<string> roots, bool canCreate, RadialMenuLayout layout)
            : base(canvas, eventArgs)
        {
            this.layout = layout;
            rootObjectIds = new List<string>(roots ?? new string[0]);
            showSelectChain = rootObjectIds.Count > 0;
            showCreateThallus = canCreate;
            canvas.CanvasPostPaintWidgets += CanvasPostPaintWidgets;
            canvas.Resize += CanvasResized;
        }

        public override bool TooltipEnabled { get { return true; } }

        public override bool IsTooltipRegion(PointF point)
        {
            return ActionAtCanvasPoint(point) != CompanionAction.None || base.IsTooltipRegion(point);
        }

        public override void SetupTooltip(PointF point, GH_TooltipDisplayEventArgs eventArgs)
        {
            CompanionAction action = ActionAtCanvasPoint(point);
            if (action == CompanionAction.Spotlight)
            {
                eventArgs.Title = "Spotlight";
                eventArgs.Text = "Spotlight";
                eventArgs.Description = LichenPriority.Spotlight.IsEnabled(Canvas) ? "Turn off dependency highlights and the legend." : "Highlight top-level third-party components. Adjust layers and focus in the legend.";
                eventArgs.Icon = SpotlightTooltipIcon;
                eventArgs.Region = CompanionBounds(Canvas, action);
                return;
            }
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
            openingReleasePending = false;
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
            bool ignoreOpeningRelease = openingReleasePending && eventArgs != null && eventArgs.Button == MouseButtons.Middle
                && layout.IsStationaryOpeningRelease(eventArgs.ControlLocation.X, eventArgs.ControlLocation.Y);
            openingReleasePending = false;
            if (ignoreOpeningRelease) return GH_ObjectResponse.Handled;
            CompanionAction action = ActionAtControlPoint(canvas, eventArgs);
            if (action != CompanionAction.None) return InvokeAction(canvas, action);
            return base.RespondToMouseUp(canvas, eventArgs);
        }

        public override void Destroy()
        {
            if (!destroyed)
            {
                destroyed = true;
                if (Canvas != null)
                {
                    Canvas.CanvasPostPaintWidgets -= CanvasPostPaintWidgets;
                    Canvas.Resize -= CanvasResized;
                }
            }
            base.Destroy();
        }

        private void CanvasResized(object sender, EventArgs eventArgs)
        {
            // Dismiss instead of rearranging hit areas under the pointer during a resize.
            if (Canvas != null && ReferenceEquals(Canvas.ActiveInteraction, this))
            {
                Canvas.ActiveInteraction = null;
                Canvas.Invalidate();
            }
        }

        private CompanionAction ActionAtControlPoint(GH_Canvas canvas, GH_CanvasMouseEvent eventArgs)
        {
            if (actionInvoked || canvas == null || eventArgs == null || eventArgs.Button == MouseButtons.Right) return CompanionAction.None;
            if (canvas.Document != null && !canvas.Document.Nested && CompanionBounds(canvas, CompanionAction.Spotlight).Contains(eventArgs.ControlLocation)) return CompanionAction.Spotlight;
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
                if (Canvas.Document != null && !Canvas.Document.Nested && CompanionBounds(Canvas, CompanionAction.Spotlight).Contains(controlPoint)) return CompanionAction.Spotlight;
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
            else if (action == CompanionAction.Spotlight) LichenPriority.Spotlight.Toggle(canvas);
            return GH_ObjectResponse.Release;
        }

        private void CanvasPostPaintWidgets(GH_Canvas canvas)
        {
            if (destroyed || !IsActive || canvas == null) return;
            try
            {
                if (showSelectChain) DrawCompanion(canvas, CompanionAction.SelectChain, SelectIcon, SelectHoverIcon);
                if (showCreateThallus) DrawCompanion(canvas, CompanionAction.CreateThallus, ThallusIcon, ThallusHoverIcon);
                if (canvas.Document != null && !canvas.Document.Nested) DrawCompanion(canvas, CompanionAction.Spotlight, SpotlightIcon, SpotlightHoverIcon);
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
            float scale = layout.Scale;
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
                    bool active = action == CompanionAction.Spotlight && LichenPriority.Spotlight.IsEnabled(canvas);
                    Bitmap visible = (hoverAction == action || active) && hoverIcon != null ? hoverIcon : icon;
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
            RadialButtonBounds bounds = layout.Button(action == CompanionAction.SelectChain ? 0 : action == CompanionAction.CreateThallus ? 1 : 2);
            return new Rectangle(bounds.Left, bounds.Top, bounds.Size, bounds.Size);
        }

    }
}
