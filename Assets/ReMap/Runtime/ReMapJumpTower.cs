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
        internal const float JumpTowerMinimumHeight = 1000f;
        internal static readonly Vector3 JumpTowerCableOffsetApex = new Vector3(-2f, 2.65f, 0f);
        private const float JumpTowerMaximumHeight = 65535f;
        private bool preparingJumpTowerModels;
        private FloatField jumpTowerHeightInput;

        private static bool IsJumpTowerBalloon(MapObject item) => item != null &&
            item.customType == "jump-tower-component" && item.customRole == "balloon";

        private static float ClampJumpTowerHeight(float height) =>
            Mathf.Clamp(height, JumpTowerMinimumHeight, JumpTowerMaximumHeight);

        private static void NormalizeJumpTowerRotation(MapObject tower)
        {
            var apex = ApexDisplay.Angles(WorldView.ToVector(tower.rotation));
            tower.rotation = WorldView.ToData(ApexDisplay.UnityAngles(
                new Vector3(0f, apex.y, 0f)));
            tower.scale = new Float3(1f, 1f, 1f);
        }

        private static void SyncJumpTowerHeightFromBalloon(MapDocument document,
            MapObject balloon)
        {
            if (!IsJumpTowerBalloon(balloon)) return;
            var tower = document.objects.Find(candidate => candidate.id == balloon.parentId &&
                candidate.customType == "jump-tower");
            if (tower == null) return;
            float height = ClampJumpTowerHeight(
                WorldView.ToVector(balloon.position).y / ApexCoordinates.MetersPerUnit);
            tower.jumpTowerHeight = height;
            balloon.position = WorldView.ToData(Vector3.up * height *
                ApexCoordinates.MetersPerUnit);
        }

        private GameAssetRecord JumpTowerModelRecord(string path) => assetLibrary?.Records.FirstOrDefault(candidate =>
            GameAssetIndex.SameModelPath(candidate.modelPath, path) && candidate.Supports(Targets));

        private MapObject CreateJumpTowerComponent(MapObject tower, string role, string modelPath, Vector3 position)
        {
            var component = new MapObject();
            ConfigureJumpTowerComponent(component, tower, role, modelPath, position);
            return component;
        }

        private void ConfigureJumpTowerComponent(MapObject component, MapObject tower,
            string role, string modelPath, Vector3 position)
        {
            var record = JumpTowerModelRecord(modelPath);
            component.assetId = record?.Id ?? "custom:jump-tower-component:" + role;
            component.displayName = role == "base" ? L.T("#JUMP_TOWER_BASE") :
                L.T("#JUMP_TOWER_BALLOON");
            component.customType = "jump-tower-component";
            component.customRole = role;
            component.parentId = tower.id;
            component.gameModelPath = modelPath;
            component.position = WorldView.ToData(position);
            component.rotation = default;
            component.scale = new Float3(1f, 1f, 1f);
            component.isGroup = record == null;
            component.commonAsset = record?.IsCommon ?? false;
            component.availableMaps = record?.origins.Select(origin => origin.mapId)
                .Where(map => map != "").Distinct().ToList() ?? new List<string>();
        }

        private void SyncJumpTowerComponents(MapDocument document, MapObject tower)
        {
            tower.jumpTowerHeight = ClampJumpTowerHeight(tower.jumpTowerHeight);
            NormalizeJumpTowerRotation(tower);
            tower.assetId = "custom:jump-tower"; tower.gameModelPath = ""; tower.isGroup = true;
            tower.commonAsset = false; tower.availableMaps = new List<string>();
            var components = document.objects.Where(candidate => candidate.parentId == tower.id &&
                candidate.customType == "jump-tower-component").ToArray();

            MapObject Ensure(string role, string path, Vector3 position)
            {
                var component = components.FirstOrDefault(candidate => candidate.customRole == role);
                if (component == null)
                {
                    component = CreateJumpTowerComponent(tower, role, path, position);
                    document.objects.Add(component);
                }
                else ConfigureJumpTowerComponent(component, tower, role, path, position);
                return component;
            }

            var towerBase = Ensure("base", JumpTowerBaseModelPath, Vector3.zero);
            towerBase.positionLocked = true;
            var balloon = Ensure("balloon", JumpTowerBalloonModelPath,
                Vector3.up * tower.jumpTowerHeight * ApexCoordinates.MetersPerUnit);
            var retained = new HashSet<string> { towerBase.id, balloon.id };
            document.objects.RemoveAll(candidate => candidate.parentId == tower.id &&
                candidate.customType == "jump-tower-component" && !retained.Contains(candidate.id));
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
                value = item.jumpTowerHeight, isDelayed = false
            });
            jumpTowerHeightInput = height;
            var balloon = snapshot.objects.Find(candidate => candidate.parentId == item.id &&
                IsJumpTowerBalloon(candidate));
            height.SetEnabled(balloon?.positionLocked != true);
            section.Add(height);
            section.Add(Label(L.T("#JUMP_TOWER_HELP"), "note"));
            height.RegisterValueChangedCallback(change => Run(() => {
                float value = ClampJumpTowerHeight(change.newValue);
                if (!Mathf.Approximately(value, change.newValue))
                    height.SetValueWithoutNotify(value);
                CommitInspectorEdit();
                session.Edit(document => {
                    var tower = document.objects.Find(candidate => candidate.id == item.id);
                    tower.jumpTowerHeight = value;
                    SyncJumpTowerComponents(document, tower);
                });
                snapshot = session.Snapshot();
                world.Sync(snapshot, selectedId);
                UpdateGizmoVisual();
            }));
        }

        private void BuildJumpTowerBalloonInspector(MapObject item, VisualElement section)
        {
            if (!IsJumpTowerBalloon(item)) return;
            var tower = snapshot.objects.Find(candidate => candidate.id == item.parentId &&
                candidate.customType == "jump-tower");
            if (tower == null) return;
            section.Add(Label(L.T("#JUMP_TOWER_BALLOON"), "inspector-subsection-title"));
            var height = CompactInspectorField(new FloatField(L.T("#JUMP_TOWER_HEIGHT_APEX_U")) {
                value = tower.jumpTowerHeight, isDelayed = false
            });
            jumpTowerHeightInput = height;
            height.SetEnabled(!item.positionLocked);
            section.Add(height);
            height.RegisterValueChangedCallback(change => Run(() => {
                float value = ClampJumpTowerHeight(change.newValue);
                if (!Mathf.Approximately(value, change.newValue))
                    height.SetValueWithoutNotify(value);
                CommitInspectorEdit();
                session.Edit(document => {
                    var editedTower = document.objects.Find(candidate => candidate.id == tower.id);
                    editedTower.jumpTowerHeight = value;
                    SyncJumpTowerComponents(document, editedTower);
                });
                snapshot = session.Snapshot();
                world.Sync(snapshot, selectedId);
                SyncInspectorValues();
                UpdateGizmoVisual();
            }));

            var locked = CompactInspectorField(new Toggle(L.T("#LOCK_CONTROL_POINT_POSITION")) {
                value = item.positionLocked
            });
            locked.tooltip = L.T("#LOCK_CONTROL_POINT_POSITION_HELP");
            section.Add(locked);
            locked.RegisterValueChangedCallback(change => Run(() => {
                CommitInspectorEdit();
                session.Edit(document => {
                    var balloon = document.objects.Find(candidate => candidate.id == item.id);
                    if (IsJumpTowerBalloon(balloon)) balloon.positionLocked = change.newValue;
                });
                Refresh();
            }));
        }

        private void SyncJumpTowerHeightInput()
        {
            if (jumpTowerHeightInput == null || selectedId == null || snapshot == null) return;
            var selected = snapshot.objects.Find(candidate => candidate.id == selectedId);
            float height;
            if (IsJumpTowerBalloon(selected))
                height = WorldView.ToVector(world.LocalPose(selected.id).position).y /
                    ApexCoordinates.MetersPerUnit;
            else if (selected?.customType == "jump-tower") height = selected.jumpTowerHeight;
            else return;
            jumpTowerHeightInput.SetValueWithoutNotify(ClampJumpTowerHeight(height));
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
