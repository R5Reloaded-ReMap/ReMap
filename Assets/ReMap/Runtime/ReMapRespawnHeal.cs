using ReMap.Standalone.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UIElements;

namespace ReMap.Standalone
{
    internal sealed class ReMapRespawnHealProfile
    {
        internal readonly int Type;
        internal readonly string NameKey;
        internal readonly string ModelPath;
        internal ReMapRespawnHealProfile(int type, string nameKey, string modelPath)
        { Type = type; NameKey = nameKey; ModelPath = modelPath; }
    }

    public sealed partial class ReMapApp
    {
        internal static readonly ReMapRespawnHealProfile[] RespawnHealProfiles = {
            new ReMapRespawnHealProfile(0, "#RESPAWN_HEAL_MEDKIT", "mdl/weapons_r5/loot/w_loot_wep_iso_health_main_large.rmdl"),
            new ReMapRespawnHealProfile(1, "#RESPAWN_HEAL_BATTERY", "mdl/weapons_r5/loot/w_loot_wep_iso_shield_battery_large.rmdl"),
            new ReMapRespawnHealProfile(2, "#RESPAWN_HEAL_SYRINGE", "mdl/weapons_r5/loot/w_loot_wep_iso_health_main_small.rmdl"),
            new ReMapRespawnHealProfile(3, "#RESPAWN_HEAL_CELL", "mdl/weapons_r5/loot/w_loot_wep_iso_shield_battery_small.rmdl"),
            new ReMapRespawnHealProfile(4, "#RESPAWN_HEAL_PHOENIX", "mdl/weapons_r5/loot/w_loot_wep_iso_phoenix_kit_v1.rmdl")
        };
        private bool preparingRespawnHealModels;

        internal static string RespawnHealModelPath(int type) =>
            RespawnHealProfiles.FirstOrDefault(profile => profile.Type == type)?.ModelPath ??
            RespawnHealProfiles[0].ModelPath;

        private GameAssetRecord RespawnHealModelRecord(ReMapRespawnHealProfile profile) =>
            assetLibrary?.Records.FirstOrDefault(candidate =>
                GameAssetIndex.SameModelPath(candidate.modelPath, profile.ModelPath) && candidate.Supports(Targets));

        private List<ReMapRespawnHealProfile> AvailableRespawnHealProfiles() =>
            RespawnHealProfiles.Where(profile => RespawnHealModelRecord(profile) != null).ToList();

        private bool RespawnHealAnyModelAvailable() => AvailableRespawnHealProfiles().Count > 0;

        private void SyncRespawnHealModel(MapObject item, ReMapRespawnHealProfile profile)
        {
            var record = RespawnHealModelRecord(profile);
            item.respawnHealType = profile.Type;
            item.gameModelPath = profile.ModelPath;
            item.assetId = record?.Id ?? "custom:respawn-heal";
            item.isGroup = record == null;
            item.commonAsset = record?.IsCommon ?? false;
            item.availableMaps = record?.origins.Select(origin => origin.mapId)
                .Where(map => map != "").Distinct().ToList() ?? new List<string>();
        }

        private MapObject CreateRespawnHeal(Vector3 position, string parent)
        {
            var profile = AvailableRespawnHealProfiles().FirstOrDefault() ?? RespawnHealProfiles[0];
            var item = new MapObject {
                assetId = "custom:respawn-heal", displayName = L.T("#RESPAWN_HEAL"),
                customType = "respawn-heal", parentId = parent ?? "", position = WorldView.ToData(position),
                respawnHealRespawnTime = 6f, respawnHealDuration = 5f,
                respawnHealAmount = 25, respawnHealProgressive = true
            };
            SyncRespawnHealModel(item, profile);
            return item;
        }

        private void InsertRespawnHeal(Vector3 position, string parent = "")
        {
            var item = CreateRespawnHeal(position, parent);
            session.Edit(document => document.objects.Add(item));
            selectedId = item.id; RevealHierarchy(item.id); Refresh();
            _ = PrepareRespawnHealModels();
            SetStatus(L.T("#RESPAWN_HEAL_CREATED"));
        }

