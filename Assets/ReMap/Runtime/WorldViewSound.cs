using ReMap.Standalone.Core;
using UnityEngine;

namespace ReMap.Standalone
{
    public sealed partial class WorldView
    {
        private const string SoundMarkerName = "__remap_sound_marker";
        private const int SoundRangeSegments = 48;

        private void EnsureSoundMarker(GameObject instance, MapObject item)
        {
            if (item.customType == "sound-point")
            {
                EnsureSoundPointMarker(instance, item.id);
                return;
            }

            var child = instance.transform.Find(SoundMarkerName);
            GameObject range = child == null
                ? CreateSoundRangeWireframe(SoundMarkerName, instance.transform, item.id)
                : child.gameObject;
            float radius = Mathf.Max(0f, item.soundRadius) *
                ApexCoordinates.MetersPerUnit;
            UpdateSoundRangeWireframe(range, radius);
        }

        private void EnsureSoundPointMarker(GameObject instance, string selectionId)
        {
            var child = instance.transform.Find(SoundMarkerName);
            GameObject marker;
            if (child == null)
            {
                marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                marker.name = SoundMarkerName;
                marker.transform.SetParent(instance.transform, false);
                marker.GetComponent<Renderer>().sharedMaterial = lineMaterial;
                var collider = marker.GetComponent<SphereCollider>();
                collider.isTrigger = true;
                instanceIds[marker] = selectionId;
            }
            else marker = child.gameObject;
            marker.transform.localPosition = Vector3.zero;
            marker.transform.localRotation = Quaternion.identity;
            marker.transform.localScale = Vector3.one * .16f;
            var tint = new MaterialPropertyBlock();
            tint.SetColor("_BaseColor", new Color(.3f, 1f, .65f));
            marker.GetComponent<Renderer>().SetPropertyBlock(tint);
        }

        private GameObject CreateSoundRangeWireframe(string name, Transform parent,
            string selectionId)
        {
            var range = new GameObject(name);
            range.transform.SetParent(parent, false);
            if (!string.IsNullOrEmpty(selectionId))
            {
                var selection = range.AddComponent<SphereCollider>();
                selection.isTrigger = true;
                selection.radius = .22f;
                instanceIds[range] = selectionId;
            }
            return range;
        }

        private LineRenderer SoundRangeGuide(GameObject range, string name)
        {
            var child = range.transform.Find(name);
            LineRenderer line;
            if (child == null)
            {
                line = new GameObject(name, typeof(LineRenderer))
                    .GetComponent<LineRenderer>();
                line.transform.SetParent(range.transform, false);
                line.useWorldSpace = false;
                line.numCapVertices = 4;
                line.numCornerVertices = 2;
                line.sharedMaterial = lineMaterial;
                var tint = new MaterialPropertyBlock();
                tint.SetColor("_BaseColor", new Color(.15f, .8f, 1f));
                line.SetPropertyBlock(tint);
            }
            else line = child.GetComponent<LineRenderer>();
            line.positionCount = SoundRangeSegments + 1;
            line.enabled = true;
            return line;
        }

        private void UpdateSoundRangeWireframe(GameObject range, float radius)
        {
            float visibleRadius = Mathf.Max(.28f, radius);
            range.transform.localPosition = Vector3.zero;
            range.transform.localRotation = Quaternion.identity;
            range.transform.localScale = Vector3.one;
            float width = Mathf.Clamp(visibleRadius * .006f, .025f, .08f);
            var horizontal = SoundRangeGuide(range, "horizontal");
            var verticalX = SoundRangeGuide(range, "vertical_x");
            var verticalZ = SoundRangeGuide(range, "vertical_z");
            horizontal.widthMultiplier = width;
            verticalX.widthMultiplier = width;
            verticalZ.widthMultiplier = width;

            for (int index = 0; index <= SoundRangeSegments; index++)
            {
                float angle = index * Mathf.PI * 2f / SoundRangeSegments;
                float first = Mathf.Cos(angle) * visibleRadius;
                float second = Mathf.Sin(angle) * visibleRadius;
                horizontal.SetPosition(index, new Vector3(first, 0f, second));
                verticalX.SetPosition(index, new Vector3(first, second, 0f));
                verticalZ.SetPosition(index, new Vector3(0f, first, second));
            }
        }
    }
}
