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
        internal const string BubbleShieldModelPath = "mdl/fx/bb_shield.rmdl";
        private bool preparingBubbleShieldModel;

        private GameAssetRecord BubbleShieldModelRecord() => assetLibrary?.Records.FirstOrDefault(candidate =>
            GameAssetIndex.SameModelPath(candidate.modelPath, BubbleShieldModelPath) && candidate.Supports(Targets));

        internal MapObject CreateBubbleShield(Vector3 position, string parent = "")
        {
            var record = BubbleShieldModelRecord();
            return new MapObject {
                assetId = record?.Id ?? "custom:bubble-shield", displayName = L.T("#BUBBLE_SHIELD"),
                customType = "bubble-shield", gameModelPath = BubbleShieldModelPath,
                parentId = parent ?? "", position = WorldView.ToData(position),
                isGroup = record == null, commonAsset = record?.IsCommon ?? false,
                availableMaps = record?.origins.Select(origin => origin.mapId)
                    .Where(map => map != "").Distinct().ToList() ?? new List<string>(),
                bubbleShieldColor = new Float3(128f, 255f, 128f)
            };
        }

        private void InsertBubbleShield(Vector3 position, string parent = "")
        {
            var shield = CreateBubbleShield(position, parent);
            session.Edit(document => document.objects.Add(shield));
            selectedId = shield.id; RevealHierarchy(shield.id); Refresh();
            _ = PrepareBubbleShieldModel();
            SetStatus(L.T("#BUBBLE_SHIELD_CREATED"));
        }

        private void BuildBubbleShieldInspector(MapObject item, VisualElement section)
        {
            section.Add(Label(L.T("#BUBBLE_SHIELD"), "inspector-subsection-title"));
            var color = new Vector3Field(L.T("#BUBBLE_SHIELD_COLOR_RGB")) {
                value = WorldView.ToVector(item.bubbleShieldColor)
            };
            color.AddToClassList("property-field");
            section.Add(color); section.Add(Label(L.T("#BUBBLE_SHIELD_HELP"), "note"));
            color.RegisterValueChangedCallback(change => {
                CommitInspectorEdit();
                session.Edit(document => {
                    var edited = document.objects.Find(candidate => candidate.id == item.id);
                    edited.bubbleShieldColor = WorldView.ToData(new Vector3(
                        Mathf.Clamp(change.newValue.x, 0f, 255f),
                        Mathf.Clamp(change.newValue.y, 0f, 255f),
                        Mathf.Clamp(change.newValue.z, 0f, 255f)));
                });
                Refresh();
            });
        }

        private async Task PrepareBubbleShieldModel()
        {
            if (assetLibrary == null || snapshot == null || preparingBubbleShieldModel) return;
            var record = BubbleShieldModelRecord();
            if (record == null) return;
            preparingBubbleShieldModel = true;
            try
            {
                bool prepared = world.models.IsPrepared(record.Id);
                if (await PrepareDropEntry(record) == null || this == null) return;
                bool needsSync = snapshot.objects.Any(item => item.customType == "bubble-shield" &&
                    (item.assetId != record.Id || item.isGroup));
                if (needsSync)
                    session.Edit(document => {
                        foreach (var item in document.objects.Where(candidate => candidate.customType == "bubble-shield"))
                        {
                            item.assetId = record.Id; item.isGroup = false;
                            item.gameModelPath = BubbleShieldModelPath; item.commonAsset = record.IsCommon;
                            item.availableMaps = record.origins.Select(origin => origin.mapId)
                                .Where(map => map != "").Distinct().ToList();
                        }
                    });
                if (!prepared) world.Reload(record.Id);
                if (needsSync || !prepared) Refresh();
            }
            catch (Exception exception)
            {
                if (this != null) SetStatus(L.T("#BUBBLE_SHIELD_PREVIEW_ERROR") + exception.Message);
            }
            finally { preparingBubbleShieldModel = false; }
        }
    }
}
