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
        private GH_Document observedDocument;

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
            ObserveDocument(document);
            LichenThallusCommands.RefreshLayouts(document);
        }

        public override void RemovedFromDocument(GH_Document document)
        {
            ObserveDocument(null);
            base.RemovedFromDocument(document);
            LichenThallusCommands.RemoveOwnedEndpoint(this, document);
            LichenThallusCommands.RefreshLayouts(document);
        }

        public override void MovedBetweenDocuments(GH_Document oldDocument, GH_Document newDocument)
        {
            base.MovedBetweenDocuments(oldDocument, newDocument);
            ObserveDocument(newDocument);
            ExpireCaches();
        }

        public override void DocumentContextChanged(GH_Document document, GH_DocumentContext context)
        {
            base.DocumentContextChanged(document, context);
            if (context == GH_DocumentContext.Close || context == GH_DocumentContext.Unloaded)
                ObserveDocument(null);
            else if (context == GH_DocumentContext.Open || context == GH_DocumentContext.Loaded)
            {
                ObserveDocument(document);
                ExpireCaches();
            }
        }

        private void ObserveDocument(GH_Document document)
        {
            if (ReferenceEquals(observedDocument, document)) return;
            if (observedDocument != null)
            {
                observedDocument.ObjectsAdded -= DocumentObjectsChanged;
                observedDocument.ObjectsDeleted -= DocumentObjectsChanged;
                observedDocument.UndoStateChanged -= DocumentUndoStateChanged;
            }
            observedDocument = document;
            if (observedDocument != null)
            {
                observedDocument.ObjectsAdded += DocumentObjectsChanged;
                observedDocument.ObjectsDeleted += DocumentObjectsChanged;
                observedDocument.UndoStateChanged += DocumentUndoStateChanged;
            }
        }

        private void DocumentObjectsChanged(object sender, GH_DocObjectEventArgs args)
        {
            // GH_Document.ExpireGroups filters on GH_Group.GroupID, so it skips this subtype.
            // Undo reconstructs new object instances with the same IDs. Drop cached references,
            // including nested groups, without changing membership or creating undo records.
            if (ReferenceEquals(sender, observedDocument)) ExpireCaches();
        }

        private void DocumentUndoStateChanged(object sender, GH_DocUndoEventArgs args)
        {
            // Refresh once more after all actions finish, including batch edits which suppress
            // object events. The normal host repaint then resolves current members by GUID.
            if (ReferenceEquals(sender, observedDocument)
                && (args.Operation == GH_UndoOperation.Undo || args.Operation == GH_UndoOperation.Redo))
                ExpireCaches();
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
