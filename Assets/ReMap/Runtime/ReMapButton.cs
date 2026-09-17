using ReMap.Standalone.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UIElements;

namespace ReMap.Standalone
{
    public sealed partial class ReMapApp
    {
        internal const string ButtonPanelModelPath = "mdl/props/global_access_panel_button/global_access_panel_button_console_w_stand.rmdl";
        internal const string ButtonArrowModelPath = "mdl/weapons/bullets/damage_arrow.rmdl";
        private bool preparingButtonModels;

        internal static string ButtonModelPath(string mode) =>
            mode == "invisible" ? ButtonArrowModelPath : ButtonPanelModelPath;

        private GameAssetRecord ButtonModelRecord(string mode) => assetLibrary?.Records.FirstOrDefault(candidate =>
            GameAssetIndex.SameModelPath(candidate.modelPath, ButtonModelPath(mode)) && candidate.Supports(Targets));

        private bool ButtonModeAvailable(string mode) => ButtonModelRecord(mode) != null;

        private MapObject CreateButton(Vector3 position, string parent = "")
        {
            var record = ButtonModelRecord("visible");
            return new MapObject {
                assetId = record?.Id ?? "custom:button", displayName = L.T("#BUTTON"),
                customType = "button", gameModelPath = ButtonPanelModelPath,
                parentId = parent ?? "", position = WorldView.ToData(position),
                isGroup = record == null, commonAsset = record?.IsCommon ?? false,
                availableMaps = record?.origins.Select(origin => origin.mapId)
                    .Where(map => map != "").Distinct().ToList() ?? new List<string>(),
                buttonMode = "visible", buttonUp = true, buttonTeleportPlaySound = true,
                buttonDestination = WorldView.ToData(Vector3.forward * 5f),
                buttonToken = "#FS_STRING_VAR", buttonMessageType = 4,
                buttonMessageDuration = 5f
            };
        }

        private void InsertButton(Vector3 position, string parent = "")
        {
            var button = CreateButton(position, parent);
            session.Edit(document => document.objects.Add(button));
            selectedId = button.id; RevealHierarchy(button.id); Refresh();
            _ = PrepareButtonModels();
            SetStatus(L.T("#BUTTON_CREATED"));
        }

