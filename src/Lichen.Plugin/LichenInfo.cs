using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using Grasshopper.Kernel;

namespace Lichen.Plugin
{
    public sealed class LichenInfo : GH_AssemblyInfo
    {
        internal const string CurrentVersion = "0.8.1";
        private const string IconResourceName = "Lichen.Plugin.Assets.lichen-icon-24.png";
        private const string SelectChainIconResourceName = "Lichen.Plugin.Assets.lichen-select-chain.svg";
        private static readonly Bitmap CachedIcon = LoadIcon();
        private static readonly string CachedSelectChainSvg = LoadTextResource(SelectChainIconResourceName);

        public override string Name { get { return "Lichen"; } }
        public override string Version { get { return CurrentVersion; } }
        public override Bitmap Icon { get { return CachedIcon; } }
        public override string Description { get { return "Exports selected or persistently marked Grasshopper graph context as deterministic Markdown and JSON without modifying the canvas."; } }
        public override Guid Id { get { return new Guid("2e725b2d-1937-4aa3-91dc-46a14a3f5b50"); } }
        public override string AuthorName { get { return "Yibin Yang"; } }
        public override string AuthorContact { get { return "https://github.com/yang-yibinn/Lichen"; } }

        internal static Bitmap CreateIconCopy()
        {
            return CachedIcon == null ? null : new Bitmap(CachedIcon);
        }

        internal static Bitmap CreateWhiteIconCopy()
        {
            using (Bitmap source = CreateIconCopy())
            {
                if (source == null) return null;
                Bitmap result = new Bitmap(source.Width, source.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                for (int y = 0; y < source.Height; y++)
                    for (int x = 0; x < source.Width; x++)
                        result.SetPixel(x, y, Color.FromArgb(source.GetPixel(x, y).A, 255, 255, 255));
                return result;
            }
        }

        internal static Bitmap CreateSelectChainIcon(int size)
        {
            if (string.IsNullOrEmpty(CachedSelectChainSvg)) return null;
            try
            {
                int pixels = Math.Max(1, size);
                return Rhino.UI.DrawingUtilities.BitmapFromSvg(CachedSelectChainSvg, pixels, pixels, false);
            }
            catch { return null; }
        }

        internal static Bitmap CreateSelectChainIcon(int size, Color color)
        {
            using (Bitmap source = CreateSelectChainIcon(size))
            {
                if (source == null) return null;
                Bitmap result = new Bitmap(source.Width, source.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                for (int y = 0; y < source.Height; y++)
                    for (int x = 0; x < source.Width; x++)
                        result.SetPixel(x, y, Color.FromArgb(source.GetPixel(x, y).A, color.R, color.G, color.B));
                return result;
            }
        }

        internal static Icon CreateDialogIcon()
        {
            using (Bitmap bitmap = CreateIconCopy())
            {
                if (bitmap == null) return null;
                IntPtr handle = bitmap.GetHicon();
                try
                {
                    using (System.Drawing.Icon icon = System.Drawing.Icon.FromHandle(handle)) return (System.Drawing.Icon)icon.Clone();
                }
                finally { DestroyIcon(handle); }
            }
        }

        private static Bitmap LoadIcon()
        {
            using (Stream stream = typeof(LichenInfo).Assembly.GetManifestResourceStream(IconResourceName))
            {
                if (stream == null) { return null; }
                using (Bitmap source = new Bitmap(stream))
                {
                    return new Bitmap(source);
                }
            }
        }

        private static string LoadTextResource(string resourceName)
        {
            using (Stream stream = typeof(LichenInfo).Assembly.GetManifestResourceStream(resourceName))
            {
                if (stream == null) return null;
                using (StreamReader reader = new StreamReader(stream)) return reader.ReadToEnd();
            }
        }

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern bool DestroyIcon(IntPtr handle);
    }
}
