using System;
using System.Runtime.CompilerServices;

namespace Lichen.Core
{
    // Session-only UI state. Never serialized into an export or a Grasshopper document.
    public sealed class SpotlightViewState
    {
        public bool Enabled { get; private set; }
        public bool ShowThirdParty = true;
        public bool ShowScripts;
        public bool Collapsed;
        public string AssemblyFocus = "";
        public string ScriptFocus = "";
        public void Toggle() { SetEnabled(!Enabled); }
        public void SetEnabled(bool enabled) { Enabled = enabled; if (!enabled) ResetView(); }
        public void ResetView() { ShowThirdParty = true; ShowScripts = false; Collapsed = false; AssemblyFocus = ""; ScriptFocus = ""; }
    }

    // Object identity, not saved document GUID: two independently opened copies never share settings.
    public sealed class SpotlightSessions<T> where T : class
    {
        private readonly ConditionalWeakTable<T, SpotlightViewState> states = new ConditionalWeakTable<T, SpotlightViewState>();
        public SpotlightViewState For(T document) { return states.GetValue(document, key => new SpotlightViewState()); }
        public void Forget(T document) { if (document != null) states.Remove(document); }
    }

    public sealed class SpotlightPanelBounds
    {
        public int Left, Top, Width, Height;
    }

    public static class SpotlightPanelLayout
    {
        public static SpotlightPanelBounds Place(int viewportWidth, int viewportHeight, float uiScale, int desiredHeight)
        {
            double scale = Single.IsNaN(uiScale) || Single.IsInfinity(uiScale) ? 1 : Math.Max(1, Math.Min(4, uiScale));
            int margin = (int)Math.Round(12 * scale);
            int width = Math.Min((int)Math.Round(360 * scale), Math.Max(0, viewportWidth - margin * 2));
            int height = Math.Min(Math.Max(0, desiredHeight), Math.Max(0, viewportHeight - margin * 2));
            return new SpotlightPanelBounds { Left = Math.Max(0, viewportWidth - margin - width), Top = Math.Min(margin, Math.Max(0, viewportHeight)), Width = width, Height = height };
        }
    }
}
