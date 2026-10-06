using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using Grasshopper.GUI;
using Lichen.Core;

namespace Lichen.Plugin
{
    // A real child control owns its hit area: clicks on the panel cannot select objects underneath.
    internal sealed class SpotlightLegendPanel : Panel
    {
        private readonly ToolStrip header = new ToolStrip();
        private readonly ToolStripLabel title = new ToolStripLabel("Spotlight");
        private readonly ToolStripButton collapse = new ToolStripButton("−");
        private readonly ToolStripDropDownButton options = new ToolStripDropDownButton("Settings") { Name = "SpotlightSettings", ToolTipText = "Layers and focus" };
        private readonly Label body = new Label();
        private float scale;
        private Font ownedFont;
        private int desiredHeight;
        internal int HeaderHeight { get { return header.Height; } }

        internal SpotlightLegendPanel(Func<ToolStripDropDown, bool> settings, Action collapseView, Action close)
        {
            Visible = false; TabStop = false;
            BackColor = Color.FromArgb(250, 249, 253); ForeColor = Color.FromArgb(45, 37, 62);
            BorderStyle = BorderStyle.FixedSingle;
            header.GripStyle = ToolStripGripStyle.Hidden; header.Dock = DockStyle.Top; header.AutoSize = false;
            header.BackColor = BackColor; header.ForeColor = ForeColor; header.CanOverflow = false;
            // The ToolStrip owns this dropdown and its menu-mode lifecycle. Reuse it after
            // dismissal; rebuilding/disposal happens at the next opening, never in Closed.
            options.DropDownItems.Add(new ToolStripMenuItem("Settings unavailable") { Enabled = false });
            options.DropDown.Opening += delegate(object sender, System.ComponentModel.CancelEventArgs args)
            {
                foreach (ToolStripItem item in options.DropDownItems.Cast<ToolStripItem>().ToArray()) item.Dispose();
                options.DropDownItems.Clear();
                try { args.Cancel = !settings(options.DropDown); }
                catch { args.Cancel = true; }
                if (options.DropDownItems.Count == 0)
                    options.DropDownItems.Add(new ToolStripMenuItem("Settings unavailable") { Enabled = false });
            };
            collapse.ToolTipText = "Collapse or expand the legend"; collapse.Click += delegate { collapseView(); };
            ToolStripButton dismiss = new ToolStripButton("×") { ToolTipText = "Turn Spotlight off" };
            dismiss.Click += delegate { close(); };
            header.Items.Add(title); header.Items.Add(options); header.Items.Add(collapse); header.Items.Add(dismiss);
            body.BackColor = BackColor; body.ForeColor = ForeColor; body.AutoEllipsis = true; body.UseCompatibleTextRendering = false;
            Controls.Add(body); Controls.Add(header);
        }
        private static string Number(int n) { return n.ToString(CultureInfo.InvariantCulture); }
        internal void CloseSettings() { options.HideDropDown(); }
        internal void UpdateView(SpotlightViewState state, SpotlightSelection selection, Size viewport, bool unavailable)
        {
            float nextScale = Math.Max(1F, GH_GraphicsUtil.UiScale);
            if (nextScale != scale)
            {
                scale = nextScale;
                Font previous = ownedFont;
                ownedFont = new Font(SystemFonts.MessageBoxFont.FontFamily, 12F * scale, FontStyle.Regular, GraphicsUnit.Pixel);
                Font = ownedFont;
                if (previous != null) previous.Dispose();
                header.Font = Font; body.Font = Font;
                header.Height = (int)Math.Round(32F * scale);
            }
            List<string> lines = new List<string>();
            lines.Add(unavailable ? "Inspection unavailable" : Number(selection.Targets.Count) + " targets in inspected set");
            if (selection.LimitReached) lines.Add("PARTIAL: 500-object limit; others not inspected");
            if (selection.UnavailableCount > 0) lines.Add(Number(selection.UnavailableCount) + " object identities unavailable");
            if (!state.Collapsed)
            {
                lines.Add(Number(selection.InspectedCount) + " top-level objects inspected");
                lines.Add("Plugins: " + (state.ShowThirdParty ? (state.AssemblyFocus.Length == 0 ? "All" : state.AssemblyFocus) : "Off"));
                lines.Add("Scripts: " + (state.ShowScripts ? (state.ScriptFocus.Length == 0 ? "All" : state.ScriptFocus) : "Off"));
                List<string> categories = selection.Targets.Where(t => t.ThirdParty && state.ShowThirdParty && (state.AssemblyFocus.Length == 0 || String.Equals(t.AssemblyName, state.AssemblyFocus, StringComparison.OrdinalIgnoreCase)))
                    .GroupBy(t => t.AssemblyName, StringComparer.OrdinalIgnoreCase).OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase).Select(g => g.Key + "  ·  " + Number(g.Count())).ToList();
                if (state.ShowScripts) categories.AddRange(selection.Targets.Where(t => t.Script && (state.ScriptFocus.Length == 0 || state.ScriptFocus == t.ScriptLanguage))
                    .GroupBy(t => t.ScriptLanguage).OrderBy(g => g.Key, StringComparer.Ordinal).Select(g => g.Key + " scripts  ·  " + Number(g.Count())));
                lines.AddRange(categories.Take(5));
                if (categories.Count > 5) lines.Add("+ " + Number(categories.Count - 5) + " categories; focus in Settings");
                lines.Add("Cluster contents and script imports not inspected");
            }
            string text = String.Join(Environment.NewLine, lines);
            if (body.Text != text) body.Text = text;
            collapse.Text = state.Collapsed ? "+" : "−";
            int contentWidth = Math.Max(1, SpotlightPanelLayout.Place(viewport.Width, viewport.Height, scale, 0).Width - (int)(20 * scale));
            desiredHeight = header.Height + body.GetPreferredSize(new Size(contentWidth, 0)).Height + (int)(20 * scale);
            Place(viewport); if (!Visible) Visible = true;
            BringToFront();
        }
        internal void Place(Size viewport)
        {
            SpotlightPanelBounds bounds = SpotlightPanelLayout.Place(viewport.Width, viewport.Height, scale, desiredHeight);
            Rectangle target = new Rectangle(bounds.Left, bounds.Top, bounds.Width, bounds.Height);
            if (Bounds != target) Bounds = target;
            title.Visible = bounds.Width >= 320 * Math.Max(1, scale);
            int gap = (int)(8 * Math.Max(1, scale));
            body.SetBounds(gap, header.Height + gap, Math.Max(0, Width - gap * 2), Math.Max(0, Height - header.Height - gap * 2));
        }
        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing && ownedFont != null) ownedFont.Dispose();
        }
    }
}
