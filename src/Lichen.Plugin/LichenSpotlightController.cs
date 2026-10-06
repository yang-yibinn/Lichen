using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Grasshopper;
using Grasshopper.GUI.Canvas;
using Grasshopper.Kernel;
using Lichen.Core;

namespace Lichen.Plugin
{
    // One binding per live canvas; weak, session-only document identity state. No document writes.
    internal sealed class LichenSpotlightController
    {
        private SpotlightSessions<GH_Document> sessions = new SpotlightSessions<GH_Document>();
        private readonly Dictionary<GH_Canvas, SpotlightCanvasBinding> bindings = new Dictionary<GH_Canvas, SpotlightCanvasBinding>();
        private GH_DocumentServer server;

        internal void Attach(GH_Canvas canvas)
        {
            if (canvas == null || canvas.IsDisposed || bindings.ContainsKey(canvas)) return;
            SpotlightCanvasBinding binding = null;
            try
            {
                if (server == null) { server = Instances.DocumentServer; server.DocumentRemoved += DocumentRemoved; }
                binding = new SpotlightCanvasBinding(this, canvas);
                bindings.Add(canvas, binding);
            }
            catch { if (binding != null) binding.Dispose(); Detach(canvas); }
        }
        internal void Detach(GH_Canvas canvas)
        {
            SpotlightCanvasBinding binding;
            if (canvas != null && bindings.TryGetValue(canvas, out binding)) { bindings.Remove(canvas); try { binding.Dispose(); } catch { } }
            if (bindings.Count == 0)
            {
                if (server != null) { server.DocumentRemoved -= DocumentRemoved; server = null; }
                sessions = new SpotlightSessions<GH_Document>();
            }
        }
        internal SpotlightViewState State(GH_Canvas canvas)
        {
            return canvas == null || canvas.IsDisposed || canvas.Document == null || canvas.Document.Nested ? null : sessions.For(canvas.Document);
        }
        internal bool IsEnabled(GH_Canvas canvas) { SpotlightViewState state = State(canvas); return state != null && state.Enabled; }
        internal void Toggle(GH_Canvas canvas)
        {
            try { Attach(canvas); SpotlightViewState state = State(canvas); if (state != null) { state.Toggle(); Refresh(); } } catch { }
        }
        internal void Refresh()
        {
            foreach (SpotlightCanvasBinding binding in bindings.Values.ToArray()) { try { binding.Refresh(); } catch { } }
        }
        private void DocumentRemoved(GH_DocumentServer sender, GH_Document document)
        {
            if (document == null) return;
            sessions.For(document).SetEnabled(false);
            sessions.Forget(document);
            foreach (SpotlightCanvasBinding binding in bindings.Values.ToArray()) { try { binding.DocumentClosed(); } catch { } }
        }
    }

    internal sealed class SpotlightCanvasBinding : IDisposable, IMessageFilter
    {
        private readonly LichenSpotlightController controller;
        private readonly GH_Canvas canvas;
        private readonly SpotlightLegendPanel legend;
        private readonly SpotlightMenuLifetime menuLifetime;
        private bool disposed, pendingRightClick, nativeMenuSeen, waitingForIdle;
        private Point rightDown;
        private WeakReference rightDocument;