        private void BuildButtonInspector(MapObject item, VisualElement section)
        {
            section.Add(Label(L.T("#BUTTON"), "inspector-subsection-title"));
            var modes = new List<string> { "visible" };
            if (ButtonModeAvailable("invisible")) modes.Add("invisible");
            var labels = modes.Select(ButtonModeLabel).ToList();
            int selected = Math.Max(0, modes.IndexOf(item.buttonMode));
            var mode = CompactInspectorField(new DropdownField(L.T("#BUTTON_MODE"), labels, selected));
            section.Add(mode);
            mode.RegisterValueChangedCallback(change => {
                int index = labels.IndexOf(change.newValue);
                if (index < 0) return;
                ChangeButtonMode(item.id, modes[index]);
            });

            if (item.buttonMode == "visible")
            {
                var prompt = CompactInspectorField(new TextField(L.T("#BUTTON_USE_TEXT")) {
                    value = item.buttonUseText, isDelayed = true
                });
                var teleport = CompactInspectorField(new Toggle(L.T("#BUTTON_TELEPORT_ON_USE")) {
                    value = item.buttonTeleportEnabled
                });
                var callback = new TextField(L.T("#BUTTON_ON_USE_CALLBACK")) {
                    value = item.buttonCallback, multiline = true, isDelayed = true
                };
                callback.AddToClassList("property-field");
                section.Add(prompt); section.Add(teleport);
                if (item.buttonTeleportEnabled)
                {
                    var sound = CompactInspectorField(new Toggle(L.T("#BUTTON_TELEPORT_SOUND")) {
                        value = item.buttonTeleportPlaySound
                    });
                    section.Add(sound);
                    section.Add(Button(L.T("#SELECT_BUTTON_TELEPORT_TARGET"),
                        () => SelectButtonTeleportTarget(item.id)));
                    sound.RegisterValueChangedCallback(change => ChangeButton(item.id,
                        edited => edited.buttonTeleportPlaySound = change.newValue));
                }
                section.Add(callback);
                section.Add(Label(L.T("#BUTTON_VISIBLE_HELP"), "note"));
                prompt.RegisterValueChangedCallback(change => ChangeButton(item.id,
                    edited => edited.buttonUseText = change.newValue ?? ""));
                teleport.RegisterValueChangedCallback(change =>
                    ChangeButtonTeleportEnabled(item.id, change.newValue));
                callback.RegisterValueChangedCallback(change => ChangeButton(item.id,
                    edited => edited.buttonCallback = change.newValue ?? ""));
                return;
            }

            var up = CompactInspectorField(new Toggle(L.T("#BUTTON_UP")) { value = item.buttonUp });
            var soundToggle = CompactInspectorField(new Toggle(L.T("#BUTTON_TELEPORT_SOUND")) {
                value = item.buttonTeleportPlaySound
            });
            var message = CompactInspectorField(new TextField(L.T("#BUTTON_MESSAGE")) {
                value = item.buttonMessage, isDelayed = true
            });
            var subMessage = CompactInspectorField(new TextField(L.T("#BUTTON_SUB_MESSAGE")) {
                value = item.buttonSubMessage, isDelayed = true
            });
            var type = CompactInspectorField(new IntegerField(L.T("#BUTTON_MESSAGE_TYPE")) {
                value = item.buttonMessageType, isDelayed = true
            });
            var duration = CompactInspectorField(new FloatField(L.T("#BUTTON_MESSAGE_DURATION")) {
                value = item.buttonMessageDuration, isDelayed = true
            });
            var token = CompactInspectorField(new TextField(L.T("#BUTTON_TOKEN")) {
                value = item.buttonToken, isDelayed = true
            });
            section.Add(up); section.Add(soundToggle);
            section.Add(Button(L.T("#SELECT_BUTTON_TELEPORT_TARGET"),
                () => SelectButtonTeleportTarget(item.id)));
            section.Add(message);
            section.Add(subMessage); section.Add(type); section.Add(duration); section.Add(token);
            section.Add(Label(L.T("#BUTTON_INVISIBLE_HELP"), "note"));
            up.RegisterValueChangedCallback(change => ChangeButton(item.id, edited => edited.buttonUp = change.newValue));
            soundToggle.RegisterValueChangedCallback(change => ChangeButton(item.id,
                edited => edited.buttonTeleportPlaySound = change.newValue));
            message.RegisterValueChangedCallback(change => ChangeButton(item.id, edited => edited.buttonMessage = change.newValue ?? ""));
            subMessage.RegisterValueChangedCallback(change => ChangeButton(item.id, edited => edited.buttonSubMessage = change.newValue ?? ""));
            type.RegisterValueChangedCallback(change => ChangeButton(item.id, edited => edited.buttonMessageType = Mathf.Clamp(change.newValue, 0, 16)));
            duration.RegisterValueChangedCallback(change => ChangeButton(item.id, edited => edited.buttonMessageDuration = Mathf.Clamp(change.newValue, 0f, 3600f)));
            token.RegisterValueChangedCallback(change => ChangeButton(item.id, edited => edited.buttonToken = change.newValue ?? ""));
        }

        private static string ButtonModeLabel(string mode) => mode == "invisible" ? L.T("#BUTTON_INVISIBLE") : L.T("#BUTTON_VISIBLE");

        private static MapObject ButtonTeleportTarget(MapDocument document, string buttonId) =>
            document.objects.FirstOrDefault(candidate => candidate.parentId == buttonId &&
                candidate.customType == "button-teleport-target");

        private static MapObject CreateButtonTeleportTarget(MapObject button) => new MapObject {
            assetId = "custom:button-teleport-target", displayName = L.T("#BUTTON_TELEPORT_TARGET"),
            customType = "button-teleport-target", customRole = "destination", parentId = button.id,
            isGroup = true, position = button.buttonDestination, rotation = button.buttonDirection
        };

        private static MapObject EnsureButtonTeleportTarget(MapDocument document, MapObject button)
        {
            var target = ButtonTeleportTarget(document, button.id);
            if (target != null) return target;
            target = CreateButtonTeleportTarget(button);
            document.objects.Add(target);
            return target;
        }

