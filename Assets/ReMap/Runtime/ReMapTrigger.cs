using ReMap.Standalone.Core;
using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace ReMap.Standalone
{
    public sealed partial class ReMapApp
    {
        internal MapObject CreateTrigger(Vector3 position, string parent = "") => new MapObject {
            assetId = "custom:trigger", displayName = L.T("#TRIGGER"), customType = "trigger",
            parentId = parent ?? "", position = WorldView.ToData(position), isGroup = true,
            triggerRadius = 100f, triggerHalfHeight = 50f
        };

        private void InsertTrigger(Vector3 position, string parent = "")
        {
            var trigger = CreateTrigger(position, parent);
            session.Edit(document => document.objects.Add(trigger));
            selectedId = trigger.id; RevealHierarchy(trigger.id); Refresh();
            SetStatus(L.T("#TRIGGER_CREATED"));
        }

        private void BuildTriggerInspector(MapObject item, VisualElement section)
        {
            section.Add(Label(L.T("#TRIGGER"), "inspector-subsection-title"));
            var radius = CompactInspectorField(new FloatField(L.T("#TRIGGER_RADIUS_APEX_U")) {
                value = item.triggerRadius, isDelayed = true
            });
            var height = CompactInspectorField(new FloatField(L.T("#TRIGGER_HALF_HEIGHT_APEX_U")) {
                value = item.triggerHalfHeight, isDelayed = true
            });
            var debug = CompactInspectorField(new Toggle(L.T("#TRIGGER_DEBUG_DRAW")) {
                value = item.triggerDebug
            });
            var realm = CompactInspectorField(new IntegerField(L.T("#REALM_ID")) {
                value = item.realmId, isDelayed = true
            });
            section.Add(radius); section.Add(height); section.Add(debug); section.Add(realm);

            radius.RegisterValueChangedCallback(change => ChangeTrigger(item.id, edited =>
                edited.triggerRadius = Mathf.Clamp(change.newValue, .1f, 65535f)));
            height.RegisterValueChangedCallback(change => ChangeTrigger(item.id, edited =>
                edited.triggerHalfHeight = Mathf.Clamp(change.newValue, .1f, 65535f)));
            debug.RegisterValueChangedCallback(change => ChangeTrigger(item.id, edited =>
                edited.triggerDebug = change.newValue));
            realm.RegisterValueChangedCallback(change => ChangeTrigger(item.id, edited =>
                edited.realmId = Math.Max(-1, change.newValue)));

            section.Add(Label(L.T("#TRIGGER_CALLBACKS"), "inspector-subsection-title"));
            section.Add(Label(L.T("#TRIGGER_CALLBACKS_HELP"), "note"));
            var enter = new TextField(L.T("#TRIGGER_ENTER_CALLBACK")) {
                value = item.triggerEnterCallback ?? "", multiline = true, isDelayed = true
            };
            var leave = new TextField(L.T("#TRIGGER_LEAVE_CALLBACK")) {
                value = item.triggerLeaveCallback ?? "", multiline = true, isDelayed = true
            };
            enter.style.minHeight = 90; leave.style.minHeight = 90;
            section.Add(enter); section.Add(leave);
            enter.RegisterValueChangedCallback(change => ChangeTrigger(item.id, edited =>
                edited.triggerEnterCallback = change.newValue ?? ""));
            leave.RegisterValueChangedCallback(change => ChangeTrigger(item.id, edited =>
                edited.triggerLeaveCallback = change.newValue ?? ""));
        }

        private void ChangeTrigger(string id, Action<MapObject> change)
        {
            CommitInspectorEdit();
            session.Edit(document => change(document.objects.Find(candidate => candidate.id == id)));
            Refresh();
        }
    }
}
