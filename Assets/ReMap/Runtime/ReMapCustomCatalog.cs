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
            new CatalogEntry("custom:door", L.T("#DOOR"), L.T("#CUSTOM"), new Vector3(1.83f, 2.44f, .15f)) { CustomType = "door" }
        };

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
                var card = Button("", () => { if (!suppressCardClick) SelectCustomAsset(entry); }, "game-card");
                RegisterDragSource(card, entry, null);
                card.tooltip = L.T("#DOUBLE_CLICK_PLACE_DRAG_SCENE");
                var image = new VisualElement(); image.AddToClassList("card-image");
                image.Add(Label("●━━━━●", "custom-card-symbol")); card.Add(image);
                card.Add(Label(entry.Name, "card-name")); card.Add(Label(entry.Category, "card-category"));
                if (previewEntry?.Id == entry.Id) card.AddToClassList("selected");
                catalogList.Add(card);
            }
            if (entries.Length == 0) catalogList.Add(Label(L.T("#NO_CUSTOM_OBJECT_MATCHES_SEARCH"), "note"));
            _ = PrepareZiplineModels();
            _ = PrepareDoorModels();
        }

        private void SelectCustomAsset(CatalogEntry entry)
        {
            CommitInspectorEdit(); CancelPlacement(); SetLibraryDetails(true);
            previewEntry = entry; lastPreviewRequest = null; retryPreviewButton.SetEnabled(false);
            if (currentThumbnail != null) Destroy(currentThumbnail);
            currentThumbnail = null; assetPreview.image = null;
            previewText.text = entry.Name + "\n" + L.T("#CATEGORY") + entry.Category + "\n\n" +
                L.T(entry.CustomType == "door" ? "#DOOR_CUSTOM_HELP" :
                    "#CABLE_TWO_INDEPENDENTLY_MOVABLE_ENDPOINTS");
            placeAssetButton.SetEnabled(true); RefreshCatalog();
            _ = PrepareZiplineModels();
            _ = PrepareDoorModels();
        }

        private void InsertCustomObject(CatalogEntry entry, Vector3 position, string parent = "")
        {
            if (entry?.CustomType == "zipline") InsertZipline(position, parent);
            else if (entry?.CustomType == "door") InsertDoor(position, parent);
        }
    }
}
