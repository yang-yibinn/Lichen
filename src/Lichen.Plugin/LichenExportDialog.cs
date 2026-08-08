using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using Grasshopper;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Special;
using Lichen.Adapters;
using Lichen.Core;

namespace Lichen.Plugin
{
    public sealed class LichenExportDialog : Form
    {
        private static readonly GH_SettingsServer PurposeSettings = new GH_SettingsServer("Lichen");

        private readonly ComboBox scope = new ComboBox();
        private readonly ComboBox detail = new ComboBox();
        private readonly TextBox purpose = new TextBox();
        private readonly TextBox task = new TextBox();
        private readonly TextBox constraints = new TextBox();
        private readonly DataGridView clusterPurposes = new DataGridView();
        private readonly CheckBox scripts = new CheckBox();
        private readonly CheckBox runtime = new CheckBox();
        private readonly CheckBox jsonAppendix = new CheckBox();
        private readonly Label exactWarning = new Label();
        private readonly Label status = new Label();
        private readonly ToolTip help = new ToolTip();
        private readonly Dictionary<string, string> savedPurposeValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private bool exactSelected;
        private bool appendixBeforeExact;
        private readonly string initialRootId;

        public LichenExportDialog() : this(null)
        {
        }

        public LichenExportDialog(string rootObjectId)
        {
            initialRootId = rootObjectId ?? "";
            Text = "Lichen — Export Grasshopper Context"; StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false; MaximizeBox = false; ShowInTaskbar = false; FormBorderStyle = FormBorderStyle.FixedDialog;
            AutoScaleMode = AutoScaleMode.Dpi; ClientSize = new Size(610, 710); Font = SystemFonts.MessageBoxFont;
            Icon = LichenInfo.CreateDialogIcon(); ShowIcon = Icon != null;
            help.AutoPopDelay = 12000; help.InitialDelay = 450; help.ReshowDelay = 100; help.ShowAlways = true;
            BuildLayout();
            FormClosing += delegate { PersistClusterPurposes(true); };
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) help.Dispose();
            base.Dispose(disposing);
        }

