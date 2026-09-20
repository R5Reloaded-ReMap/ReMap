using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ReMap.Standalone.Core;
using UnityEngine;

namespace ReMap.Standalone
{
    public sealed partial class ReMapApp
    {
        private IEnumerable<GameAssetRecord[]> LibraryImportBatches(IEnumerable<GameAssetRecord> records,string[] targets)
        {
            int maximum=assetLibrary.BulkExportsSupported?64:8;
            foreach(var archiveGroup in records.GroupBy(record=>assetLibrary.OriginArchive(record,targets),StringComparer.OrdinalIgnoreCase))
            {
                var batches=new List<List<GameAssetRecord>>();
                foreach(var record in archiveGroup)
                {
                    int index=0;
                    while(index<batches.Count&&(batches[index].Count>=maximum||batches[index].Any(item=>
                        item.Name.Equals(record.Name,StringComparison.OrdinalIgnoreCase))))index++;
                    if(index==batches.Count)batches.Add(new List<GameAssetRecord>());
                    batches[index].Add(record);
                }
                foreach(var batch in batches)yield return batch.ToArray();
            }
        }

        private static void MergeBatchResult(AssetBatchResult destination,AssetBatchResult source)
        {
            foreach(var pair in source.Paths)destination.Paths[pair.Key]=pair.Value;
            foreach(var pair in source.Errors)destination.Errors[pair.Key]=pair.Value;
            destination.OfficialAttempts.UnionWith(source.OfficialAttempts);
            destination.TargetAttempts.UnionWith(source.TargetAttempts);
            destination.Deferred.UnionWith(source.Deferred);
        }

        private async Task<AssetBatchResult> ExtractLibraryBatch(GameAssetRecord[] batch,string[] targets,bool forceRefresh,CancellationToken cancellation)
        {
            var result=await assetLibrary.ExtractBatchAsync(batch,targets,cancellation,forceRefresh);
            var deferred=batch.Where(record=>result.Deferred.Contains(record.Id)&&!result.Paths.ContainsKey(record.Id)).ToArray();
            if(deferred.Length>0)
            {
                var fallback=await assetLibrary.ExtractBatchAsync(deferred,targets,cancellation,forceRefresh);
                MergeBatchResult(result,fallback);
            }
            return result;
        }

        private async Task ImportSelectedLibraryAssets()
        {
            if(assetBusy||indexRequested||catalogMode==null||catalogMode.index!=0)return;
            var selected=SelectedLibraryRecords();if(selected.Length==0)return;
            string[] targets=Targets;
            bool forceRefresh=selected.All(record=>assetLibrary.CachedModel(record)!=null||assetLibrary.HasExistingModelExport(record));
            var extract=forceRefresh?selected:selected.Where(record=>assetLibrary.CachedModel(record)==null).ToArray();
            string generation=assetLibrary.CacheRoot;
            CancelManualPreviewSessionRelease();InterruptBackgroundFor(force:true);SetAssetBusy(true);CancelPlacement();
            var paths=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
            var failures=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                foreach(var record in selected)
                {
                    string cached=forceRefresh?null:assetLibrary.CachedModel(record);
                    if(cached!=null)paths[record.Id]=cached;
                }
                int completed=paths.Count;
                foreach(var batch in LibraryImportBatches(extract,targets))
                {
                    if(this==null||backgroundStopped||generation!=assetLibrary.CacheRoot)return;
                    SetStatus(L.F("#IMPORTING_SELECTED_MODELS_ARG0_ARG1",completed,selected.Length));
                    var result=await ExtractLibraryBatch(batch,targets,forceRefresh,CancellationToken.None);
                    foreach(var pair in result.Paths)paths[pair.Key]=pair.Value;
                    foreach(var pair in result.Errors)if(!paths.ContainsKey(pair.Key))failures[pair.Key]=pair.Value;
                    completed=paths.Count;
                }

                var staged=new List<StagedThumbnail>();
                foreach(var record in selected)
                {
                    if(!paths.TryGetValue(record.Id,out string cast))
                    {
                        string error=failures.TryGetValue(record.Id,out string value)?value:L.T("#EXPORT_MISSING");
                        ThumbnailFailure(record,new IOException(error));continue;
                    }
                    try
                    {
                        await SharedTextureCache.Normalize(assetLibrary.ModelDirectory(record),SharedTextureCache.PreviewMaximumSize);
                        var work=new StagedThumbnail{record=record,cast=cast,inspection=SharedTextureCache.InspectAlbedos(cast)};
                        staged.Add(work);
                    }
                    catch(Exception ex){ThumbnailFailure(record,ex);}
                }

                foreach(var batch in StagedRepairBatches(staged,targets))
                {
                    await assetLibrary.TryRepairOfficialTexturesBatchAsync(batch.Select(work=>
                        new OfficialTextureRepairRequest{Entry=work.record,LegacyCast=work.cast,Inspection=work.inspection}),
                        targets,CancellationToken.None,forceRefresh);
                    foreach(var work in batch)work.repairAttempted=true;
                }

                var reloadScene=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach(var work in staged)
                {
                    try{RenderStagedThumbnail(work,reloadScene);}
                    catch(Exception ex){ThumbnailFailure(work.record,ex);}
                    await Task.Yield();
                }
                foreach(string id in reloadScene)world.Reload(id);
                Refresh();RefreshCatalog();
                int imported=selected.Count(record=>ReadyPlacementEntry(record)!=null);
                SetStatus(L.F("#IMPORTED_SELECTED_MODELS_ARG0_ARG1",imported,selected.Length));
            }
            catch(OperationCanceledException)
            {
                if(this!=null&&!backgroundStopped)SetStatus(L.T("#IMPORT_CANCELED"));
            }
            catch(Exception ex)
            {
                if(this!=null){SetStatus(ex.Message);Debug.LogWarning(ex);}
            }
            finally
            {
                if(this!=null)
                {
                    SetAssetBusy(false);ScheduleManualPreviewSessionRelease(generation);UpdateLibraryAssetActions();
                    var active=lastPreviewRequest;
                    if(active!=null&&selectedLibraryAssetIds.Contains(active.Id)&&assetLibrary.CachedModel(active)!=null)
                        _=PreviewGameAsset(active);
                }
            }
        }
    }
}
