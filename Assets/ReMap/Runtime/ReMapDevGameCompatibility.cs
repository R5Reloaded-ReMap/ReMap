#if UNITY_EDITOR || DEVELOPMENT_BUILD || REMAP_DEVELOPER_TOOLS
using ReMap.Standalone.Core;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace ReMap.Standalone
{
    public sealed partial class ReMapApp
    {
        private VisualElement devGameTestOverlay, devGameTestList;
        private Label devGameTestSummary;
        private Button devGameTestRun;

        private sealed class DevGameTestReport
        {
            internal readonly List<string> Generated = new List<string>();
            internal readonly List<string> Ignored = new List<string>();
            internal string ScriptPath = "";
            internal string Error = "";
        }

        private void BuildDevGameCompatibilityDialog()
        {
            devGameTestOverlay = new VisualElement();
            devGameTestOverlay.AddToClassList("modal-overlay");
            root.Add(devGameTestOverlay);
            var panel = new VisualElement();
            panel.AddToClassList("new-map-panel");
            devGameTestOverlay.Add(panel);
            var heading = DockTitle(L.T("#DEV_GAME_TEST"));
            panel.Add(heading);
            heading.Add(Button("×", () => ShowDevGameCompatibilityDialog(false), "dock-close"));
            var content = new VisualElement();
            content.AddToClassList("new-map-content");
            panel.Add(content);
            content.Add(Label(L.T("#DEV_GAME_TEST_HELP"), "note"));
            devGameTestSummary = Label("", "map-source-warning");
            content.Add(devGameTestSummary);
            var scroll = new ScrollView();
            scroll.AddToClassList("port-report-scroll");
            content.Add(scroll);
            devGameTestList = new VisualElement();
            devGameTestList.AddToClassList("port-report-list");
            scroll.Add(devGameTestList);
            var actions = new VisualElement();
            actions.AddToClassList("dialog-actions");
            panel.Add(actions);
            actions.Add(Button(L.T("#CLOSE"), () => ShowDevGameCompatibilityDialog(false)));
            devGameTestRun = Button(L.T("#RUN_DEV_GAME_TEST"), RunDevGameCompatibilityTest, "primary");
            actions.Add(devGameTestRun);
            devGameTestOverlay.style.display = DisplayStyle.None;
            root.RegisterCallback<KeyDownEvent>(e => {
                if (e.keyCode != KeyCode.Escape || devGameTestOverlay.style.display.value != DisplayStyle.Flex) return;
                ShowDevGameCompatibilityDialog(false); e.StopPropagation();
            }, TrickleDown.TrickleDown);
        }

        private void ShowDevGameCompatibilityDialog(bool show)
        {
            if (devGameTestOverlay == null) return;
            devGameTestOverlay.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
            if (!show) return;
            devGameTestSummary.text = L.T("#DEV_GAME_TEST_READY");
            devGameTestList.Clear();
            devGameTestRun.SetEnabled(snapshot != null && assetLibrary != null);
            devGameTestOverlay.BringToFront();
        }

        private async void RunDevGameCompatibilityTest()
        {
            if (snapshot == null || assetLibrary == null || devGameTestRun == null || !devGameTestRun.enabledSelf) return;
            var report = new DevGameTestReport();
            devGameTestRun.SetEnabled(false);
            Loading(true, L.T("#DEV_GAME_TEST_RUNNING"));
            try
            {
                CommitInspectorEdit();
                string launchError = ActiveSceneLaunchError();
                if (!string.IsNullOrEmpty(launchError)) throw new InvalidOperationException(launchError);
                string map = ReMapGameScript.EditingMap(snapshot);
                Vector3 anchor = SelectionPivot();
                try
                {
                    var progress = new Progress<MapReferenceProgress>(state => {
                        if (this != null) Loading(true, state.Message, state.Value);
                    });
                    string source = await MapReferenceExtractor.ExtractEntityLumpsAsync(
                        assetLibrary, map, false, progress, default);
                    if (this == null) return;
                    if (ReMapEntExporter.TryReadSinglePlayerStart(File.ReadAllText(source),
                        out Vector3 origin, out _, out int count))
                        anchor = ApexDisplay.UnityPosition(origin) - WorldView.ToVector(snapshot.originOffset);
                    else
                        report.Ignored.Add(L.F("#DEV_GAME_TEST_SPAWN_FALLBACK_ARG0", count));
                }
                catch (Exception exception)
                {
                    report.Ignored.Add(L.F("#DEV_GAME_TEST_SPAWN_READ_FAILED_ARG0", exception.Message));
                }

                MapDocument test = BuildDevGameCompatibilityDocument(snapshot, anchor, report);
                List<MapObject> generatedObjects = DevGameGenerationObjects(test);
                test.Validate();
                ReMapGameScript.Generate(test, generatedObjects, SelectedScriptRpaks());
                ReMapEntExporter.RestoreLooseMap(map, test.gameTarget,
                    assetLibrary.GameDirectory, assetLibrary.PlatformDirectory);
                report.ScriptPath = ReMapGameScriptInstaller.Write(assetLibrary.PlatformDirectory,
                    test, generatedObjects, SelectedScriptRpaks());
                ShowDevGameCompatibilityReport(report);
                Loading(true, L.T("#RESTARTING_CURRENT_MAP"));
                await ReloadMapAsync(map);
                SetStatus(L.F("#DEV_GAME_TEST_DONE_ARG0_ARG1", report.Generated.Count, report.Ignored.Count));
            }
            catch (Exception exception)
            {
                report.Error = exception.Message;
                SetStatus(L.F("#DEV_GAME_TEST_FAILED_ARG0", exception.Message));
                Debug.LogException(exception);
            }
            finally
            {
                if (this != null)
                {
                    Loading(false);
                    ShowDevGameCompatibilityReport(report);
                    devGameTestRun.SetEnabled(true);
                }
            }
        }

        private MapDocument BuildDevGameCompatibilityDocument(MapDocument source,
            Vector3 anchor, DevGameTestReport report)
        {
            var test = new MapDocument {
                name = "ReMap development compatibility test",
                gameTarget = source.gameTarget,
                editingMap = source.editingMap,
                originOffset = source.originOffset,
                targetMaps = new List<string>(source.targetMaps ?? new List<string>())
            };

            AddDevTestProp(source, test, anchor, report);

            MapObject worldSpawn = CreateSpawnPoint(anchor);
            worldSpawn.customRole = WorldSpawnPointRole;
            worldSpawn.displayName = L.T("#WORLD_PLAYER_SPAWN_MARKER");
            test.objects.Add(worldSpawn);
            report.Generated.Add(L.T("#WORLD_PLAYER_SPAWN_MARKER"));

            if (ReMapModelAvailability.HasModel(assetLibrary.Records, Targets, SpawnPointModelPath))
            {
                for (int team = 0; team <= 2; team++)
                {
                    MapObject marker = CreateSpawnPoint(anchor + new Vector3(3f + team * 2f, 0f, 0f));
                    marker.displayName = L.T("#SPAWN_POINT") + " — team " + team;
                    marker.spawnPointTeam = team;
                    test.objects.Add(marker);
                    report.Generated.Add(marker.displayName);
                }
            }
            else report.Ignored.Add(L.T("#DEV_GAME_TEST_SPAWN_MODEL_MISSING"));

            var availableProfiles = ReMapZiplineProfiles.All.Where(profile =>
                ReMapModelAvailability.ZiplineProfile(assetLibrary.Records, Targets, profile)).ToArray();
            foreach (var profile in ReMapZiplineProfiles.All.Except(availableProfiles))
                report.Ignored.Add(L.F("#DEV_GAME_TEST_PROFILE_MISSING_ARG0", L.T(profile.Label)));

            string[] modes = { "horizontal", "vertical" };
            int configuration = 0;
            foreach (string mode in modes)
            foreach (var startProfile in ReMapZiplineProfiles.All)
            foreach (var endProfile in ReMapZiplineProfiles.All)
            {
                string label = ZiplineTestLabel(mode, startProfile, endProfile);
                if (!availableProfiles.Contains(startProfile) || !availableProfiles.Contains(endProfile))
                {
                    report.Ignored.Add(label + " — " + L.T("#CUSTOM_OBJECT_MODELS_UNAVAILABLE"));
                    configuration++;
                    continue;
                }
                int localIndex = configuration % 9;
                float height = mode == "vertical" ? 12f : 5f;
                Vector3 pivot = anchor + new Vector3(15f + (localIndex % 3) * 14f, height,
                    15f + (localIndex / 3) * 14f + (mode == "vertical" ? 43f : 0f));
                MapObject[] created = CreateDefaultZiplineObjects(pivot);
                MapObject zipline = created[0], start = created[1], end = created[2];
                zipline.displayName = label;
                zipline.ziplineMode = mode;
                start.customProfile = startProfile.Id;
                end.customProfile = endProfile.Id;
                start.position = WorldView.ToData(Vector3.zero);
                end.position = WorldView.ToData((mode == "vertical" ? Vector3.down : Vector3.right) *
                    400f * ApexCoordinates.MetersPerUnit);
                test.objects.AddRange(created);
                SyncZiplineComponents(test, start);
                SyncZiplineComponents(test, end);
                report.Generated.Add(label);
                configuration++;
            }

            if (!GameTargets.SupportsCustomType(test.gameTarget, "curved-zipline"))
                report.Ignored.Add(L.T("#DEV_GAME_TEST_CURVED_DISABLED"));
            else
            {
                int curvedIndex = 0;
                foreach (var profile in ReMapZiplineProfiles.All)
                {
                    string label = L.T("#CURVED_ZIPLINE") + " — " + L.T(profile.Label);
                    if (!availableProfiles.Contains(profile))
                    {
                        report.Ignored.Add(label + " — " + L.T("#CUSTOM_OBJECT_MODELS_UNAVAILABLE"));
                        continue;
                    }
                    Vector3 pivot = anchor + new Vector3(15f + curvedIndex * 18f, 6f, 100f);
                    MapObject[] created = CreateDefaultCurvedZiplineObjects(pivot);
                    created[0].displayName = label;
                    test.objects.AddRange(created);
                    foreach (MapObject point in created.Where(item => item.customType == "curved-zipline-point"))
                    {
                        point.customProfile = profile.Id;
                        SyncCurvedZiplineSupport(test, point);
                    }
                    report.Generated.Add(label);
                    curvedIndex++;
                }
            }

            report.Ignored.Add(L.T("#DEV_GAME_TEST_ZIPRAIL_DISABLED"));
            AddDevCustomObjectConfigurations(test, anchor, report);
            return test;
        }

        private void AddDevTestProp(MapDocument source, MapDocument test, Vector3 anchor,
            DevGameTestReport report)
        {
            MapObject prop = source.objects.FirstOrDefault(item => item != null && !item.isGroup &&
                !item.disabled && string.IsNullOrEmpty(item.customType) &&
                !string.IsNullOrWhiteSpace(item.gameModelPath) && assetLibrary.Records.Any(candidate =>
                    GameAssetIndex.SameModelPath(candidate.modelPath, item.gameModelPath) &&
                    candidate.Supports(Targets)))?.Copy();
            if (prop == null)
            {
                var record = assetLibrary.Records.FirstOrDefault(candidate =>
                    !string.IsNullOrWhiteSpace(candidate.modelPath) && candidate.Supports(Targets));
                if (record == null)
                {
                    report.Ignored.Add("Prop — " + L.T("#CUSTOM_OBJECT_MODELS_UNAVAILABLE"));
                    return;
                }
                prop = new MapObject {
                    assetId = record.Id, displayName = "Dev prop — " + record.Name,
                    gameModelPath = record.modelPath, commonAsset = record.IsCommon,
                    availableMaps = record.origins.Select(origin => origin.mapId)
                        .Where(map => map != "").Distinct().ToList()
                };
            }
            prop.id = Guid.NewGuid().ToString("N");
            prop.displayName = "Dev prop — " + prop.displayName;
            prop.parentId = "";
            prop.customType = "";
            prop.customRole = "";
            prop.isGroup = false;
            prop.disabled = false;
            prop.clientSide = false;
            prop.position = WorldView.ToData(anchor + new Vector3(-8f, 0f, 8f));
            prop.rotation = default;
            prop.scale = new Float3(1f, 1f, 1f);
            test.objects.Add(prop);
            report.Generated.Add(prop.displayName);
        }

        private void AddDevCustomObjectConfigurations(MapDocument test, Vector3 anchor,
            DevGameTestReport report)
        {
            int slot = 0;
            Vector3 Next(float height = 1f)
            {
                int current = slot++;
                return anchor + new Vector3(15f + (current % 8) * 12f, height,
                    140f + (current / 8) * 14f);
            }
            void Added(string label) => report.Generated.Add(label);
            void Ignored(string label) => report.Ignored.Add(label + " — " +
                L.T("#CUSTOM_OBJECT_MODELS_UNAVAILABLE"));
            bool Supported(string type, string label)
            {
                if (GameTargets.SupportsCustomType(test.gameTarget, type)) return true;
                report.Ignored.Add(label + " — target " + test.gameTarget);
                return false;
            }

            if (Supported("door", L.T("#DOOR")))
                foreach (var profile in ReMapDoorProfiles.All)
                {
                    if (!DoorProfileAvailable(profile))
                    {
                        Ignored(L.T("#DOOR") + " — " + L.T(profile.Label));
                        continue;
                    }
                    foreach (bool spawnOpen in new[] { false, true })
                    foreach (bool gold in profile.SupportsGold ? new[] { false, true } : new[] { false })
                    {
                        string label = L.T("#DOOR") + " — " + L.T(profile.Label) +
                            " — open=" + spawnOpen + (profile.SupportsGold ? " — gold=" + gold : "");
                        MapObject[] created = CreateDefaultDoorObjects(Next());
                        MapObject door = created[0];
                        door.displayName = label;
                        door.doorType = profile.Id;
                        door.doorSpawnOpen = spawnOpen;
                        door.doorGold = gold;
                        test.objects.AddRange(created);
                        SyncDoorComponents(test, door);
                        Added(label);
                    }
                }

            if (Supported("loot-bin", L.T("#LOOT_BIN")))
            {
                if (LootBinModelRecord() == null) Ignored(L.T("#LOOT_BIN"));
                else for (int skin = 0; skin < 4; skin++)
                {
                    MapObject item = CreateLootBin(Next(), "");
                    item.lootBinSkin = skin;
                    item.displayName = L.T("#LOOT_BIN") + " — skin " + skin;
                    test.objects.Add(item); Added(item.displayName);
                }
            }

            if (Supported("jump-pad", L.T("#JUMP_PAD")))
            {
                if (JumpPadModelRecord() == null) Ignored(L.T("#JUMP_PAD"));
                else foreach (bool doubleJump in new[] { false, true })
                {
                    MapObject item = CreateJumpPad(Next(), "");
                    item.jumpPadDoubleJump = doubleJump;
                    item.displayName = L.T("#JUMP_PAD") + " — double-jump=" + doubleJump;
                    test.objects.Add(item); Added(item.displayName);
                }
            }

            if (Supported("trigger", L.T("#TRIGGER")))
            {
                for (int mode = 0; mode < 4; mode++)
                {
                    MapObject item = CreateTrigger(Next(), "");
                    item.triggerDebug = mode == 1;
                    item.triggerTeleportEnabled = mode >= 2;
                    item.triggerTeleportPlaySound = mode == 3;
                    item.triggerDestination = WorldView.ToData(Vector3.up * 4f);
                    item.triggerEnterCallback = "printt( \"ReMap dev trigger enter\" )";
                    item.triggerLeaveCallback = "printt( \"ReMap dev trigger leave\" )";
                    item.displayName = L.T("#TRIGGER") + " — " +
                        (mode == 0 ? "callbacks" : mode == 1 ? "debug" :
                        mode == 2 ? "teleport" : "teleport + sound");
                    test.objects.Add(item); Added(item.displayName);
                }
            }

            if (Supported("jump-tower", L.T("#JUMP_TOWER")))
            {
                if (JumpTowerModelRecord(JumpTowerBaseModelPath) == null ||
                    JumpTowerModelRecord(JumpTowerBalloonModelPath) == null) Ignored(L.T("#JUMP_TOWER"));
                else
                {
                    MapObject[] created = CreateJumpTower(Next());
                    test.objects.AddRange(created); Added(L.T("#JUMP_TOWER"));
                }
            }

            if (Supported("weapon-rack", L.T("#WEAPON_RACK")))
            {
                if (WeaponRackModelRecord() == null) Ignored(L.T("#WEAPON_RACK"));
                else foreach (string weapon in WeaponRackWeapons)
                {
                    MapObject item = CreateWeaponRack(Next(), "");
                    item.weaponRackWeapon = weapon;
                    item.displayName = L.T("#WEAPON_RACK") + " — " + weapon;
                    test.objects.Add(item); Added(item.displayName);
                }
            }

            if (Supported("respawn-heal", L.T("#RESPAWN_HEAL")))
                foreach (var profile in RespawnHealProfiles)
                {
                    if (RespawnHealModelRecord(profile) == null)
                    {
                        Ignored(L.T("#RESPAWN_HEAL") + " — " + L.T(profile.NameKey));
                        continue;
                    }
                    foreach (bool progressive in new[] { false, true })
                    {
                        MapObject item = CreateRespawnHeal(Next(), "");
                        SyncRespawnHealModel(item, profile);
                        item.respawnHealProgressive = progressive;
                        item.displayName = L.T("#RESPAWN_HEAL") + " — " + L.T(profile.NameKey) +
                            " — progressive=" + progressive;
                        test.objects.Add(item); Added(item.displayName);
                    }
                }

            if (Supported("button", L.T("#BUTTON")))
            {
                foreach (var profile in ReMapButtonProfiles.All)
                {
                    if (!ButtonProfileAvailable(profile))
                    {
                        Ignored(L.T("#BUTTON") + " — " + L.T(profile.Label));
                        continue;
                    }
                    foreach (bool teleport in new[] { false, true })
                    {
                        MapObject item = CreateButton(Next(), "");
                        item.customProfile = profile.Id;
                        item.buttonTeleportEnabled = teleport;
                        item.buttonTeleportPlaySound = false;
                        item.buttonDestination = WorldView.ToData(Vector3.up * 4f);
                        item.buttonCallback = "printt( \"ReMap dev button\" )";
                        SyncButtonModel(item);
                        item.displayName = L.T("#BUTTON") + " — " + L.T(profile.Label) +
                            " — teleport=" + teleport;
                        test.objects.Add(item); Added(item.displayName);
                    }
                }
                if (!ButtonModeAvailable("invisible")) Ignored(L.T("#BUTTON") + " — invisible");
                else foreach (bool up in new[] { false, true })
                {
                    MapObject item = CreateButton(Next(), "");
                    item.buttonMode = "invisible";
                    item.buttonTeleportEnabled = true;
                    item.buttonTeleportPlaySound = false;
                    item.buttonUp = up;
                    item.buttonMessage = "ReMap development test";
                    item.buttonDestination = WorldView.ToData(Vector3.up * 4f);
                    SyncButtonModel(item);
                    item.displayName = L.T("#BUTTON") + " — invisible — up=" + up;
                    test.objects.Add(item); Added(item.displayName);
                }
            }

            if (Supported("speed-boost", L.T("#SPEED_BOOST")))
            {
                if (!SpeedBoostModelsAvailable()) Ignored(L.T("#SPEED_BOOST"));
                else
                {
                    MapObject[] created = CreateSpeedBoost(Next());
                    test.objects.AddRange(created); Added(L.T("#SPEED_BOOST"));
                }
            }

            if (Supported("bubble-shield", L.T("#BUBBLE_SHIELD")))
            {
                if (BubbleShieldModelRecord() == null) Ignored(L.T("#BUBBLE_SHIELD"));
                else
                {
                    MapObject item = CreateBubbleShield(Next(), "");
                    test.objects.Add(item); Added(L.T("#BUBBLE_SHIELD"));
                }
            }

            if (Supported("camera-path", L.T("#CAMERA_PATH")))
                foreach (bool trackTarget in new[] { false, true })
                {
                    MapObject[] created = CreateDefaultCameraPathObjects(Next(4f));
                    created[0].cameraPathTrackTarget = trackTarget;
                    created[0].cameraPathSpacingEnabled = trackTarget;
                    created[0].cameraPathSpacing = trackTarget ? 300f : 0f;
                    created[0].displayName = L.T("#CAMERA_PATH") + " — track=" + trackTarget;
                    test.objects.AddRange(created); Added(created[0].displayName);
                }

            if (Supported("animated-camera", L.T("#ANIMATED_CAMERA")))
            {
                if (!AnimatedCameraModelsAvailable()) Ignored(L.T("#ANIMATED_CAMERA"));
                else
                {
                    MapObject[] created = CreateAnimatedCamera(Next());
                    test.objects.AddRange(created); Added(L.T("#ANIMATED_CAMERA"));
                }
            }

            if (Supported("sound", L.T("#SOUND")))
                foreach (bool polyline in new[] { false, true })
                {
                    MapObject[] created = CreateDefaultSoundObjects(Next());
                    MapObject sound = created[0];
                    sound.soundName = "Lifeline_Drone_Healing_1P";
                    sound.soundRadius = polyline ? 512f : 0f;
                    sound.soundWaveAmbient = polyline;
                    sound.displayName = L.T("#SOUND") + " — " + (polyline ? "polyline" : "point");
                    test.objects.AddRange(created);
                    if (polyline)
                    {
                        test.objects.Add(CreateSoundPoint(sound.id, 0, Vector3.right * 4f));
                        test.objects.Add(CreateSoundPoint(sound.id, 1, Vector3.right * 8f + Vector3.forward * 3f));
                    }
                    Added(sound.displayName);
                }

            if (Supported("location-pair", L.T("#NEW_LOCATION_PAIR")))
            {
                MapObject item = CreateLocationPair(Next());
                test.objects.Add(item); Added(L.T("#NEW_LOCATION_PAIR"));
            }

            if (Supported("text-info-panel", L.T("#TEXT_INFO_PANEL")))
                foreach (bool showPin in new[] { false, true })
                {
                    MapObject item = CreateTextInfoPanel(Next());
                    item.textInfoPanelTitle = "ReMap development test";
                    item.textInfoPanelDescription = "Generated compatibility panel";
                    item.textInfoPanelShowPin = showPin;
                    item.displayName = L.T("#TEXT_INFO_PANEL") + " — pin=" + showPin;
                    test.objects.Add(item); Added(item.displayName);
                }

            if (Supported("window-hint", L.T("#WINDOW_HINT")))
            {
                MapObject item = CreateWindowHint(Next());
                test.objects.Add(item); Added(L.T("#WINDOW_HINT"));
            }
        }

        private static string ZiplineTestLabel(string mode, ReMapZiplineProfile start,
            ReMapZiplineProfile end) => L.T("#ZIPLINE") + " — " +
            L.T(mode == "vertical" ? "#VERTICAL" : "#HORIZONTAL") + " — " +
            L.T(start.Label) + " / " + L.T(end.Label);

        private static List<MapObject> DevGameGenerationObjects(MapDocument document)
        {
            document.Validate();
            var byId = document.objects.ToDictionary(item => item.id);
            var matrices = new Dictionary<string, Matrix4x4>();
            Matrix4x4 WorldMatrix(MapObject item)
            {
                if (matrices.TryGetValue(item.id, out Matrix4x4 cached)) return cached;
                Matrix4x4 local = Matrix4x4.TRS(WorldView.ToVector(item.position),
                    Quaternion.Euler(WorldView.ToVector(item.rotation)), WorldView.ToVector(item.scale));
                Matrix4x4 result = !string.IsNullOrEmpty(item.parentId) && byId.TryGetValue(item.parentId, out MapObject parent)
                    ? WorldMatrix(parent) * local : local;
                matrices[item.id] = result;
                return result;
            }

            var result = new List<MapObject>();
            foreach (MapObject item in document.objects)
            {
                if (!MapHierarchy.IsEnabled(document, item.id) ||
                    !GameTargets.SupportsCustomType(document.gameTarget, item.customType)) continue;
                bool generatedGroup = item.customType == "zipline" || item.customType == "zipline-endpoint" ||
                    item.customType == "curved-zipline" || item.customType == "curved-zipline-point" ||
                    item.customType == "spawn-point" || item.customType == "door" ||
                    item.customType == "loot-bin" || item.customType == "jump-pad" ||
                    item.customType == "trigger" || item.customType == "trigger-teleport-target" ||
                    item.customType == "jump-tower" || item.customType == "weapon-rack" ||
                    item.customType == "respawn-heal" || item.customType == "button" ||
                    item.customType == "button-teleport-target" || item.customType == "speed-boost" ||
                    item.customType == "bubble-shield" || item.customType == "camera-path" ||
                    item.customType == "camera-path-point" || item.customType == "camera-path-target" ||
                    item.customType == "animated-camera" || item.customType == "sound" ||
                    item.customType == "sound-point" || item.customType == "location-pair" ||
                    item.customType == "text-info-panel" || item.customType == "window-hint";
                if (item.isGroup && !generatedGroup) continue;
                MapObject copy = item.Copy();
                Matrix4x4 matrix = WorldMatrix(item);
                copy.position = WorldView.ToData(matrix.MultiplyPoint3x4(Vector3.zero));
                copy.rotation = WorldView.ToData(matrix.rotation.eulerAngles);
                copy.scale = WorldView.ToData(matrix.lossyScale);
                bool keepParent = item.customType == "curved-zipline-point" ||
                    item.customType == "camera-path-point" || item.customType == "camera-path-target" ||
                    item.customType == "sound-point" || item.customType == "button-teleport-target" ||
                    item.customType == "trigger-teleport-target";
                copy.parentId = keepParent ? item.parentId : "";
                result.Add(copy);
            }
            return result;
        }

        private void ShowDevGameCompatibilityReport(DevGameTestReport report)
        {
            if (report == null || devGameTestOverlay == null) return;
            devGameTestOverlay.style.display = DisplayStyle.Flex;
            devGameTestSummary.text = string.IsNullOrEmpty(report.Error)
                ? L.F("#DEV_GAME_TEST_DONE_ARG0_ARG1", report.Generated.Count, report.Ignored.Count)
                : L.F("#DEV_GAME_TEST_FAILED_ARG0", report.Error);
            devGameTestList.Clear();
            devGameTestList.Add(Label(L.F("#DEV_GAME_TEST_GENERATED_ARG0", report.Generated.Count), "section-title"));
            foreach (string item in report.Generated) devGameTestList.Add(Label("✓ " + item, "note"));
            devGameTestList.Add(Label(L.F("#DEV_GAME_TEST_IGNORED_ARG0", report.Ignored.Count), "section-title"));
            foreach (string item in report.Ignored) devGameTestList.Add(Label("— " + item, "note"));
            if (!string.IsNullOrEmpty(report.ScriptPath))
                devGameTestList.Add(Label(L.F("#DEV_GAME_TEST_SCRIPT_ARG0", report.ScriptPath), "note"));
            devGameTestOverlay.BringToFront();
        }
    }
}
#endif
