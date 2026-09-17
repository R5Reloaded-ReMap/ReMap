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
        internal const string JumpTowerBaseModelPath = "mdl/props/zipline_balloon/zipline_balloon_base.rmdl";
        internal const string JumpTowerBalloonModelPath = "mdl/props/zipline_balloon/zipline_balloon.rmdl";
        private bool preparingJumpTowerModels;

        private GameAssetRecord JumpTowerModelRecord(string path) => assetLibrary?.Records.FirstOrDefault(candidate =>
            GameAssetIndex.SameModelPath(candidate.modelPath, path) && candidate.Supports(Targets));

        private MapObject CreateJumpTowerComponent(MapObject tower, string role, string modelPath, Vector3 position)
        {
            var record = JumpTowerModelRecord(modelPath);
            return new MapObject {
                assetId = record?.Id ?? "custom:jump-tower-component:" + role,
                displayName = role == "base" ? L.T("#JUMP_TOWER_BASE") : L.T("#JUMP_TOWER_BALLOON"),
                customType = "jump-tower-component", customRole = role, parentId = tower.id,
                gameModelPath = modelPath, position = WorldView.ToData(position),
                isGroup = record == null, commonAsset = record?.IsCommon ?? false,
                availableMaps = record?.origins.Select(origin => origin.mapId)
                    .Where(map => map != "").Distinct().ToList() ?? new List<string>()
            };
        }

        private void SyncJumpTowerComponents(MapDocument document, MapObject tower)
        {
            document.objects.RemoveAll(candidate => candidate.parentId == tower.id &&
                candidate.customType == "jump-tower-component");
            tower.assetId = "custom:jump-tower"; tower.gameModelPath = ""; tower.isGroup = true;
            tower.commonAsset = false; tower.availableMaps = new List<string>();
            document.objects.Add(CreateJumpTowerComponent(tower, "base", JumpTowerBaseModelPath, Vector3.zero));
            document.objects.Add(CreateJumpTowerComponent(tower, "balloon", JumpTowerBalloonModelPath,
                Vector3.up * tower.jumpTowerHeight * ApexCoordinates.MetersPerUnit));
        }

        internal MapObject[] CreateJumpTower(Vector3 position, string parent = "")
        {
            var tower = new MapObject {
                assetId = "custom:jump-tower", displayName = L.T("#JUMP_TOWER"),
                customType = "jump-tower", parentId = parent ?? "", position = WorldView.ToData(position),
                isGroup = true, jumpTowerHeight = 2000f
            };
            var document = new MapDocument(); document.objects.Add(tower);
            SyncJumpTowerComponents(document, tower);
            return document.objects.ToArray();
        }

        private void InsertJumpTower(Vector3 position, string parent = "")
        {
            var created = CreateJumpTower(position, parent);
            session.Edit(document => document.objects.AddRange(created));
            selectedId = created[0].id; RevealHierarchy(selectedId); Refresh();
            _ = PrepareJumpTowerModels();
            SetStatus(L.T("#JUMP_TOWER_CREATED"));
        }

        private void BuildJumpTowerInspector(MapObject item, VisualElement section)
        {
            section.Add(Label(L.T("#JUMP_TOWER"), "inspector-subsection-title"));
            var height = CompactInspectorField(new FloatField(L.T("#JUMP_TOWER_HEIGHT_APEX_U")) {
                value = item.jumpTowerHeight, isDelayed = true
            });
            section.Add(height);
            section.Add(Label(L.T("#JUMP_TOWER_HELP"), "note"));
            height.RegisterValueChangedCallback(change => {
                CommitInspectorEdit();
                session.Edit(document => {
                    var tower = document.objects.Find(candidate => candidate.id == item.id);
                    tower.jumpTowerHeight = Mathf.Clamp(change.newValue, 128f, 65535f);
                    SyncJumpTowerComponents(document, tower);
                });
                Refresh();
            });
        }

        private bool JumpTowerComponentsNeedSync(MapDocument document, MapObject tower)
        {
            var components = document.objects.Where(candidate => candidate.parentId == tower.id &&
                candidate.customType == "jump-tower-component").ToArray();
            if (components.Length != 2) return true;
            bool Current(string role, string path, Vector3 position)
            {
                var component = components.FirstOrDefault(candidate => candidate.customRole == role);
                var record = JumpTowerModelRecord(path);
                return component != null && GameAssetIndex.SameModelPath(component.gameModelPath, path) &&
                    component.assetId == (record?.Id ?? "custom:jump-tower-component:" + role) &&
                    component.isGroup == (record == null) &&
                    Vector3.Distance(WorldView.ToVector(component.position), position) < .0001f;
            }
            return !Current("base", JumpTowerBaseModelPath, Vector3.zero) ||
                !Current("balloon", JumpTowerBalloonModelPath,
                    Vector3.up * tower.jumpTowerHeight * ApexCoordinates.MetersPerUnit);
        }

        private async Task PrepareJumpTowerModels()
        {
            if (assetLibrary == null || snapshot == null || preparingJumpTowerModels) return;
            preparingJumpTowerModels = true;
            var prepared = new List<string>();
            try
            {
                foreach (string path in new[] { JumpTowerBaseModelPath, JumpTowerBalloonModelPath })
                {
                    var record = JumpTowerModelRecord(path);
                    if (record == null) return;
                    bool wasPrepared = world.models.IsPrepared(record.Id);
                    if (await PrepareDropEntry(record) == null || this == null) return;
                    if (!wasPrepared) prepared.Add(record.Id);
                }
                bool needsSync = snapshot.objects.Where(candidate => candidate.customType == "jump-tower")
                    .Any(tower => JumpTowerComponentsNeedSync(snapshot, tower));
                if (needsSync)
                    session.Edit(document => {
                        foreach (var tower in document.objects.Where(candidate =>
                            candidate.customType == "jump-tower").ToArray())
                            if (JumpTowerComponentsNeedSync(document, tower)) SyncJumpTowerComponents(document, tower);
                    });
                foreach (string assetId in prepared) world.Reload(assetId);
                if (needsSync || prepared.Count > 0) Refresh();
            }
            catch (Exception exception)
            {
                if (this != null) SetStatus(L.T("#JUMP_TOWER_PREVIEW_ERROR") + exception.Message);
            }
            finally { preparingJumpTowerModels = false; }
        }
    }
}