        private void BuildRespawnHealInspector(MapObject item, VisualElement section)
        {
            section.Add(Label(L.T("#RESPAWN_HEAL"), "inspector-subsection-title"));
            var profiles = AvailableRespawnHealProfiles();
            if (profiles.Count == 0)
            {
                section.Add(Label(L.T("#CUSTOM_OBJECT_MODELS_UNAVAILABLE"), "note"));
                return;
            }
            int selected = profiles.FindIndex(profile => profile.Type == item.respawnHealType);
            if (selected < 0) selected = 0;
            var type = CompactInspectorField(new DropdownField(L.T("#RESPAWN_HEAL_TYPE"),
                profiles.Select(profile => L.T(profile.NameKey)).ToList(), selected));
            var respawn = CompactInspectorField(new FloatField(L.T("#RESPAWN_HEAL_RESPAWN_TIME")) {
                value = item.respawnHealRespawnTime, isDelayed = true
            });
            var duration = CompactInspectorField(new FloatField(L.T("#RESPAWN_HEAL_DURATION")) {
                value = item.respawnHealDuration, isDelayed = true
            });
            var amount = CompactInspectorField(new IntegerField(L.T("#RESPAWN_HEAL_AMOUNT")) {
                value = item.respawnHealAmount, isDelayed = true
            });
            amount.SetEnabled(item.respawnHealType == 2 || item.respawnHealType == 3);
            var progressive = CompactInspectorField(new Toggle(L.T("#RESPAWN_HEAL_PROGRESSIVE")) {
                value = item.respawnHealProgressive
            });
            section.Add(type); section.Add(respawn); section.Add(duration); section.Add(amount);
            section.Add(progressive); section.Add(Label(L.T("#RESPAWN_HEAL_HELP"), "note"));
            type.RegisterValueChangedCallback(change => Run(() => {
                int index = type.choices.IndexOf(change.newValue);
                if (index < 0 || index >= profiles.Count) return;
                session.Edit(document => SyncRespawnHealModel(
                    document.objects.Find(candidate => candidate.id == item.id), profiles[index]));
                Refresh(); _ = PrepareRespawnHealModels();
            }));
            respawn.RegisterValueChangedCallback(change => Run(() => {
                session.Edit(document => document.objects.Find(candidate => candidate.id == item.id)
                    .respawnHealRespawnTime = Mathf.Clamp(change.newValue, 0f, 86400f)); Refresh();
            }));
            duration.RegisterValueChangedCallback(change => Run(() => {
                session.Edit(document => document.objects.Find(candidate => candidate.id == item.id)
                    .respawnHealDuration = Mathf.Clamp(change.newValue, .05f, 3600f)); Refresh();
            }));
            amount.RegisterValueChangedCallback(change => Run(() => {
                session.Edit(document => document.objects.Find(candidate => candidate.id == item.id)
                    .respawnHealAmount = Mathf.Clamp(change.newValue, 1, 1000)); Refresh();
            }));
            progressive.RegisterValueChangedCallback(change => Run(() => {
                session.Edit(document => document.objects.Find(candidate => candidate.id == item.id)
                    .respawnHealProgressive = change.newValue); Refresh();
            }));
        }

        private async Task PrepareRespawnHealModels()
        {
            if (assetLibrary == null || snapshot == null || preparingRespawnHealModels) return;
            var available = AvailableRespawnHealProfiles();
            if (available.Count == 0) return;
            preparingRespawnHealModels = true;
            var prepared = new List<string>();
            try
            {
                foreach (var profile in available)
                {
                    var record = RespawnHealModelRecord(profile);
                    bool wasPrepared = world.models.IsPrepared(record.Id);
                    if (await PrepareDropEntry(record) == null || this == null) return;
                    if (!wasPrepared) prepared.Add(record.Id);
                }
                bool needsSync = snapshot.objects.Where(item => item.customType == "respawn-heal").Any(item => {
                    var profile = available.FirstOrDefault(candidate => candidate.Type == item.respawnHealType) ?? available[0];
                    var record = RespawnHealModelRecord(profile);
                    return item.respawnHealType != profile.Type || item.assetId != record.Id || item.isGroup ||
                        !GameAssetIndex.SameModelPath(item.gameModelPath, profile.ModelPath);
                });
                if (needsSync)
                    session.Edit(document => {
                        foreach (var item in document.objects.Where(candidate => candidate.customType == "respawn-heal"))
                        {
                            var profile = available.FirstOrDefault(candidate => candidate.Type == item.respawnHealType) ?? available[0];
                            SyncRespawnHealModel(item, profile);
                        }
                    });
                foreach (string assetId in prepared) world.Reload(assetId);
                if (needsSync || prepared.Count > 0) Refresh();
            }
            catch (Exception exception)
            {
                if (this != null) SetStatus(L.T("#RESPAWN_HEAL_PREVIEW_ERROR") + exception.Message);
            }
            finally { preparingRespawnHealModels = false; }
        }
    }
}
