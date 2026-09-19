using System;
using System.IO;
using System.Linq;
using ReMap.Standalone.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace ReMap.Standalone
{
    public sealed partial class ReMapApp
    {
        private CatalogEntry[] CustomCatalogEntries() => new[] {
            new CatalogEntry("custom:zipline", L.T("#ZIPLINE"), L.T("#CUSTOM"), new Vector3(1, 10.16f, 1)) { CustomType = "zipline" },
            new CatalogEntry("custom:door", L.T("#DOOR"), L.T("#CUSTOM"), new Vector3(1.83f, 2.44f, .15f)) { CustomType = "door" },
            new CatalogEntry("custom:curved-zipline", L.T("#CURVED_ZIPLINE"), L.T("#CUSTOM"), new Vector3(15.24f, 2.04f, 2.54f)) {
                CustomType = "curved-zipline", SupportedGameTargets = new[] { GameTargets.R5Reloaded }
            },
            new CatalogEntry("custom:ziprail", L.T("#ZIPRAIL"), L.T("#CUSTOM"), new Vector3(15.24f, 2.04f, 2.54f)) {
                CustomType = "ziprail", SupportedGameTargets = new[] { GameTargets.R5Flowstate }
            },
            new CatalogEntry("custom:loot-bin", L.T("#LOOT_BIN"), L.T("#CUSTOM"), new Vector3(2.1f, 1.25f, 1.05f)) { CustomType = "loot-bin" },
            new CatalogEntry("custom:jump-pad", L.T("#JUMP_PAD"), L.T("#CUSTOM"), new Vector3(1.2f, .3f, 1.2f)) { CustomType = "jump-pad" },
            new CatalogEntry("custom:spawn-point", L.T("#SPAWN_POINT"), L.T("#CUSTOM"), new Vector3(1.22f, 1.83f, .81f)) { CustomType = "spawn-point" },
            new CatalogEntry("custom:trigger", L.T("#TRIGGER"), L.T("#CUSTOM"), new Vector3(5.08f, 2.54f, 5.08f)) { CustomType = "trigger" },
            new CatalogEntry("custom:jump-tower", L.T("#JUMP_TOWER"), L.T("#CUSTOM"), new Vector3(3f, 50.8f, 3f)) { CustomType = "jump-tower" },
            new CatalogEntry("custom:weapon-rack", L.T("#WEAPON_RACK"), L.T("#CUSTOM"), new Vector3(.9f, .9f, .55f)) { CustomType = "weapon-rack" },
            new CatalogEntry("custom:respawn-heal", L.T("#RESPAWN_HEAL"), L.T("#CUSTOM"), new Vector3(.5f, .5f, .5f)) { CustomType = "respawn-heal" },
            new CatalogEntry("custom:button", L.T("#BUTTON"), L.T("#CUSTOM"), new Vector3(.8f, 1.2f, .8f)) { CustomType = "button" },
            new CatalogEntry("custom:speed-boost", L.T("#SPEED_BOOST"), L.T("#CUSTOM"), new Vector3(1.5f, 1.3f, 1.5f)) { CustomType = "speed-boost" },
            new CatalogEntry("custom:bubble-shield", L.T("#BUBBLE_SHIELD"), L.T("#CUSTOM"), new Vector3(5.2f, 5.2f, 5.2f)) { CustomType = "bubble-shield" },
            new CatalogEntry("custom:camera-path", L.T("#CAMERA_PATH"), L.T("#CUSTOM"), new Vector3(15f, 2f, 2f)) { CustomType = "camera-path" },
            new CatalogEntry("custom:animated-camera", L.T("#ANIMATED_CAMERA"), L.T("#CUSTOM"), new Vector3(1f, 1f, 1f)) { CustomType = "animated-camera" },
            new CatalogEntry("custom:sound", L.T("#SOUND"), L.T("#CUSTOM"), new Vector3(1f, 1f, 1f)) { CustomType = "sound" },
            new CatalogEntry("custom:new-location-pair", L.T("#NEW_LOCATION_PAIR"), L.T("#CUSTOM"), new Vector3(1f, 1f, 1f)) { CustomType = "location-pair" },
            new CatalogEntry("custom:text-info-panel", L.T("#TEXT_INFO_PANEL"), L.T("#CUSTOM"), new Vector3(2.4f, 1.2f, .1f)) { CustomType = "text-info-panel" },
            new CatalogEntry("custom:window-hint", L.T("#WINDOW_HINT"), L.T("#CUSTOM"), new Vector3(3.6576f, 3.2512f, .08f)) { CustomType = "window-hint" }
        };

        private bool CustomObjectAvailable(CatalogEntry entry)
        {
            if (entry == null || !entry.SupportsGame(snapshot?.gameTarget)) return false;
            if (entry.CustomType == "door")
                return ReMapModelAvailability.Door(assetLibrary?.Records, Targets);
            if (entry.CustomType == "loot-bin")
                return ReMapModelAvailability.HasModel(assetLibrary?.Records, Targets,
                    LootBinModelPath);
            if (entry.CustomType == "jump-pad")
                return ReMapModelAvailability.HasModel(assetLibrary?.Records, Targets,
                    JumpPadModelPath);
            if (entry.CustomType == "spawn-point")
                return ReMapModelAvailability.HasModel(assetLibrary?.Records, Targets,
                    SpawnPointModelPath);
            if (entry.CustomType == "jump-tower")
                return ReMapModelAvailability.HasModel(assetLibrary?.Records, Targets,
                    JumpTowerBaseModelPath) && ReMapModelAvailability.HasModel(assetLibrary?.Records, Targets,
                    JumpTowerBalloonModelPath);
            if (entry.CustomType == "weapon-rack")
                return ReMapModelAvailability.HasModel(assetLibrary?.Records, Targets,
                    WeaponRackModelPath);
            if (entry.CustomType == "respawn-heal") return RespawnHealAnyModelAvailable();
            if (entry.CustomType == "button") return ButtonModeAvailable("visible");
            if (entry.CustomType == "speed-boost") return SpeedBoostModelsAvailable();
            if (entry.CustomType == "bubble-shield")
                return ReMapModelAvailability.HasModel(assetLibrary?.Records, Targets, BubbleShieldModelPath);
            if (entry.CustomType == "animated-camera") return AnimatedCameraModelsAvailable();
            return true;
        }

        private static string CustomThumbnailSymbol(string type) =>
            type == "curved-zipline" || type == "ziprail" ? "●╮●╰●" :
            type == "door" ? "▥" :
            type == "trigger" ? "◌" :
            type == "camera-path" || type == "animated-camera" ? "◉—●" :
            type == "sound" ? "◖)))" :
            type == "location-pair" ? "◆ ⇄ ◆" :
            type == "text-info-panel" ? "▤" :
            type == "window-hint" ? "▣" : "●━━━━●";

        private GameAssetRecord CustomThumbnailRecord(CatalogEntry entry)
        {
            if (entry == null || assetLibrary?.Records == null) return null;
            GameAssetRecord first = null;
            foreach (string path in CustomThumbnailModelPaths(entry.CustomType))
            {
                var record = assetLibrary.Records.FirstOrDefault(candidate =>
                    GameAssetIndex.SameModelPath(candidate.modelPath, path) &&
                    candidate.Supports(Targets));
                if (record == null) continue;
                first = first ?? record;
                if (assetLibrary.CacheRoot != null && File.Exists(Path.Combine(
                    assetLibrary.ModelDirectory(record), "thumbnail.png"))) return record;
            }
            return first;
        }

        private Texture2D LoadCustomThumbnail(GameAssetRecord record)
        {
            if (record == null || assetLibrary?.CacheRoot == null) return null;
            string path = Path.Combine(assetLibrary.ModelDirectory(record), "thumbnail.png");
            if (!File.Exists(path)) return null;
            var texture = new Texture2D(2, 2);
            try
            {
                if (ImageConversion.LoadImage(texture, File.ReadAllBytes(path), true)) return texture;
            }
            catch (Exception exception)
            {
                Debug.LogWarning(L.T("#THUMBNAIL_CACHE") + exception.Message);
            }
            Destroy(texture);
            return null;
        }

        private void RefreshSelectedCustomThumbnail()
        {
            if (currentThumbnail != null || previewEntry == null ||
                !previewEntry.Id.StartsWith("custom:", StringComparison.Ordinal)) return;
            currentThumbnail = LoadCustomThumbnail(CustomThumbnailRecord(previewEntry));
            assetPreview.image = currentThumbnail;
        }

        private void RenderCustomCatalog()
        {
            ClearRenderedCatalog(); ConfigureFlowCatalog();
            string term = search.value ?? "";
            var entries = CustomCatalogEntries().Where(entry => entry.SupportsGame(snapshot.gameTarget) && (
                entry.Name.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0 ||
                entry.Category.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0)).ToArray();
            pageState.text = L.F("#ARG0_CUSTOM_OBJECTS", entries.Length);
            foreach (var entry in entries)
            {
                bool available = CustomObjectAvailable(entry);
                var card = Button("", () => { if (!suppressCardClick) SelectCustomAsset(entry); }, "game-card");
                if (available) RegisterDragSource(card, entry, null);
                card.SetEnabled(available);
                card.tooltip = available ? L.T("#DOUBLE_CLICK_PLACE_DRAG_SCENE") :
                    L.T("#CUSTOM_OBJECT_MODELS_UNAVAILABLE");
                var image = new Image { scaleMode = ScaleMode.ScaleToFit };
                image.AddToClassList("card-image");
                var thumbnailRecord = CustomThumbnailRecord(entry);
                if (thumbnailRecord != null) visibleAssets.Add(thumbnailRecord);
                var thumbnail = LoadCustomThumbnail(thumbnailRecord);
                if (thumbnail != null)
                {
                    pageThumbnails.Add(thumbnail); image.image = thumbnail;
                    if (ThumbnailMissingAlbedo(thumbnailRecord))
                        image.Add(Label("!", "card-warning"));
                }
                else image.Add(Label(thumbnailRecord == null
                    ? CustomThumbnailSymbol(entry.CustomType)
                    : ThumbnailStatus(thumbnailRecord), thumbnailRecord == null
                        ? "custom-card-symbol" : "card-placeholder"));
                card.Add(image);
                card.Add(Label(entry.Name, "card-name")); card.Add(Label(entry.Category, "card-category"));
                if (previewEntry?.Id == entry.Id) card.AddToClassList("selected");
                catalogList.Add(card);
            }
            if (entries.Length == 0) catalogList.Add(Label(L.T("#NO_CUSTOM_OBJECT_MATCHES_SEARCH"), "note"));
            PrioritizeVisibleThumbnails();
            RefreshSelectedCustomThumbnail();
            _ = PrepareZiplineModels();
            _ = PrepareZiprailModels();
            _ = PrepareDoorModels();
            _ = PrepareLootBinModel();
            _ = PrepareJumpPadModel();
            _ = PrepareSpawnPointModel();
            _ = PrepareJumpTowerModels();
            _ = PrepareWeaponRackModel();
            _ = PrepareRespawnHealModels();
            _ = PrepareButtonModels();
            _ = PrepareSpeedBoostModels();
            _ = PrepareBubbleShieldModel();
            _ = PrepareAnimatedCameraModels();
        }

        private void SelectCustomAsset(CatalogEntry entry)
        {
            if (!CustomObjectAvailable(entry)) return;
            CommitInspectorEdit(); CancelPlacement(); SetLibraryDetails(true);
            previewEntry = entry; lastPreviewRequest = null; retryPreviewButton.SetEnabled(false);
            if (currentThumbnail != null) Destroy(currentThumbnail);
            currentThumbnail = null; assetPreview.image = null;
            previewText.text = entry.Name + "\n" + L.T("#CATEGORY") + entry.Category + "\n\n" +
                L.T(entry.CustomType == "door" ? "#DOOR_CUSTOM_HELP" :
                    entry.CustomType == "curved-zipline" ? "#CURVED_ZIPLINE_CUSTOM_HELP" :
                    entry.CustomType == "ziprail" ? "#ZIPRAIL_CUSTOM_HELP" :
                    entry.CustomType == "loot-bin" ? "#LOOT_BIN_CUSTOM_HELP" :
                    entry.CustomType == "jump-pad" ? "#JUMP_PAD_CUSTOM_HELP" :
                    entry.CustomType == "spawn-point" ? "#SPAWN_POINT_CUSTOM_HELP" :
                    entry.CustomType == "trigger" ? "#TRIGGER_CUSTOM_HELP" :
                    entry.CustomType == "jump-tower" ? "#JUMP_TOWER_CUSTOM_HELP" :
                    entry.CustomType == "weapon-rack" ? "#WEAPON_RACK_CUSTOM_HELP" :
                    entry.CustomType == "respawn-heal" ? "#RESPAWN_HEAL_CUSTOM_HELP" :
                    entry.CustomType == "button" ? "#BUTTON_CUSTOM_HELP" :
                    entry.CustomType == "speed-boost" ? "#SPEED_BOOST_CUSTOM_HELP" :
                    entry.CustomType == "bubble-shield" ? "#BUBBLE_SHIELD_CUSTOM_HELP" :
                    entry.CustomType == "camera-path" ? "#CAMERA_PATH_CUSTOM_HELP" :
                    entry.CustomType == "animated-camera" ? "#ANIMATED_CAMERA_CUSTOM_HELP" :
                    entry.CustomType == "sound" ? "#SOUND_CUSTOM_HELP" :
                    entry.CustomType == "location-pair" ? "#LOCATION_PAIR_CUSTOM_HELP" :
                    entry.CustomType == "text-info-panel" ? "#TEXT_INFO_PANEL_CUSTOM_HELP" :
                    entry.CustomType == "window-hint" ? "#WINDOW_HINT_CUSTOM_HELP" :
                    "#CABLE_TWO_INDEPENDENTLY_MOVABLE_ENDPOINTS");
            RefreshSelectedCustomThumbnail();
            placeAssetButton.SetEnabled(true); RefreshCatalog();
            _ = PrepareZiplineModels();
            _ = PrepareZiprailModels();
            _ = PrepareDoorModels();
            _ = PrepareLootBinModel();
            _ = PrepareJumpPadModel();
            _ = PrepareSpawnPointModel();
            _ = PrepareJumpTowerModels();
            _ = PrepareWeaponRackModel();
            _ = PrepareRespawnHealModels();
            _ = PrepareButtonModels();
            _ = PrepareSpeedBoostModels();
            _ = PrepareBubbleShieldModel();
            _ = PrepareAnimatedCameraModels();
        }

        private void InsertCustomObject(CatalogEntry entry, Vector3 position, string parent = "")
        {
            if (!CustomObjectAvailable(entry))
                throw new InvalidOperationException(L.T("#CUSTOM_OBJECT_MODELS_UNAVAILABLE"));
            if (entry?.CustomType == "zipline") InsertZipline(position, parent);
            else if (entry?.CustomType == "door") InsertDoor(position, parent);
            else if (entry?.CustomType == "curved-zipline") InsertCurvedZipline(position, parent);
            else if (entry?.CustomType == "ziprail") InsertZiprail(position, parent);
            else if (entry?.CustomType == "loot-bin") InsertLootBin(position, parent);
            else if (entry?.CustomType == "jump-pad") InsertJumpPad(position, parent);
            else if (entry?.CustomType == "spawn-point") InsertSpawnPoint(position, parent);
            else if (entry?.CustomType == "trigger") InsertTrigger(position, parent);
            else if (entry?.CustomType == "jump-tower") InsertJumpTower(position, parent);
            else if (entry?.CustomType == "weapon-rack") InsertWeaponRack(position, parent);
            else if (entry?.CustomType == "respawn-heal") InsertRespawnHeal(position, parent);
            else if (entry?.CustomType == "button") InsertButton(position, parent);
            else if (entry?.CustomType == "speed-boost") InsertSpeedBoost(position, parent);
            else if (entry?.CustomType == "bubble-shield") InsertBubbleShield(position, parent);
            else if (entry?.CustomType == "camera-path") InsertCameraPath(position, parent);
            else if (entry?.CustomType == "animated-camera") InsertAnimatedCamera(position, parent);
            else if (entry?.CustomType == "sound") InsertSound(position, parent);
            else if (entry?.CustomType == "location-pair") InsertLocationPair(position, parent);
            else if (entry?.CustomType == "text-info-panel") InsertTextInfoPanel(position, parent);
            else if (entry?.CustomType == "window-hint") InsertWindowHint(position, parent);
        }
    }
}