        internal SpotlightCanvasBinding(LichenSpotlightController controller, GH_Canvas canvas)
        {
            this.controller = controller; this.canvas = canvas;
            menuLifetime = new SpotlightMenuLifetime(action => canvas.BeginInvoke(action));
            legend = new SpotlightLegendPanel(OpenSettings, Collapse, TurnOff);
            try
            {
                canvas.Controls.Add(legend);
                canvas.CanvasPostPaintOverlay += Paint;
                canvas.DocumentChanged += DocumentChanged;
                canvas.Resize += Resized;
                canvas.MouseDown += MouseDown;
                canvas.MouseMove += MouseMove;
                canvas.MouseUp += MouseUp;
                canvas.Disposed += CanvasDisposed;
                Application.AddMessageFilter(this);
            }
            catch { Dispose(); throw; }
        }
        internal void Refresh()
        {
            if (disposed || canvas.IsDisposed) return;
            if (canvas.InvokeRequired) { try { canvas.BeginInvoke(new Action(Refresh)); } catch { } return; }
            SpotlightViewState state = controller.State(canvas);
            if (state == null || !state.Enabled) legend.Visible = false;
            canvas.Invalidate();
        }
        internal void DocumentClosed()
        {
            if (disposed || canvas.IsDisposed) return;
            if (canvas.InvokeRequired) { try { canvas.BeginInvoke(new Action(DocumentClosed)); } catch { } return; }
            CloseMenus(); CancelPendingMenu(); legend.Visible = false; Refresh();
        }
        private void DocumentChanged(GH_Canvas sender, GH_CanvasDocumentChangedEventArgs args) { DocumentClosed(); }
        private void Resized(object sender, EventArgs args) { legend.Place(canvas.ClientSize); }
        private void CanvasDisposed(object sender, EventArgs args) { controller.Detach(canvas); }
        private void Paint(GH_Canvas sender)
        {
            if (disposed || canvas.DrawingMode != GH_CanvasMode.Control) return;
            SpotlightViewState state = controller.State(canvas);
            if (state == null || !state.Enabled) { legend.Visible = false; return; }
            try
            {
                SpotlightSelection selection = LichenSpotlightDrawing.Inspect(canvas.Document, state);
                LichenSpotlightDrawing.DrawOverlay(canvas, state, selection);
                legend.UpdateView(state, selection, canvas.ClientSize, false);
            }
            catch { try { legend.UpdateView(state, new SpotlightSelection(), canvas.ClientSize, true); } catch { legend.Visible = false; } }
        }
        private void TurnOff() { SpotlightViewState state = controller.State(canvas); if (state != null) state.SetEnabled(false); CloseMenus(); controller.Refresh(); }
        private void Collapse() { SpotlightViewState state = controller.State(canvas); if (state != null) { state.Collapsed = !state.Collapsed; controller.Refresh(); } }

        private bool OpenSettings(ToolStripDropDown menu)
        {
            try
            {
                SpotlightViewState state = controller.State(canvas);
                if (disposed || state == null || !state.Enabled) return false;
                PopulateSettings(menu, state);
                return true;
            }
            catch { return false; }
        }
        private void PopulateSettings(ToolStripDropDown menu, SpotlightViewState state)
        {
            AddChoice(menu.Items, "Show third-party components", state.ShowThirdParty, delegate { state.ShowThirdParty = !state.ShowThirdParty; }, state);
            AddChoice(menu.Items, "Show script components", state.ShowScripts, delegate { state.ShowScripts = !state.ShowScripts; }, state);
            menu.Items.Add(new ToolStripSeparator());
            try
            {
                SpotlightSelection selection = LichenSpotlightDrawing.Inspect(canvas.Document, state);
                ToolStripMenuItem assemblies = new ToolStripMenuItem("Focus third-party assembly") { Enabled = state.ShowThirdParty };
                AddChoice(assemblies.DropDownItems, "All assemblies", state.AssemblyFocus.Length == 0, delegate { state.AssemblyFocus = ""; }, state);
                foreach (string name in selection.AvailableTargets.Where(t => t.ThirdParty).Select(t => t.AssemblyName).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(n => n, StringComparer.OrdinalIgnoreCase))
                {
                    string value = name;
                    AddChoice(assemblies.DropDownItems, name, String.Equals(name, state.AssemblyFocus, StringComparison.OrdinalIgnoreCase), delegate { state.AssemblyFocus = value; }, state);
                }
                menu.Items.Add(assemblies);
                ToolStripMenuItem scripts = new ToolStripMenuItem("Focus script language") { Enabled = state.ShowScripts };
                AddChoice(scripts.DropDownItems, "All script languages", state.ScriptFocus.Length == 0, delegate { state.ScriptFocus = ""; }, state);
                foreach (string name in selection.AvailableTargets.Where(t => t.Script).Select(t => t.ScriptLanguage).Distinct().OrderBy(n => n, StringComparer.Ordinal))
                {
                    string value = name;
                    AddChoice(scripts.DropDownItems, name, state.ScriptFocus == name, delegate { state.ScriptFocus = value; }, state);
                }
                menu.Items.Add(scripts);
            }
            catch { menu.Items.Add(new ToolStripMenuItem("Inspection unavailable") { Enabled = false }); }
            menu.Items.Add(new ToolStripSeparator());
            AddChoice(menu.Items, "Reset view", false, state.ResetView, state);
            menu.Items.Add(new ToolStripMenuItem("Top-level only; no cluster contents or script imports") { Enabled = false });
        }
        private void AddChoice(ToolStripItemCollection items, string text, bool selected, Action action, SpotlightViewState expected)
        {
            SpotlightMenuChoices.Add(items, text, selected,
                delegate { return !disposed && Object.ReferenceEquals(controller.State(canvas), expected) && expected.Enabled; }, action, controller.Refresh);
        }

