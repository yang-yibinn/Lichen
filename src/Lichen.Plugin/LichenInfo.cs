using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using Grasshopper.Kernel;

namespace Lichen.Plugin
{
    public sealed class LichenInfo : GH_AssemblyInfo
    {
        private const string IconResourceName = "Lichen.Plugin.Assets.lichen-icon-24.png";
        private static readonly Bitmap CachedIcon = LoadIcon();

        public override string Name { get { return "Lichen"; } }
        public override string Version { get { return "0.8.0"; } }
        public override Bitmap Icon { get { return CachedIcon; } }
        public override string Description { get { return "Exports selected or persistently marked Grasshopper graph context as deterministic Markdown and JSON without modifying the canvas."; } }
        public override Guid Id { get { return new Guid("2e725b2d-1937-4aa3-91dc-46a14a3f5b50"); } }
        public override string AuthorName { get { return "Yibin Yang"; } }
        public override string AuthorContact { get { return "https://github.com/yang-yibinn/Lichen"; } }

        internal static Bitmap CreateIconCopy()
        {
            return CachedIcon == null ? null : new Bitmap(CachedIcon);
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

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern bool DestroyIcon(IntPtr handle);
    }
}
