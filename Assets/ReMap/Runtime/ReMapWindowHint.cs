using ReMap.Standalone.Core;
using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace ReMap.Standalone
{
    public sealed partial class ReMapApp
    {
        internal static MapObject CreateWindowHint(Vector3 position, string parent = "") =>
            new MapObject {
                assetId = "custom:window-hint", displayName = L.T("#WINDOW_HINT"),
                customType = "window-hint", parentId = parent ?? "", position = WorldView.ToData(position),
                isGroup = true, windowHintHalfHeight = 64f, windowHintHalfWidth = 72f
            };

        private void InsertWindowHint(Vector3 position, string parent = "")
        {
            var hint = CreateWindowHint(position, parent);
            session.Edit(document => document.objects.Add(hint));
            selectedId = hint.id; RevealHierarchy(hint.id); Refresh();
            SetStatus(L.T("#WINDOW_HINT_CREATED"));
        }

        private void BuildWindowHintInspector(MapObject item, VisualElement section)
        {
            section.Add(Label(L.T("#WINDOW_HINT"), "inspector-subsection-title"));
            var halfHeight = CompactInspectorField(new FloatField(L.T("#WINDOW_HINT_HALF_HEIGHT")) {
                value = item.windowHintHalfHeight, isDelayed = true
            });
            var halfWidth = CompactInspectorField(new FloatField(L.T("#WINDOW_HINT_HALF_WIDTH")) {
                value = item.windowHintHalfWidth, isDelayed = true
            });
            section.Add(halfHeight); section.Add(halfWidth);
            section.Add(Label(L.T("#WINDOW_HINT_HELP"), "note"));

            void Change(Action<MapObject> change)
            {
                CommitInspectorEdit();
                session.Edit(document => change(document.objects.Find(candidate => candidate.id == item.id)));
                Refresh();
            }
            halfHeight.RegisterValueChangedCallback(change => Change(edited =>
                edited.windowHintHalfHeight = Mathf.Clamp(change.newValue, .01f, 65535f)));
            halfWidth.RegisterValueChangedCallback(change => Change(edited =>
                edited.windowHintHalfWidth = Mathf.Clamp(change.newValue, .01f, 65535f)));
        }
    }
}
