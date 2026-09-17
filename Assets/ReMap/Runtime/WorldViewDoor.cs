using ReMap.Standalone.Core;
using UnityEngine;

namespace ReMap.Standalone
{
    public sealed partial class WorldView
    {
        internal const string DoorOpeningArrowName = "__remap_door_opening_direction";

        internal static bool DoorShowsOpeningDirection(MapObject door) =>
            door != null && door.customType == "door" &&
            (door.doorType == "single" || door.doorType == "double");

        internal static Vector3 DoorOpeningDirection(Quaternion worldRotation) =>
            worldRotation * Vector3.forward;

        private void EnsureDoorOpeningArrow(GameObject instance, MapObject door)
        {
            var child = instance.transform.Find(DoorOpeningArrowName);
            LineRenderer arrow;
            if (child == null)
            {
                arrow = new GameObject(DoorOpeningArrowName,
                    typeof(LineRenderer)).GetComponent<LineRenderer>();
                arrow.transform.SetParent(instance.transform, false);
                arrow.useWorldSpace = true;
                arrow.positionCount = 5;
                arrow.numCapVertices = 4;
                arrow.widthMultiplier = .045f;
                arrow.sharedMaterial = lineMaterial;
            }
            else arrow = child.GetComponent<LineRenderer>();

            arrow.enabled = DoorShowsOpeningDirection(door);
            if (!arrow.enabled) return;

            Vector3 direction = DoorOpeningDirection(instance.transform.rotation).normalized;
            Vector3 up = instance.transform.up.normalized;
            Vector3 side = Vector3.Cross(up, direction);
            if (side.sqrMagnitude < .0001f) side = instance.transform.right;
            side.Normalize();

            float elevation = DoorArrowElevation(instance, up);
            float behind = 42f * ApexCoordinates.MetersPerUnit;
            float ahead = 66f * ApexCoordinates.MetersPerUnit;
            float head = 18f * ApexCoordinates.MetersPerUnit;
            Vector3 anchor = instance.transform.position + up * elevation;
            Vector3 start = anchor - direction * behind;
            Vector3 tip = anchor + direction * ahead;
            arrow.SetPosition(0, start);
            arrow.SetPosition(1, tip);
            arrow.SetPosition(2, tip - direction * head + side * head * .55f);
            arrow.SetPosition(3, tip);
            arrow.SetPosition(4, tip - direction * head - side * head * .55f);
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
                    Vector3 point = bounds.center + Vector3.Scale(bounds.extents,
                        new Vector3((corner & 1) == 0 ? -1f : 1f,
                            (corner & 2) == 0 ? -1f : 1f,
                            (corner & 4) == 0 ? -1f : 1f));
                    elevation = Mathf.Max(elevation, Vector3.Dot(point - origin, up) +
                        8f * ApexCoordinates.MetersPerUnit);
                }
            }
            return elevation;
        }
    }
}
