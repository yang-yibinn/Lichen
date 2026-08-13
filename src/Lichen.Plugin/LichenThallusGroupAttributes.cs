using Grasshopper;
using Grasshopper.GUI;
using Grasshopper.GUI.Canvas;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Special;

namespace Lichen.Plugin
{
    internal sealed class LichenThallusGroupAttributes : GH_GroupAttributes, IGH_ResponsiveObject
    {
        internal LichenThallusGroupAttributes(LichenThallusGroup owner) : base(owner) { }

        public new GH_ObjectResponse RespondToMouseDoubleClick(GH_Canvas canvas, GH_CanvasMouseEvent eventArgs)
        {
            LichenThallusGroup group = DocObject as LichenThallusGroup;
            if (group != null) LichenThallusEditor.Edit(group, Instances.DocumentEditor);
            return GH_ObjectResponse.Handled;
        }
    }
}
