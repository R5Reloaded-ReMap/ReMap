using System.Collections.Generic;
using UnityEngine;
namespace ReMap.Standalone
{
    public sealed partial class WorldView
    {
        public static bool IsConstructionGeometryRenderer(Renderer renderer) =>
            renderer != null && renderer.enabled && !(renderer is LineRenderer) &&
            renderer.gameObject.name != ZiplineMarkerName;

        public Bounds? GeometryBounds(string id)
        {
            if (id == null || !instances.TryGetValue(id, out var instance)) return null;
            Bounds result = default; bool found = false;
            foreach (var renderer in instance.GetComponentsInChildren<Renderer>())
            {
                if (!IsConstructionGeometryRenderer(renderer)) continue;
                if (!found) { result = renderer.bounds; found = true; }
                else result.Encapsulate(renderer.bounds);
            }
            if (found) return result;
            var item = syncedZiplineDocument?.objects.Find(candidate => candidate.id == id);
            return item != null && (item.customType == "zipline" || item.customType == "zipline-endpoint")
                ? new Bounds(instance.transform.position, Vector3.zero) : (Bounds?)null;
        }
        public Vector3 ParentDelta(string id, Vector3 delta) => instances[id].transform.parent.InverseTransformVector(delta);
        public Vector3 LocalDelta(string id, Vector3 delta) => instances[id].transform.InverseTransformVector(delta);
        // Project mesh bounds onto the requested world axis, without temporarily rotating the scene.
        public float ProjectedSize(string id, Vector3 axis)
        {
            float min = float.PositiveInfinity, max = float.NegativeInfinity;
            EncapsulateProjection(id, axis, ref min, ref max);
            return float.IsFinite(min) ? max - min : 0;
        }
        public bool TryProjectedRange(IEnumerable<string> ids, Vector3 axis, out float min, out float max)
        {
            min = float.PositiveInfinity; max = float.NegativeInfinity;
            foreach (var id in ids) if (instances.ContainsKey(id)) EncapsulateProjection(id, axis, ref min, ref max);
            return float.IsFinite(min);
        }
        public bool TryProjectedRangeAfterTurn(IEnumerable<string> roots, Vector3 pivot, Quaternion orbit, Quaternion objectRotation, Vector3 axis, out float min, out float max)
            => TryProjectedRangeAfterTurn(roots, pivot, orbit, objectRotation, null, axis, out min, out max);
        public bool TryProjectedRangeAfterTurn(IEnumerable<string> roots, Vector3 pivot, Quaternion orbit, Quaternion objectRotation, Vector3 objectPivot, Vector3 axis, out float min, out float max)
            => TryProjectedRangeAfterTurn(roots, pivot, orbit, objectRotation, (Vector3?)objectPivot, axis, out min, out max);
        private bool TryProjectedRangeAfterTurn(IEnumerable<string> roots, Vector3 pivot, Quaternion orbit, Quaternion objectRotation, Vector3? objectPivot, Vector3 axis, out float min, out float max)
        {
            min = float.PositiveInfinity; max = float.NegativeInfinity; var delta = objectRotation * orbit;
            foreach (var id in roots)
            {
                if (!instances.TryGetValue(id, out var root)) continue;
                var sourceRoot = root.transform.position; var targetRoot = pivot + orbit * (sourceRoot - pivot);
                if (objectPivot.HasValue)
                {
                    var turnedPivot = pivot + orbit * (objectPivot.Value - pivot);
                    targetRoot = turnedPivot + objectRotation * (targetRoot - turnedPivot);
                }
                foreach (var renderer in root.GetComponentsInChildren<Renderer>())
                {
                    var filter = renderer.GetComponent<MeshFilter>();
                    var bounds = filter?.sharedMesh != null ? filter.sharedMesh.bounds : renderer.bounds;
                    for (int i = 0; i < 8; i++)
                    {
                        var point = bounds.center + Vector3.Scale(bounds.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                        if (filter?.sharedMesh != null) point = renderer.transform.TransformPoint(point);
                        point = targetRoot + delta * (point - sourceRoot);
                        float projection = Vector3.Dot(point, axis); min = Mathf.Min(min, projection); max = Mathf.Max(max, projection);
                    }
                }
            }
            return float.IsFinite(min);
        }
        private void EncapsulateProjection(string id, Vector3 axis, ref float min, ref float max)
        {
            foreach (var renderer in instances[id].GetComponentsInChildren<Renderer>())
            {
                var filter = renderer.GetComponent<MeshFilter>();
                var b = filter?.sharedMesh != null ? filter.sharedMesh.bounds : renderer.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var p = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    if (filter?.sharedMesh != null) p = renderer.transform.TransformPoint(p);
                    float d = Vector3.Dot(p, axis); min = Mathf.Min(min, d); max = Mathf.Max(max, d);
                }
            }
        }
        public HashSet<Collider> GroundSupports(HashSet<string> excluded)
        {
            var supports = new HashSet<Collider>();
            foreach (var pair in instances)
                if (!excluded.Contains(pair.Key) && pair.Value.activeInHierarchy)
                    foreach (var collider in pair.Value.GetComponents<Collider>())
                        if (collider.enabled && !collider.isTrigger) supports.Add(collider);
            return supports;
        }
        public bool TryConstructionSurface(Ray ray, HashSet<Collider> supports, out Vector3 point)
        {
            point = Vector3.zero; float nearest = 200000f; bool found = false;
            foreach (var hit in Physics.RaycastAll(ray, nearest, ~0, QueryTriggerInteraction.Ignore))
            {
                if (!supports.Contains(hit.collider) || hit.distance >= nearest) continue;
                nearest = hit.distance; point = hit.point; found = true;
            }
            if (TryBspRaycast(ray, nearest, out var bspPoint, out float bspDistance) && bspDistance < nearest)
            {
                point = bspPoint; found = true;
            }
            return found;
        }
        public bool TryCeilingPosition(string id, HashSet<Collider> supports, float clearance, out Vector3 position)
        {
            position = ToVector(WorldPose(id).position);
            var bounds = GeometryBounds(id); if (!bounds.HasValue) return false;
            var b = bounds.Value; float height = float.PositiveInfinity;
            for (int x = -1; x <= 1; x++) for (int z = -1; z <= 1; z++)
            {
                var origin = new Vector3(b.center.x + b.extents.x * x * .98f, b.max.y - .005f, b.center.z + b.extents.z * z * .98f);
                if (TryConstructionSurface(new Ray(origin, Vector3.up), supports, out var point) &&
                    point.y >= b.max.y - .005f)
                    height = Mathf.Min(height, point.y);
            }
            if (!float.IsFinite(height)) return false;
            position.y += height - b.max.y - clearance;
            return true;
        }
        public bool TryGroundPosition(string id, HashSet<Collider> supports, float clearance, out Vector3 position)
        {
            position = ToVector(WorldPose(id).position);
            var bounds = GeometryBounds(id); if (!bounds.HasValue) return false;
            var b = bounds.Value; float height = float.NegativeInfinity;
            // Models use their runtime colliders; the map uses its non-PhysX BSP query.
            // The construction grid is intentionally excluded and remains available through the explicit Y=0 action.
            for (int x = -1; x <= 1; x++) for (int z = -1; z <= 1; z++)
            {
                var origin = new Vector3(b.center.x + b.extents.x * x * .98f, b.min.y + .005f, b.center.z + b.extents.z * z * .98f);
                var ray = new Ray(origin, Vector3.down);
                if (TryConstructionSurface(ray, supports, out var point) && point.y <= b.min.y + .005f)
                    height = Mathf.Max(height, point.y);
            }
            if (!float.IsFinite(height)) return false;
            position.y += height - b.min.y + clearance;
            return true;
        }
    }
}
