using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using GH_IO.Serialization;
using Grasshopper;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Special;
using Lichen.Core;

namespace Lichen.Plugin
{
    public sealed class LichenThallusGroup : GH_Group, ILichenThallusMetadata
    {
        private const string DescriptionKey = "LichenThallusDescription";
        private const string PropertyCountKey = "LichenThallusPropertyCount";
        private const string PropertyKey = "LichenThallusPropertyKey";
        private const string PropertyValue = "LichenThallusPropertyValue";
        private readonly List<ContextMetadataEntry> properties = new List<ContextMetadataEntry>();
        private string thallusDescription = "";

        public LichenThallusGroup()
        {
            Name = "Thallus";
            NickName = "Thallus";
            Description = "An author-defined Lichen workflow group with exact export membership.";
            Colour = Color.FromArgb(80, 105, 174, 91);
            Border = GH_GroupBorder.Box;
        }

        public override Guid ComponentGuid { get { return LichenComponentIds.Thallus; } }
        public override GH_Exposure Exposure { get { return GH_Exposure.hidden; } }
        public string ThallusDescription { get { return thallusDescription; } }
        public IList<ContextMetadataEntry> ThallusProperties { get { return properties; } }

        public override void CreateAttributes()
        {
            m_attributes = new LichenThallusGroupAttributes(this);
        }

        public override void AddedToDocument(GH_Document document)
        {
            base.AddedToDocument(document);
            LichenThallusCommands.RefreshLayouts(document);
        }

        public override void RemovedFromDocument(GH_Document document)
        {
            base.RemovedFromDocument(document);
            LichenThallusCommands.RemoveOwnedEndpoint(this, document);
            LichenThallusCommands.RefreshLayouts(document);
        }

        internal void ApplyMetadata(string name, string description, IEnumerable<ContextMetadataEntry> values)
        {
            NickName = String.IsNullOrWhiteSpace(name) ? "Thallus" : name.Trim();
            thallusDescription = (description ?? "").Trim();
            properties.Clear();
            foreach (ContextMetadataEntry value in values ?? Enumerable.Empty<ContextMetadataEntry>())
            {
                if (value == null || String.IsNullOrWhiteSpace(value.Key)) continue;
                properties.Add(new ContextMetadataEntry { Key = value.Key.Trim(), Value = (value.Value ?? "").Trim() });
            }
            properties.Sort(delegate(ContextMetadataEntry a, ContextMetadataEntry b)
            {
                int key = StringComparer.OrdinalIgnoreCase.Compare(a.Key, b.Key);
                return key != 0 ? key : StringComparer.Ordinal.Compare(a.Value, b.Value);
            });
            ExpireCaches();
        }

        public override bool AppendMenuItems(ToolStripDropDown menu)
        {
            bool result = base.AppendMenuItems(menu);
            menu.Items.Add(new ToolStripSeparator());
            ToolStripMenuItem edit = new ToolStripMenuItem("Edit Thallus description and properties…");
            edit.Click += delegate { LichenThallusEditor.Edit(this, Instances.DocumentEditor); };
            menu.Items.Add(edit);
            ToolStripMenuItem select = new ToolStripMenuItem("Select Thallus members");
            select.Click += delegate { LichenThallusCommands.SelectMembers(this); };
            menu.Items.Add(select);
            ToolStripMenuItem add = new ToolStripMenuItem("Add selected objects to Thallus");
            add.Click += delegate { LichenThallusCommands.AddSelection(this); };
            menu.Items.Add(add);
            ToolStripMenuItem remove = new ToolStripMenuItem("Remove selected objects from Thallus");
            remove.Click += delegate { LichenThallusCommands.RemoveSelection(this); };
            menu.Items.Add(remove);
            return result;
        }

        public override bool Write(GH_IWriter writer)
        {
            writer.SetString(DescriptionKey, thallusDescription ?? "");
            writer.SetInt32(PropertyCountKey, properties.Count);
            for (int i = 0; i < properties.Count; i++)
            {
                writer.SetString(PropertyKey, i, properties[i].Key ?? "");
                writer.SetString(PropertyValue, i, properties[i].Value ?? "");
            }
            return base.Write(writer);
        }

        public override bool Read(GH_IReader reader)
        {
            bool result = base.Read(reader);
            string value = "";
            thallusDescription = reader.TryGetString(DescriptionKey, ref value) ? value ?? "" : "";
            properties.Clear();
            int count = 0;
            if (reader.TryGetInt32(PropertyCountKey, ref count))
                for (int i = 0; i < Math.Max(0, count); i++)
                {
                    string key = "", propertyValue = "";
                    if (!reader.TryGetString(PropertyKey, i, ref key) || String.IsNullOrWhiteSpace(key)) continue;
                    reader.TryGetString(PropertyValue, i, ref propertyValue);
                    properties.Add(new ContextMetadataEntry { Key = key.Trim(), Value = (propertyValue ?? "").Trim() });
                }
            return result;
        }
    }
}
