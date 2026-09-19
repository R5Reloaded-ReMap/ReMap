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
        internal const string SpawnPointModelPath = "mdl/dev/mp_spawn.rmdl";
        internal const string WorldSpawnPointRole = "world-spawn";
        private bool preparingSpawnPointModel;

        internal static bool IsWorldSpawnPoint(MapObject item)
        {
            return item?.customType == "spawn-point" && item.customRole == WorldSpawnPointRole;
        }

        private MapObject WorldSpawnPoint()
        {
            return snapshot?.objects.FirstOrDefault(IsWorldSpawnPoint);
        }

        private GameAssetRecord SpawnPointModelRecord() => assetLibrary?.Records.FirstOrDefault(candidate =>
            GameAssetIndex.SameModelPath(candidate.modelPath, SpawnPointModelPath) && candidate.Supports(Targets));

        internal MapObject CreateSpawnPoint(Vector3 position, string parent = "")
        {
            var record = SpawnPointModelRecord();
            return new MapObject {
                assetId = record?.Id ?? "custom:spawn-point", displayName = L.T("#SPAWN_POINT"),
                customType = "spawn-point", gameModelPath = SpawnPointModelPath,
                parentId = parent ?? "", position = WorldView.ToData(position),
                isGroup = record == null, commonAsset = record?.IsCommon ?? false,
                availableMaps = record?.origins.Select(origin => origin.mapId)
                    .Where(map => map != "").Distinct().ToList() ?? new List<string>(),
                spawnPointTeam = 0
            };
        }

        private void InsertSpawnPoint(Vector3 position, string parent = "")
        {
            var spawnPoint = CreateSpawnPoint(position, parent);
            session.Edit(document => document.objects.Add(spawnPoint));
            selectedId = spawnPoint.id; RevealHierarchy(spawnPoint.id); Refresh();
            _ = PrepareSpawnPointModel();
            SetStatus(L.T("#SPAWN_POINT_CREATED"));
        }

        private void SetWorldSpawnPoint(bool enabled)
        {
            CommitInspectorEdit();
            MapObject existing = WorldSpawnPoint();
            if (enabled)
            {
                if (existing == null)
                {
                    existing = CreateSpawnPoint(Vector3.zero);
                    existing.customRole = WorldSpawnPointRole;
                    existing.displayName = L.T("#WORLD_PLAYER_SPAWN_MARKER");
                    MapObject created = existing;
                    session.Edit(document => document.objects.Add(created));
                }
                else
                {
                    string id = existing.id;
                    session.Edit(document => {
                        MapObject marker = document.objects.Find(item => item.id == id);
                        marker.disabled = false;
                    });
                }
                selectedId = existing.id;
                RevealHierarchy(existing.id);
                Refresh();
                FocusHierarchy(existing.id);
                _ = PrepareSpawnPointModel();
                SetStatus(L.T("#WORLD_PLAYER_SPAWN_ENABLED"));
                return;
            }

            if (existing != null)
            {
                session.Edit(document => document.objects.RemoveAll(IsWorldSpawnPoint));
                selectedId = null;
                sceneRootSelected = true;
                Refresh();
            }
            SetStatus(L.T("#WORLD_PLAYER_SPAWN_DISABLED"));
        }

        private void SelectWorldSpawnPoint()
        {
            MapObject spawnPoint = WorldSpawnPoint();
            if (spawnPoint == null) return;
            selectedId = spawnPoint.id;
            RevealHierarchy(spawnPoint.id);
            RefreshInspector();
            RefreshObjects();
            FocusHierarchy(spawnPoint.id);
        }

        private void BuildSpawnPointInspector(MapObject item, VisualElement section)
        {
            section.Add(Label(L.T(IsWorldSpawnPoint(item) ? "#WORLD_PLAYER_SPAWN_MARKER" : "#SPAWN_POINT"), "inspector-subsection-title"));
            if (IsWorldSpawnPoint(item)) section.Add(Label(L.T("#WORLD_PLAYER_SPAWN_MARKER_HELP"), "note"));
            var team = CompactInspectorField(new IntegerField(L.T("#SPAWN_POINT_TEAM")) {
                value = item.spawnPointTeam, isDelayed = true
            });
            team.tooltip = L.T("#SPAWN_POINT_TEAM_HELP");
            section.Add(team);
            section.Add(Label(L.T("#SPAWN_POINT_MODES_HELP"), "note"));
            team.RegisterValueChangedCallback(change => {
                CommitInspectorEdit();
                session.Edit(document => document.objects.Find(candidate => candidate.id == item.id).spawnPointTeam =
                    Math.Max(0, Math.Min(64, change.newValue)));
                Refresh();
            });
        }

        private async Task PrepareSpawnPointModel()
        {
            if (assetLibrary == null || snapshot == null || preparingSpawnPointModel) return;
            var record = SpawnPointModelRecord();
            if (record == null) return;
            preparingSpawnPointModel = true;
            try
            {
                bool prepared = world.models.IsPrepared(record.Id);
                var entry = await PrepareDropEntry(record);
                if (this == null || entry == null) return;
                bool needsSync = snapshot.objects.Any(item => item.customType == "spawn-point" &&
                    (item.assetId != record.Id || item.isGroup));
                if (needsSync)
                    session.Edit(document => {
                        foreach (var item in document.objects.Where(candidate => candidate.customType == "spawn-point"))
                        {
                            item.assetId = record.Id; item.isGroup = false;
                            item.gameModelPath = SpawnPointModelPath;
                            item.commonAsset = record.IsCommon;
                            item.availableMaps = record.origins.Select(origin => origin.mapId)
                                .Where(map => map != "").Distinct().ToList();
                        }
                    });
                if (!prepared) world.Reload(record.Id);
                if (needsSync || !prepared) Refresh();
            }
            catch (Exception exception)
            {
                if (this != null) SetStatus(L.T("#SPAWN_POINT_PREVIEW_ERROR") + exception.Message);
            }
            finally { preparingSpawnPointModel = false; }
        }
    }
}