        private void ChangeButtonMode(string id, string value)
        {
            CommitInspectorEdit();
            session.Edit(document => {
                var edited = document.objects.Find(candidate => candidate.id == id);
                edited.buttonMode = value;
                if (value == "invisible")
                {
                    edited.buttonTeleportEnabled = true;
                    EnsureButtonTeleportTarget(document, edited);
                }
                SyncButtonModel(edited);
            });
            Refresh();
            root.schedule.Execute(() => Run(RefreshInspector));
        }

        private void ChangeButtonTeleportEnabled(string id, bool enabled)
        {
            CommitInspectorEdit();
            session.Edit(document => {
                var edited = document.objects.Find(candidate => candidate.id == id);
                edited.buttonTeleportEnabled = enabled;
                var target = ButtonTeleportTarget(document, id);
                if (enabled) EnsureButtonTeleportTarget(document, edited);
                else if (target != null)
                {
                    edited.buttonDestination = target.position;
                    edited.buttonDirection = target.rotation;
                    var removed = MapHierarchy.Subtree(document, target.id);
                    document.objects.RemoveAll(candidate => removed.Contains(candidate.id));
                }
            });
            selectedId = id; Refresh();
        }

        private void SelectButtonTeleportTarget(string buttonId)
        {
            string targetId = null;
            CommitInspectorEdit();
            session.Edit(document => {
                var button = document.objects.Find(candidate => candidate.id == buttonId);
                button.buttonTeleportEnabled = true;
                targetId = EnsureButtonTeleportTarget(document, button).id;
            });
            selectedId = targetId; RevealHierarchy(targetId); Refresh();
        }

        private void BuildButtonTeleportTargetInspector(MapObject item, VisualElement section)
        {
            section.Add(Label(L.T("#BUTTON_TELEPORT_TARGET"), "inspector-subsection-title"));
            section.Add(Label(L.T("#BUTTON_TELEPORT_TARGET_HELP"), "note"));
            section.Add(Button(L.T("#SELECT_BUTTON"), () => Select(item.parentId)));
        }

        private void ChangeButton(string id, Action<MapObject> change, bool refreshInspector = false)
        {
            CommitInspectorEdit();
            session.Edit(document => change(document.objects.Find(candidate => candidate.id == id)));
            Refresh();
            if (refreshInspector) root.schedule.Execute(() => Run(RefreshInspector));
        }

        private void SyncButtonModel(MapObject item)
        {
            string path = ButtonModelPath(item.buttonMode);
            var record = ButtonModelRecord(item.buttonMode);
            item.assetId = record?.Id ?? "custom:button";
            item.gameModelPath = path; item.isGroup = record == null;
            item.commonAsset = record?.IsCommon ?? false;
            item.availableMaps = record?.origins.Select(origin => origin.mapId)
                .Where(map => map != "").Distinct().ToList() ?? new List<string>();
        }

        private async Task PrepareButtonModels()
        {
            if (assetLibrary == null || snapshot == null || preparingButtonModels) return;
            preparingButtonModels = true;
            var prepared = new List<string>();
            try
            {
                foreach (string mode in new[] { "visible", "invisible" })
                {
                    var record = ButtonModelRecord(mode);
                    if (record == null) continue;
                    bool wasPrepared = world.models.IsPrepared(record.Id);
                    if (await PrepareDropEntry(record) == null || this == null) return;
                    if (!wasPrepared) prepared.Add(record.Id);
                }
                bool needsSync = snapshot.objects.Any(item => item.customType == "button" &&
                    ButtonModelRecord(item.buttonMode) is GameAssetRecord record &&
                    (item.assetId != record.Id || item.isGroup ||
                    !GameAssetIndex.SameModelPath(item.gameModelPath, ButtonModelPath(item.buttonMode))));
                if (needsSync)
                    session.Edit(document => {
                        foreach (var item in document.objects.Where(candidate => candidate.customType == "button"))
                            if (ButtonModelRecord(item.buttonMode) != null) SyncButtonModel(item);
                    });
                foreach (string assetId in prepared) world.Reload(assetId);
                if (needsSync || prepared.Count > 0) Refresh();
            }
            catch (Exception exception)
            {
                if (this != null) SetStatus(L.T("#BUTTON_PREVIEW_ERROR") + exception.Message);
            }
            finally { preparingButtonModels = false; }
        }
    }
}
