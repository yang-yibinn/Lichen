using System;
using Lichen.Core;

namespace Lichen.Tests
{
    internal static partial class Program
    {
        private static void RunRadialMenuLayoutTests()
        {
            Run("radial companions keep their arc and native button clearance at edges and display scales", RadialArcClearance);
            Run("small canvases retain the native menu rather than compressing companion buttons", RadialSmallCanvas);
            Run("relocated radial menus do not activate on a stationary opening release", RadialOpeningRelease);
        }

        private static void RadialArcClearance()
        {
            foreach (float scale in new[] { 1F, 1.25F, 1.5F, 1.75F, 2F, 2.5F, 3F, 4F })
            {
                const int width = 2400, height = 1600;
                var middle = RadialMenuLayout.Place(width, height, width / 2, height / 2, scale);
                foreach (int x in new[] { 0, 4, width / 2, width - 4, width })
                foreach (int y in new[] { 0, 4, height / 2, height - 4, height })
                {
                    var layout = RadialMenuLayout.Place(width, height, x, y, scale);
                    Equal(true, layout.Fits);
                    for (int slot = 0; slot < 3; slot++)
                    {
                        var button = layout.Button(slot); var reference = middle.Button(slot);
                        Equal(reference.Left - middle.CenterX, button.Left - layout.CenterX);
                        Equal(reference.Top - middle.CenterY, button.Top - layout.CenterY);
                        Equal(true, button.Left >= 0 && button.Top >= 0 && button.Left + button.Size <= width && button.Top + button.Size <= height);
                        for (int other = slot + 1; other < 3; other++) Equal(false, RadialOverlap(button, layout.Button(other)));

                        // SDK 8.0 native fixture: eight inner slots at 55 DIP and five outer
                        // slots on the right/top/bottom at 90 DIP; icons are 24 DIP with a
                        // two-control-pixel hover margin. No native slot occupies the left arc.
                        for (int angle = 0; angle < 360; angle += 45) CheckNativeClearance(layout, button, 55, angle);
                        for (int angle = -90; angle <= 90; angle += 45) CheckNativeClearance(layout, button, 90, angle);
                    }
                    var upper = layout.Button(0); var lower = layout.Button(2);
                    Equal(upper.Left, lower.Left);
                    Equal(layout.CenterY - upper.Top - upper.Size / 2, lower.Top + lower.Size / 2 - layout.CenterY);
                    Equal(true, layout.Button(1).Left < upper.Left);
                }
            }
        }

        private static void CheckNativeClearance(RadialMenuLayout layout, RadialButtonBounds button, int radius, int angle)
        {
            double radians = angle * Math.PI / 180;
            int distance = (int)Math.Round(radius * layout.Scale);
            int half = (int)Math.Round(12 * layout.Scale) + 2;
            var native = new RadialButtonBounds
            {
                Left = layout.CenterX + (int)Math.Round(distance * Math.Cos(radians)) - half,
                Top = layout.CenterY - (int)Math.Round(distance * Math.Sin(radians)) - half,
                Size = half * 2
            };
            Equal(false, RadialOverlap(button, native));
        }

        private static bool RadialOverlap(RadialButtonBounds a, RadialButtonBounds b)
        {
            return a.Left < b.Left + b.Size && b.Left < a.Left + a.Size && a.Top < b.Top + b.Size && b.Top < a.Top + a.Size;
        }

        private static void RadialSmallCanvas()
        {
            foreach (float scale in new[] { 1F, 1.25F, 1.5F, 2F, 4F })
            {
                int minimum = 2 * (int)Math.Ceiling(134 * scale);
                Equal(true, RadialMenuLayout.Place(minimum, minimum, 0, 0, scale).Fits);
                var narrow = RadialMenuLayout.Place(minimum - 1, 2000, 0, 0, scale);
                Equal(false, narrow.Fits); Equal(0, narrow.Button(2).Size);
                Equal(false, RadialMenuLayout.Place(2000, minimum - 1, 0, 0, scale).Fits);
            }
            Equal(false, RadialMenuLayout.Place(0, 0, 0, 0, 1).Fits);
            Equal(true, RadialMenuLayout.Place(1000, 800, 500, 400, Single.NaN).Fits);
        }

        private static void RadialOpeningRelease()
        {
            var shifted = RadialMenuLayout.Place(1000, 800, 80, 400, 1);
            Equal(true, shifted.IsStationaryOpeningRelease(80, 400));
            Equal(true, shifted.IsStationaryOpeningRelease(83, 398));
            Equal(false, shifted.IsStationaryOpeningRelease(100, 400));
            var centered = RadialMenuLayout.Place(1000, 800, 500, 400, 1);
            Equal(false, centered.IsStationaryOpeningRelease(500, 400));
        }
    }
}
