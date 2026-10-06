using System;

namespace Lichen.Core
{
    public sealed class ExportContextDraft
    {
        public ExportContextDraft()
        {
            Purpose = "";
            RequestedTask = "";
            Constraints = "";
        }

        public string Purpose { get; set; }
        public string RequestedTask { get; set; }
        public string Constraints { get; set; }
    }

    public interface IExportContextDraftSettings
    {
        string GetValue(string key, string fallback);
        void SetValue(string key, string value);
        void WritePersistentSettings();
    }

    public sealed class ExportContextDraftStore
    {
        private readonly IExportContextDraftSettings settings;

        public ExportContextDraftStore(IExportContextDraftSettings settings)
        {
            if (settings == null) throw new ArgumentNullException("settings");
            this.settings = settings;
        }

        public ExportContextDraft Load(string contextKey)
        {
            ExportContextDraft draft = new ExportContextDraft();
            if (String.IsNullOrWhiteSpace(contextKey)) return draft;
            draft.Purpose = settings.GetValue(Key(contextKey, "Purpose"), "") ?? "";
            draft.RequestedTask = settings.GetValue(Key(contextKey, "RequestedTask"), "") ?? "";
            draft.Constraints = settings.GetValue(Key(contextKey, "Constraints"), "") ?? "";
            return draft;
        }

        public bool Save(string contextKey, ExportContextDraft draft)
        {
            if (String.IsNullOrWhiteSpace(contextKey) || draft == null) return false;
            bool changed = SetIfChanged(Key(contextKey, "Purpose"), draft.Purpose)
                | SetIfChanged(Key(contextKey, "RequestedTask"), draft.RequestedTask)
                | SetIfChanged(Key(contextKey, "Constraints"), draft.Constraints);
            if (changed) settings.WritePersistentSettings();
            return changed;
        }

        private bool SetIfChanged(string key, string value)
        {
            string normalized = value ?? "";
            string previous = settings.GetValue(key, "") ?? "";
            if (String.Equals(previous, normalized, StringComparison.Ordinal)) return false;
            settings.SetValue(key, normalized);
            return true;
        }

        private static string Key(string contextKey, string field)
        {
            return "ExportContext_" + contextKey + "_" + field;
        }
    }
}
