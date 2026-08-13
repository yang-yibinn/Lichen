using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Lichen.Core;

namespace Lichen.Plugin
{
    internal static class LichenThallusEditor
    {
        internal static void Edit(LichenThallusGroup group, IWin32Window owner)
        {
            if (group == null) return;
            using (ThallusMetadataForm form = new ThallusMetadataForm(group))
            {
                if (form.ShowDialog(owner) != DialogResult.OK) return;
                group.RecordUndoEvent("Edit Thallus context");
                group.ApplyMetadata(form.ThallusName, form.ThallusDescription, form.Properties);
                if (group.Attributes != null) group.Attributes.ExpireLayout();
                try { Grasshopper.Instances.ActiveCanvas.Invalidate(); } catch { }
            }
        }

        private sealed class ThallusMetadataForm : Form
        {
            private readonly TextBox nameBox;
            private readonly TextBox descriptionBox;
            private readonly TextBox propertiesBox;

            internal ThallusMetadataForm(LichenThallusGroup group)
            {
                Text = "Edit Thallus"; Width = 520; Height = 500; MinimizeBox = false; MaximizeBox = false;
                FormBorderStyle = FormBorderStyle.FixedDialog; StartPosition = FormStartPosition.CenterParent; ShowInTaskbar = false;
                Font = SystemFonts.MessageBoxFont; Padding = new Padding(14);
                Label nameLabel = LabelFor("Name", 12); nameBox = new TextBox { Text = group.NickName ?? "Thallus", Dock = DockStyle.Fill };
                Label descriptionLabel = LabelFor("Description", 12);
                descriptionBox = new TextBox
                {
                    Text = group.ThallusDescription ?? "", Dock = DockStyle.Fill, Multiline = true,
                    AcceptsReturn = true, ScrollBars = ScrollBars.Vertical
                };
                Label propertyLabel = LabelFor("Properties (one key = value per line)", 12);
                propertiesBox = new TextBox
                {
                    Text = String.Join(Environment.NewLine, group.ThallusProperties.Select(p => p.Key + " = " + p.Value).ToArray()),
                    Dock = DockStyle.Fill, Multiline = true, AcceptsReturn = true, ScrollBars = ScrollBars.Vertical
                };
                Label help = new Label
                {
                    Text = "Purpose, role, stage, and discipline can help Lichen infer the Markdown workflow. All properties remain clearly labeled as user-provided.",
                    Dock = DockStyle.Fill, AutoSize = true, MaximumSize = new Size(460, 0), ForeColor = Color.DimGray
                };
                FlowLayoutPanel buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false };
                Button ok = new Button { Text = "Save", DialogResult = DialogResult.OK, AutoSize = true };
                Button cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
                buttons.Controls.Add(ok); buttons.Controls.Add(cancel); AcceptButton = ok; CancelButton = cancel;
                TableLayoutPanel layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 8 };
                layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30F));
                layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); layout.RowStyles.Add(new RowStyle(SizeType.Percent, 45F));
                layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); layout.RowStyles.Add(new RowStyle(SizeType.Percent, 35F));
                layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38F));
                layout.Controls.Add(nameLabel); layout.Controls.Add(nameBox); layout.Controls.Add(descriptionLabel); layout.Controls.Add(descriptionBox);
                layout.Controls.Add(propertyLabel); layout.Controls.Add(propertiesBox); layout.Controls.Add(help); layout.Controls.Add(buttons);
                Controls.Add(layout);
            }

            internal string ThallusName { get { return nameBox.Text; } }
            internal string ThallusDescription { get { return descriptionBox.Text; } }
            internal IEnumerable<ContextMetadataEntry> Properties
            {
                get
                {
                    List<ContextMetadataEntry> values = new List<ContextMetadataEntry>();
                    foreach (string line in (propertiesBox.Text ?? "").Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        int split = line.IndexOf('=');
                        string key = split < 0 ? line.Trim() : line.Substring(0, split).Trim();
                        string value = split < 0 ? "" : line.Substring(split + 1).Trim();
                        if (!String.IsNullOrWhiteSpace(key)) values.Add(new ContextMetadataEntry { Key = key, Value = value });
                    }
                    return values;
                }
            }

            private static Label LabelFor(string text, int paddingTop)
            {
                return new Label { Text = text, Dock = DockStyle.Fill, AutoSize = true, Padding = new Padding(0, paddingTop, 0, 3) };
            }
        }
    }
}
