using System;

namespace Lichen.Core
{
    public sealed class ThallusEndpointBounds
    {
        public double X { get; set; }
        public double Y { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
    }

    public sealed class ThallusEndpointPortBounds
    {
        public double SocketX { get; set; }
        public double SocketY { get; set; }
        public ThallusEndpointBounds HitBounds { get; set; }
        public ThallusEndpointBounds LabelBounds { get; set; }
    }

    public static class ThallusEndpointLayout
    {
        public static ThallusEndpointBounds ExcludedGroupBounds(double pivotX, double pivotY)
        {
            return new ThallusEndpointBounds
            {
                X = pivotX,
                Y = pivotY,
                Width = 0.0,
                Height = 0.0
            };
        }

        public static ThallusEndpointPortBounds OutsideRightPort(
            ThallusEndpointBounds groupBounds,
            double labelWidth,
            double labelHeight,
            double labelGap,
            double hitSize,
            double outerMargin)
        {
            if (groupBounds == null) throw new ArgumentNullException("groupBounds");
            if (groupBounds.Width <= 0.0 || groupBounds.Height <= 0.0)
                throw new ArgumentOutOfRangeException("groupBounds", "The owning group must have positive bounds.");
            if (labelWidth <= 0.0 || labelHeight <= 0.0 || labelGap < 0.0 || hitSize <= 0.0 || outerMargin < 0.0)
                throw new ArgumentOutOfRangeException("labelWidth", "Port dimensions and spacing must be positive or zero where allowed.");

            double labelX = groupBounds.X + groupBounds.Width + outerMargin;
            double socketX = labelX + labelWidth + labelGap;
            double socketY = groupBounds.Y + groupBounds.Height * 0.5;
            return new ThallusEndpointPortBounds
            {
                SocketX = socketX,
                SocketY = socketY,
                HitBounds = new ThallusEndpointBounds
                {
                    X = socketX - hitSize * 0.5,
                    Y = socketY - hitSize * 0.5,
                    Width = hitSize,
                    Height = hitSize
                },
                LabelBounds = new ThallusEndpointBounds
                {
                    X = labelX,
                    Y = socketY - labelHeight * 0.5,
                    Width = labelWidth,
                    Height = labelHeight
                }
            };
        }
    }
}
