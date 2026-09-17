using System;
using System.Collections.Generic;
using System.Linq;
namespace ReMap.Standalone.Core
{
    public static class MapHierarchy
    {
        public static void Validate(MapDocument document)
        {
            var byId = document.objects.ToDictionary(o => o.id);
            foreach (var item in document.objects)
            {
                string parent = item.parentId; var visited = new HashSet<string> { item.id };
                while (!string.IsNullOrEmpty(parent))
                {
                    // Models remain generated geometry while also acting as hierarchy nodes.
                    // This mirrors Unity GameObjects, where any object may own children.
                    if (!byId.TryGetValue(parent, out var folder)) throw new ArgumentException(L.T("#PARENT_EXISTING_GROUP"));
                    if (!visited.Add(parent) || visited.Count > 64) throw new ArgumentException(L.T("#CYCLIC_OVERLY_DEEP_HIERARCHY_MAXIMUM"));
                    parent = folder.parentId;
                }
            }
        }
        public static HashSet<string> Subtree(MapDocument doc, string id)
        {
            var result = new HashSet<string> { id }; bool changed;
            do { changed = false; foreach (var item in doc.objects) if (!string.IsNullOrEmpty(item.parentId) && result.Contains(item.parentId)) changed |= result.Add(item.id); } while (changed);
            return result;
        }
        public static HashSet<string> SelectionContainers(MapDocument doc, IEnumerable<string> selection)
        {
            var byId = doc.objects.ToDictionary(item => item.id);
            var containers = new HashSet<string>(doc.objects.Where(item => !string.IsNullOrEmpty(item.parentId)).Select(item => item.parentId));
            var result = new HashSet<string>();
            foreach (var selectedId in selection ?? Enumerable.Empty<string>())
            {
                string id = selectedId;
                while (!string.IsNullOrEmpty(id) && byId.TryGetValue(id, out var item))
                {
                    if (containers.Contains(id)) result.Add(id); id = item.parentId;
                }
            }
            return result;
        }
        public static bool IsEnabled(MapDocument doc, string id)
        {
            var byId = doc.objects.ToDictionary(o => o.id); var seen = new HashSet<string>();
            while (!string.IsNullOrEmpty(id))
            {
                if (!seen.Add(id) || !byId.TryGetValue(id, out var item) || item.disabled) return false;
                id = item.parentId;
            }
            return true;
        }
        public static IEnumerable<MapObject> GenerationObjects(MapDocument doc)
        {
            doc.Validate(); return doc.objects.Where(o => !o.isGroup && IsEnabled(doc, o.id));
        }
        public static void Reparent(MapDocument doc, string id, string parent)
        {
            var item = doc.objects.Single(o => o.id == id); string previous = item.parentId;
            item.parentId = parent ?? "";
            try { Validate(doc); } catch { item.parentId = previous; throw; }
        }
        public static void Reorder(MapDocument doc, string id, string parent, string beforeSiblingId = null)
        {
            var item = doc.objects.Single(o => o.id == id);
            parent = parent ?? "";
            if (beforeSiblingId == id && (item.parentId ?? "") == parent) return;
            if (beforeSiblingId != null)
            {
                var sibling = doc.objects.SingleOrDefault(o => o.id == beforeSiblingId);
                if (sibling == null || (sibling.parentId ?? "") != parent || sibling.id == id)
                    throw new ArgumentException(L.T("#REORDER_TARGET_SAME_FOLDER"));
            }

            int previousIndex = doc.objects.IndexOf(item);
            string previousParent = item.parentId;
            doc.objects.RemoveAt(previousIndex);
            item.parentId = parent;
            int insertionIndex = beforeSiblingId == null
                ? doc.objects.Count
                : doc.objects.FindIndex(o => o.id == beforeSiblingId);
            doc.objects.Insert(insertionIndex, item);
            try { Validate(doc); }
            catch
            {
                doc.objects.Remove(item);
                item.parentId = previousParent;
                doc.objects.Insert(Math.Min(previousIndex, doc.objects.Count), item);
                throw;
            }
        }
        public static void RemapInternalReferences(MapObject item, IReadOnlyDictionary<string, string> mapping)
        {
            if (!string.IsNullOrEmpty(item.ziplineStartId) && mapping.TryGetValue(item.ziplineStartId, out var start)) item.ziplineStartId = start;
            if (!string.IsNullOrEmpty(item.ziplineEndId) && mapping.TryGetValue(item.ziplineEndId, out var end)) item.ziplineEndId = end;
        }

        public static string Duplicate(MapDocument doc, string id)
        {
            var requested = doc.objects.Single(o => o.id == id);
            if (requested.customType == "zipline-endpoint") id = requested.parentId;
            if (requested.customType == "button-teleport-target") id = requested.parentId;
            if (requested.customType == "curved-zipline-component")
            {
                var point = doc.objects.SingleOrDefault(item => item.id == requested.parentId);
                id = point?.customType == "curved-zipline-point" ? point.parentId : requested.parentId;
            }
            else if (requested.customType == "curved-zipline-point")
                id = requested.parentId;
            if (requested.customType == "ziprail-component")
            {
                var point = doc.objects.SingleOrDefault(item => item.id == requested.parentId);
                id = point?.customType == "ziprail-point" ? point.parentId : requested.parentId;
            }
            else if (requested.customType == "ziprail-point")
                id = requested.parentId;
            var ids = Subtree(doc, id); var source = doc.objects.Where(o => ids.Contains(o.id)).ToArray();
            var mapping = source.ToDictionary(o => o.id, o => Guid.NewGuid().ToString("N"));
            foreach (var item in source)
            {
                var copy = item.Copy(); copy.id = mapping[item.id];
                if (!string.IsNullOrEmpty(copy.parentId) && mapping.TryGetValue(copy.parentId, out var parent)) copy.parentId = parent;
                RemapInternalReferences(copy, mapping);
                if (item.id == id) { copy.displayName += L.T("#COPY"); copy.position.x += 1; }
                doc.objects.Add(copy);
            }
            return mapping[id];
        }
    }
}
