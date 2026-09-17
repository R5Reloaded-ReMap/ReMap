using ReMap.Standalone.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UIElements;

namespace ReMap.Standalone
{
    internal sealed class ReMapDoorProfile
    {
        internal readonly string Id;
        internal readonly string Label;
        internal readonly string ScriptConstant;
        internal readonly string ModelPath;
        internal readonly bool SupportsGold;

        internal ReMapDoorProfile(string id, string label, string scriptConstant,
            string modelPath, bool supportsGold)
        {
            Id = id;
            Label = label;
            ScriptConstant = scriptConstant;
            ModelPath = modelPath;
            SupportsGold = supportsGold;
        }
    }

    internal static class ReMapDoorProfiles
    {
        internal const string SingleModelPath = "mdl/door/canyonlands_door_single_02.rmdl";
        internal const string VerticalModelPath = "mdl/door/door_canyonlands_large_01_animated.rmdl";
        internal const string HorizontalModelPath = "mdl/door/door_256x256x8_elevatorstyle02_animated.rmdl";
        internal static readonly string[] RequiredModelPaths = {
            SingleModelPath, VerticalModelPath, HorizontalModelPath
        };
        internal static readonly ReMapDoorProfile[] All = {
            new ReMapDoorProfile("single", "#DOOR_SINGLE", "REMAP_DOOR_SINGLE", SingleModelPath, true),
            new ReMapDoorProfile("double", "#DOOR_DOUBLE", "REMAP_DOOR_DOUBLE", SingleModelPath, true),
            new ReMapDoorProfile("vertical", "#DOOR_VERTICAL", "REMAP_DOOR_VERTICAL", VerticalModelPath, false),
            new ReMapDoorProfile("horizontal", "#DOOR_HORIZONTAL", "REMAP_DOOR_HORIZONTAL", HorizontalModelPath, false)
        };

        internal static ReMapDoorProfile Find(string id) =>
            All.FirstOrDefault(profile => profile.Id == id) ?? All[0];
    }

    public sealed partial class ReMapApp
    {
        private bool preparingDoorModels;

        private GameAssetRecord DoorModelRecord(string modelPath) =>
            assetLibrary?.Records.FirstOrDefault(candidate => GameAssetIndex.SameModelPath(
                candidate.modelPath, modelPath) && candidate.Supports(Targets));

        private bool DoorProfileAvailable(ReMapDoorProfile profile) =>
            ReMapModelAvailability.DoorProfile(assetLibrary?.Records, Targets, profile);

        private MapObject CreateDoorComponent(MapObject door, string role, string modelPath,
            Vector3 localPosition, Vector3 localRotation)
        {
            var record = DoorModelRecord(modelPath);
            return new MapObject {
                displayName = L.T("#DOOR_PANEL"), parentId = door.id,
                customType = "door-component", customRole = role,
                gameModelPath = modelPath, position = WorldView.ToData(localPosition),
                rotation = WorldView.ToData(localRotation),
                assetId = record?.Id ?? "custom:door-component:" + role,
                isGroup = record == null, commonAsset = record?.IsCommon ?? false,
                availableMaps = record?.origins.Select(origin => origin.mapId)
                    .Where(map => map != "").Distinct().ToList() ?? new List<string>()
            };
        }

        private void SyncDoorComponents(MapDocument document, MapObject door)
        {
            document.objects.RemoveAll(candidate => candidate.parentId == door.id &&
                candidate.customType == "door-component");
            var profile = ReMapDoorProfiles.Find(door.doorType);
            door.doorType = profile.Id;
            door.assetId = "custom:door";
            door.gameModelPath = "";
            door.isGroup = true;
            door.commonAsset = false;
            door.availableMaps = new List<string>();

            if (profile.Id == "double")
            {
                document.objects.Add(CreateDoorComponent(door, "left", profile.ModelPath,
                    ApexDisplay.UnityPosition(new Vector3(0f, -60f, 0f)), Vector3.zero));
                document.objects.Add(CreateDoorComponent(door, "right", profile.ModelPath,
                    ApexDisplay.UnityPosition(new Vector3(0f, 60f, 0f)),
                    ApexDisplay.UnityAngles(new Vector3(0f, 180f, 0f))));
            }
            else
            {
                document.objects.Add(CreateDoorComponent(door, "panel", profile.ModelPath,
                    Vector3.zero, Vector3.zero));
            }
        }

        internal static MapObject[] CreateDefaultDoorObjects(Vector3 pivot, string parent = "")
        {
            var door = new MapObject {
                assetId = "custom:door", displayName = L.T("#DOOR"), isGroup = true,
                customType = "door", doorType = "single", parentId = parent ?? "",
                position = WorldView.ToData(pivot)
            };
            var panel = new MapObject {
                assetId = "custom:door-component:panel", displayName = L.T("#DOOR_PANEL"),
                isGroup = true, customType = "door-component", customRole = "panel",
                parentId = door.id, gameModelPath = ReMapDoorProfiles.SingleModelPath
            };
            return new[] { door, panel };
        }

