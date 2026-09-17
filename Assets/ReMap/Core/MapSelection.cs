using System.Collections.Generic;
using System.Linq;
namespace ReMap.Standalone.Core
{
    public static class MapSelection
    {
        // Selecting a folder and one of its descendants must transform the branch only once.
        public static List<string> Roots(MapDocument document, IEnumerable<string> selection)
        {
            var lookup = document.objects.ToDictionary(o => o.id);
            var ids = new HashSet<string>(selection.Where(id => id != null && lookup.ContainsKey(id)));
            return document.objects.Where(o => ids.Contains(o.id) && !HasSelectedParent(o, lookup, ids)).Select(o => o.id).ToList();
        }
        private static bool HasSelectedParent(MapObject item, Dictionary<string, MapObject> lookup, HashSet<string> ids)
        {
            while (!string.IsNullOrEmpty(item.parentId) && lookup.TryGetValue(item.parentId, out item))
                if (ids.Contains(item.id)) return true;
            return false;
        }
        public static HashSet<string> Branches(MapDocument document, IEnumerable<string> roots)
        {
            var result = new HashSet<string>(roots);
            var children = document.objects.GroupBy(o => o.parentId ?? "").ToDictionary(g => g.Key, g => g.Select(o => o.id).ToArray());
            var queue = new Queue<string>(result);
            while (queue.Count > 0) if (children.TryGetValue(queue.Dequeue(), out var nested))
                foreach (var id in nested) if (result.Add(id)) queue.Enqueue(id);
            return result;
        }
        public static string PasteParent(MapDocument document, string focusedFolderId, string originalParent)
        {
            if (!string.IsNullOrEmpty(focusedFolderId) && document.objects.Any(item => item.id == focusedFolderId && item.isGroup))
                return focusedFolderId;
            return !string.IsNullOrEmpty(originalParent) && document.objects.Any(item => item.id == originalParent) ? originalParent : "";
        }
    }
}
