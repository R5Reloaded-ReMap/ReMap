using ReMap.Standalone.Core;
using UnityEngine;

namespace ReMap.Standalone
{
    public sealed partial class WorldView
    {
        internal const string DoorOpeningArrowName = "__remap_door_opening_direction";
        internal const string DoorOppositeOpeningArrowName = "__remap_door_opening_direction_opposite";

        internal static bool DoorShowsOpeningDirection(MapObject door) => door != null && door.customType == "door" && (door.doorType == "single" || door.doorType == "double");

        internal static Vector3 DoorOpeningDirection(Quaternion worldRotation) => worldRotation * Vector3.forward;

        internal static Vector3[] DoorOpeningArc(Vector3 hinge, Vector3 closedDirection, Vector3 openDirection, Vector3 up, float radius, int segments = 18)
        {
            closedDirection.Normalize();
            openDirection.Normalize();
            up.Normalize();
            var arc = new Vector3[segments + 1];
            for (int index = 0; index <= segments; index++) arc[index] = hinge + Vector3.Slerp(closedDirection, openDirection, index / (float)segments).normalized * radius;
            var points = new Vector3[segments + 7];
            float head = radius * .28f;
            Vector3 startTangent = (arc[1] - arc[0]).normalized;
            Vector3 startSide = Vector3.Cross(up, startTangent).normalized;
            points[0] = arc[0] + startTangent * head + startSide * head * .65f;
            points[1] = arc[0];
            points[2] = arc[0] + startTangent * head - startSide * head * .65f;
            points[3] = arc[0];
            for (int index = 1; index <= segments; index++) points[index + 3] = arc[index];
            Vector3 tip = arc[segments];
            Vector3 tangent = (tip - arc[segments - 1]).normalized;
            Vector3 side = Vector3.Cross(up, tangent).normalized;
            points[segments + 4] = tip - tangent * head + side * head * .65f;
            points[segments + 5] = tip;
            points[segments + 6] = tip - tangent * head - side * head * .65f;
            return points;
        }

        private void EnsureDoorOpeningArrow(GameObject instance, MapObject door)
        {
            LineRenderer primary = EnsureDoorOpeningGuide(instance, DoorOpeningArrowName);
            LineRenderer opposite = EnsureDoorOpeningGuide(instance, DoorOppositeOpeningArrowName);
            bool visible = DoorShowsOpeningDirection(door);
            primary.enabled = visible;
            opposite.enabled = visible && door.doorType == "double";
            if (!visible) return;

            Vector3 forward = instance.transform.forward.normalized;
            Vector3 right = instance.transform.right.normalized;
            Vector3 up = instance.transform.up.normalized;
            float elevation = DoorArrowElevation(instance, up);
            float radius = 64f * ApexCoordinates.MetersPerUnit;
            float hingeOffset = 64f * ApexCoordinates.MetersPerUnit;
            Vector3 anchor = instance.transform.position + up * elevation;
            if (door.doorType == "double")
            {
                UpdateDoorOpeningGuide(primary, anchor + right * hingeOffset, -right, forward, up, radius);
                UpdateDoorOpeningGuide(opposite, anchor - right * hingeOffset, right, forward, up, radius);
            }
            else UpdateDoorOpeningGuide(primary, anchor - right * hingeOffset, right, forward, up, radius);
        }

        private LineRenderer EnsureDoorOpeningGuide(GameObject instance, string name)
        {
            var child = instance.transform.Find(name);
            LineRenderer guide;
            if (child == null)
            {
                guide = new GameObject(name, typeof(LineRenderer)).GetComponent<LineRenderer>();
                guide.transform.SetParent(instance.transform, false);
                guide.useWorldSpace = true;
                guide.numCapVertices = 4;
                guide.numCornerVertices = 4;
                guide.widthMultiplier = .065f;
                guide.sharedMaterial = lineMaterial;
                var tint = new MaterialPropertyBlock();
                tint.SetColor("_BaseColor", new Color(1f, .55f, .08f));
                guide.SetPropertyBlock(tint);
            }
            else guide = child.GetComponent<LineRenderer>();
            return guide;
        }

        private static void UpdateDoorOpeningGuide(LineRenderer guide, Vector3 hinge, Vector3 closedDirection, Vector3 openDirection, Vector3 up, float radius)
        {
            Vector3[] points = DoorOpeningArc(hinge, closedDirection, openDirection, up, radius);
            guide.positionCount = points.Length;
            guide.SetPositions(points);
        }

        private static float DoorArrowElevation(GameObject instance, Vector3 up)
        {
            float elevation = 72f * ApexCoordinates.MetersPerUnit;
            Vector3 origin = instance.transform.position;
            foreach (var renderer in instance.GetComponentsInChildren<Renderer>(true))
            {
                if (!renderer.enabled || renderer is LineRenderer) continue;
                Bounds bounds = renderer.bounds;
                for (int corner = 0; corner < 8; corner++)
                {
                    Vector3 point = bounds.center + Vector3.Scale(bounds.extents, new Vector3((corner & 1) == 0 ? -1f : 1f, (corner & 2) == 0 ? -1f : 1f, (corner & 4) == 0 ? -1f : 1f));
                    elevation = Mathf.Max(elevation, Vector3.Dot(point - origin, up) + 8f * ApexCoordinates.MetersPerUnit);
                }
            }
            return elevation;
        }
    }
}
