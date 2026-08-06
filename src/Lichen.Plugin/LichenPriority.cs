using System;
using System.Linq;
using System.Windows.Forms;
using Grasshopper;
using Grasshopper.GUI.Canvas;
using Grasshopper.Kernel;

namespace Lichen.Plugin
{
    public sealed class LichenPriority : GH_AssemblyPriority
    {
        private const string MenuName = "LichenMainMenu";

        public override GH_LoadingInstruction PriorityLoad()
        {
            Instances.CanvasCreated += OnCanvasCreated;
            return GH_LoadingInstruction.Proceed;
        }

        private static void OnCanvasCreated(GH_Canvas canvas)
        {
            try
            {
                Form editor = Instances.DocumentEditor;
                if (editor == null || editor.MainMenuStrip == null) return;
                if (editor.InvokeRequired) editor.BeginInvoke(new Action<Form>(InstallMenu), editor);
                else InstallMenu(editor);
            }
            catch
            {
                // Menu setup must never prevent Grasshopper from loading other plugins.
            }
        }

        private static void InstallMenu(Form editor)
        {
            if (editor.MainMenuStrip.Items.Cast<ToolStripItem>().Any(item => item.Name == MenuName)) return;
            ToolStripMenuItem menu = new ToolStripMenuItem("Lichen"); menu.Name = MenuName;
            ToolStripMenuItem copy = new ToolStripMenuItem("Copy Context…"); copy.Name = "LichenCopyContext";
            copy.Image = LichenInfo.CreateIconCopy();
            copy.ToolTipText = "Export selected Grasshopper context without changing the document.";
            copy.Click += delegate
            {
                using (LichenExportDialog dialog = new LichenExportDialog()) dialog.ShowDialog(editor);
            };
            menu.DropDownItems.Add(copy); editor.MainMenuStrip.Items.Add(menu);
        }
    }
}
