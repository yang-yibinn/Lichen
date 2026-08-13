using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Grasshopper;
using Grasshopper.GUI.Canvas;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Special;
using Lichen.Core;

namespace Lichen.Plugin
{
    internal static class LichenThallusCommands
    {
        internal static bool CanCreate(GH_Document document)
        {
            return EligibleSelection(document).Count > 0;
        }

        internal static bool CreateFromSelection(GH_Canvas canvas)
        {
            if (canvas == null || canvas.Document == null) return false;
            GH_Document document = canvas.Document;
            List<IGH_DocumentObject> members = RemoveNestedMemberDuplicates(document, EligibleSelection(document), null);
            if (members.Count == 0) return false;
            List<LichenThallusGroup> containingParents = FindDirectContainingThalli(document, members);
            if (containingParents.Count > 1)
            {
                MessageBox.Show(Instances.DocumentEditor, "The selected objects are direct members of more than one Thallus, so Lichen cannot infer a single parent. Resolve that overlapping membership before creating a nested Thallus.", "Create Thallus", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return false;
            }
            LichenThallusGroup containingParent = containingParents.FirstOrDefault();
            string problem = ValidateChildThalli(document, members, containingParent);
            if (!String.IsNullOrWhiteSpace(problem))
            {
                MessageBox.Show(Instances.DocumentEditor, problem, "Create Thallus", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return false;
            }

            RectangleF bounds = CombinedBounds(members);
            LichenThallusEndpointComponent endpoint = new LichenThallusEndpointComponent();
            LichenThallusGroup group = new LichenThallusGroup();
            foreach (IGH_DocumentObject member in members) group.AddObject(member.InstanceGuid);
            group.AddObject(endpoint.InstanceGuid);

            int groupIndex = members.OfType<LichenThallusGroup>().Select(child => document.Objects.IndexOf(child)).Where(value => value >= 0)
                .DefaultIfEmpty(document.Objects.Count).Min();
            if (!document.AddObject(endpoint, false, document.Objects.Count)) return false;
            if (endpoint.Attributes != null)
            {
                endpoint.Attributes.Pivot = new PointF(bounds.Right + 24F, bounds.Top + bounds.Height * 0.5F);
                // GH_Group reads member attribute bounds as soon as it joins the document. Lay out the
                // hidden endpoint at its assigned pivot first so the group never caches its old origin bounds.
                endpoint.Attributes.ExpireLayout();
                endpoint.Attributes.PerformLayout();
            }
            if (!document.AddObject(group, false, groupIndex))
            {
                document.RemoveObject(endpoint, false);
                return false;
            }
            if (containingParent != null)
            {
                containingParent.RecordUndoEvent("Create Thallus");
                RemoveDirectMembersCoveredByChildThalli(containingParent, members, document);
                foreach (IGH_DocumentObject member in members) containingParent.RemoveObject(member.InstanceGuid);
                containingParent.AddObject(group.InstanceGuid);
                containingParent.ExpireCaches();
            }
            document.UndoUtil.RecordAddObjectEvent("Create Thallus", new IGH_DocumentObject[] { endpoint, group });
            if (containingParent != null) document.UndoUtil.MergeRecords(2);
            group.ExpireCaches();
            ExpireEndpointLayouts(document);
            document.DeselectAll();
            if (group.Attributes != null) group.Attributes.Selected = true;
            canvas.Invalidate();
            return true;
        }

        internal static void AddSelection(LichenThallusGroup group)
        {
            GH_Document document = group == null ? null : group.OnPingDocument();
            if (document == null) return;
            List<IGH_DocumentObject> members = RemoveNestedMemberDuplicates(document, EligibleSelection(document).Where(o => o.InstanceGuid != group.InstanceGuid), group);
            if (members.Count == 0) return;
            string problem = ValidateChildThalli(document, members, group);
            if (!String.IsNullOrWhiteSpace(problem))
            {
                MessageBox.Show(Instances.DocumentEditor, problem, "Add to Thallus", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            group.RecordUndoEvent("Add objects to Thallus");
            RemoveDirectMembersCoveredByChildThalli(group, members, document);
            foreach (IGH_DocumentObject member in members) group.AddObject(member.InstanceGuid);
            group.ExpireCaches();
            ExpireEndpointLayouts(document);
            Redraw();
        }

        internal static void RemoveSelection(LichenThallusGroup group)
        {
            GH_Document document = group == null ? null : group.OnPingDocument();
            if (document == null) return;
            HashSet<Guid> selected = new HashSet<Guid>(document.SelectedObjects().Select(o => o.InstanceGuid));
            List<Guid> removable = group.ObjectIDs.Where(selected.Contains).Where(id =>
            {
                IGH_DocumentObject value = document.Objects.FirstOrDefault(o => o.InstanceGuid == id);
                return value == null || SafeComponentGuid(value) != LichenComponentIds.ThallusEndpoint;
            }).ToList();
            if (removable.Count == 0) return;
            group.RecordUndoEvent("Remove objects from Thallus");
            foreach (Guid id in removable) group.RemoveObject(id);
            group.ExpireCaches();
            ExpireEndpointLayouts(document);
            Redraw();
        }

        internal static void SelectMembers(LichenThallusGroup group)
        {
            GH_Document document = group == null ? null : group.OnPingDocument();
            if (document == null) return;
            HashSet<Guid> ids = new HashSet<Guid>();
            CollectEffectiveMembers(group, document, ids, new HashSet<Guid>());
            document.DeselectAll();
            foreach (IGH_DocumentObject obj in document.Objects)
                if (ids.Contains(obj.InstanceGuid) && obj.Attributes != null) obj.Attributes.Selected = true;
            Redraw();
        }

        internal static LichenThallusGroup FindOwner(LichenThallusEndpointComponent endpoint)
        {
            GH_Document document = endpoint == null ? null : endpoint.OnPingDocument();
            return document == null ? null : document.Objects.OfType<LichenThallusGroup>()
                .FirstOrDefault(group => group.ObjectIDs.Contains(endpoint.InstanceGuid));
        }

        internal static LichenThallusGroup FindParent(LichenThallusGroup child)
        {
            GH_Document document = child == null ? null : child.OnPingDocument();
            return document == null ? null : document.Objects.OfType<LichenThallusGroup>()
                .FirstOrDefault(group => group.InstanceGuid != child.InstanceGuid && group.ObjectIDs.Contains(child.InstanceGuid));
        }

        internal static void RemoveOwnedEndpoint(LichenThallusGroup group, GH_Document document)
        {
            if (group == null || document == null) return;
            if (document.Context == GH_DocumentContext.Close || document.Context == GH_DocumentContext.Unloaded) return;
            HashSet<Guid> memberIds = new HashSet<Guid>(group.ObjectIDs);
            LichenThallusEndpointComponent endpoint = document.Objects.OfType<LichenThallusEndpointComponent>()
                .FirstOrDefault(value => memberIds.Contains(value.InstanceGuid));
            if (endpoint == null) return;

            // Normal Grasshopper deletion records the selected group immediately before removing it.
            // Extend that fresh removal record so one undo/redo operation owns both the group and its
            // hidden socket implementation. During redo the already-merged endpoint action handles
            // removal; it does not create a new delete/remove/cut record, preventing recursion here.
            string priorName = document.UndoServer.FirstUndoName ?? "";
            bool freshRemoval = document.UndoServer.UndoCount > 0
                && (priorName.IndexOf("delete", StringComparison.OrdinalIgnoreCase) >= 0
                    || priorName.IndexOf("remove", StringComparison.OrdinalIgnoreCase) >= 0
                    || priorName.IndexOf("cut", StringComparison.OrdinalIgnoreCase) >= 0);
            if (!freshRemoval) return;

            Guid endpointRecord = document.UndoUtil.RecordRemoveObjectEvent("Delete Thallus output", endpoint);
            if (!document.RemoveObject(endpoint, false)) return;
            if (endpointRecord != Guid.Empty) document.UndoUtil.MergeRecords(2);
        }

        internal static void RefreshLayouts(GH_Document document)
        {
            if (document == null) return;
            ExpireEndpointLayouts(document);
            Redraw();
        }

        private static List<IGH_DocumentObject> EligibleSelection(GH_Document document)
        {
            if (document == null) return new List<IGH_DocumentObject>();
            Dictionary<Guid, IGH_DocumentObject> byId = document.Objects.GroupBy(o => o.InstanceGuid).ToDictionary(g => g.Key, g => g.First());
            List<IGH_DocumentObject> result = new List<IGH_DocumentObject>();
            HashSet<Guid> seen = new HashSet<Guid>();
            foreach (IGH_DocumentObject selected in document.SelectedObjects().OrderBy(o => o.InstanceGuid)) ExpandSelection(selected, byId, result, seen);
            return result.OrderBy(o => o.InstanceGuid).ToList();
        }

        private static void ExpandSelection(IGH_DocumentObject obj, IDictionary<Guid, IGH_DocumentObject> objects, IList<IGH_DocumentObject> result, ISet<Guid> seen)
        {
            if (obj == null || !seen.Add(obj.InstanceGuid)) return;
            Guid componentId = SafeComponentGuid(obj);
            if (componentId == LichenComponentIds.ExportRoot || componentId == LichenComponentIds.ThallusEndpoint) return;
            if (componentId == LichenComponentIds.Thallus) { result.Add(obj); return; }
            GH_Group nativeGroup = obj as GH_Group;
            if (nativeGroup != null)
            {
                foreach (Guid id in nativeGroup.ObjectIDs.OrderBy(value => value))
                {
                    IGH_DocumentObject member;
                    if (objects.TryGetValue(id, out member)) ExpandSelection(member, objects, result, seen);
                }
                return;
            }
            result.Add(obj);
        }

        private static string ValidateChildThalli(GH_Document document, IEnumerable<IGH_DocumentObject> members, LichenThallusGroup target)
        {
            foreach (LichenThallusGroup child in members.OfType<LichenThallusGroup>())
            {
                if (target != null && IsAncestor(child, target)) return "That selection would create a cycle in the Thallus hierarchy.";
                LichenThallusGroup existingParent = FindParent(child);
                if (existingParent != null && existingParent != target) return "“" + child.NickName + "” already belongs to an outer Thallus. Remove it from that parent before nesting it again.";
                LichenThallusEndpointComponent endpoint = document.Objects.OfType<LichenThallusEndpointComponent>()
                    .FirstOrDefault(value => child.ObjectIDs.Contains(value.InstanceGuid));
                if (endpoint != null && endpoint.Params.Output.Count > 0 && endpoint.Params.Output[0].Recipients.Count > 0)
                    return "Disconnect “" + child.NickName + "” from Lichen before nesting it. Only outermost Thalli have an output.";
            }
            return "";
        }

        private static List<IGH_DocumentObject> RemoveNestedMemberDuplicates(GH_Document document, IEnumerable<IGH_DocumentObject> values, LichenThallusGroup target)
        {
            List<IGH_DocumentObject> members = (values ?? Enumerable.Empty<IGH_DocumentObject>()).Distinct().ToList();
            List<LichenThallusGroup> children = members.OfType<LichenThallusGroup>().ToList();
            if (target != null)
                foreach (Guid id in target.ObjectIDs)
                {
                    LichenThallusGroup child = document.Objects.OfType<LichenThallusGroup>().FirstOrDefault(group => group.InstanceGuid == id);
                    if (child != null && !children.Contains(child)) children.Add(child);
                }
            HashSet<Guid> nestedMembers = new HashSet<Guid>();
            foreach (LichenThallusGroup child in children) CollectEffectiveMembers(child, document, nestedMembers, new HashSet<Guid>());
            return members.Where(member => member is LichenThallusGroup || !nestedMembers.Contains(member.InstanceGuid)).OrderBy(member => member.InstanceGuid).ToList();
        }

        private static List<LichenThallusGroup> FindDirectContainingThalli(GH_Document document, IEnumerable<IGH_DocumentObject> members)
        {
            HashSet<Guid> required = new HashSet<Guid>((members ?? Enumerable.Empty<IGH_DocumentObject>()).Where(member => member != null).Select(member => member.InstanceGuid));
            if (required.Count == 0) return new List<LichenThallusGroup>();
            return document.Objects.OfType<LichenThallusGroup>()
                .Where(group => !required.Contains(group.InstanceGuid) && required.All(id => group.ObjectIDs.Contains(id)))
                .OrderBy(group => group.InstanceGuid).ToList();
        }

        private static void RemoveDirectMembersCoveredByChildThalli(LichenThallusGroup target, IEnumerable<IGH_DocumentObject> members, GH_Document document)
        {
            HashSet<Guid> nestedMembers = new HashSet<Guid>();
            foreach (LichenThallusGroup child in (members ?? Enumerable.Empty<IGH_DocumentObject>()).OfType<LichenThallusGroup>())
                CollectEffectiveMembers(child, document, nestedMembers, new HashSet<Guid>());
            foreach (Guid id in target.ObjectIDs.Where(nestedMembers.Contains).ToList()) target.RemoveObject(id);
        }

        private static bool IsAncestor(LichenThallusGroup possibleAncestor, LichenThallusGroup value)
        {
            HashSet<Guid> visited = new HashSet<Guid>();
            LichenThallusGroup current = value;
            while (current != null && visited.Add(current.InstanceGuid))
            {
                current = FindParent(current);
                if (current == possibleAncestor) return true;
            }
            return false;
        }

        private static void CollectEffectiveMembers(LichenThallusGroup group, GH_Document document, ISet<Guid> destination, ISet<Guid> visited)
        {
            if (group == null || !visited.Add(group.InstanceGuid)) return;
            foreach (Guid id in group.ObjectIDs)
            {
                IGH_DocumentObject member = document.Objects.FirstOrDefault(o => o.InstanceGuid == id);
                if (member == null || SafeComponentGuid(member) == LichenComponentIds.ThallusEndpoint) continue;
                LichenThallusGroup child = member as LichenThallusGroup;
                if (child != null) CollectEffectiveMembers(child, document, destination, visited);
                else if (!(member is GH_Group) && SafeComponentGuid(member) != LichenComponentIds.ExportRoot) destination.Add(id);
            }
        }

        private static RectangleF CombinedBounds(IEnumerable<IGH_DocumentObject> members)
        {
            List<RectangleF> bounds = members.Where(o => o.Attributes != null).Select(o => o.Attributes.Bounds).Where(b => b.Width > 0F && b.Height > 0F).ToList();
            if (bounds.Count == 0) return new RectangleF(0F, 0F, 100F, 60F);
            RectangleF result = bounds[0];
            for (int i = 1; i < bounds.Count; i++) result = RectangleF.Union(result, bounds[i]);
            return result;
        }

        private static Guid SafeComponentGuid(IGH_DocumentObject obj) { try { return obj.ComponentGuid; } catch { return Guid.Empty; } }
        private static void ExpireEndpointLayouts(GH_Document document)
        {
            foreach (LichenThallusEndpointComponent endpoint in document.Objects.OfType<LichenThallusEndpointComponent>())
                if (endpoint.Attributes != null) endpoint.Attributes.ExpireLayout();
            foreach (LichenThallusGroup group in document.Objects.OfType<LichenThallusGroup>()) group.ExpireCaches();
        }
        private static void Redraw() { try { if (Instances.ActiveCanvas != null) Instances.ActiveCanvas.Invalidate(); } catch { } }
    }
}
