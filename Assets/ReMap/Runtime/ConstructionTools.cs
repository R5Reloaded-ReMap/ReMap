using System;
using System.Collections.Generic;
using System.Linq;
using ReMap.Standalone.Core;
using UnityEngine;
namespace ReMap.Standalone
{
    public enum GroupPivotAnchor
    {
        BottomCenter,
        Center,
        TopCenter
    }

    public static class ConstructionTools
    {
        public static Vector3 PivotTarget(Bounds bounds, GroupPivotAnchor anchor)
        {
            if (anchor == GroupPivotAnchor.BottomCenter)
                return new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
            if (anchor == GroupPivotAnchor.TopCenter)
                return new Vector3(bounds.center.x, bounds.max.y, bounds.center.z);
            return bounds.center;
        }

        public static string[] PivotReferences(MapDocument doc, string groupId, IEnumerable<string> selectedRoots)
        {
            var group = doc.objects.Find(item => item.id == groupId);
            if (group == null || !group.isGroup) return Array.Empty<string>();
            var subtree = MapHierarchy.Subtree(doc, groupId);
            return (selectedRoots ?? Array.Empty<string>()).Where(subtree.Contains).Distinct().ToArray();
        }

        public static void RepositionGroupPivot(MapDocument doc, WorldView world, string id, Vector3 targetWorldPosition)
        {
            var group = doc.objects.Find(item => item.id == id);
            if (group == null || !group.isGroup || !string.IsNullOrEmpty(group.customType))
                throw new ArgumentException(L.T("#SELECT_SINGLE_FOLDER_PIVOT"));
            if (!WorldView.ToData(targetWorldPosition).IsFinite || !ApexCoordinates.ContainsUnity(WorldView.ToData(targetWorldPosition)))
                throw new ArgumentException(ApexCoordinates.LimitMessage);

            var pose = world.WorldPose(id);
            Vector3 currentWorldPosition = WorldView.ToVector(pose.position);
            Vector3 worldDelta = targetWorldPosition - currentWorldPosition;
            if (worldDelta.sqrMagnitude < .00000001f) return;

            // The group's linear transform does not change. Compensating its direct children
            // in the old group space keeps the complete descendant geometry fixed in world space.
            Vector3 childLocalDelta = world.LocalDelta(id, worldDelta);
            foreach (var child in doc.objects.Where(item => item.parentId == id))
                child.position = WorldView.ToData(WorldView.ToVector(child.position) - childLocalDelta);

            world.StoreWorldPose(group, targetWorldPosition, WorldView.ToVector(pose.rotation));
        }

