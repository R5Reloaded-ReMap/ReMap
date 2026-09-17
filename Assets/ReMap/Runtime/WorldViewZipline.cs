using ReMap.Standalone.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ReMap.Standalone
{
    public sealed partial class WorldView
    {
        private const string ZiplineMarkerName = "__remap_zipline_endpoint";
        private const string ZiplineDetachStartName = "__remap_zipline_detach_start";
        private const string ZiplineDetachEndName = "__remap_zipline_detach_end";
        private MapDocument syncedZiplineDocument;
        private Material ziplineCableMaterial, ziplineDetachMaterial;

        private static string InstanceAssetKey(MapObject item) => item.isGroup ? "group:" + (item.customType ?? "") : item.assetId;

        private void RefreshZiplineVisuals()
        {
            if (syncedZiplineDocument != null) UpdateZiplineVisuals(syncedZiplineDocument);
        }

        private void UpdateZiplineVisuals(MapDocument document)
        {
            foreach (var item in document.objects)
            {
                if (!instances.TryGetValue(item.id, out var instance)) continue;
                if (item.customType == "zipline-endpoint" || item.customType == "curved-zipline-point")
                    EnsureZiplineMarker(instance, item);
                if (item.customType == "curved-zipline-component")
                    foreach (var collider in instance.GetComponentsInChildren<Collider>(true))
                        collider.enabled = false;
            }
            foreach (var item in document.objects)
            {
                if (item.customType != "zipline" || !instances.TryGetValue(item.id, out var instance)) continue;
                var line = instance.GetComponent<LineRenderer>();
                if (line == null)
                {
                    line = instance.AddComponent<LineRenderer>();
                    line.useWorldSpace = true; line.numCapVertices = 4;
                }
                line.sharedMaterial = ZiplineCableMaterial();
                line.widthMultiplier = Mathf.Max(.1f, item.ziplineWidth) * ApexCoordinates.MetersPerUnit;
                GameObject start = null, end = null;
                bool visible = instances.TryGetValue(item.ziplineStartId, out start) && instances.TryGetValue(item.ziplineEndId, out end);
                line.enabled = visible;
                if (visible)
                {
                    var startItem = document.objects.Find(o => o.id == item.ziplineStartId);
                    var endItem = document.objects.Find(o => o.id == item.ziplineEndId);
                    Vector3 startAnchor = ZiplineCableAnchor(start, startItem);
                    Vector3 endAnchor = ZiplineCableAnchor(end, endItem);
                    if (item.ziplineMode == "vertical")
                    {
                        endAnchor = VerticalEndAnchor(startAnchor, endAnchor);
                        SetZiplineMarkerPosition(end, endAnchor);
                    }
                    Vector3 cableStart = startAnchor, cableEnd = endAnchor;
                    if (TryZiplineDetachGuides(startAnchor, endAnchor,
                        item.ziplineAutoDetachStart, item.ziplineAutoDetachEnd,
                        out var startGuideEnd, out var endGuideStart))
                    {
                        if (item.ziplineAutoDetachStart > 0f) cableStart = startGuideEnd;
                        if (item.ziplineAutoDetachEnd > 0f) cableEnd = endGuideStart;
                    }
                    var cablePoints = ZiplinePreviewPoints(cableStart, cableEnd,
                        item.ziplineLengthScale, item.ziplineMode == "vertical");
                    line.positionCount = cablePoints.Length;
                    line.SetPositions(cablePoints);
                    UpdateZiplineDetachGuides(instance, startAnchor, endAnchor,
                        item.ziplineAutoDetachStart, item.ziplineAutoDetachEnd);
                    Vector3 pushDirection = ZiplinePushDirection(item.ziplinePushOffAngle);
                    UpdateZiplinePushArrow(instance, item.ziplineMode == "vertical" && item.ziplinePushOffInDirectionX,
                        startAnchor, pushDirection);
                }
                else
                {
                    UpdateZiplineDetachGuides(instance, Vector3.zero, Vector3.zero, 0f, 0f);
                    UpdateZiplinePushArrow(instance, false, Vector3.zero, Vector3.right);
                }
            }
            foreach (var item in document.objects)
            {
                if (item.customType != "curved-zipline" || !instances.TryGetValue(item.id, out var instance))
                    continue;
                var line = instance.GetComponent<LineRenderer>();
                if (line == null)
                {
                    line = instance.AddComponent<LineRenderer>();
                    line.useWorldSpace = true; line.numCapVertices = 4;
                }
                line.sharedMaterial = ZiplineCableMaterial();
                line.widthMultiplier = Mathf.Max(.1f, item.ziplineWidth) * ApexCoordinates.MetersPerUnit;
                var controls = document.objects.Where(candidate => candidate.parentId == item.id &&
                    candidate.customType == "curved-zipline-point")
                    .OrderBy(candidate => int.TryParse(candidate.customRole, out int index) ? index : int.MaxValue)
                    .Select(candidate => instances.TryGetValue(candidate.id, out var point)
                        ? point.transform.position : Vector3.zero).ToArray();
                line.enabled = controls.Length >= 2;
                if (!line.enabled) continue;
                if (item.curvedZiplineSupport)
                    controls[0] = instance.transform.TransformPoint(
                        ReMapZiplineProfiles.UnityOffset(ReMapZiplineProfiles.ArmToCableApex));
                var curve = CurvedZiplinePreviewPoints(controls, item.curvedZiplineSegments);
                line.positionCount = curve.Length;
                line.SetPositions(curve);
            }
        }

        private void EnsureZiplineMarker(GameObject instance, MapObject item)
        {
            var markerTransform = instance.transform.Find(ZiplineMarkerName);
            GameObject marker;
            if (markerTransform == null)
            {
                marker = GameObject.CreatePrimitive(PrimitiveType.Sphere); marker.name = ZiplineMarkerName;
                marker.transform.SetParent(instance.transform, false); marker.transform.localScale = Vector3.one * .18f;
                marker.GetComponent<Collider>().enabled = false;
                marker.GetComponent<Renderer>().sharedMaterial = lineMaterial;
                instanceIds[marker] = item.id;
            }
            else marker = markerTransform.gameObject;
            marker.transform.position = ZiplineCableAnchor(instance, item);
            var tint = new MaterialPropertyBlock();
            bool start = item.customRole == "start" || item.customType == "curved-zipline-point" && item.customRole == "0";
            tint.SetColor("_BaseColor", start ? new Color(.25f, 1f, .55f) :
                item.customType == "curved-zipline-point" ? new Color(.2f, .75f, 1f) : new Color(1f, .58f, .2f));
            marker.GetComponent<Renderer>().SetPropertyBlock(tint);
        }

        public static Vector3[] CurvedZiplinePreviewPoints(IReadOnlyList<Vector3> controls, int segmentsPerSpan)
        {
            if (controls == null || controls.Count < 2) return Array.Empty<Vector3>();
            segmentsPerSpan = Mathf.Clamp(segmentsPerSpan, 2, 32);
            var tangents = new Vector3[controls.Count];
            tangents[0] = (controls[1] - controls[0]) * .5f;
            tangents[tangents.Length - 1] = (controls[controls.Count - 1] -
                controls[controls.Count - 2]) * .5f;
            for (int index = 1; index < controls.Count - 1; index++)
            {
                Vector3 direction = controls[index + 1] - controls[index - 1];
                float length = Mathf.Min(Vector3.Distance(controls[index - 1], controls[index]),
                    Vector3.Distance(controls[index], controls[index + 1])) * .5f;
                tangents[index] = direction.sqrMagnitude < .000001f
                    ? Vector3.zero : direction.normalized * length;
            }

            var result = new List<Vector3>((controls.Count - 1) * segmentsPerSpan + 1) { controls[0] };
            for (int span = 0; span < controls.Count - 1; span++)
            {
                Vector3 p0 = controls[span], p1 = p0 + tangents[span];
                Vector3 p3 = controls[span + 1], p2 = p3 - tangents[span + 1];
                for (int segment = 1; segment <= segmentsPerSpan; segment++)
                {
                    float t = segment / (float)segmentsPerSpan;
                    Vector3 a = Vector3.Lerp(p0, p1, t), b = Vector3.Lerp(p1, p2, t);
                    Vector3 c = Vector3.Lerp(p2, p3, t);
                    result.Add(Vector3.Lerp(Vector3.Lerp(a, b, t), Vector3.Lerp(b, c, t), t));
                }
            }
            return result.ToArray();
        }

        private static Vector3 ZiplineCableAnchor(GameObject instance, MapObject item)
        {
            if (instance == null || item == null) return instance == null ? Vector3.zero : instance.transform.position;
            var apexOffset = ReMapZiplineProfiles.CableOffsetApex(item.customProfile, item.ziplineArmHeight);
            return instance.transform.TransformPoint(ReMapZiplineProfiles.UnityOffset(apexOffset));
        }

        public Vector3 ZiplineCableAnchorPosition(string endpointId)
        {
            if (!instances.TryGetValue(endpointId, out var instance) || syncedZiplineDocument == null)
                return Vector3.zero;
            return ZiplineCableAnchor(instance,
                syncedZiplineDocument.objects.Find(item => item.id == endpointId));
        }

        private static Vector3 VerticalEndAnchor(Vector3 startAnchor, Vector3 endAnchor) =>
            new Vector3(startAnchor.x, endAnchor.y, startAnchor.z);

        public static Vector3[] ZiplinePreviewPoints(Vector3 start, Vector3 end,
            float lengthScale, bool vertical, int segments = 24)
        {
            segments = Mathf.Clamp(segments, 1, 128);
            var points = new Vector3[segments + 1];
            Vector3 delta = end - start;
            float distance = delta.magnitude;
            float horizontal = new Vector2(delta.x, delta.z).magnitude;
            // Apex computes its cable shape natively. Approximate a uniformly loaded cable with
            // y = gL² / 8H: Earth gravity supplies the load and lengthScale controls an empirical
            // horizontal tension. Squaring it makes loose values visibly softer while 1 stays taut,
            // but never perfectly straight as it is in game.
            const float gravity = 9.81f;
            const float referenceTension = 1200f;
            float tension = Mathf.Max(.1f, Mathf.Clamp(lengthScale, 0f, 1.2f));
            float sag = vertical || distance < .0001f ? 0f :
                gravity * horizontal * horizontal / (8f * referenceTension * tension * tension);
            sag = Mathf.Min(sag, distance * .35f);
            for (int index = 0; index <= segments; index++)
            {
                float t = index / (float)segments;
                points[index] = Vector3.Lerp(start, end, t) + Vector3.down * (sag * 4f * t * (1f - t));
            }
            return points;
        }

        private static void SetZiplineMarkerPosition(GameObject endpoint, Vector3 position)
        {
            var marker = endpoint?.transform.Find(ZiplineMarkerName);
            if (marker != null) marker.position = position;
        }

        public Vector3 ZiplineGizmoPivot(string endpointId)
        {
            if (!instances.TryGetValue(endpointId, out var instance) || syncedZiplineDocument == null)
                return Vector3.zero;
            var item = syncedZiplineDocument.objects.Find(o => o.id == endpointId);
            if (item == null) return instance.transform.position;
            var zipline = syncedZiplineDocument.objects.Find(o => o.id == item.parentId);
            if (item.customRole == "end" && zipline?.customType == "zipline" &&
                zipline.ziplineMode == "vertical" &&
                instances.TryGetValue(zipline.ziplineStartId, out var start))
            {
                var startItem = syncedZiplineDocument.objects.Find(o => o.id == zipline.ziplineStartId);
                return VerticalEndAnchor(ZiplineCableAnchor(start, startItem),
                    ZiplineCableAnchor(instance, item));
            }
            return ReMapZiplineProfiles.Find(item.customProfile).HasSupport
                ? instance.transform.position : ZiplineCableAnchor(instance, item);
        }

        private static bool TryLocalGeometryBounds(GameObject instance, out Bounds bounds)
        {
            bounds = default; bool found = false; var worldToLocal = instance.transform.worldToLocalMatrix;
            foreach (var renderer in instance.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer.gameObject.name == ZiplineMarkerName) continue;
                var filter = renderer.GetComponent<MeshFilter>();
                if (filter?.sharedMesh == null) continue;
                var source = filter.sharedMesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var corner = source.center + Vector3.Scale(source.extents,
                        new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    var point = worldToLocal.MultiplyPoint3x4(renderer.transform.TransformPoint(corner));
                    if (!found) { bounds = new Bounds(point, Vector3.zero); found = true; }
                    else bounds.Encapsulate(point);
                }
            }
            return found;
        }

        private Material ZiplineCableMaterial()
        {
            if (ziplineCableMaterial != null) return ziplineCableMaterial;
            ziplineCableMaterial = new Material(lineMaterial) { name = "cable/zipline.vmt preview" };
            ziplineCableMaterial.SetColor("_BaseColor", new Color(1f, .82f, .05f));
            return ziplineCableMaterial;
        }

        private Material ZiplineDetachMaterial()
        {
            if (ziplineDetachMaterial != null) return ziplineDetachMaterial;
            ziplineDetachMaterial = new Material(lineMaterial) { name = "zipline auto-detach preview" };
            ziplineDetachMaterial.SetColor("_BaseColor", new Color(1f, .18f, .12f));
            return ziplineDetachMaterial;
        }

        public static bool TryZiplineDetachGuides(Vector3 start, Vector3 end,
            float startApex, float endApex, out Vector3 startGuideEnd, out Vector3 endGuideStart)
        {
            startGuideEnd = start; endGuideStart = end;
            Vector3 delta = end - start;
            float length = delta.magnitude;
            float startDistance = Mathf.Max(0f, startApex) * ApexCoordinates.MetersPerUnit;
            float endDistance = Mathf.Max(0f, endApex) * ApexCoordinates.MetersPerUnit;
            if (length < .0001f || length <= startDistance + endDistance) return false;
            Vector3 direction = delta / length;
            startGuideEnd = start + direction * startDistance;
            endGuideStart = end - direction * endDistance;
            return startDistance > .0001f || endDistance > .0001f;
        }

        public static Vector3 ZiplinePushDirection(float worldYaw) =>
            Quaternion.AngleAxis(-worldYaw, Vector3.up) * Vector3.right;

        private LineRenderer ZiplineDetachGuide(GameObject zipline, string name)
        {
            var child = zipline.transform.Find(name);
            if (child != null) return child.GetComponent<LineRenderer>();
            var line = new GameObject(name, typeof(LineRenderer)).GetComponent<LineRenderer>();
            line.transform.SetParent(zipline.transform, false);
            line.useWorldSpace = true; line.positionCount = 2; line.numCapVertices = 4;
            line.widthMultiplier = .055f; line.sharedMaterial = ZiplineDetachMaterial();
            return line;
        }

        private void UpdateZiplineDetachGuides(GameObject zipline, Vector3 start, Vector3 end,
            float startApex, float endApex)
        {
            bool visible = TryZiplineDetachGuides(start, end, startApex, endApex,
                out var startGuideEnd, out var endGuideStart);
            var startGuide = ZiplineDetachGuide(zipline, ZiplineDetachStartName);
            var endGuide = ZiplineDetachGuide(zipline, ZiplineDetachEndName);
            startGuide.enabled = visible && startApex > 0f;
            endGuide.enabled = visible && endApex > 0f;
            if (!visible) return;
            startGuide.SetPosition(0, start); startGuide.SetPosition(1, startGuideEnd);
            endGuide.SetPosition(0, end); endGuide.SetPosition(1, endGuideStart);
        }

        private void UpdateZiplinePushArrow(GameObject zipline, bool visible, Vector3 origin, Vector3 direction)
        {
            const string name = "__remap_zipline_push_direction";
            var child = zipline.transform.Find(name);
            LineRenderer arrow;
            if (child == null)
            {
                arrow = new GameObject(name, typeof(LineRenderer)).GetComponent<LineRenderer>();
                arrow.transform.SetParent(zipline.transform, false);
                arrow.useWorldSpace = true;
                arrow.positionCount = 5;
                arrow.numCapVertices = 4;
                arrow.sharedMaterial = lineMaterial;
                arrow.widthMultiplier = .035f;
            }
            else arrow = child.GetComponent<LineRenderer>();
            arrow.enabled = visible;
            if (!visible) return;
            direction = direction.sqrMagnitude > .0001f ? direction.normalized : Vector3.right;
            Vector3 side = Vector3.Cross(Vector3.up, direction);
            if (side.sqrMagnitude < .0001f) side = Vector3.forward;
            side.Normalize();
            float length = 48f * ApexCoordinates.MetersPerUnit;
            float head = 14f * ApexCoordinates.MetersPerUnit;
            Vector3 tip = origin + direction * length;
            arrow.SetPosition(0, origin);
            arrow.SetPosition(1, tip);
            arrow.SetPosition(2, tip - direction * head + side * head * .55f);
            arrow.SetPosition(3, tip);
            arrow.SetPosition(4, tip - direction * head - side * head * .55f);
        }

        public bool TryZiplineSurfaceBelow(string endpointId, HashSet<string> excludedIds, out float surfaceHeight)
        {
            surfaceHeight = 0f;
            if (!instances.TryGetValue(endpointId, out var endpoint)) return false;
            return TryZiplineSurfaceBelow(endpoint.transform.position, excludedIds, out surfaceHeight);
        }

        public bool TryZiplineSurfaceBelow(Vector3 origin, HashSet<string> excludedIds, out float surfaceHeight)
        {
            surfaceHeight = 0f;
            origin += Vector3.up * .02f;
            float nearest = float.PositiveInfinity;
            foreach (var hit in Physics.RaycastAll(origin, Vector3.down, ApexCoordinates.MaxUnityCoord * 2f,
                ~(1 << 31), QueryTriggerInteraction.Ignore))
            {
                string id = null;
                for (var current = hit.collider.transform; current != null && id == null; current = current.parent)
                    instanceIds.TryGetValue(current.gameObject, out id);
                if (id == null || excludedIds.Contains(id) || hit.distance >= nearest) continue;
                nearest = hit.distance;
                surfaceHeight = hit.point.y;
            }
            if (TryBspSurfaceBelow(origin, out float bspHeight))
            {
                float distance = origin.y - bspHeight;
                if (distance >= 0f && distance < nearest)
                {
                    nearest = distance;
                    surfaceHeight = bspHeight;
                }
            }
            return !float.IsPositiveInfinity(nearest);
        }

        private void PreviewCustom(CatalogEntry entry, Vector3? position)
        {
            if (entry == null || !position.HasValue) { ClearPreview(); return; }
            if (ghost == null || ghostAsset != entry.Id)
            {
                ClearPreview(); ghostAsset = entry.Id;
                if (entry.CustomType == "loot-bin")
                {
                    ghost = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    ghost.name = "Loot bin placement preview";
                    ghost.transform.SetParent(root.transform);
                    ghost.transform.localScale = entry.Size;
                    ghost.GetComponent<Collider>().enabled = false;
                    ghost.GetComponent<Renderer>().sharedMaterial = lineMaterial;
                    ghost.transform.position = position.Value + Vector3.up * entry.Size.y * .5f;
                    return;
                }
                if (entry.CustomType == "curved-zipline")
                {
                    ghost = new GameObject("Curved zipline placement preview");
                    ghost.transform.SetParent(root.transform);
                    var controls = new[] { Vector3.zero, new Vector3(3.8f, 1.2f, 1.5f), new Vector3(7.6f, 0f, 0f) };
                    foreach (var control in controls)
                    {
                        var marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                        marker.transform.SetParent(ghost.transform, false);
                        marker.transform.localPosition = control;
                        marker.transform.localScale = Vector3.one * .18f;
                        marker.GetComponent<Collider>().enabled = false;
                        marker.GetComponent<Renderer>().sharedMaterial = lineMaterial;
                    }
                    var curveLine = ghost.AddComponent<LineRenderer>();
                    curveLine.sharedMaterial = lineMaterial; curveLine.useWorldSpace = false;
                    var curve = CurvedZiplinePreviewPoints(controls, 8);
                    curveLine.positionCount = curve.Length; curveLine.SetPositions(curve);
                    curveLine.widthMultiplier = .04f; curveLine.numCapVertices = 4;
                    ghost.transform.position = position.Value;
                    return;
                }
                if (entry.CustomType == "door")
                {
                    ghost = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    ghost.name = "Door placement preview";
                    ghost.transform.SetParent(root.transform);
                    ghost.transform.localScale = entry.Size;
                    ghost.GetComponent<Collider>().enabled = false;
                    ghost.GetComponent<Renderer>().sharedMaterial = lineMaterial;
                    var doorTint = new MaterialPropertyBlock();
                    doorTint.SetColor("_BaseColor", new Color(.3f, .95f, .65f));
                    ghost.GetComponent<Renderer>().SetPropertyBlock(doorTint);
                    ghost.transform.position = position.Value + Vector3.up * entry.Size.y * .5f;
                    return;
                }
                ghost = new GameObject("Zipline placement preview"); ghost.transform.SetParent(root.transform);
                float length = Mathf.Max(1, entry.Size.y);
                var start = GameObject.CreatePrimitive(PrimitiveType.Cylinder); start.transform.SetParent(ghost.transform, false);
                start.transform.localPosition = Vector3.up * .5f; start.transform.localScale = new Vector3(.12f, .5f, .12f);
                var arm = GameObject.CreatePrimitive(PrimitiveType.Cube); arm.transform.SetParent(ghost.transform, false);
                arm.transform.localPosition = new Vector3(.35f, 1f, 0); arm.transform.localScale = new Vector3(.7f, .1f, .1f);
                var end = GameObject.CreatePrimitive(PrimitiveType.Sphere); end.transform.SetParent(ghost.transform, false);
                end.transform.localPosition = Vector3.down * length; end.transform.localScale = Vector3.one * .18f;
                foreach (var part in new[] { start, arm, end })
                {
                    part.GetComponent<Collider>().enabled = false; part.GetComponent<Renderer>().sharedMaterial = lineMaterial;
                    var tint = new MaterialPropertyBlock(); tint.SetColor("_BaseColor", new Color(.3f, .95f, .65f));
                    part.GetComponent<Renderer>().SetPropertyBlock(tint);
                }
                var line = ghost.AddComponent<LineRenderer>(); line.sharedMaterial = lineMaterial; line.useWorldSpace = false;
                line.positionCount = 2; line.SetPosition(0, Vector3.up); line.SetPosition(1, Vector3.down * length);
                line.widthMultiplier = .04f; line.numCapVertices = 4;
            }
            ghost.transform.position = position.Value + (entry.CustomType == "door" || entry.CustomType == "loot-bin"
                ? Vector3.up * entry.Size.y * .5f : Vector3.zero);
        }
    }
}
