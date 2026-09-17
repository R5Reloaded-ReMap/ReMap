using ReMap.Standalone.Core;
using UnityEngine;

namespace ReMap.Standalone
{
    public sealed partial class WorldView
    {
        private const int TriggerRingSegments = 40;
        private const int TriggerVerticalGuides = 8;

        private void EnsureTriggerVisual(GameObject instance, MapObject item)
        {
            var child = instance.transform.Find(TriggerPreviewName);
            GameObject volume = child == null
                ? CreateTriggerWireframe(TriggerPreviewName, instance.transform, item.id)
                : child.gameObject;
            float radius = Mathf.Max(.1f, item.triggerRadius) *
                ApexCoordinates.MetersPerUnit;
            float halfHeight = Mathf.Max(.1f, item.triggerHalfHeight) *
                ApexCoordinates.MetersPerUnit;
            UpdateTriggerWireframe(volume, radius, halfHeight);
        }

        private GameObject CreateTriggerWireframe(string name, Transform parent,
            string selectionId)
        {
            var volume = new GameObject(name);
            volume.transform.SetParent(parent, false);
            if (!string.IsNullOrEmpty(selectionId))
            {
                var selection = volume.AddComponent<SphereCollider>();
                selection.isTrigger = true;
                instanceIds[volume] = selectionId;
            }
            return volume;
        }

        private LineRenderer TriggerGuide(GameObject volume, string name,
            int positionCount)
        {
            var child = volume.transform.Find(name);
            LineRenderer line;
            if (child == null)
            {
                line = new GameObject(name, typeof(LineRenderer))
                    .GetComponent<LineRenderer>();
                line.transform.SetParent(volume.transform, false);
                line.useWorldSpace = false;
                line.numCapVertices = 4;
                line.numCornerVertices = 2;
                line.sharedMaterial = lineMaterial;
                var tint = new MaterialPropertyBlock();
                tint.SetColor("_BaseColor", new Color(.15f, .7f, 1f));
                line.SetPropertyBlock(tint);
            }
            else line = child.GetComponent<LineRenderer>();
            line.positionCount = positionCount;
            line.enabled = true;
            return line;
        }

        private void UpdateTriggerWireframe(GameObject volume, float radius,
            float halfHeight)
        {
            radius = Mathf.Max(.0025f, radius);
            halfHeight = Mathf.Max(.0025f, halfHeight);
            volume.transform.localPosition = Vector3.zero;
            volume.transform.localRotation = Quaternion.identity;
            volume.transform.localScale = Vector3.one;
            float width = Mathf.Clamp(Mathf.Min(radius, halfHeight) * .025f,
                .025f, .08f);

            var top = TriggerGuide(volume, "top", TriggerRingSegments + 1);
            var bottom = TriggerGuide(volume, "bottom", TriggerRingSegments + 1);
            top.widthMultiplier = width;
            bottom.widthMultiplier = width;
            for (int index = 0; index <= TriggerRingSegments; index++)
            {
                float angle = index * Mathf.PI * 2f / TriggerRingSegments;
                var radial = new Vector3(Mathf.Cos(angle) * radius, 0f,
                    Mathf.Sin(angle) * radius);
                top.SetPosition(index, radial + Vector3.up * halfHeight);
                bottom.SetPosition(index, radial - Vector3.up * halfHeight);
            }

            for (int index = 0; index < TriggerVerticalGuides; index++)
            {
                float angle = index * Mathf.PI * 2f / TriggerVerticalGuides;
                var radial = new Vector3(Mathf.Cos(angle) * radius, 0f,
                    Mathf.Sin(angle) * radius);
                var vertical = TriggerGuide(volume, "vertical_" + index, 2);
                vertical.widthMultiplier = width;
                vertical.SetPosition(0, radial - Vector3.up * halfHeight);
                vertical.SetPosition(1, radial + Vector3.up * halfHeight);
            }

            var selection = volume.GetComponent<SphereCollider>();
            if (selection != null)
            {
                selection.enabled = true;
                selection.isTrigger = true;
                selection.radius = Mathf.Clamp(Mathf.Min(radius, halfHeight) * .1f,
                    .18f, .45f);
            }
        }
    }
}