        public static string[] Targets(MapDocument doc, string id, bool children)
        {
            var item = doc.objects.Find(o => o.id == id);
            if (item == null) throw new ArgumentException(L.T("#SELECT_OBJECT_GROUP_0ED54C"));
            if (!MapHierarchy.IsEnabled(doc, id)) throw new ArgumentException(L.T("#ENABLE_SELECTION_USE_TOOL"));
            if (!children || !item.isGroup) return new[] { id };
            var subtree = MapHierarchy.Subtree(doc, id);
            var byId = doc.objects.ToDictionary(o => o.id);
            bool Active(MapObject candidate) {
                while (candidate != null) {
                    if (candidate.disabled) return false;
                    candidate = !string.IsNullOrEmpty(candidate.parentId) && byId.TryGetValue(candidate.parentId, out var parent) ? parent : null;
                }
                return true;
            }
            return doc.objects.Where(o => subtree.Contains(o.id) && !o.isGroup && Active(o)).Select(o => o.id).ToArray();
        }
        public static void Budget(MapDocument doc, int added)
        {
            if (added > 1000 || (long)doc.objects.Count + added > 10000)
                throw new ArgumentException(L.T("#TOO_MANY_COPIES_MOST_1"));
        }
        public static string CloneBranch(MapDocument doc, string id, string parent, Vector3 position)
        {
            var ids = MapHierarchy.Subtree(doc, id);
            var source = doc.objects.Where(o => ids.Contains(o.id)).ToArray();
            Budget(doc, source.Length);
            var mapping = source.ToDictionary(o => o.id, o => Guid.NewGuid().ToString("N"));
            foreach (var item in source)
            {
                var copy = item.Copy(); copy.id = mapping[item.id];
                if (item.id == id) { copy.parentId = parent; copy.position = WorldView.ToData(position); }
                else copy.parentId = mapping[item.parentId];
                MapHierarchy.RemapInternalReferences(copy, mapping);
                doc.objects.Add(copy);
            }
            return mapping[id];
        }
        public static string Duplicate(MapDocument doc, WorldView world, string id, Vector3 worldOffset)
        {
            if (!WorldView.ToData(worldOffset).IsFinite) throw new ArgumentException(L.T("#INVALID_SPACING"));
            var source = doc.objects.Single(o => o.id == id);
            return CloneBranch(doc, id, source.parentId, WorldView.ToVector(source.position) + world.ParentDelta(id, worldOffset));
        }
        public static string Grid(MapDocument doc, WorldView world, string id, int columns, int rows, Vector3 columnStep, Vector3 rowStep)
        {
            if (columns < 1 || rows < 1 || columns > 100 || rows > 100 || (long)columns * rows < 2)
                throw new ArgumentException(L.T("#CHOOSE_1_100_ROWS_COLUMNS"));
            if (!WorldView.ToData(columnStep).IsFinite || !WorldView.ToData(rowStep).IsFinite || (columns > 1 && columnStep.sqrMagnitude < .000001f) || (rows > 1 && rowStep.sqrMagnitude < .000001f))
                throw new ArgumentException(L.T("#SPACING_POSITIVE_USED_AXIS"));
            int branchSize = MapHierarchy.Subtree(doc, id).Count;
            Budget(doc, checked((columns * rows - 1) * branchSize + 1));
            var source = doc.objects.Single(o => o.id == id);
            var folder = new MapObject { isGroup = true, assetId = "group:", displayName = L.T("#GRID") + columns + " × " + rows, parentId = source.parentId };
            var original = WorldView.ToVector(source.position);
            // The source becomes the first cell. The identity folder preserves its complete local transform.
            doc.objects.Add(folder); source.parentId = folder.id;
            for (int row = 0; row < rows; row++) for (int column = 0; column < columns; column++)
            {
                if (row == 0 && column == 0) continue;
                CloneBranch(doc, id, folder.id, original + world.ParentDelta(id, columnStep * column + rowStep * row));
            }
            return folder.id;
        }
        public static int PlaceTop(MapDocument doc, WorldView world, string[] targets, float clearance)
        {
            Range(clearance, 0, 1000, L.T("#MARGIN"));
            var excluded = ExpandedTargets(doc, targets);
            var supports = world.GroundSupports(excluded);
            int changed = 0;
            foreach (string id in targets)
            {
                var pose = world.WorldPose(id); var position = WorldView.ToVector(pose.position);
                if (!world.TryCeilingPosition(id, supports, clearance, out position)) continue;
                if (Vector3.Distance(position, WorldView.ToVector(pose.position)) < .0001f) continue;
                world.StoreWorldPose(doc.objects.Single(o => o.id == id), position, WorldView.ToVector(pose.rotation)); changed++;
            }
            return changed;
        }
        private static HashSet<string> ExpandedTargets(MapDocument doc, IEnumerable<string> targets)
        {
            var excluded = new HashSet<string>(targets); bool expanded;
            do { expanded = false; foreach (var item in doc.objects) if (!string.IsNullOrEmpty(item.parentId) && excluded.Contains(item.parentId)) expanded |= excluded.Add(item.id); } while (expanded);
            return excluded;
        }
        public static string CornerTurnCopy(MapDocument doc, WorldView world, string id, Vector3 pivot, Vector3 axis, float orbitAngle, float objectAngle, Vector3 anchorOffset, Vector3? objectPivot = null)
        {
            var sourcePose = world.WorldPose(id); var sourcePosition = WorldView.ToVector(sourcePose.position);
            var sourceRotation = Quaternion.Euler(WorldView.ToVector(sourcePose.rotation));
            var orbit = Quaternion.AngleAxis(orbitAngle, axis.normalized); var objectRotation = Quaternion.AngleAxis(objectAngle, axis.normalized);
            var targetPosition = pivot + orbit * (sourcePosition - pivot);
            if (objectPivot.HasValue)
            {
                var turnedPivot = pivot + orbit * (objectPivot.Value - pivot);
                targetPosition = turnedPivot + objectRotation * (targetPosition - turnedPivot);
            }
            string copyId = Duplicate(doc, world, id, Vector3.zero);
            var copy = doc.objects.Single(o => o.id == copyId);
            world.StoreWorldPoseForParent(copy, targetPosition + anchorOffset, (objectRotation * orbit * sourceRotation).eulerAngles);
            return copyId;
        }
        public static int Drop(MapDocument doc, WorldView world, string[] targets, bool firstSurface, float clearance)
        {
            Range(clearance, 0, 1000, L.T("#MARGIN"));
            var excluded = ExpandedTargets(doc, targets);
            var supports = world.GroundSupports(excluded);
            int changed = 0;
            foreach (string id in targets)
            {
                var bounds = world.GeometryBounds(id); if (!bounds.HasValue) continue;
                var pose = world.WorldPose(id); var position = WorldView.ToVector(pose.position);
                if (firstSurface) { if (!world.TryGroundPosition(id, supports, clearance, out position)) continue; }
                else position.y += clearance - bounds.Value.min.y;
                if (Vector3.Distance(position, WorldView.ToVector(pose.position)) < .0001f) continue;
                world.StoreWorldPose(doc.objects.Single(o => o.id == id), position, WorldView.ToVector(pose.rotation)); changed++;
            }
            return changed;
        }
        public static void Randomize(MapDocument doc, WorldView world, string[] targets, bool rotation, float min, float max, System.Random random)
        {
            Range(min, rotation ? -360 : .01f, rotation ? 360 : 100, L.T("#MINIMUM"));
            Range(max, rotation ? -360 : .01f, rotation ? 360 : 100, L.T("#MAXIMUM"));
            if (max < min) throw new ArgumentException(L.T("#MAXIMUM_GREATER_THAN_EQUAL_MINIMUM"));
            foreach (string id in targets)
            {
                var item = doc.objects.Single(o => o.id == id); float value = Mathf.Lerp(min, max, (float)random.NextDouble());
                if (rotation)
                {
                    var pose = world.WorldPose(id);
                    var q = Quaternion.AngleAxis(value, Vector3.up) * Quaternion.Euler(WorldView.ToVector(pose.rotation));
                    world.StoreWorldPose(item, WorldView.ToVector(pose.position), q.eulerAngles);
                }
                else item.scale = WorldView.ToData(WorldView.ToVector(item.scale) * value);
            }
        }
        public static void Range(float value, float min, float max, string label)
        {
            if (!float.IsFinite(value) || value < min || value > max)
                throw new ArgumentException(label + L.T("#EXPECTED_VALUE_BETWEEN") + min + L.T("#MESSAGE") + max + ".");
        }
    }
}
