using System;
using System.Threading.Tasks;
using ReMap.Standalone.Core;
using UnityEngine;
namespace ReMap.Standalone
{
    public sealed partial class ReMapApp
    {
        private int placementRequestVersion, dragPreviewVersion;
        private bool dragPreviewFailed;

        private async Task RequestModelPlacement(GameAssetRecord record)
        {
            CancelPlacement(); viewport.Focus();
            int version = placementRequestVersion;
            try
            {
                var entry = await PrepareDropEntry(record);
                if (this == null || version != placementRequestVersion || entry == null || !record.Supports(Targets)) return;
                BeginPlacement(entry); viewport.Focus();
            }
            catch (Exception ex) { if (this != null && version == placementRequestVersion) SetStatus(ex.Message); }
        }

        private void StartDragPreview()
        {
            int version = ++dragPreviewVersion;
            dragPreviewFailed = false;
            if (dragObjectId == null && dragRecord != null)
            {
                dragEntry = ReadyPlacementEntry(dragRecord);
                if (dragEntry == null) {dragPreviewFailed=true;SetStatus(L.T("#IMPORT_MODEL_BEFORE_PLACING"));}
            }
        }

        private async Task PrepareDragPreview(GameAssetRecord record, int version)
        {
            try
            {
                var entry = await PrepareDropEntry(record);
                if (this != null && libraryDragging && version == dragPreviewVersion && dragRecord?.Id == record.Id)
                    dragEntry = entry;
            }
            catch (Exception ex)
            {
                if (this != null && libraryDragging && version == dragPreviewVersion)
                { dragPreviewFailed = true; SetStatus(ex.Message); }
            }
        }

        private void UpdateDragPreview(bool insideScene, Vector2 screenPosition)
        {
            if (!insideScene || dragObjectId != null || dragEntry == null || dragPreviewFailed)
            { world.ClearPreview(); return; }
            try
            {
                if (world.Placement(screenPosition, dragEntry, snap, out var point)) world.Preview(dragEntry, point);
                else world.ClearPreview();
            }
            catch (Exception ex) { world.ClearPreview(); dragPreviewFailed = true; SetStatus(ex.Message); }
        }
    }
}
