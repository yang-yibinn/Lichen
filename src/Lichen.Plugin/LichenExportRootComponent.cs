using System;
using System.Drawing;
using System.Windows.Forms;
using GH_IO.Serialization;
using Grasshopper;
using Grasshopper.Kernel;
using Lichen.Core;

namespace Lichen.Plugin
{
    public sealed class LichenExportRootComponent : GH_Component
    {
        internal const int MaximumNodes = 500;

        public LichenExportRootComponent()
            : base("Lichen", "Lichen", "Marks the complete contributing upstream graph as a persistent Lichen export scope.", "Lichen", "Main")
        {
        }

        public override Guid ComponentGuid { get { return LichenComponentIds.ExportRoot; } }
        public override GH_Exposure Exposure { get { return GH_Exposure.primary; } }
        protected override Bitmap Icon { get { return LichenInfo.CreateIconCopy(); } }

        public override void CreateAttributes()
        {
            m_attributes = new LichenExportRootAttributes(this);
        }

        protected override void RegisterInputParams(GH_InputParamManager parameters)
        {
            int index = parameters.AddGenericParameter("X", "X", "Result whose complete contributing upstream graph should be exported.", GH_ParamAccess.tree);
            parameters[index].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager parameters)
        {
        }

        protected override void SolveInstance(IGH_DataAccess access)
        {
            // The component is intentionally a graph marker. It does not request or retain X.
        }

        public override bool Read(GH_IReader reader)
        {
            bool result = base.Read(reader);
            if (String.Equals(NickName, "Export Root", StringComparison.OrdinalIgnoreCase) || String.Equals(NickName, "Lichen Export Root", StringComparison.OrdinalIgnoreCase)) NickName = "Lichen";
            return result;
        }

        protected override void AppendAdditionalComponentMenuItems(ToolStripDropDown menu)
        {
            base.AppendAdditionalComponentMenuItems(menu);
            Menu_AppendItem(menu, "Select chain", SelectChain, LichenInfo.CreateSelectChainIcon(24), true, false);
            Menu_AppendItem(menu, "Export this root…", OpenExportDialog, LichenInfo.CreateIconCopy(), true, false);
        }

        private void SelectChain(object sender, EventArgs eventArgs)
        {
            Grasshopper.GUI.Canvas.GH_Canvas canvas = Instances.ActiveCanvas;
            if (canvas == null || canvas.Document == null) return;
            LichenChainSelection.Select(canvas, LichenChainSelection.RootIdsForContext(canvas.Document, InstanceGuid.ToString("D")));
        }

        private void OpenExportDialog(object sender, EventArgs eventArgs)
        {
            ShowExportDialog();
        }

        internal void ShowExportDialog()
        {
            Form owner = Instances.DocumentEditor;
            using (LichenExportDialog dialog = new LichenExportDialog(InstanceGuid.ToString("D"))) dialog.ShowDialog(owner);
        }
    }
}
