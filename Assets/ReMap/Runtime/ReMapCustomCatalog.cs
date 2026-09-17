using System;
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
            new CatalogEntry("custom:curved-zipline", L.T("#CURVED_ZIPLINE"), L.T("#CUSTOM"), new Vector3(15.24f, 2.04f, 2.54f)) { CustomType = "curved-zipline" },
            new CatalogEntry("custom:loot-bin", L.T("#LOOT_BIN"), L.T("#CUSTOM"), new Vector3(2.1f, 1.25f, 1.05f)) { CustomType = "loot-bin" },
            new CatalogEntry("custom:jump-pad", L.T("#JUMP_PAD"), L.T("#CUSTOM"), new Vector3(1.2f, .3f, 1.2f)) { CustomType = "jump-pad" },
            new CatalogEntry("custom:spawn-point", L.T("#SPAWN_POINT"), L.T("#CUSTOM"), new Vector3(1.22f, 1.83f, .81f)) { CustomType = "spawn-point" },
            new CatalogEntry("custom:trigger", L.T("#TRIGGER"), L.T("#CUSTOM"), new Vector3(5.08f, 2.54f, 5.08f)) { CustomType = "trigger" }
        };

        private bool CustomObjectAvailable(CatalogEntry entry)
        {
            if (entry == null) return false;
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
            return true;
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
                var image = new VisualElement(); image.AddToClassList("card-image");
                image.Add(Label(entry.CustomType == "curved-zipline" ? "●╮●╰●" : "●━━━━●", "custom-card-symbol")); card.Add(image);
                card.Add(Label(entry.Name, "card-name")); card.Add(Label(entry.Category, "card-category"));
                if (previewEntry?.Id == entry.Id) card.AddToClassList("selected");
                catalogList.Add(card);
            }
            if (entries.Length == 0) catalogList.Add(Label(L.T("#NO_CUSTOM_OBJECT_MATCHES_SEARCH"), "note"));
            _ = PrepareZiplineModels();
            _ = PrepareDoorModels();
            _ = PrepareLootBinModel();
            _ = PrepareJumpPadModel();
            _ = PrepareSpawnPointModel();
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
                    entry.CustomType == "loot-bin" ? "#LOOT_BIN_CUSTOM_HELP" :
                    entry.CustomType == "jump-pad" ? "#JUMP_PAD_CUSTOM_HELP" :
                    entry.CustomType == "spawn-point" ? "#SPAWN_POINT_CUSTOM_HELP" :
                    entry.CustomType == "trigger" ? "#TRIGGER_CUSTOM_HELP" :
                    "#CABLE_TWO_INDEPENDENTLY_MOVABLE_ENDPOINTS");
            placeAssetButton.SetEnabled(true); RefreshCatalog();
            _ = PrepareZiplineModels();
            _ = PrepareDoorModels();
            _ = PrepareLootBinModel();
            _ = PrepareJumpPadModel();
            _ = PrepareSpawnPointModel();
        }

        private void InsertCustomObject(CatalogEntry entry, Vector3 position, string parent = "")
        {
            if (!CustomObjectAvailable(entry))
                throw new InvalidOperationException(L.T("#CUSTOM_OBJECT_MODELS_UNAVAILABLE"));
            if (entry?.CustomType == "zipline") InsertZipline(position, parent);
            else if (entry?.CustomType == "door") InsertDoor(position, parent);
            else if (entry?.CustomType == "curved-zipline") InsertCurvedZipline(position, parent);
            else if (entry?.CustomType == "loot-bin") InsertLootBin(position, parent);
            else if (entry?.CustomType == "jump-pad") InsertJumpPad(position, parent);
            else if (entry?.CustomType == "spawn-point") InsertSpawnPoint(position, parent);
            else if (entry?.CustomType == "trigger") InsertTrigger(position, parent);
        }
    }
}