        private void InsertDoor(Vector3 pivot, string parent = "")
        {
            var objects = CreateDefaultDoorObjects(pivot, parent);
            var door = objects[0];
            door.doorType = ReMapDoorProfiles.All.First(DoorProfileAvailable).Id;
            session.Edit(document => {
                document.objects.AddRange(objects);
                SyncDoorComponents(document, door);
            });
            selectedId = door.id;
            RevealHierarchy(door.id);
            Refresh();
            _ = PrepareDoorModels();
            SetStatus(L.T("#DOOR_CREATED"));
        }

        private void BuildDoorInspector(MapObject item, VisualElement section)
        {
            section.Add(Label(L.T("#DOOR"), "inspector-subsection-title"));
            var current = ReMapDoorProfiles.Find(item.doorType);
            var profiles = ReMapDoorProfiles.All.Where(DoorProfileAvailable).ToList();
            var labels = profiles.Select(profile => L.T(profile.Label)).ToList();
            bool currentAvailable = profiles.Contains(current);
            if (!currentAvailable)
            {
                profiles.Insert(0, current);
                labels.Insert(0, L.F("#ARG0_UNAVAILABLE", L.T(current.Label)));
                section.Add(Label(L.F("#MODEL_NOT_IN_SELECTED_RPAKS", current.ModelPath), "note"));
            }
            int selected = Math.Max(0, profiles.IndexOf(current));
            var type = CompactInspectorField(new DropdownField(L.T("#DOOR_TYPE"), labels, selected));
            section.Add(type);
            type.RegisterValueChangedCallback(change => Run(() => {
                int index = Math.Max(0, labels.IndexOf(change.newValue));
                session.Edit(document => {
                    var door = document.objects.Find(candidate => candidate.id == item.id);
                    if (!DoorProfileAvailable(profiles[index])) return;
                    door.doorType = profiles[index].Id;
                    SyncDoorComponents(document, door);
                });
                Refresh();
                _ = PrepareDoorModels();
            }));

            if (current.SupportsGold)
            {
                var gold = CompactInspectorField(new Toggle(L.T("#GOLD_DOOR")) { value = item.doorGold });
                section.Add(gold);
                gold.RegisterValueChangedCallback(change => Run(() => {
                    session.Edit(document => document.objects.Find(candidate => candidate.id == item.id)
                        .doorGold = change.newValue);
                    Refresh();
                }));
            }

            var spawnOpen = CompactInspectorField(new Toggle(L.T("#SPAWN_OPEN")) {
                value = item.doorSpawnOpen
            });
            section.Add(spawnOpen);
            spawnOpen.RegisterValueChangedCallback(change => Run(() => {
                session.Edit(document => document.objects.Find(candidate => candidate.id == item.id)
                    .doorSpawnOpen = change.newValue);
                Refresh();
            }));
        }

        private async Task PrepareDoorModels()
        {
            if (assetLibrary == null || snapshot == null || preparingDoorModels) return;
            preparingDoorModels = true;
            var prepared = new List<string>();
            try
            {
                foreach (string modelPath in ReMapDoorProfiles.RequiredModelPaths)
                {
                    var record = DoorModelRecord(modelPath);
                    if (record == null || !record.Supports(Targets)) continue;
                    bool wasPrepared = world.models.IsPrepared(record.Id);
                    var entry = await PrepareDropEntry(record);
                    if (this == null || entry == null) return;
                    if (!wasPrepared) prepared.Add(record.Id);
                }
                if (this == null) return;
                bool needsSync = snapshot.objects.Where(candidate => candidate.customType == "door")
                    .Any(DoorComponentsNeedSync);
                if (needsSync)
                    session.Edit(document => {
                        foreach (var door in document.objects.Where(candidate =>
                            candidate.customType == "door").ToArray())
                            SyncDoorComponents(document, door);
                    });
                foreach (string assetId in prepared) world.Reload(assetId);
                if (needsSync || prepared.Count > 0) Refresh();
            }
            catch (Exception exception)
            {
                if (this != null) SetStatus(L.T("#DOOR_PREVIEW_ERROR") + exception.Message);
            }
            finally { preparingDoorModels = false; }
        }

        private bool DoorComponentsNeedSync(MapObject door)
        {
            var profile = ReMapDoorProfiles.Find(door.doorType);
            var components = snapshot.objects.Where(candidate => candidate.parentId == door.id &&
                candidate.customType == "door-component").ToArray();
            int expected = profile.Id == "double" ? 2 : 1;
            if (components.Length != expected) return true;
            return components.Any(component => !GameAssetIndex.SameModelPath(
                component.gameModelPath, profile.ModelPath) ||
                component.assetId != (DoorModelRecord(profile.ModelPath)?.Id ??
                    "custom:door-component:" + component.customRole));
        }
    }
}
