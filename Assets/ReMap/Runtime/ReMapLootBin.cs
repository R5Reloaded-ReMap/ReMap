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
        internal const string LootBinModelPath = "mdl/props/loot_bin/loot_bin_01_animated.rmdl";
        private bool preparingLootBinModel;

        private GameAssetRecord LootBinModelRecord() => assetLibrary?.Records.FirstOrDefault(candidate =>
            GameAssetIndex.SameModelPath(candidate.modelPath, LootBinModelPath) && candidate.Supports(Targets));

        private MapObject CreateLootBin(Vector3 position, string parent)
        {
            var record = LootBinModelRecord();
            return new MapObject {
                assetId = record?.Id ?? "custom:loot-bin", displayName = L.T("#LOOT_BIN"),
                customType = "loot-bin", gameModelPath = LootBinModelPath,
                parentId = parent ?? "", position = WorldView.ToData(position),
                isGroup = record == null, commonAsset = record?.IsCommon ?? false,
                availableMaps = record?.origins.Select(origin => origin.mapId)
                    .Where(map => map != "").Distinct().ToList() ?? new List<string>()
            };
        }

        private void InsertLootBin(Vector3 position, string parent = "")
        {
            var lootBin = CreateLootBin(position, parent);
            session.Edit(document => document.objects.Add(lootBin));
            selectedId = lootBin.id; RevealHierarchy(lootBin.id); Refresh();
            _ = PrepareLootBinModel();
            SetStatus(L.T("#LOOT_BIN_CREATED"));
        }

        private void BuildLootBinInspector(MapObject item, VisualElement section)
        {
            section.Add(Label(L.T("#LOOT_BIN"), "inspector-subsection-title"));
            var labels = new[] { "#LOOT_BIN_DEFAULT", "#LOOT_BIN_BLUE", "#LOOT_BIN_GOLD", "#LOOT_BIN_YELLOW" }
                .Select(L.T).ToList();
            var skin = CompactInspectorField(new DropdownField(L.T("#LOOT_BIN_SKIN"), labels,
                Mathf.Clamp(item.lootBinSkin, 0, labels.Count - 1)));
            section.Add(skin);
            skin.RegisterValueChangedCallback(change => Run(() => {
                int value = Math.Max(0, labels.IndexOf(change.newValue));
                session.Edit(document => document.objects.Find(candidate => candidate.id == item.id)
                    .lootBinSkin = value);
                Refresh();
            }));
        }

        private async Task PrepareLootBinModel()
        {
            if (assetLibrary == null || snapshot == null || preparingLootBinModel) return;
            var record = LootBinModelRecord();
            if (record == null) return;
            preparingLootBinModel = true;
            try
            {
                bool prepared = world.models.IsPrepared(record.Id);
                var entry = await PrepareDropEntry(record);
                if (this == null || entry == null) return;
                bool needsSync = snapshot.objects.Any(item => item.customType == "loot-bin" &&
                    (item.assetId != record.Id || item.isGroup));
                if (needsSync)
                    session.Edit(document => {
                        foreach (var item in document.objects.Where(candidate =>
                            candidate.customType == "loot-bin"))
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
                if (this != null) SetStatus(L.T("#LOOT_BIN_PREVIEW_ERROR") + exception.Message);
            }
            finally { preparingLootBinModel = false; }
        }
    }
}
