using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Lichen.Core;
using Lichen.Plugin;

namespace Grasshopper.GUI { public static class GH_GraphicsUtil { public static float UiScale = 1; } }

internal static class Program
{
    private sealed class TestMenu : ToolStripDropDownMenu
    {
        internal void NotifyClosed(ToolStripDropDownCloseReason reason) { OnClosed(new ToolStripDropDownClosedEventArgs(reason)); }
    }
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static void Flush(Queue<Action> work) { while (work.Count != 0) work.Dequeue()(); }
    [STAThread] private static int Main()
    {
        try
        {
            OldLifetimeViolation(); OwnedClick(); OutsideDismissal(); BorrowedMenu(); SettingsReopen(); StaleChoice(); Teardown(); FailedDispatch();
            Console.WriteLine("Spotlight menu checks: 8 passed; 0 failed (standalone WinForms, no Rhino/Grasshopper loaded).");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }

    private static void OldLifetimeViolation()
    {
        using (var menu = new TestMenu())
        {
            var item = new ToolStripMenuItem("Scripts"); menu.Items.Add(item);
            menu.Closed += delegate { menu.Dispose(); }; // Previous production lifetime policy.
            menu.NotifyClosed(ToolStripDropDownCloseReason.ItemClicked);
            Check(menu.IsDisposed && item.IsDisposed, "Old lifetime policy no longer disposes items during Closed");
        }
    }
    private static void OwnedClick()
    {
        var work = new Queue<Action>(); var state = new SpotlightViewState(); state.SetEnabled(true); int refreshes = 0;
        using (var lifetime = new SpotlightMenuLifetime(work.Enqueue))
        using (var menu = new TestMenu())
        {
            var item = SpotlightMenuChoices.Add(menu.Items, "Show script components", state.ShowScripts,
                () => state.Enabled, () => state.ShowScripts = !state.ShowScripts, () => refreshes++);
            lifetime.Track(menu, true, null);
            menu.NotifyClosed(ToolStripDropDownCloseReason.ItemClicked);
            Check(!menu.IsDisposed && !item.IsDisposed, "Closed disposed the clicked item before dispatch completed");
            item.PerformClick();
            Check(state.ShowScripts && refreshes == 1, "Script toggle did not apply exactly once");
            Check(Selection(state).Targets.Count == 1, "Enabled script layer did not resolve its script target");
            Flush(work); Check(menu.IsDisposed, "Owned menu not released after dispatch");
        }
    }
    private static SpotlightSelection Selection(SpotlightViewState state)
    {
        return DependencySpotlightSelection.Resolve(new[] { new SpotlightCandidate
        {
            InstanceId = new Guid("10000000-0000-0000-0000-000000000001"), AssemblyName = "RhinoCodePluginGH",
            ScriptLanguage = DependencyClassification.ScriptLanguage("RhinoCodePluginGH", "RhinoCodePluginGH.Components.Python3Component"),
            IsTopLevel = true, HasVisibleBounds = true
        } }, state.Enabled, state.ShowThirdParty, state.ShowScripts, state.AssemblyFocus, state.ScriptFocus, 500);
    }
    private static void OutsideDismissal()
    {
        using (var dispatchOwner = new Control())
        {
            IntPtr handle = dispatchOwner.Handle;
            using (var lifetime = new SpotlightMenuLifetime(action => dispatchOwner.BeginInvoke(action)))
            foreach (var reason in new[] { ToolStripDropDownCloseReason.AppClicked, ToolStripDropDownCloseReason.Keyboard })
            {
                var menu = new TestMenu(); int cleanup = 0;
                lifetime.Track(menu, true, () => cleanup++);
                menu.NotifyClosed(reason); menu.NotifyClosed(reason);
                Check(!menu.IsDisposed && cleanup == 0, "Dismissal performed cleanup inside Closed");
                bool responsive = false; dispatchOwner.BeginInvoke(new Action(() => responsive = true));
                Application.DoEvents();
                Check(responsive && menu.IsDisposed && cleanup == 1, "Deferred dismissal blocked message processing or repeated cleanup");
            }
        }
    }
    private static void BorrowedMenu()
    {
        var work = new Queue<Action>(); int clicks = 0, itemDisposals = 0;
        using (var lifetime = new SpotlightMenuLifetime(work.Enqueue))
        using (var menu = new TestMenu())
        {
            var native = new ToolStripMenuItem("Native action"); menu.Items.Add(native);
            var ours = SpotlightMenuChoices.Add(menu.Items, "Spotlight", false, () => true, () => clicks++, () => { });
            ours.Disposed += delegate { itemDisposals++; };
            lifetime.Track(menu, false, () => { menu.Items.Remove(ours); ours.Dispose(); });
            menu.NotifyClosed(ToolStripDropDownCloseReason.ItemClicked); ours.PerformClick();
            Check(clicks == 1, "Borrowed menu removed the toggle before Click");
            Flush(work);
            Check(!menu.IsDisposed && !native.IsDisposed && menu.Items.Count == 1 && itemDisposals == 1, "Cleanup damaged native items or did not dispose our item");
        }
    }
    private static void SettingsReopen()
    {
        var state = new SpotlightViewState(); state.SetEnabled(true); int openings = 0, refreshes = 0;
        ToolStripDropDown ownedMenu = null;
        using (var panel = new SpotlightLegendPanel(menu =>
        {
            openings++;
            SpotlightMenuChoices.Add(menu.Items, "Show script components", state.ShowScripts,
                () => state.Enabled, () => state.ShowScripts = !state.ShowScripts, () => refreshes++);
            menu.Items.Add(new ToolStripMenuItem("Focus script language") { Enabled = state.ShowScripts });
            return true;
        }, () => { }, () => { }))
        {
            var header = panel.Controls.OfType<ToolStrip>().Single();
            var button = (ToolStripDropDownButton)header.Items["SpotlightSettings"];
            ownedMenu = button.DropDown;
            Check(ReferenceEquals(ownedMenu.OwnerItem, button), "Settings has no owning dropdown button");
            // Run the real native opening pipeline, cancelling at its end so no window appears.
            ownedMenu.Opening += delegate(object sender, System.ComponentModel.CancelEventArgs args) { args.Cancel = true; };
            for (int i = 0; i < 20; i++)
            {
                button.ShowDropDown(); Check(openings == i + 1 && ownedMenu.Items.Count == 2, "Settings did not rebuild on opening");
                var toggle = (ToolStripMenuItem)ownedMenu.Items[0];
                Check(toggle.Checked == state.ShowScripts && ownedMenu.Items[1].Enabled == state.ShowScripts, "Reopened settings shows stale state");
                toggle.PerformClick(); panel.CloseSettings();
                Check(!ownedMenu.IsDisposed && refreshes == i + 1, "Settings close disposed its menu or lost a click");
                panel.UpdateView(state, Selection(state), new Size(1000, 700), false);
                Check(panel.Controls.OfType<Label>().Single().Text.Contains(state.ShowScripts ? "Scripts: All" : "Scripts: Off"), "Legend did not reflect the script setting");
            }
        }
        Check(ownedMenu.IsDisposed, "Panel teardown did not dispose its owned Settings menu");
    }
    private static void StaleChoice()
    {
        using (var menu = new TestMenu())
        {
            int changes = 0;
            var item = SpotlightMenuChoices.Add(menu.Items, "Stale", false, () => false, () => changes++, () => changes++);
            item.PerformClick(); Check(changes == 0, "Stale document choice changed state");
        }
    }
    private static void Teardown()
    {
        var work = new Queue<Action>(); var lifetime = new SpotlightMenuLifetime(work.Enqueue);
        var menu = new TestMenu(); int cleanups = 0;
        lifetime.Track(menu, false, () => cleanups++); lifetime.Track(menu, true, null);
        menu.NotifyClosed(ToolStripDropDownCloseReason.CloseCalled); lifetime.Dispose(); Flush(work);
        Check(menu.IsDisposed && cleanups == 1, "Teardown leaked a menu or reran queued cleanup");
    }
    private static void FailedDispatch()
    {
        var lifetime = new SpotlightMenuLifetime(action => { throw new InvalidOperationException("Owner handle closing"); });
        var menu = new TestMenu(); lifetime.Track(menu, true, null);
        menu.NotifyClosed(ToolStripDropDownCloseReason.AppClicked);
        Check(!menu.IsDisposed, "Failed dispatch fell back to disposal inside Closed");
        lifetime.Dispose(); Check(menu.IsDisposed, "Teardown lost a menu after dispatch failure");
    }
}