        // Observe native WinForms dropdowns for this one empty-canvas right click. Never consume input.
        // If the native legacy menu is disabled, use GH's public menu factory as the fallback.
        private void MouseDown(object sender, MouseEventArgs args)
        {
            CancelPendingMenu();
            if (args.Button != MouseButtons.Right || controller.State(canvas) == null || Control.ModifierKeys != Keys.None) return;
            try
            {
                if (canvas.Document.FindAttribute(canvas.CursorCanvasPosition, false) != null) return;
                rightDown = args.Location; rightDocument = new WeakReference(canvas.Document);
                pendingRightClick = true; nativeMenuSeen = false;
            }
            catch { CancelPendingMenu(); }
        }
        private void MouseMove(object sender, MouseEventArgs args)
        {
            if (pendingRightClick && (Math.Abs(args.X - rightDown.X) > SystemInformation.DragSize.Width / 2 || Math.Abs(args.Y - rightDown.Y) > SystemInformation.DragSize.Height / 2)) CancelPendingMenu();
        }
        private void MouseUp(object sender, MouseEventArgs args)
        {
            MouseMove(sender, args);
            if (args.Button != MouseButtons.Right || !pendingRightClick || waitingForIdle) return;
            waitingForIdle = true; Application.Idle += FinishRightClick;
        }
        public bool PreFilterMessage(ref Message message)
        {
            if (disposed || !pendingRightClick || nativeMenuSeen || message.Msg != 0x000F) return false;
            try
            {
                ToolStripDropDown menu = Control.FromHandle(message.HWnd) as ToolStripDropDown;
                if (menu != null && menu.Visible && menu.OwnerItem == null && IsSameRightDocument())
                {
                    Point origin = canvas.PointToScreen(rightDown);
                    Rectangle vicinity = menu.Bounds; vicinity.Inflate(80, 80);
                    if (vicinity.Contains(origin)) { AppendToggle(menu); nativeMenuSeen = true; }
                }
            }
            catch { }
            return false;
        }
        private bool IsSameRightDocument() { return rightDocument != null && Object.ReferenceEquals(rightDocument.Target, canvas.Document); }
        private void FinishRightClick(object sender, EventArgs args)
        {
            bool show = pendingRightClick && !nativeMenuSeen && IsSameRightDocument() && !disposed && !canvas.IsDisposed;
            CancelPendingMenu();
            if (!show) return;
            try
            {
                if (canvas.ActiveInteraction != null) return;
                ToolStripDropDownMenu menu = canvas.CanvasOldSchoolMenu();
                if (menu == null) return;
                AppendToggle(menu); menuLifetime.Track(menu, true, null); menu.Show(canvas, rightDown);
            }
            catch { }
        }
        private void AppendToggle(ToolStripDropDown menu)
        {
            if (menu.Items.ContainsKey("LichenDependencySpotlightToggle")) return;
            ToolStripSeparator separator = new ToolStripSeparator();
            ToolStripMenuItem item = new ToolStripMenuItem("Spotlight") { Name = "LichenDependencySpotlightToggle", Checked = controller.IsEnabled(canvas), Image = LichenInfo.CreateSpotlightIcon(24) };
            WeakReference document = new WeakReference(canvas.Document);
            item.Click += delegate { if (!disposed && Object.ReferenceEquals(document.Target, canvas.Document)) controller.Toggle(canvas); };
            menu.Items.Add(separator); menu.Items.Add(item);
            menuLifetime.Track(menu, false, delegate
            {
                try
                {
                    if (!menu.IsDisposed) { menu.Items.Remove(item); menu.Items.Remove(separator); }
                    if (item.Image != null) { item.Image.Dispose(); item.Image = null; }
                    item.Dispose(); separator.Dispose();
                }
                catch { }
            });
        }
        private void CloseMenus()
        {
            legend.CloseSettings();
            menuLifetime.CloseAll();
        }
        private void CancelPendingMenu() { pendingRightClick = false; rightDocument = null; if (waitingForIdle) { Application.Idle -= FinishRightClick; waitingForIdle = false; } }
        public void Dispose()
        {
            if (disposed) return; disposed = true;
            CancelPendingMenu(); Application.RemoveMessageFilter(this); CloseMenus();
            menuLifetime.Dispose();
            canvas.CanvasPostPaintOverlay -= Paint; canvas.DocumentChanged -= DocumentChanged; canvas.Resize -= Resized;
            canvas.MouseDown -= MouseDown; canvas.MouseMove -= MouseMove; canvas.MouseUp -= MouseUp; canvas.Disposed -= CanvasDisposed;
            canvas.Controls.Remove(legend); legend.Dispose();
        }
    }
}
