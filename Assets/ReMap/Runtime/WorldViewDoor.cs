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

        internal static Vector3[] DoorOpeningArc(Vector3 hinge, Vector3 closedDirection, Vector3 openDirection, Vector3 up, float radius, int segments = 12)
        {
            closedDirection.Normalize();
            openDirection.Normalize();
            up.Normalize();
            var points = new Vector3[segments + 4];
            for (int index = 0; index <= segments; index++) points[index] = hinge + Vector3.Slerp(closedDirection, openDirection, index / (float)segments).normalized * radius;
            Vector3 tip = points[segments];
            Vector3 tangent = (tip - points[segments - 1]).normalized;
            Vector3 side = Vector3.Cross(up, tangent).normalized;
            float head = radius * .22f;
            points[segments + 1] = tip - tangent * head + side * head * .55f;
            points[segments + 2] = tip;
            points[segments + 3] = tip - tangent * head - side * head * .55f;
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
            float radius = 86f * ApexCoordinates.MetersPerUnit;
            float hingeOffset = 60f * ApexCoordinates.MetersPerUnit;
            Vector3 anchor = instance.transform.position + up * elevation;
            if (door.doorType == "double")
            {
                UpdateDoorOpeningGuide(primary, anchor + right * hingeOffset, -right, forward, up, radius);
                UpdateDoorOpeningGuide(opposite, anchor - right * hingeOffset, right, forward, up, radius);
            }
            else UpdateDoorOpeningGuide(primary, anchor, right, forward, up, radius);
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
                guide.widthMultiplier = .045f;
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
