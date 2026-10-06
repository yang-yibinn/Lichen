// Compiles the production LichenThallusGroup against minimal host doubles.
// These tests verify our event wiring and cache invalidation, not Grasshopper rendering.
using System;
using System.Linq;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Special;
using Lichen.Plugin;

internal static class Program
{
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static LichenThallusGroup Group(GH_Document document, params GH_Group[] members)
    {
        var group = new LichenThallusGroup();
        foreach (var member in members) { if (!document.Objects.Contains(member)) document.Objects.Add(member); group.ObjectIDs.Add(member.InstanceGuid); }
        document.Objects.Add(group);
        group.AddedToDocument(document);
        return group;
    }
    private static int Main()
    {
        try
        {
            DeleteUndoRedo(); Nested(); BatchUndo(); Cleanup(); Persistence(); ExplicitRemoval();
            Console.WriteLine("Thallus lifecycle doubles: 6 passed; 0 failed (no Rhino/Grasshopper loaded).");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error.Message); return 1; }
    }
    private static void DeleteUndoRedo()
    {
        var document = new GH_Document(); var original = new GH_Group();
        var group = Group(document, original); var ids = group.ObjectIDs.ToArray();
        Check(group.Objects().Single() == original, "Initial cache missing member");
        for (int i = 0; i < 3; i++)
        {
            document.Objects.RemoveAll(value => value.InstanceGuid == original.InstanceGuid);
            document.Deleted();
            Check(group.Objects().Count == 0, "Deletion retains a detached member in the cache");
            var restored = new GH_Group { InstanceGuid = original.InstanceGuid };
            document.Objects.Add(restored); document.Added();
            Check(group.Objects().Single() == restored, "Undo does not resolve the restored instance");
            Check(group.ObjectIDs.SequenceEqual(ids), "Refresh changed authored membership");
        }
    }
    private static void Nested()
    {
        var document = new GH_Document(); var member = new GH_Group();
        var child = Group(document, member); var parent = Group(document, child);
        parent.Objects(); child.Objects(); int parentExpires = parent.ExpirationCount;
        document.Objects.Remove(member); document.Deleted();
        var restored = new GH_Group { InstanceGuid = member.InstanceGuid };
        document.Objects.Add(restored); document.Added();
        Check(parent.ExpirationCount > parentExpires && child.Objects().Single() == restored,
            "Nested parent or child cache was not invalidated");
        Check(parent.ObjectIDs.SequenceEqual(new[] { child.InstanceGuid }), "Nested membership was flattened");
    }
    private static void BatchUndo()
    {
        var document = new GH_Document(); var member = new GH_Group(); var group = Group(document, member);
        group.Objects(); document.Objects.Remove(member); document.Undo(GH_UndoOperation.Redo);
        Check(group.Objects().Count == 0, "Redo completion did not refresh suppressed object events");
        var restored = new GH_Group { InstanceGuid = member.InstanceGuid };
        document.Objects.Add(restored); document.Undo(GH_UndoOperation.Undo);
        Check(group.Objects().Single() == restored, "Undo completion did not resolve batch restoration");
        int before = group.ExpirationCount; document.Undo(GH_UndoOperation.RecordAdded);
        Check(before == group.ExpirationCount, "Unrelated undo bookkeeping causes invalidation");
    }
    private static void Cleanup()
    {
        var first = new GH_Document(); var second = new GH_Document(); var group = Group(first);
        group.AddedToDocument(first); group.DocumentContextChanged(first, GH_DocumentContext.Loaded);
        Check(first.SubscriberCount == 3, "Duplicate subscriptions on repeated attach");
        group.MovedBetweenDocuments(first, second);
        Check(first.SubscriberCount == 0 && second.SubscriberCount == 3, "Document transfer leaks handlers");
        int before = group.ExpirationCount; first.Added(); first.Deleted(); first.Undo(GH_UndoOperation.Undo);
        Check(before == group.ExpirationCount, "Old document still affects the group");
        group.DocumentContextChanged(second, GH_DocumentContext.Unloaded);
        Check(second.SubscriberCount == 0, "Unload leaks handlers");
        group.DocumentContextChanged(second, GH_DocumentContext.Loaded);
        group.DocumentContextChanged(second, GH_DocumentContext.Close);
        Check(second.SubscriberCount == 0, "Close leaks handlers");
        group.AddedToDocument(second); group.RemovedFromDocument(second);
        Check(second.SubscriberCount == 0, "Removal leaks handlers");
        group.AddedToDocument(second);
        Check(second.SubscriberCount == 3, "Restored group failed to subscribe");
    }
    private static void Persistence()
    {
        var document = new GH_Document(); var member = new GH_Group(); var group = Group(document, member);
        group.ApplyMetadata("Region", "Description", null);
        var archive = new Archive(); group.Write(archive);
        document.Objects.Remove(member); document.Deleted(); group.Objects();
        var restored = new GH_Group { InstanceGuid = member.InstanceGuid };
        document.Objects.Add(restored); document.Added();
        var after = new Archive(); group.Write(after);
        Check(archive.SameAs(after), "Delete/undo changed serialized membership or metadata");
        var reopened = new LichenThallusGroup(); reopened.Read(after);
        var newDocument = new GH_Document(); newDocument.Objects.Add(restored); reopened.AddedToDocument(newDocument);
        Check(reopened.Objects().Single() == restored && reopened.ThallusDescription == "Description",
            "Reopened group lost restored membership or metadata");
        Check(newDocument.SubscriberCount == 3, "Reopened group failed to observe its new document");
    }
    private static void ExplicitRemoval()
    {
        var document = new GH_Document(); var member = new GH_Group(); var group = Group(document, member);
        group.Objects(); group.ObjectIDs.Remove(member.InstanceGuid); group.ExpireCaches();
        document.Objects.Remove(member); document.Deleted();
        document.Objects.Add(new GH_Group { InstanceGuid = member.InstanceGuid }); document.Added(); document.Undo(GH_UndoOperation.Undo);
        Check(group.ObjectIDs.Count == 0 && group.Objects().Count == 0,
            "Refresh re-enrolled an explicitly removed member");
    }
}
