using System;
using System.Drawing;
using System.Windows.Forms;
using Grasshopper;
using Grasshopper.Kernel;
using Lichen.Adapters;
using Lichen.Core;

namespace Lichen.Plugin
{
    public sealed class LichenThallusEndpointComponent : GH_Component
    {
        public LichenThallusEndpointComponent()
            : base("Thallus Output", "T", "Connect an outermost Thallus to Lichen.T directly or through native Merge, Jitter Values, or Relay routing.", "Lichen", "Main") { }

        public override Guid ComponentGuid { get { return LichenComponentIds.ThallusEndpoint; } }
        public override GH_Exposure Exposure { get { return GH_Exposure.hidden; } }
        protected override Bitmap Icon { get { return LichenInfo.CreateThallusIcon(24); } }

        public override void CreateAttributes()
        {
            m_attributes = new LichenThallusEndpointAttributes(this);
        }

        protected override void RegisterInputParams(GH_InputParamManager parameters) { }

        protected override void RegisterOutputParams(GH_OutputParamManager parameters)
        {
            parameters.AddGenericParameter("Thallus", "T", "Opaque outermost-Thallus identity for direct or validated native routing to Lichen.T.", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess access)
        {
            if (Params.Output.Count == 0) return;
            LichenThallusGroup owner = LichenThallusCommands.FindOwner(this);
            if (owner == null)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "This internal Thallus output no longer has an owning Thallus.");
                return;
            }
            if (!IsOutermost && Params.Output[0].Recipients.Count > 0)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Only an outermost Thallus can connect to Lichen.T.");
            if (IsOutermost) access.SetData(0, new LichenThallusIdentityGoo(InstanceGuid.ToString("D").ToLowerInvariant(), owner.InstanceGuid.ToString("D").ToLowerInvariant()));
            foreach (IGH_Param recipient in Params.Output[0].Recipients)
            {
                if (!GrasshopperThallusIdentityResolver.IsSupportedImmediateRecipient(recipient))
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Thallus identities connect only to Lichen.T, directly or through native Merge, Jitter Values, or Relay routing.");
            }
        }

        protected override void AppendAdditionalComponentMenuItems(ToolStripDropDown menu)
        {
            base.AppendAdditionalComponentMenuItems(menu);
            Menu_AppendItem(menu, "Edit owning Thallus…", delegate
            {
                LichenThallusGroup owner = LichenThallusCommands.FindOwner(this);
                if (owner != null) LichenThallusEditor.Edit(owner, Instances.DocumentEditor);
            });
        }

        internal bool IsOutermost
        {
            get
            {
                LichenThallusGroup owner = LichenThallusCommands.FindOwner(this);
                return owner != null && LichenThallusCommands.FindParent(owner) == null;
            }
        }

        internal bool IsOrphan { get { return LichenThallusCommands.FindOwner(this) == null; } }
        internal bool IsVisible { get { return IsOutermost; } }
    }
}
