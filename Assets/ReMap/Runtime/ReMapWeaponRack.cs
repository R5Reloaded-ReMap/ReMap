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
        internal const string WeaponRackModelPath = "mdl/industrial/gun_rack_arm_down.rmdl";
        private static readonly string[] WeaponRackWeapons = {
            "mp_weapon_alternator_smg", "mp_weapon_autopistol", "mp_weapon_defender",
            "mp_weapon_dmr", "mp_weapon_doubletake", "mp_weapon_energy_ar",
            "mp_weapon_energy_shotgun", "mp_weapon_esaw", "mp_weapon_g2",
            "mp_weapon_hemlok", "mp_weapon_lmg", "mp_weapon_lstar", "mp_weapon_mastiff",
            "mp_weapon_pdw", "mp_weapon_r97", "mp_weapon_rspn101", "mp_weapon_semipistol",
            "mp_weapon_shotgun", "mp_weapon_shotgun_pistol", "mp_weapon_sniper",
            "mp_weapon_vinson", "mp_weapon_wingman"
        };
        private bool preparingWeaponRackModel;

        private GameAssetRecord WeaponRackModelRecord() => assetLibrary?.Records.FirstOrDefault(candidate =>
            GameAssetIndex.SameModelPath(candidate.modelPath, WeaponRackModelPath) && candidate.Supports(Targets));

        private MapObject CreateWeaponRack(Vector3 position, string parent)
        {
            var record = WeaponRackModelRecord();
            return new MapObject {
                assetId = record?.Id ?? "custom:weapon-rack", displayName = L.T("#WEAPON_RACK"),
                customType = "weapon-rack", gameModelPath = WeaponRackModelPath,
                parentId = parent ?? "", position = WorldView.ToData(position),
                weaponRackWeapon = "mp_weapon_rspn101", weaponRackRespawnTime = .5f,
                isGroup = record == null, commonAsset = record?.IsCommon ?? false,
                availableMaps = record?.origins.Select(origin => origin.mapId)
                    .Where(map => map != "").Distinct().ToList() ?? new List<string>()
            };
        }

        private void InsertWeaponRack(Vector3 position, string parent = "")
        {
            var rack = CreateWeaponRack(position, parent);
            session.Edit(document => document.objects.Add(rack));
            selectedId = rack.id; RevealHierarchy(rack.id); Refresh();
            _ = PrepareWeaponRackModel();
            SetStatus(L.T("#WEAPON_RACK_CREATED"));
        }

        private void BuildWeaponRackInspector(MapObject item, VisualElement section)
        {
            section.Add(Label(L.T("#WEAPON_RACK"), "inspector-subsection-title"));
            var references = WeaponRackWeapons.ToList();
            if (!references.Contains(item.weaponRackWeapon)) references.Add(item.weaponRackWeapon);
            int selected = Math.Max(0, references.IndexOf(item.weaponRackWeapon));
            var weapon = CompactInspectorField(new DropdownField(L.T("#WEAPON_RACK_WEAPON"),
                references, selected));
            var respawnTime = CompactInspectorField(new FloatField(L.T("#WEAPON_RACK_RESPAWN_TIME")) {
                value = item.weaponRackRespawnTime, isDelayed = true
            });
            section.Add(weapon); section.Add(respawnTime);
            section.Add(Label(L.T("#WEAPON_RACK_HELP"), "note"));
            weapon.RegisterValueChangedCallback(change => Run(() => {
                session.Edit(document => document.objects.Find(candidate => candidate.id == item.id)
                    .weaponRackWeapon = change.newValue);
                Refresh();
            }));
            respawnTime.RegisterValueChangedCallback(change => Run(() => {
                session.Edit(document => document.objects.Find(candidate => candidate.id == item.id)
                    .weaponRackRespawnTime = Mathf.Clamp(change.newValue, 0f, 86400f));
                Refresh();
            }));
        }

        private async Task PrepareWeaponRackModel()
        {
            if (assetLibrary == null || snapshot == null || preparingWeaponRackModel) return;
            var record = WeaponRackModelRecord();
            if (record == null) return;
            preparingWeaponRackModel = true;
            try
            {
                bool prepared = world.models.IsPrepared(record.Id);
                var entry = await PrepareDropEntry(record);
                if (this == null || entry == null) return;
                bool needsSync = snapshot.objects.Any(item => item.customType == "weapon-rack" &&
                    (item.assetId != record.Id || item.isGroup));
                if (needsSync)
                    session.Edit(document => {
                        foreach (var item in document.objects.Where(candidate =>
                            candidate.customType == "weapon-rack"))
                        {
                            item.assetId = record.Id; item.isGroup = false;
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
                if (this != null) SetStatus(L.T("#WEAPON_RACK_PREVIEW_ERROR") + exception.Message);
            }
            finally { preparingWeaponRackModel = false; }
        }
    }
}
