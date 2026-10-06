using System;
using System.Drawing;
using System.Windows.Forms;

namespace Lichen.Plugin
{
    // Keep native hover, focus, pressed and disabled feedback; only the glyph is custom.
    internal sealed class LichenExportButton : Button
    {
        private readonly Func<int, Bitmap> createIcon;
        private int iconPixels;

        internal LichenExportButton(string text, Func<int, Bitmap> createIcon)
        {
            this.createIcon = createIcon;
            Text = text; Dock = DockStyle.Fill; AutoEllipsis = false;
            TextImageRelation = TextImageRelation.ImageBeforeText;
            ImageAlign = ContentAlignment.MiddleCenter; TextAlign = ContentAlignment.MiddleCenter;
            Padding = new Padding(3, 0, 3, 0); UseVisualStyleBackColor = true;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            // Rasterize the embedded vector at the actual drawing DPI, including after
            // moving the dialog between displays. Do not stretch a cached 24px bitmap.
            int pixels = Math.Max(1, (int)Math.Round(24F * e.Graphics.DpiX / 96F));
            if (pixels != iconPixels)
            {
                Image previous = Image;
                Image = createIcon(pixels); iconPixels = pixels;
                if (previous != null) previous.Dispose();
            }
            base.OnPaint(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { Image previous = Image; Image = null; if (previous != null) previous.Dispose(); }
            base.Dispose(disposing);
        }
    }
}
