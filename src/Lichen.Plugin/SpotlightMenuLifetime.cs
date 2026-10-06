using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;

namespace Lichen.Plugin
{
    // WinForms can close a dropdown before delivering the item's Click. Never remove items
    // or dispose their menu from Closed: let that input dispatch and menu-mode cleanup finish.
    internal sealed class SpotlightMenuLifetime : IDisposable
    {
        private sealed class Entry
        {
            internal bool Owned, Queued;
            internal Action Cleanup;
            internal ToolStripDropDownClosedEventHandler Closed;
            internal EventHandler Disposed;
        }
        private readonly Action<Action> post;
        private readonly Dictionary<ToolStripDropDown, Entry> entries = new Dictionary<ToolStripDropDown, Entry>();
        private bool disposed;
        internal SpotlightMenuLifetime(Action<Action> post) { this.post = post; }
        internal void Track(ToolStripDropDown menu, bool owned, Action cleanup)
        {
            if (disposed) throw new ObjectDisposedException("SpotlightMenuLifetime");
            Entry entry;
            if (!entries.TryGetValue(menu, out entry))
            {
                entry = new Entry(); entries.Add(menu, entry);
                entry.Closed = delegate { QueueCleanup(menu, entry); };
                entry.Disposed = delegate { QueueCleanup(menu, entry); };
                menu.Closed += entry.Closed; menu.Disposed += entry.Disposed;
            }
            entry.Owned |= owned; entry.Cleanup += cleanup;
        }
        private void QueueCleanup(ToolStripDropDown menu, Entry entry)
        {
            if (entry.Queued) return;
            entry.Queued = true;
            try { post(delegate { Complete(menu, entry); }); }
            catch { entry.Queued = false; } // Keep tracked for document/canvas teardown.
        }
        private void Complete(ToolStripDropDown menu, Entry entry)
        {
            Entry current;
            if (!entries.TryGetValue(menu, out current) || !ReferenceEquals(current, entry)) return;
            entries.Remove(menu);
            menu.Closed -= entry.Closed; menu.Disposed -= entry.Disposed;
            try { if (entry.Cleanup != null) entry.Cleanup(); } catch { }
            finally { if (entry.Owned && !menu.IsDisposed) { try { menu.Dispose(); } catch { } } }
        }
        internal void CloseAll()
        {
            foreach (var pair in entries.ToArray())
            {
                if (pair.Value.Owned && !pair.Key.IsDisposed) { try { pair.Key.Close(); } catch { } }
                QueueCleanup(pair.Key, pair.Value);
            }
        }
        public void Dispose()
        {
            if (disposed) return;
            disposed = true; CloseAll();
            // Canvas teardown cannot rely on a future message dispatch. Close has returned,
            // and queued callbacks become no-ops after these registrations are removed.
            foreach (var pair in entries.ToArray()) Complete(pair.Key, pair.Value);
        }
    }

    internal static class SpotlightMenuChoices
    {
        internal static ToolStripMenuItem Add(ToolStripItemCollection items, string text, bool selected,
            Func<bool> canApply, Action apply, Action refresh)
        {
            var item = new ToolStripMenuItem(text) { Checked = selected };
            item.Click += delegate { if (canApply()) { apply(); refresh(); } };
            items.Add(item); return item;
        }
    }
}
