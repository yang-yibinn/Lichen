using System;

namespace Lichen.Core
{
    public sealed class RadialButtonBounds
    {
        public int Left, Top, Size;
    }

    // One control-space origin for the native wheel and the Lichen arc. No host objects.
    public sealed class RadialMenuLayout
    {
        public bool Fits { get; private set; }
        public int CenterX { get; private set; }
        public int CenterY { get; private set; }
        public float Scale { get; private set; }
        private int openingX, openingY;

        public static RadialMenuLayout Place(int width, int height, int openingX, int openingY, float scale)
        {
            scale = Single.IsNaN(scale) || Single.IsInfinity(scale) ? 1F : Math.Max(1F, scale);
            var result = new RadialMenuLayout { Scale = scale, openingX = openingX, openingY = openingY };
            // SDK 8.0's native wheel shadow reaches 130 DIP; include four DIP of clearance.
            double padding = Math.Ceiling(134.0 * scale);
            if (width < 2 * padding || height < 2 * padding) return result;
            int inset = (int)padding;
            result.Fits = true;
            result.CenterX = Math.Max(inset, Math.Min(openingX, width - inset));
            result.CenterY = Math.Max(inset, Math.Min(openingY, height - inset));
            return result;
        }

        // Fixed slots: upper-left, left, lower-left. Unavailable actions leave their slot empty.
        public RadialButtonBounds Button(int slot)
        {
            if (slot < 0 || slot > 2) throw new ArgumentOutOfRangeException("slot");
            if (!Fits) return new RadialButtonBounds();
            int size = (int)Math.Round(34F * Scale);
            int dx = (int)Math.Round((slot == 1 ? -102F : -72F) * Scale);
            int dy = (int)Math.Round((slot - 1) * 72F * Scale);
            return new RadialButtonBounds { Left = CenterX + dx - size / 2, Top = CenterY + dy - size / 2, Size = size };
        }

        public bool IsStationaryOpeningRelease(int x, int y)
        {
            // A shifted menu must not activate a button merely by appearing under the opening click.
            return Fits && (CenterX != openingX || CenterY != openingY)
                && Math.Abs((double)x - openingX) <= 4 * Scale && Math.Abs((double)y - openingY) <= 4 * Scale;
        }
    }
}
