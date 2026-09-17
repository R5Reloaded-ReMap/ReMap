using ReMap.Standalone.Core;
using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace ReMap.Standalone
{
    public sealed partial class ReMapApp
    {
        internal static MapObject CreateTextInfoPanel(Vector3 position, string parent = "") =>
            new MapObject {
                assetId = "custom:text-info-panel", displayName = L.T("#TEXT_INFO_PANEL"),
                customType = "text-info-panel", parentId = parent ?? "", position = WorldView.ToData(position),
                isGroup = true, textInfoPanelShowPin = true, textInfoPanelScale = 1f
            };

        private void InsertTextInfoPanel(Vector3 position, string parent = "")
        {
            var panel = CreateTextInfoPanel(position, parent);
            session.Edit(document => document.objects.Add(panel));
            selectedId = panel.id; RevealHierarchy(panel.id); Refresh();
            SetStatus(L.T("#TEXT_INFO_PANEL_CREATED"));
        }

        private void BuildTextInfoPanelInspector(MapObject item, VisualElement section)
        {
            section.Add(Label(L.T("#TEXT_INFO_PANEL"), "inspector-subsection-title"));
            var title = CompactInspectorField(new TextField(L.T("#TEXT_INFO_PANEL_TITLE")) {
                value = item.textInfoPanelTitle ?? "", isDelayed = true
            });
            var descriptionField = new TextField(L.T("#TEXT_INFO_PANEL_DESCRIPTION")) {
                value = item.textInfoPanelDescription ?? "", isDelayed = true, multiline = true
            };
            var description = CompactInspectorField(descriptionField);
            var showPin = CompactInspectorField(new Toggle(L.T("#TEXT_INFO_PANEL_SHOW_PIN")) {
                value = item.textInfoPanelShowPin
            });
            var scale = CompactInspectorField(new FloatField(L.T("#TEXT_INFO_PANEL_SCALE")) {
                value = item.textInfoPanelScale, isDelayed = true
            });
            section.Add(title); section.Add(description); section.Add(showPin); section.Add(scale);
            section.Add(Label(L.T("#TEXT_INFO_PANEL_HELP"), "note"));

            void Change(Action<MapObject> change)
            {
                CommitInspectorEdit();
                session.Edit(document => change(document.objects.Find(candidate => candidate.id == item.id)));
                Refresh();
            }
            title.RegisterValueChangedCallback(change => Change(edited => edited.textInfoPanelTitle = change.newValue ?? ""));
            description.RegisterValueChangedCallback(change => Change(edited => edited.textInfoPanelDescription = change.newValue ?? ""));
            showPin.RegisterValueChangedCallback(change => Change(edited => edited.textInfoPanelShowPin = change.newValue));
            scale.RegisterValueChangedCallback(change => Change(edited =>
                edited.textInfoPanelScale = Mathf.Clamp(change.newValue, .01f, 100f)));
        }
    }
}
