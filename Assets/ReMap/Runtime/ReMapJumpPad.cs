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
        internal const string JumpPadModelPath = "mdl/props/octane_jump_pad/octane_jump_pad.rmdl";
        private bool preparingJumpPadModel;

        private GameAssetRecord JumpPadModelRecord() => assetLibrary?.Records.FirstOrDefault(candidate =>
            GameAssetIndex.SameModelPath(candidate.modelPath, JumpPadModelPath) && candidate.Supports(Targets));

        internal MapObject CreateJumpPad(Vector3 position, string parent = "")
        {
            var record = JumpPadModelRecord();
            return new MapObject {
                assetId = record?.Id ?? "custom:jump-pad", displayName = L.T("#JUMP_PAD"),
                customType = "jump-pad", gameModelPath = JumpPadModelPath,
                parentId = parent ?? "", position = WorldView.ToData(position),
                isGroup = record == null, commonAsset = record?.IsCommon ?? false,
                availableMaps = record?.origins.Select(origin => origin.mapId)
                    .Where(map => map != "").Distinct().ToList() ?? new List<string>(),
                jumpPadLaunchVelocity = 1000f, jumpPadForwardScale = 1.7f,
                jumpPadRadius = 45f, jumpPadDoubleJump = true
            };
        }

        private void InsertJumpPad(Vector3 position, string parent = "")
        {
            var jumpPad = CreateJumpPad(position, parent);
            session.Edit(document => document.objects.Add(jumpPad));
            selectedId = jumpPad.id; RevealHierarchy(jumpPad.id); Refresh();
            _ = PrepareJumpPadModel();
            SetStatus(L.T("#JUMP_PAD_CREATED"));
        }

        private void BuildJumpPadInspector(MapObject item, VisualElement section)
        {
            section.Add(Label(L.T("#JUMP_PAD"), "inspector-subsection-title"));
            var velocity = CompactInspectorField(new FloatField(L.T("#JUMP_PAD_LAUNCH_VELOCITY")) {
                value = item.jumpPadLaunchVelocity, isDelayed = true
            });
            var forward = CompactInspectorField(new FloatField(L.T("#JUMP_PAD_FORWARD_SCALE")) {
                value = item.jumpPadForwardScale, isDelayed = true
            });
            var radius = CompactInspectorField(new FloatField(L.T("#JUMP_PAD_RADIUS")) {
                value = item.jumpPadRadius, isDelayed = true
            });
            var doubleJump = CompactInspectorField(new Toggle(L.T("#JUMP_PAD_DOUBLE_JUMP")) {
                value = item.jumpPadDoubleJump
            });
            section.Add(velocity); section.Add(forward); section.Add(radius); section.Add(doubleJump);

            velocity.RegisterValueChangedCallback(change => ChangeJumpPad(item.id, edited =>
                edited.jumpPadLaunchVelocity = Mathf.Clamp(change.newValue, 100f, 5000f)));
            forward.RegisterValueChangedCallback(change => ChangeJumpPad(item.id, edited =>
                edited.jumpPadForwardScale = Mathf.Clamp(change.newValue, .1f, 10f)));
            radius.RegisterValueChangedCallback(change => ChangeJumpPad(item.id, edited =>
                edited.jumpPadRadius = Mathf.Clamp(change.newValue, 1f, 512f)));
            doubleJump.RegisterValueChangedCallback(change => ChangeJumpPad(item.id, edited =>
                edited.jumpPadDoubleJump = change.newValue));

            section.Add(Label(L.T("#CLASSIC_PROP_SETTINGS"), "inspector-subsection-title"));
            var mantle = CompactInspectorField(new Toggle(L.T("#ALLOW_MANTLE")) { value = item.allowMantle });
            var fade = CompactInspectorField(new FloatField(L.T("#FADE_DISTANCE_APEX_U")) {
                value = item.fadeDistance, isDelayed = true
            });
            var realm = CompactInspectorField(new IntegerField(L.T("#REALM_ID")) {
                value = item.realmId, isDelayed = true
            });
            section.Add(mantle); section.Add(fade); section.Add(realm);
            mantle.RegisterValueChangedCallback(change => ChangeJumpPad(item.id,
                edited => edited.allowMantle = change.newValue));
            fade.RegisterValueChangedCallback(change => ChangeJumpPad(item.id, edited =>
                edited.fadeDistance = float.IsFinite(change.newValue) ? Mathf.Max(-1f, change.newValue) : 50000f));
            realm.RegisterValueChangedCallback(change => ChangeJumpPad(item.id,
                edited => edited.realmId = Math.Max(-1, change.newValue)));
        }

        private void ChangeJumpPad(string id, Action<MapObject> change)
        {
            CommitInspectorEdit();
            session.Edit(document => change(document.objects.Find(candidate => candidate.id == id)));
            Refresh();
        }

        private async Task PrepareJumpPadModel()
        {
            if (assetLibrary == null || snapshot == null || preparingJumpPadModel) return;
            var record = JumpPadModelRecord();
            if (record == null) return;
            preparingJumpPadModel = true;
            try
            {
                bool prepared = world.models.IsPrepared(record.Id);
                var entry = await PrepareDropEntry(record);
                if (this == null || entry == null) return;
                bool needsSync = snapshot.objects.Any(item => item.customType == "jump-pad" &&
                    (item.assetId != record.Id || item.isGroup));
                if (needsSync)
                    session.Edit(document => {
                        foreach (var item in document.objects.Where(candidate => candidate.customType == "jump-pad"))
                        {
                            item.assetId = record.Id; item.isGroup = false;
                            item.gameModelPath = JumpPadModelPath;
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
                if (this != null) SetStatus(L.T("#JUMP_PAD_PREVIEW_ERROR") + exception.Message);
            }
            finally { preparingJumpPadModel = false; }
        }
    }
}
