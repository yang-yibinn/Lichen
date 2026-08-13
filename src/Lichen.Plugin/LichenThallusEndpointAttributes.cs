using System.Drawing;
using System.Drawing.Drawing2D;
using Grasshopper.GUI;
using Grasshopper.GUI.Canvas;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Attributes;

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

        internal LichenThallusEndpointAttributes(LichenThallusEndpointComponent owner) : base(owner) { }

        protected override void Layout()
        {
            m_innerBounds = new RectangleF(Pivot.X - 0.5F, Pivot.Y - 0.5F, 1F, 1F);
            Bounds = m_innerBounds;
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
                Owner.Params.Output[0].Attributes.Pivot = socket;
                Owner.Params.Output[0].Attributes.Bounds = hitBounds;
            }
            else
            {
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
            socket = new PointF(groupBounds.Right, groupBounds.Top + groupBounds.Height * 0.5F);
            hitBounds = new RectangleF(socket.X - SocketHitSize * 0.5F, socket.Y - SocketHitSize * 0.5F, SocketHitSize, SocketHitSize);
            labelBounds = new RectangleF(socket.X - LabelGap - LabelWidth, socket.Y - LabelHeight * 0.5F, LabelWidth, LabelHeight);
            return true;
        }
    }
}
