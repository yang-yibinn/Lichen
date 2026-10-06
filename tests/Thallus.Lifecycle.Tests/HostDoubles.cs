// Deliberately small public-API doubles. No real Rhino or Grasshopper assembly is loaded.
// The group cache model follows SDK 8.0: cached objects survive until ExpireCaches;
// object GUID membership survives deletion, and undo supplies a new object instance.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace GH_IO.Serialization
{
    public interface GH_IWriter { void SetString(string key, string value); void SetString(string key, int i, string value); void SetInt32(string key, int value); }
    public interface GH_IReader { bool TryGetString(string key, ref string value); bool TryGetString(string key, int i, ref string value); bool TryGetInt32(string key, ref int value); }
}
internal sealed class Archive : GH_IO.Serialization.GH_IWriter, GH_IO.Serialization.GH_IReader
{
    private readonly Dictionary<string, object> values = new Dictionary<string, object>();
    public void SetString(string key, string value) { values[key] = value; }
    public void SetString(string key, int i, string value) { SetString(key + i, value); }
    public void SetInt32(string key, int value) { values[key] = value; }
    public bool TryGetString(string key, ref string value) { object found; if (!values.TryGetValue(key, out found)) return false; value = (string)found; return true; }
    public bool TryGetString(string key, int i, ref string value) { return TryGetString(key + i, ref value); }
    public bool TryGetInt32(string key, ref int value) { object found; if (!values.TryGetValue(key, out found)) return false; value = (int)found; return true; }
    public bool SameAs(Archive other) { return values.Count == other.values.Count && values.All(pair => other.values.ContainsKey(pair.Key) && Equals(pair.Value, other.values[pair.Key])); }
}
namespace Grasshopper { internal static class Instances { public static IWin32Window DocumentEditor { get { return null; } } } }
namespace Grasshopper.Kernel
{
    public enum GH_Exposure { hidden }
    public enum GH_DocumentContext { Open, Close, Loaded, Unloaded }
    public enum GH_UndoOperation { Undo, Redo, RecordAdded }
    public sealed class GH_DocObjectEventArgs : EventArgs { }
    public sealed class GH_DocUndoEventArgs : EventArgs { public GH_UndoOperation Operation; }
    public sealed class GH_Document
    {
        public readonly List<Special.GH_Group> Objects = new List<Special.GH_Group>();
        public event EventHandler<GH_DocObjectEventArgs> ObjectsAdded;
        public event EventHandler<GH_DocObjectEventArgs> ObjectsDeleted;
        public event EventHandler<GH_DocUndoEventArgs> UndoStateChanged;
        public int SubscriberCount { get { return Count(ObjectsAdded) + Count(ObjectsDeleted) + Count(UndoStateChanged); } }
        private static int Count(Delegate value) { return value == null ? 0 : value.GetInvocationList().Length; }
        public void Added() { if (ObjectsAdded != null) ObjectsAdded(this, new GH_DocObjectEventArgs()); }
        public void Deleted() { if (ObjectsDeleted != null) ObjectsDeleted(this, new GH_DocObjectEventArgs()); }
        public void Undo(GH_UndoOperation operation) { if (UndoStateChanged != null) UndoStateChanged(this, new GH_DocUndoEventArgs { Operation = operation }); }
    }
}
namespace Grasshopper.Kernel.Special
{
    public enum GH_GroupBorder { Box }
    public class GH_Group
    {
        protected object m_attributes;
        private GH_Document document;
        private List<GH_Group> cache;
        public Guid InstanceGuid = Guid.NewGuid();
        public string Name, NickName, Description;
        public Color Colour;
        public GH_GroupBorder Border;
        public readonly List<Guid> ObjectIDs = new List<Guid>();
        public int ExpirationCount;
        public virtual Guid ComponentGuid { get { return Guid.Empty; } }
        public virtual GH_Exposure Exposure { get { return GH_Exposure.hidden; } }
        public virtual void CreateAttributes() { }
        public virtual void AddedToDocument(GH_Document value) { document = value; }
        public virtual void RemovedFromDocument(GH_Document value) { document = null; }
        public virtual void MovedBetweenDocuments(GH_Document oldDocument, GH_Document newDocument) { document = newDocument; }
        public virtual void DocumentContextChanged(GH_Document value, GH_DocumentContext context) { }
        public void ExpireCaches() { cache = null; ExpirationCount++; }
        public List<GH_Group> Objects() { return cache ?? (cache = document.Objects.Where(value => ObjectIDs.Contains(value.InstanceGuid)).ToList()); }
        public virtual bool AppendMenuItems(ToolStripDropDown menu) { return true; }
        public virtual bool Write(GH_IO.Serialization.GH_IWriter writer)
        {
            writer.SetString("Ids", String.Join(",", ObjectIDs)); writer.SetString("Name", NickName); return true;
        }
        public virtual bool Read(GH_IO.Serialization.GH_IReader reader)
        {
            string ids = "", name = ""; reader.TryGetString("Ids", ref ids); reader.TryGetString("Name", ref name);
            ObjectIDs.Clear(); ObjectIDs.AddRange(ids.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(Guid.Parse));
            NickName = name; ExpireCaches(); return true;
        }
    }
}
namespace Lichen.Plugin
{
    internal sealed class LichenThallusGroupAttributes { internal LichenThallusGroupAttributes(LichenThallusGroup group) { } }
    internal static class LichenThallusEditor { internal static void Edit(LichenThallusGroup group, IWin32Window owner) { } }
    internal static class LichenThallusCommands
    {
        internal static void RefreshLayouts(Grasshopper.Kernel.GH_Document document) { }
        internal static void RemoveOwnedEndpoint(LichenThallusGroup group, Grasshopper.Kernel.GH_Document document) { }
        internal static void SelectMembers(LichenThallusGroup group) { }
        internal static void AddSelection(LichenThallusGroup group) { }
        internal static void RemoveSelection(LichenThallusGroup group) { }
    }
}
