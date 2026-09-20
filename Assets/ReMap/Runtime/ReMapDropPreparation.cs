using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ReMap.Standalone.Core;
using UnityEngine;
namespace ReMap.Standalone {
    public sealed partial class ReMapApp {
        private readonly Dictionary<string,CatalogEntry> preparedPlacementEntries=new Dictionary<string,CatalogEntry>();
        private int pendingAssetDrops;
        private CatalogEntry RememberPlacementEntry(GameAssetRecord record,GameObject model) {
            var entry=new CatalogEntry(record.Id,record.Name,record.IsCommon?"Common":L.T("#MAP"),Vector3.one){GameAsset=record,PlacementLift=-model.GetComponent<MeshFilter>().sharedMesh.bounds.min.y};
            preparedPlacementEntries[record.Id]=entry;return entry;
        }
        private CatalogEntry ReadyPlacementEntry(GameAssetRecord record) {
            if(!world.models.IsPrepared(record.Id))return null;
            if(previewEntry?.Id==record.Id)return previewEntry;
            return preparedPlacementEntries.TryGetValue(record.Id,out var entry)?entry:null;
        }
        private readonly Dictionary<string,Task<CatalogEntry>> dropPreparations=new Dictionary<string,Task<CatalogEntry>>();
        private async Task<CatalogEntry> PrepareDropEntry(GameAssetRecord record) {
            if(!record.Supports(Targets))throw new InvalidOperationException(L.T("#MODEL_MISSING_LOADED_ARCHIVES"));
            var ready=ReadyPlacementEntry(record);if(ready!=null)return ready;
            if(dropPreparations.TryGetValue(record.Id,out var pending))return await pending;
            var operation=PrepareDropEntryCore(record);dropPreparations[record.Id]=operation;
            try{return await operation;}finally{dropPreparations.Remove(record.Id);}
        }
        private async Task<CatalogEntry> PrepareDropEntryCore(GameAssetRecord record) {
            if(!record.Supports(Targets))throw new InvalidOperationException(L.T("#MODEL_MISSING_LOADED_ARCHIVES"));
            var ready=ReadyPlacementEntry(record);if(ready!=null)return ready;
            CancelManualPreviewSessionRelease();
            string generation=assetLibrary.CacheRoot;pendingAssetDrops++;
            InterruptBackgroundFor(record,assetLibrary.CachedModel(record)==null);
            bool ownsBusy=false;
            try {
                // A cached model can be prepared while an unrelated RSX batch is still running.
                while(assetBusy&&(assetLibrary.CachedModel(record)==null||extractingThumbnails.Contains(record.Id))) {
                    SetStatus(L.T("#PREPARING_DROP")+record.Name+L.T("#WAITING_CURRENT_EXTRACTION"));
                    await Task.Delay(50);if(this==null)return null;
                    if(generation!=assetLibrary.CacheRoot)throw new InvalidOperationException(L.T("#SOURCES_CHANGED_DROP_CANCELED"));
                    ready=ReadyPlacementEntry(record);if(ready!=null)return ready;
                }
                if(!assetBusy){SetAssetBusy(true);ownsBusy=true;}
                string path=assetLibrary.CachedModel(record);
                if(path==null){SetStatus(L.T("#EXTRACTING_DROP")+record.Name);path=await assetLibrary.ExtractAsync(record,Targets);}
                if(this==null)return null;
                if(generation!=assetLibrary.CacheRoot||!record.Supports(Targets))throw new InvalidOperationException(L.T("#ARCHIVES_CHANGED_DROP_CANCELED"));
                await SharedTextureCache.Normalize(assetLibrary.ModelDirectory(record),SharedTextureCache.PreviewMaximumSize);
                if(this==null)return null;
                if(generation!=assetLibrary.CacheRoot)throw new InvalidOperationException(L.T("#SOURCES_CHANGED_DROP_CANCELED"));
                world.models.Prepare(record.Id,path);var model=world.models.Create(record.Id,false);
                try{return RememberPlacementEntry(record,model);}finally{world.models.Release(record.Id,model);}
            }finally{pendingAssetDrops--;if(this!=null){if(ownsBusy){SetAssetBusy(false);RunQueuedPreview();}if(pendingAssetDrops==0)ScheduleManualPreviewSessionRelease(generation);}}
        }
    }
}
