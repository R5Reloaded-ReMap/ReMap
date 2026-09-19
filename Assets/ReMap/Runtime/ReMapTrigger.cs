using ReMap.Standalone.Core;
using System;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace ReMap.Standalone
{
    public sealed partial class ReMapApp
    {
        internal MapObject CreateTrigger(Vector3 position, string parent = "") => new MapObject {
            assetId = "custom:trigger", displayName = L.T("#TRIGGER"), customType = "trigger",
            parentId = parent ?? "", position = WorldView.ToData(position), isGroup = true,
            triggerRadius = 100f, triggerHalfHeight = 50f, triggerTeleportPlaySound = true,
            triggerDestination = WorldView.ToData(Vector3.up * 5f)
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
            var teleport = CompactInspectorField(new Toggle(L.T("#TRIGGER_TELEPORT_ON_ENTER")) {
                value = item.triggerTeleportEnabled
            });
            section.Add(radius); section.Add(height); section.Add(debug); section.Add(realm); section.Add(teleport);
            if (item.triggerTeleportEnabled)
            {
                var sound = CompactInspectorField(new Toggle(L.T("#BUTTON_TELEPORT_SOUND")) {
                    value = item.triggerTeleportPlaySound
                });
                section.Add(sound);
                section.Add(Button(L.T("#SELECT_TRIGGER_TELEPORT_TARGET"), () => SelectTriggerTeleportTarget(item.id)));
                sound.RegisterValueChangedCallback(change => ChangeTrigger(item.id, edited => edited.triggerTeleportPlaySound = change.newValue));
            }

            radius.RegisterValueChangedCallback(change => ChangeTrigger(item.id, edited =>
                edited.triggerRadius = Mathf.Clamp(change.newValue, .1f, 65535f)));
            height.RegisterValueChangedCallback(change => ChangeTrigger(item.id, edited =>
                edited.triggerHalfHeight = Mathf.Clamp(change.newValue, .1f, 65535f)));
            debug.RegisterValueChangedCallback(change => ChangeTrigger(item.id, edited =>
                edited.triggerDebug = change.newValue));
            realm.RegisterValueChangedCallback(change => ChangeTrigger(item.id, edited =>
                edited.realmId = Math.Max(-1, change.newValue)));
            teleport.RegisterValueChangedCallback(change => ChangeTriggerTeleportEnabled(item.id, change.newValue));

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

        private static MapObject TriggerTeleportTarget(MapDocument document, string triggerId) => document.objects.FirstOrDefault(candidate => candidate.parentId == triggerId && candidate.customType == "trigger-teleport-target");

        private static MapObject CreateTriggerTeleportTarget(MapObject trigger) => new MapObject {
            assetId = "custom:trigger-teleport-target", displayName = L.T("#TRIGGER_TELEPORT_TARGET"),
            customType = "trigger-teleport-target", customRole = "destination", parentId = trigger.id,
            isGroup = true, position = trigger.triggerDestination, rotation = trigger.triggerDirection
        };

        private static MapObject EnsureTriggerTeleportTarget(MapDocument document, MapObject trigger)
        {
            var target = TriggerTeleportTarget(document, trigger.id);
            if (target != null) return target;
            target = CreateTriggerTeleportTarget(trigger);
            document.objects.Add(target);
            return target;
        }

        private void ChangeTriggerTeleportEnabled(string id, bool enabled)
        {
            CommitInspectorEdit();
            session.Edit(document => {
                var edited = document.objects.Find(candidate => candidate.id == id);
                edited.triggerTeleportEnabled = enabled;
                var target = TriggerTeleportTarget(document, id);
                if (enabled) EnsureTriggerTeleportTarget(document, edited);
                else if (target != null)
                {
                    edited.triggerDestination = target.position;
                    edited.triggerDirection = target.rotation;
                    var removed = MapHierarchy.Subtree(document, target.id);
                    document.objects.RemoveAll(candidate => removed.Contains(candidate.id));
                }
            });
            selectedId = id; Refresh();
        }

        private void SelectTriggerTeleportTarget(string triggerId)
        {
            string targetId = null;
            CommitInspectorEdit();
            session.Edit(document => {
                var trigger = document.objects.Find(candidate => candidate.id == triggerId);
                trigger.triggerTeleportEnabled = true;
                targetId = EnsureTriggerTeleportTarget(document, trigger).id;
            });
            selectedId = targetId; RevealHierarchy(targetId); Refresh();
        }

        private void BuildTriggerTeleportTargetInspector(MapObject item, VisualElement section)
        {
            section.Add(Label(L.T("#TRIGGER_TELEPORT_TARGET"), "inspector-subsection-title"));
            section.Add(Label(L.T("#TRIGGER_TELEPORT_TARGET_HELP"), "note"));
            AddTeleportTargetPositionLock(item, section);
            section.Add(Button(L.T("#SELECT_TRIGGER"), () => Select(item.parentId)));
        }
    }
}
