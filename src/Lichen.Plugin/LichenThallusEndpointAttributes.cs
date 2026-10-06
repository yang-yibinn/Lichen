using System.Drawing;
using System.Drawing.Drawing2D;
using Grasshopper.GUI;
using Grasshopper.GUI.Canvas;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Attributes;
using Lichen.Core;

namespace Lichen.Plugin
{
    internal sealed class LichenThallusEndpointAttributes : GH_ComponentAttributes
    {
        private static readonly Color SocketEdge = Color.FromArgb(35, 35, 33);
        private static readonly Color Label = Color.FromArgb(28, 78, 25);
        private const float SocketDiameter = 12F;
        private const float SocketHitSize = 20F;
        private const float LabelWidth = 18F;
        private const float LabelHeight = 20F;
        private const float LabelGap = 7F;
        private const float OuterMargin = 3F;

        internal LichenThallusEndpointAttributes(LichenThallusEndpointComponent owner) : base(owner) { }

        protected override void Layout()
        {
            SetInfrastructureAnchor(Pivot);
            UpdateBoundaryLocation();
        }

        protected override void PrepareForRender(GH_Canvas canvas)
        {
            base.PrepareForRender(canvas);
            UpdateBoundaryLocation();
        }

        internal void UpdateBoundaryLocation()
        {
            if (Owner.Params.Output.Count == 0 || Owner.Params.Output[0].Attributes == null) return;

            PointF socket;
            RectangleF hitBounds, labelBounds;
            if (TryGetPortLayout(out socket, out hitBounds, out labelBounds))
            {
                SetInfrastructureAnchor(socket);
                Owner.Params.Output[0].Attributes.Pivot = socket;
                Owner.Params.Output[0].Attributes.Bounds = hitBounds;
            }
            else
            {
                SetInfrastructureAnchor(Pivot);
                Owner.Params.Output[0].Attributes.Pivot = Pivot;
                Owner.Params.Output[0].Attributes.Bounds = m_innerBounds;
            }
        }

        public override bool IsPickRegion(PointF point)
        {
            PointF socket;
            RectangleF hitBounds, labelBounds;
            return TryGetPortLayout(out socket, out hitBounds, out labelBounds) && hitBounds.Contains(point);
        }

        public override bool IsTooltipRegion(PointF point)
        {
            return IsPickRegion(point);
        }

        public override void SetupTooltip(PointF point, GH_TooltipDisplayEventArgs eventArgs)
        {
            eventArgs.Title = "Thallus output";
            eventArgs.Text = "T";
            eventArgs.Description = "Connect this outermost Thallus to Lichen.T directly or through native Merge, Jitter Values, or Relay routing.";
            PointF socket;
            RectangleF hitBounds, labelBounds;
            if (TryGetPortLayout(out socket, out hitBounds, out labelBounds)) eventArgs.Region = Rectangle.Round(hitBounds);
        }

        protected override void Render(GH_Canvas canvas, Graphics graphics, GH_CanvasChannel channel)
        {
            if (channel == GH_CanvasChannel.Wires) return;
            if (channel != GH_CanvasChannel.Overlay) return;

            PointF socket;
            RectangleF hitBounds, labelBounds;
            if (!TryGetPortLayout(out socket, out hitBounds, out labelBounds)) return;
            RectangleF socketBounds = new RectangleF(socket.X - SocketDiameter * 0.5F, socket.Y - SocketDiameter * 0.5F, SocketDiameter, SocketDiameter);
            RectangleF socketCenter = socketBounds; socketCenter.Inflate(-3F, -3F);
            SmoothingMode previous = graphics.SmoothingMode;
            using (Brush brush = new SolidBrush(Label))
            using (Brush socketEdge = new SolidBrush(SocketEdge))
            using (Brush socketFill = new SolidBrush(GH_Skin.canvas_back))
            using (Font font = new Font(SystemFonts.MessageBoxFont.FontFamily, 9F, FontStyle.Bold))
            using (StringFormat format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            {
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                graphics.DrawString("T", font, brush, labelBounds, format);
                graphics.FillEllipse(socketEdge, socketBounds);
                graphics.FillEllipse(socketFill, socketCenter);
            }
            graphics.SmoothingMode = previous;
        }

        private bool TryGetPortLayout(out PointF socket, out RectangleF hitBounds, out RectangleF labelBounds)
        {
            socket = PointF.Empty;
            hitBounds = RectangleF.Empty;
            labelBounds = RectangleF.Empty;
            LichenThallusEndpointComponent endpoint = (LichenThallusEndpointComponent)Owner;
            if (!endpoint.IsOutermost) return false;
            LichenThallusGroup group = LichenThallusCommands.FindOwner(endpoint);
            if (group == null || group.Attributes == null) return false;
            RectangleF groupBounds = group.Attributes.Bounds;
            if (groupBounds.Width <= 0F || groupBounds.Height <= 0F) return false;
            ThallusEndpointPortBounds port = ThallusEndpointLayout.OutsideRightPort(
                new ThallusEndpointBounds
                {
                    X = groupBounds.X,
                    Y = groupBounds.Y,
                    Width = groupBounds.Width,
                    Height = groupBounds.Height
                },
                LabelWidth,
                LabelHeight,
                LabelGap,
                SocketHitSize,
                OuterMargin);
            socket = new PointF((float)port.SocketX, (float)port.SocketY);
            hitBounds = ToRectangle(port.HitBounds);
            labelBounds = ToRectangle(port.LabelBounds);
            return true;
        }

        private void SetInfrastructureAnchor(PointF anchor)
        {
            m_innerBounds = new RectangleF(anchor.X - 0.5F, anchor.Y - 0.5F, 1F, 1F);
            // The endpoint is ownership and wiring infrastructure, not visible group content.
            // GH_Group ignores empty member bounds, so this live anchor cannot pin the outline.
            ThallusEndpointBounds excluded = ThallusEndpointLayout.ExcludedGroupBounds(anchor.X, anchor.Y);
            Bounds = ToRectangle(excluded);
        }

        private static RectangleF ToRectangle(ThallusEndpointBounds bounds)
        {
            return new RectangleF((float)bounds.X, (float)bounds.Y, (float)bounds.Width, (float)bounds.Height);
        }
    }
}
