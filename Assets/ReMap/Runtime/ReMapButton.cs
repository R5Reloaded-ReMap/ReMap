using ReMap.Standalone.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UIElements;

namespace ReMap.Standalone
{
    internal sealed class ReMapButtonProfile
    {
        internal readonly string Id;
        internal readonly string Label;
        internal readonly string ModelPath;

        internal ReMapButtonProfile(string id, string label, string modelPath)
        {
            Id = id;
            Label = label;
            ModelPath = modelPath;
        }
    }

    internal static class ReMapButtonProfiles
    {
        internal static readonly ReMapButtonProfile[] All = {
            new ReMapButtonProfile("console-stand", "#BUTTON_MODEL_CONSOLE_STAND", ReMapApp.ButtonPanelModelPath),
            new ReMapButtonProfile("console", "#BUTTON_MODEL_CONSOLE", ReMapApp.ButtonConsoleModelPath),
            new ReMapButtonProfile("wall", "#BUTTON_MODEL_WALL", ReMapApp.ButtonWallModelPath)
        };
        internal static readonly string[] RequiredModelPaths = All.Select(profile => profile.ModelPath).ToArray();

        internal static ReMapButtonProfile Find(string id) => All.FirstOrDefault(profile => profile.Id == id) ?? All[0];
    }

    public sealed partial class ReMapApp
    {
        internal const string ButtonPanelModelPath = "mdl/props/global_access_panel_button/global_access_panel_button_console_w_stand.rmdl";
        internal const string ButtonConsoleModelPath = "mdl/props/global_access_panel_button/global_access_panel_button_console.rmdl";
        internal const string ButtonWallModelPath = "mdl/props/global_access_panel_button/global_access_panel_button_wall.rmdl";
        internal const string ButtonArrowModelPath = "mdl/weapons/bullets/damage_arrow.rmdl";
        private bool preparingButtonModels;

        internal static string ButtonModelPath(string mode, string profile = "") => mode == "invisible" ? ButtonArrowModelPath : ReMapButtonProfiles.Find(profile).ModelPath;

        private GameAssetRecord ButtonModelRecord(string mode, string profile = "") => assetLibrary?.Records.FirstOrDefault(candidate => GameAssetIndex.SameModelPath(candidate.modelPath, ButtonModelPath(mode, profile)) && candidate.Supports(Targets));

        private bool ButtonModeAvailable(string mode) => mode == "invisible" ? ButtonModelRecord(mode) != null : ReMapButtonProfiles.All.Any(ButtonProfileAvailable);

        private bool ButtonProfileAvailable(ReMapButtonProfile profile) => ButtonModelRecord("visible", profile.Id) != null;

        private MapObject CreateButton(Vector3 position, string parent = "")
        {
            var profile = ReMapButtonProfiles.All.FirstOrDefault(ButtonProfileAvailable) ?? ReMapButtonProfiles.All[0];
            var record = ButtonModelRecord("visible", profile.Id);
            return new MapObject {
                assetId = record?.Id ?? "custom:button", displayName = L.T("#BUTTON"),
                customType = "button", customProfile = profile.Id, gameModelPath = profile.ModelPath,
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
                var currentProfile = ReMapButtonProfiles.Find(item.customProfile);
                var profiles = ReMapButtonProfiles.All.Where(ButtonProfileAvailable).ToList();
                var profileLabels = profiles.Select(profile => L.T(profile.Label)).ToList();
                if (!profiles.Contains(currentProfile))
                {
                    profiles.Insert(0, currentProfile);
                    profileLabels.Insert(0, L.F("#ARG0_UNAVAILABLE", L.T(currentProfile.Label)));
                }
                var model = CompactInspectorField(new DropdownField(L.T("#BUTTON_MODEL"), profileLabels, Math.Max(0, profiles.IndexOf(currentProfile))));
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
                section.Add(model); section.Add(prompt); section.Add(teleport);
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
                model.RegisterValueChangedCallback(change => {
                    int index = profileLabels.IndexOf(change.newValue);
                    if (index >= 0) ChangeButtonProfile(item.id, profiles[index].Id);
                });
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

        private void ChangeButtonProfile(string id, string profile)
        {
            CommitInspectorEdit();
            session.Edit(document => {
                var edited = document.objects.Find(candidate => candidate.id == id);
                edited.customProfile = ReMapButtonProfiles.Find(profile).Id;
                SyncButtonModel(edited);
            });
            Refresh();
            _ = PrepareButtonModels();
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
            AddTeleportTargetPositionLock(item, section);
            section.Add(Button(L.T("#SELECT_BUTTON"), () => Select(item.parentId)));
        }

        private static bool IsTeleportTarget(MapObject item) => item != null &&
            (item.customType == "button-teleport-target" || item.customType == "trigger-teleport-target");

        private void AddTeleportTargetPositionLock(MapObject item, VisualElement section)
        {
            var locked = CompactInspectorField(new Toggle(L.T("#LOCK_TELEPORT_TARGET_POSITION")) {
                value = item.positionLocked
            });
            locked.tooltip = L.T("#LOCK_TELEPORT_TARGET_POSITION_HELP");
            section.Add(locked);
            locked.RegisterValueChangedCallback(change => Run(() => {
                CommitInspectorEdit();
                session.Edit(document => {
                    var target = document.objects.Find(candidate => candidate.id == item.id);
                    if (IsTeleportTarget(target)) target.positionLocked = change.newValue;
                });
                Refresh();
            }));
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
            var profile = ReMapButtonProfiles.Find(item.customProfile);
            item.customProfile = profile.Id;
            string path = ButtonModelPath(item.buttonMode, profile.Id);
            var record = ButtonModelRecord(item.buttonMode, profile.Id);
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
                foreach (string path in ReMapButtonProfiles.RequiredModelPaths.Concat(new[] { ButtonArrowModelPath }))
                {
                    var record = assetLibrary.Records.FirstOrDefault(candidate => GameAssetIndex.SameModelPath(candidate.modelPath, path) && candidate.Supports(Targets));
                    if (record == null) continue;
                    bool wasPrepared = world.models.IsPrepared(record.Id);
                    if (await PrepareDropEntry(record) == null || this == null) return;
                    if (!wasPrepared) prepared.Add(record.Id);
                }
                bool needsSync = snapshot.objects.Any(item => item.customType == "button" &&
                    ButtonModelRecord(item.buttonMode, item.customProfile) is GameAssetRecord record &&
                    (item.assetId != record.Id || item.isGroup ||
                    !GameAssetIndex.SameModelPath(item.gameModelPath, ButtonModelPath(item.buttonMode, item.customProfile))));
                if (needsSync)
                    session.Edit(document => {
                        foreach (var item in document.objects.Where(candidate => candidate.customType == "button"))
                            if (ButtonModelRecord(item.buttonMode, item.customProfile) != null) SyncButtonModel(item);
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
