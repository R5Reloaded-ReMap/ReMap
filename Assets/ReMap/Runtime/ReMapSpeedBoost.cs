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
        internal const string SpeedBoostOrbModelPath = "mdl/fx/plasma_sphere_01.rmdl";
        internal const string SpeedBoostBaseModelPath = "mdl/fx/ar_edge_sphere_512.rmdl";
        private bool preparingSpeedBoostModels;

        private GameAssetRecord SpeedBoostModelRecord(string path) => assetLibrary?.Records.FirstOrDefault(candidate =>
            GameAssetIndex.SameModelPath(candidate.modelPath, path) && candidate.Supports(Targets));

        private bool SpeedBoostModelsAvailable() => SpeedBoostModelRecord(SpeedBoostOrbModelPath) != null &&
            SpeedBoostModelRecord(SpeedBoostBaseModelPath) != null;

        private MapObject CreateSpeedBoostComponent(MapObject boost, string role, string path,
            Vector3 position, float scale)
        {
            var record = SpeedBoostModelRecord(path);
            return new MapObject {
                assetId = record?.Id ?? "custom:speed-boost-component:" + role,
                displayName = role == "orb" ? L.T("#SPEED_BOOST_ORB") : L.T("#SPEED_BOOST_BASE"),
                customType = "speed-boost-component", customRole = role, parentId = boost.id,
                gameModelPath = path, position = WorldView.ToData(position),
                scale = new Float3(scale, scale, scale), isGroup = record == null,
                commonAsset = record?.IsCommon ?? false,
                availableMaps = record?.origins.Select(origin => origin.mapId)
                    .Where(map => map != "").Distinct().ToList() ?? new List<string>()
            };
        }

        private void SyncSpeedBoostComponents(MapDocument document, MapObject boost)
        {
            document.objects.RemoveAll(candidate => candidate.parentId == boost.id &&
                candidate.customType == "speed-boost-component");
            boost.assetId = "custom:speed-boost"; boost.gameModelPath = ""; boost.isGroup = true;
            boost.commonAsset = false; boost.availableMaps = new List<string>();
            document.objects.Add(CreateSpeedBoostComponent(boost, "base", SpeedBoostBaseModelPath,
                Vector3.zero, .1f));
            document.objects.Add(CreateSpeedBoostComponent(boost, "orb", SpeedBoostOrbModelPath,
                Vector3.up * 50f * ApexCoordinates.MetersPerUnit, .5f));
        }

        internal MapObject[] CreateSpeedBoost(Vector3 position, string parent = "")
        {
            var boost = new MapObject {
                assetId = "custom:speed-boost", displayName = L.T("#SPEED_BOOST"),
                customType = "speed-boost", parentId = parent ?? "", position = WorldView.ToData(position),
                isGroup = true, speedBoostColor = new Float3(255f, 255f, 255f),
                speedBoostRespawnTime = 5f, speedBoostStrength = .35f,
                speedBoostDuration = 3f, speedBoostFadeTime = 0f
            };
            var document = new MapDocument(); document.objects.Add(boost);
            SyncSpeedBoostComponents(document, boost);
            return document.objects.ToArray();
        }

        private void InsertSpeedBoost(Vector3 position, string parent = "")
        {
            var created = CreateSpeedBoost(position, parent);
            session.Edit(document => document.objects.AddRange(created));
            selectedId = created[0].id; RevealHierarchy(selectedId); Refresh();
            _ = PrepareSpeedBoostModels();
            SetStatus(L.T("#SPEED_BOOST_CREATED"));
        }

        private void BuildSpeedBoostInspector(MapObject item, VisualElement section)
        {
            section.Add(Label(L.T("#SPEED_BOOST"), "inspector-subsection-title"));
            var color = CompactInspectorField(new Vector3Field(L.T("#SPEED_BOOST_COLOR_RGB")) {
                value = WorldView.ToVector(item.speedBoostColor)
            });
            var respawn = CompactInspectorField(new FloatField(L.T("#SPEED_BOOST_RESPAWN_TIME")) {
                value = item.speedBoostRespawnTime, isDelayed = true
            });
            var strength = CompactInspectorField(new FloatField(L.T("#SPEED_BOOST_STRENGTH")) {
                value = item.speedBoostStrength, isDelayed = true
            });
            var duration = CompactInspectorField(new FloatField(L.T("#SPEED_BOOST_DURATION")) {
                value = item.speedBoostDuration, isDelayed = true
            });
            var fade = CompactInspectorField(new FloatField(L.T("#SPEED_BOOST_FADE_TIME")) {
                value = item.speedBoostFadeTime, isDelayed = true
            });
            section.Add(color); section.Add(respawn); section.Add(strength); section.Add(duration); section.Add(fade);
            section.Add(Label(L.T("#SPEED_BOOST_HELP"), "note"));
            color.RegisterValueChangedCallback(change => ChangeSpeedBoost(item.id, edited =>
                edited.speedBoostColor = WorldView.ToData(new Vector3(
                    Mathf.Clamp(change.newValue.x, 0f, 255f), Mathf.Clamp(change.newValue.y, 0f, 255f),
                    Mathf.Clamp(change.newValue.z, 0f, 255f)))));
            respawn.RegisterValueChangedCallback(change => ChangeSpeedBoost(item.id, edited =>
                edited.speedBoostRespawnTime = Mathf.Clamp(change.newValue, 0f, 86400f)));
            strength.RegisterValueChangedCallback(change => ChangeSpeedBoost(item.id, edited =>
                edited.speedBoostStrength = Mathf.Clamp(change.newValue, 0f, 100f)));
            duration.RegisterValueChangedCallback(change => ChangeSpeedBoost(item.id, edited => {
                edited.speedBoostDuration = Mathf.Clamp(change.newValue, .05f, 3600f);
                edited.speedBoostFadeTime = Mathf.Min(edited.speedBoostFadeTime, edited.speedBoostDuration);
            }));
            fade.RegisterValueChangedCallback(change => ChangeSpeedBoost(item.id, edited =>
                edited.speedBoostFadeTime = Mathf.Clamp(change.newValue, 0f, edited.speedBoostDuration)));
        }

        private void ChangeSpeedBoost(string id, Action<MapObject> change)
        {
            CommitInspectorEdit();
            session.Edit(document => change(document.objects.Find(candidate => candidate.id == id)));
            Refresh();
        }

        private bool SpeedBoostComponentsNeedSync(MapDocument document, MapObject boost)
        {
            var components = document.objects.Where(candidate => candidate.parentId == boost.id &&
                candidate.customType == "speed-boost-component").ToArray();
            if (components.Length != 2) return true;
            bool Current(string role, string path, Vector3 position, float scale)
            {
                var component = components.FirstOrDefault(candidate => candidate.customRole == role);
                var record = SpeedBoostModelRecord(path);
                return component != null && GameAssetIndex.SameModelPath(component.gameModelPath, path) &&
                    component.assetId == (record?.Id ?? "custom:speed-boost-component:" + role) &&
                    component.isGroup == (record == null) &&
                    Vector3.Distance(WorldView.ToVector(component.position), position) < .0001f &&
                    Mathf.Abs(component.scale.x - scale) < .0001f;
            }
            return !Current("base", SpeedBoostBaseModelPath, Vector3.zero, .1f) ||
                !Current("orb", SpeedBoostOrbModelPath,
                    Vector3.up * 50f * ApexCoordinates.MetersPerUnit, .5f);
        }

        private async Task PrepareSpeedBoostModels()
        {
            if (assetLibrary == null || snapshot == null || preparingSpeedBoostModels) return;
            preparingSpeedBoostModels = true;
            var prepared = new List<string>();
            try
            {
                foreach (string path in new[] { SpeedBoostOrbModelPath, SpeedBoostBaseModelPath })
                {
                    var record = SpeedBoostModelRecord(path);
                    if (record == null) return;
                    bool wasPrepared = world.models.IsPrepared(record.Id);
                    if (await PrepareDropEntry(record) == null || this == null) return;
                    if (!wasPrepared) prepared.Add(record.Id);
                }
                bool needsSync = snapshot.objects.Where(candidate => candidate.customType == "speed-boost")
                    .Any(boost => SpeedBoostComponentsNeedSync(snapshot, boost));
                if (needsSync)
                    session.Edit(document => {
                        foreach (var boost in document.objects.Where(candidate =>
                            candidate.customType == "speed-boost").ToArray())
                            if (SpeedBoostComponentsNeedSync(document, boost)) SyncSpeedBoostComponents(document, boost);
                    });
                foreach (string assetId in prepared) world.Reload(assetId);
                if (needsSync || prepared.Count > 0) Refresh();
            }
            catch (Exception exception)
            {
                if (this != null) SetStatus(L.T("#SPEED_BOOST_PREVIEW_ERROR") + exception.Message);
            }
            finally { preparingSpeedBoostModels = false; }
        }
    }
}