        private void BuildLayout()
        {
            int left = 18, labelWidth = 145, fieldLeft = 170, fieldWidth = 418, top = 18;

            Label scopeLabel = AddLabel("Scope", left, top, labelWidth);
            scope.SetBounds(fieldLeft, top - 3, fieldWidth, 26); scope.DropDownStyle = ComboBoxStyle.DropDownList;
            scope.Items.AddRange(new object[] { new ScopeChoice("Selected objects only", ScopeMode.SelectedOnly), new ScopeChoice("Selected + immediate upstream", ScopeMode.SelectedPlusImmediateUpstream), new ScopeChoice("Selected + all upstream", ScopeMode.SelectedPlusAllUpstream), new ScopeChoice("Entire document", ScopeMode.EntireDocument) });
            AddExportRootChoices(); scope.SelectedIndex = InitialScopeIndex(); Controls.Add(scope);
            SetHelp("Chooses selected, document, or persistent Export Root scope. Root scopes follow only wires that contribute to the marked X input and do not change canvas selection.", scopeLabel, scope);
            top += 38;

            Label detailLabel = AddLabel("Detail level", left, top, labelWidth);
            detail.SetBounds(fieldLeft, top - 3, fieldWidth, 26); detail.DropDownStyle = ComboBoxStyle.DropDownList;
            detail.Items.AddRange(new object[] { new DetailChoice("Brief", DetailLevel.Brief), new DetailChoice("Technical", DetailLevel.Technical), new DetailChoice("Exact", DetailLevel.Exact) }); detail.SelectedIndex = 1; Controls.Add(detail);
            SetHelp("Brief summarizes the workflow, Technical adds useful implementation detail, and Exact includes every captured connection, stable ID, runtime count, and the full JSON appendix.", detailLabel, detail);
            top += 35;

            exactWarning.SetBounds(fieldLeft, top - 2, fieldWidth, 34); exactWarning.ForeColor = Color.DarkGoldenrod; exactWarning.AutoSize = false;
            exactWarning.Text = "Warning: Exact includes every connection and the full JSON appendix. Large definitions can produce very large Markdown.";
            Controls.Add(exactWarning); SetHelp("Use Exact when complete machine-readable wiring is required. Technical is usually more compact for routine AI handoff.", exactWarning);
            top += 40;

            Label purposeLabel = AddLabel("Purpose", left, top, labelWidth);
            purpose.SetBounds(fieldLeft, top - 3, fieldWidth, 62); purpose.Multiline = true; purpose.ScrollBars = ScrollBars.Vertical; Controls.Add(purpose);
            SetHelp("Optional description of what the selected workflow is intended to accomplish. Lichen labels this as user-provided rather than inferred.", purposeLabel, purpose); top += 72;

            Label taskLabel = AddLabel("Requested task", left, top, labelWidth);
            task.SetBounds(fieldLeft, top - 3, fieldWidth, 62); task.Multiline = true; task.ScrollBars = ScrollBars.Vertical; Controls.Add(task);
            SetHelp("Optional instructions for the person or coding agent receiving the export, such as what to review, explain, or change.", taskLabel, task); top += 72;

            Label constraintsLabel = AddLabel("Constraints", left, top, labelWidth);
            constraints.SetBounds(fieldLeft, top - 3, fieldWidth, 62); constraints.Multiline = true; constraints.ScrollBars = ScrollBars.Vertical; Controls.Add(constraints);
            SetHelp("Optional requirements or boundaries the recipient should preserve, such as plugins, tolerances, performance limits, or read-only rules.", constraintsLabel, constraints); top += 75;

            Label clustersLabel = AddLabel("Clusters", left, top, labelWidth);
            clusterPurposes.SetBounds(fieldLeft, top - 3, fieldWidth, 140);
            clusterPurposes.AllowUserToAddRows = false; clusterPurposes.AllowUserToDeleteRows = false; clusterPurposes.AllowUserToResizeRows = false; clusterPurposes.RowHeadersVisible = false;
            clusterPurposes.AutoGenerateColumns = false; clusterPurposes.SelectionMode = DataGridViewSelectionMode.CellSelect; clusterPurposes.MultiSelect = false; clusterPurposes.ShowCellToolTips = true;
            DataGridViewTextBoxColumn clusterColumn = new DataGridViewTextBoxColumn(); clusterColumn.HeaderText = "Cluster definition"; clusterColumn.ReadOnly = true; clusterColumn.FillWeight = 44F;
            DataGridViewTextBoxColumn purposeColumn = new DataGridViewTextBoxColumn(); purposeColumn.HeaderText = "Purpose"; purposeColumn.FillWeight = 56F;
            clusterPurposes.Columns.Add(clusterColumn); clusterPurposes.Columns.Add(purposeColumn); clusterPurposes.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            clusterPurposes.CellEndEdit += delegate { PersistClusterPurposes(false); };
            Controls.Add(clusterPurposes); PopulateClusterPurposes();
            SetHelp("Optional purpose notes for cluster definitions. Copies of the same cluster share one entry. Notes are stored locally and restored in later Lichen and Grasshopper sessions.", clustersLabel, clusterPurposes); top += 150;

            scripts.Text = "Include safely accessible script source"; scripts.Checked = true; scripts.SetBounds(fieldLeft, top, fieldWidth, 24); Controls.Add(scripts);
            SetHelp("Includes source only when Grasshopper exposes it through a supported safe API. Lichen never executes or compiles the source.", scripts); top += 28;
            runtime.Text = "Include bounded runtime summary"; runtime.Checked = true; runtime.SetBounds(fieldLeft, top, fieldWidth, 24); Controls.Add(runtime);
            SetHelp("Includes bounded summaries of already-computed data and existing runtime messages without forcing a new Grasshopper solution.", runtime); top += 28;
            jsonAppendix.Text = "Include exact JSON appendix in Markdown"; jsonAppendix.SetBounds(fieldLeft, top, fieldWidth, 24); Controls.Add(jsonAppendix);
            SetHelp("Appends the complete machine-readable JSON graph to Markdown. This is always enabled at Exact detail and can substantially increase export size.", jsonAppendix); top += 40;

            TableLayoutPanel actionRow = new TableLayoutPanel(); actionRow.SetBounds(left, top, 570, 36); actionRow.ColumnCount = 4; actionRow.RowCount = 1; actionRow.Margin = Padding.Empty; actionRow.Padding = Padding.Empty;
            actionRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 28F)); actionRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30F)); actionRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 24F)); actionRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 18F));
            Button copy = MakeButton("Copy Markdown"); copy.Margin = new Padding(0, 0, 8, 0); copy.Click += delegate { RunAction(CopyMarkdown); }; SetHelp("Builds the selected export and copies its Markdown to the Windows clipboard.", copy);
            Button saveMarkdown = MakeButton("Save Markdown…"); saveMarkdown.Margin = new Padding(0, 0, 8, 0); saveMarkdown.Click += delegate { RunAction(SaveMarkdown); }; SetHelp("Builds the selected export and saves its Markdown to a file you choose.", saveMarkdown);
            Button saveJson = MakeButton("Save JSON…"); saveJson.Margin = new Padding(0, 0, 8, 0); saveJson.Click += delegate { RunAction(SaveJson); }; SetHelp("Builds the selected export and saves the complete machine-readable JSON graph.", saveJson);
            Button close = MakeButton("Close"); close.Margin = Padding.Empty; close.DialogResult = DialogResult.Cancel; CancelButton = close; SetHelp("Saves cluster purpose notes locally and closes this dialog.", close);
            actionRow.Controls.Add(copy, 0, 0); actionRow.Controls.Add(saveMarkdown, 1, 0); actionRow.Controls.Add(saveJson, 2, 0); actionRow.Controls.Add(close, 3, 0); Controls.Add(actionRow);
            top += 48;
            status.SetBounds(left, top, 570, 60); status.AutoEllipsis = true; status.ForeColor = SystemColors.GrayText; status.Text = "Lichen reads the current graph only. It does not solve or modify the Grasshopper document."; Controls.Add(status);
            SetHelp("Shows export counts, elapsed time, success messages, or a clear explanation if an action cannot be completed.", status);

            detail.SelectedIndexChanged += delegate { UpdateDetailState(); };
            UpdateDetailState();
        }

        private void UpdateDetailState()
        {
            DetailChoice choice = detail.SelectedItem as DetailChoice;
            bool isExact = choice != null && choice.Level == DetailLevel.Exact;
            if (isExact && !exactSelected)
            {
                appendixBeforeExact = jsonAppendix.Checked;
                jsonAppendix.Checked = true; jsonAppendix.Enabled = false;
            }
            else if (!isExact && exactSelected)
            {
                jsonAppendix.Enabled = true; jsonAppendix.Checked = appendixBeforeExact;
            }
            exactSelected = isExact; exactWarning.Visible = isExact;
        }

        private void RunAction(Action action)
        {
            try { UseWaitCursor = true; Enabled = false; status.ForeColor = SystemColors.GrayText; action(); }
            catch (Exception ex) { status.ForeColor = Color.Firebrick; status.Text = ex.Message; MessageBox.Show(this, ex.Message, "Lichen", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
            finally { Enabled = true; UseWaitCursor = false; }
        }

        private ContextExportPackage Export()
        {
            Stopwatch watch = Stopwatch.StartNew();
            GH_Document document = Instances.ActiveDocument;
            if (document == null) throw new InvalidOperationException("No active Grasshopper document was found.");
            ContextExportOptions options = Options();
            List<string> before = SelectedIds(document);
            if (options.ScopeMode != ScopeMode.EntireDocument && options.ScopeMode != ScopeMode.ExportRoot && before.Count == 0) throw new InvalidOperationException("Select one or more Grasshopper objects before exporting this scope.");
            ContextSnapshot snapshot = new GrasshopperGraphExtractor().Capture(document, options.IncludeScriptSource, options.IncludeRuntimeSummary, options.MaximumNodes, options);
            List<string> after = SelectedIds(document);
            if (!before.SequenceEqual(after, StringComparer.OrdinalIgnoreCase)) throw new InvalidOperationException("The Grasshopper selection changed during export. Please try again.");
            ContextExportPackage package = new ContextExporter().Export(snapshot, options);
            watch.Stop();
            status.ForeColor = Color.DarkGreen;
            status.Text = "Exported " + package.Document.Nodes.Count + " objects, " + package.Document.Edges.Count + " connections, " + package.Document.BoundaryInputs.Count + " boundary inputs, and " + package.Document.BoundaryOutputs.Count + " boundary outputs in " + watch.ElapsedMilliseconds + " ms.\n"
                + ExportSize("Markdown", package.Markdown) + "; " + ExportSize("JSON", package.Json) + ".";
            return package;
        }

        private void CopyMarkdown()
        {
            ContextExportPackage package = Export();
            try { Clipboard.SetText(package.Markdown); status.Text += " Markdown copied to the clipboard."; }
            catch (Exception ex) { throw new InvalidOperationException("The export succeeded, but the clipboard could not be updated: " + ex.Message, ex); }
        }

        private void SaveMarkdown()
        {
            ContextExportPackage package = Export();
            using (SaveFileDialog dialog = new SaveFileDialog())
            {
                dialog.Title = "Save Lichen Markdown"; dialog.Filter = "Markdown files (*.md)|*.md|All files (*.*)|*.*"; dialog.DefaultExt = "md"; dialog.FileName = "lichen-context.md";
                if (dialog.ShowDialog(this) == DialogResult.OK) { WriteFile(dialog.FileName, package.Markdown); status.Text += " Markdown saved."; }
            }
        }

        private void SaveJson()
        {
            ContextExportPackage package = Export();
            using (SaveFileDialog dialog = new SaveFileDialog())
            {
                dialog.Title = "Save Lichen JSON"; dialog.Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*"; dialog.DefaultExt = "json"; dialog.FileName = "lichen-context.json";
                if (dialog.ShowDialog(this) == DialogResult.OK) { WriteFile(dialog.FileName, package.Json); status.Text += " JSON saved."; }
            }
        }

        private ContextExportOptions Options()
        {
            clusterPurposes.EndEdit(); PersistClusterPurposes(true);
            ScopeChoice scopeChoice = (ScopeChoice)scope.SelectedItem; DetailChoice detailChoice = (DetailChoice)detail.SelectedItem;
            ContextExportOptions options = new ContextExportOptions { ScopeMode = scopeChoice.Mode, RootObjectId = scopeChoice.RootObjectId, RootLabel = scopeChoice.RootLabel, DetailLevel = detailChoice.Level, Purpose = purpose.Text, RequestedTask = task.Text, Constraints = constraints.Text, IncludeScriptSource = scripts.Checked, IncludeRuntimeSummary = runtime.Checked, IncludeJsonAppendix = jsonAppendix.Checked || detailChoice.Level == DetailLevel.Exact, MaximumNodes = 500, ExporterVersion = LichenInfo.CurrentVersion };
            foreach (DataGridViewRow row in clusterPurposes.Rows)
            {
                ClusterPurposeRow item = row.Tag as ClusterPurposeRow; string value = Convert.ToString(row.Cells[1].Value);
                if (item == null || String.IsNullOrWhiteSpace(value)) continue;
                foreach (string id in item.InstanceIds) options.ClusterPurposeNotes[id] = value.Trim();
            }
            return options;
        }

        private static string ExportSize(string label, string content)
        {
            string value = content ?? "";
            return label + ": " + value.Length.ToString("N0", System.Globalization.CultureInfo.InvariantCulture) + " chars / "
                + Encoding.UTF8.GetByteCount(value).ToString("N0", System.Globalization.CultureInfo.InvariantCulture) + " UTF-8 bytes";
        }

        private void AddExportRootChoices()
        {
            GH_Document document = Instances.ActiveDocument;
            if (document == null) return;
            List<ExportRootDefinition> roots = new GrasshopperExportRootAdapter().FindRoots(document);
            foreach (ExportRootDefinition root in roots)
            {
                bool duplicateLabel = roots.Count(r => String.Equals(r.Label, root.Label, StringComparison.OrdinalIgnoreCase)) > 1;
                string label = "Export Root: " + root.Label;
                if (duplicateLabel) label += " [" + root.ObjectId.Substring(0, 8) + "]";
                scope.Items.Add(new ScopeChoice(label, ScopeMode.ExportRoot, root.ObjectId, root.Label));
            }
        }

        private int InitialScopeIndex()
        {
            if (!String.IsNullOrWhiteSpace(initialRootId))
            {
                for (int i = 0; i < scope.Items.Count; i++)
                {
                    ScopeChoice choice = scope.Items[i] as ScopeChoice;
                    if (choice != null && String.Equals(choice.RootObjectId, initialRootId, StringComparison.OrdinalIgnoreCase)) return i;
                }
            }
            return 0;
        }

        private void PopulateClusterPurposes()
        {
            GH_Document document = Instances.ActiveDocument;
            if (document == null) return;
            HashSet<string> selected = new HashSet<string>(SelectedIds(document), StringComparer.OrdinalIgnoreCase);
            Dictionary<string, ClusterPurposeRow> grouped = new Dictionary<string, ClusterPurposeRow>(StringComparer.OrdinalIgnoreCase);
            foreach (GH_Cluster cluster in document.Objects.OfType<GH_Cluster>().OrderBy(c => c.InstanceGuid))
            {
                Guid definitionId = SafeDocumentId(cluster);
                string definitionKey = definitionId == Guid.Empty ? "instance:" + cluster.InstanceGuid.ToString("N") : "document:" + definitionId.ToString("N");
                ClusterPurposeRow item;
                if (!grouped.TryGetValue(definitionKey, out item))
                {
                    item = new ClusterPurposeRow { DefinitionId = definitionId, DefinitionKey = definitionKey, SettingsKey = "ClusterPurpose_" + definitionKey.Replace(":", "_") };
                    grouped.Add(definitionKey, item);
                }
                item.Instances.Add(cluster); item.InstanceIds.Add(cluster.InstanceGuid.ToString("D").ToLowerInvariant());
                if (selected.Contains(cluster.InstanceGuid.ToString("D"))) item.SelectedCount++;
            }

            List<ClusterPurposeRow> items = grouped.Values.ToList();
            foreach (ClusterPurposeRow item in items)
            {
                item.Name = ClusterName(item.Instances.OrderBy(c => c.InstanceGuid).First());
                item.Protection = ClusterProtection(item.Instances);
                item.InstanceCount = SafeInstanceCount(document, item);
                string stored = PurposeSettings.GetValue(item.SettingsKey, "") ?? "";
                item.Purpose = stored; savedPurposeValues[item.SettingsKey] = stored;
            }
            foreach (ClusterPurposeRow item in items)
            {
                bool collides = items.Count(other => String.Equals(other.Name, item.Name, StringComparison.OrdinalIgnoreCase)) > 1;
                string identity = item.DefinitionId == Guid.Empty ? item.Instances[0].InstanceGuid.ToString("N") : item.DefinitionId.ToString("N");
                item.DisplayName = collides ? item.Name + " [" + identity.Substring(0, 8) + "]" : item.Name;
            }
            items = items.OrderByDescending(i => i.SelectedCount > 0).ThenBy(i => i.Name, StringComparer.OrdinalIgnoreCase).ThenBy(i => i.DefinitionKey, StringComparer.OrdinalIgnoreCase).ToList();

            AddClusterSection("Inside current selection", items.Where(i => i.SelectedCount > 0).ToList());
            AddClusterSection("Rest of document", items.Where(i => i.SelectedCount == 0).ToList());
        }

        private void AddClusterSection(string title, List<ClusterPurposeRow> items)
        {
            int headerIndex = clusterPurposes.Rows.Add(title + " (" + items.Count + ")", "");
            DataGridViewRow header = clusterPurposes.Rows[headerIndex]; header.ReadOnly = true; header.Tag = null;
            header.DefaultCellStyle.BackColor = SystemColors.Control; header.DefaultCellStyle.ForeColor = SystemColors.ControlText;
            header.DefaultCellStyle.Font = new Font(clusterPurposes.Font, FontStyle.Bold); header.Cells[0].ToolTipText = title + " cluster definitions."; header.Cells[1].ToolTipText = "";
            foreach (ClusterPurposeRow item in items)
            {
                string count = item.InstanceCount == 1 ? "1 instance" : item.InstanceCount + " instances";
                if (item.SelectedCount > 0 && item.InstanceCount > 1) count += "; " + item.SelectedCount + " selected";
                string label = item.DisplayName + item.Protection + " — " + count;
                int rowIndex = clusterPurposes.Rows.Add(label, item.Purpose);
                DataGridViewRow row = clusterPurposes.Rows[rowIndex]; row.Tag = item;
                string identity = item.DefinitionId == Guid.Empty ? "This cluster could not expose a shared definition ID, so its purpose is stored for this instance only." : "Copies share cluster document ID " + item.DefinitionId.ToString("D") + " and therefore share this purpose.";
                row.Cells[0].ToolTipText = identity; row.Cells[1].ToolTipText = "Optional user-provided purpose. Stored locally in %AppData%\\Grasshopper\\Lichen.xml.";
            }
        }

        private void PersistClusterPurposes(bool showError)
        {
            try
            {
                clusterPurposes.EndEdit(); bool changed = false;
                foreach (DataGridViewRow row in clusterPurposes.Rows)
                {
                    ClusterPurposeRow item = row.Tag as ClusterPurposeRow;
                    if (item == null) continue;
                    string value = Convert.ToString(row.Cells[1].Value) ?? ""; value = value.Trim();
                    string previous;
                    if (savedPurposeValues.TryGetValue(item.SettingsKey, out previous) && String.Equals(previous, value, StringComparison.Ordinal)) continue;
                    PurposeSettings.SetValue(item.SettingsKey, value); savedPurposeValues[item.SettingsKey] = value; changed = true;
                }
                if (changed) PurposeSettings.WritePersistentSettings();
            }
            catch (Exception ex)
            {
                if (showError) MessageBox.Show(this, "Cluster purposes could not be saved for the next session: " + ex.Message, "Lichen", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                else { status.ForeColor = Color.DarkGoldenrod; status.Text = "Cluster purposes could not be saved for the next session: " + ex.Message; }
            }
        }

        private static Guid SafeDocumentId(GH_Cluster cluster) { try { return cluster.DocumentId; } catch { return Guid.Empty; } }
        private static string ClusterName(GH_Cluster cluster) { return String.IsNullOrWhiteSpace(cluster.NickName) ? (cluster.Name ?? "Cluster") : cluster.NickName; }
        private static string ClusterProtection(IEnumerable<GH_Cluster> clusters)
        {
            try
            {
                List<GH_ClusterProtection> values = clusters.Select(c => c.ProtectionLevel).Distinct().ToList();
                if (values.Count > 1) return " (mixed protection)";
                return values.Count == 1 && values[0] != GH_ClusterProtection.Unprotected ? " (protected)" : "";
            }
            catch { return " (protection unknown)"; }
        }
        private static int SafeInstanceCount(GH_Document document, ClusterPurposeRow item)
        {
            if (item.DefinitionId == Guid.Empty) return item.Instances.Count;
            try { return Math.Max(item.Instances.Count, document.ClusterInstanceCount(item.DefinitionId)); }
            catch { return item.Instances.Count; }
        }
        private static List<string> SelectedIds(GH_Document document) { return document.SelectedObjects().Select(o => o.InstanceGuid.ToString("D")).OrderBy(s => s, StringComparer.OrdinalIgnoreCase).ToList(); }
        private static void WriteFile(string path, string content) { try { File.WriteAllText(path, content, new System.Text.UTF8Encoding(false)); } catch (Exception ex) { throw new InvalidOperationException("The file could not be written: " + ex.Message, ex); } }
        private Label AddLabel(string text, int x, int y, int width) { Label label = new Label(); label.Text = text; label.SetBounds(x, y, width, 22); Controls.Add(label); return label; }
        private void SetHelp(string text, params Control[] controls) { foreach (Control control in controls) if (control != null) help.SetToolTip(control, text); }
        private static Button MakeButton(string text) { Button button = new Button(); button.Text = text; button.Dock = DockStyle.Fill; button.AutoEllipsis = false; return button; }

        private sealed class ScopeChoice
        {
            public ScopeChoice(string label, ScopeMode mode) : this(label, mode, "", "") { }
            public ScopeChoice(string label, ScopeMode mode, string rootObjectId, string rootLabel) { Label = label; Mode = mode; RootObjectId = rootObjectId ?? ""; RootLabel = rootLabel ?? ""; }
            public string Label; public ScopeMode Mode; public string RootObjectId; public string RootLabel;
            public override string ToString() { return Label; }
        }
        private sealed class DetailChoice { public DetailChoice(string label, DetailLevel level) { Label = label; Level = level; } public string Label; public DetailLevel Level; public override string ToString() { return Label; } }
        private sealed class ClusterPurposeRow
        {
            public ClusterPurposeRow() { Instances = new List<GH_Cluster>(); InstanceIds = new List<string>(); Name = "Cluster"; DisplayName = "Cluster"; Protection = ""; Purpose = ""; DefinitionKey = ""; SettingsKey = ""; }
            public Guid DefinitionId; public string DefinitionKey; public string SettingsKey; public string Name; public string DisplayName; public string Protection; public string Purpose;
            public int InstanceCount; public int SelectedCount; public List<GH_Cluster> Instances; public List<string> InstanceIds;
        }
    }
}
