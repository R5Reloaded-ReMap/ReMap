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
        private sealed class CacheVerificationRepair
        {
            public GameAssetRecord record;
            public string cast;
            public bool reimport;
        }

        private static void RemoveCacheMarker(string folder,string name)
        {
            try {string path=Path.Combine(folder,name);if(File.Exists(path))File.Delete(path);}
            catch(IOException){}catch(UnauthorizedAccessException){}
        }

        private static bool ThumbnailPngIsReadable(string path)
        {
            Texture2D texture=null;
            try
            {
                if(!File.Exists(path))return false;
                texture=new Texture2D(2,2,TextureFormat.RGBA32,false);
                return ImageConversion.LoadImage(texture,File.ReadAllBytes(path),true)&&texture.width>0&&texture.height>0;
            }
            catch(Exception exception)when(exception is IOException||exception is UnauthorizedAccessException||exception is ArgumentException)
            {return false;}
            finally{if(texture!=null)UnityEngine.Object.Destroy(texture);}
        }

        private static string CachedCastForVerification(string folder)
        {
            try
            {
                string marker=Path.Combine(folder,"complete.txt");
                if(!File.Exists(marker))return null;
                string relative=File.ReadLines(marker).FirstOrDefault();
                if(string.IsNullOrWhiteSpace(relative)||!relative.EndsWith(".cast",StringComparison.OrdinalIgnoreCase))return null;
                string path=Path.GetFullPath(Path.Combine(folder,relative));
                return path.StartsWith(Path.GetFullPath(folder)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)&&
                    File.Exists(path)?path:null;
            }
            catch(Exception exception)when(exception is IOException||exception is UnauthorizedAccessException||
                exception is ArgumentException||exception is NotSupportedException){return null;}
        }

        private async Task VerifyAssetCache()
        {
            if(assetBusy||indexRequested||assetLibrary?.CacheRoot==null)return;
            bool wasPaused=thumbnailPaused;thumbnailPaused=true;UpdateThumbnailPauseButtons();
            bool usesRsx=false;
            using var cancellation=new CancellationTokenSource();
            Loading(true,L.F("#VERIFYING_ASSET_CACHE_ARG0_ARG1",0,0),0,cancellation.Cancel);
            try
            {
                InterruptBackgroundFor(force:true);
                while((assetBusy||extractingThumbnails.Count>0)&&!cancellation.IsCancellationRequested)
                    await Task.Delay(50,cancellation.Token);
                ReadThumbnailState();
                var records=assetLibrary.Records.Where(record=>Directory.Exists(assetLibrary.ModelDirectory(record)))
                    .GroupBy(record=>record.Id,StringComparer.OrdinalIgnoreCase).Select(group=>group.First()).ToArray();
                var verifiedTextures=new Dictionary<string,bool>(StringComparer.OrdinalIgnoreCase);
                var repairs=new List<CacheVerificationRepair>();
                int checkedModels=0,invalidModels=0,invalidTextures=0,invalidThumbnails=0;
                for(int index=0;index<records.Length;index++)
                {
                    cancellation.Token.ThrowIfCancellationRequested();
                    var record=records[index];string folder=assetLibrary.ModelDirectory(record);
                    Loading(true,L.F("#VERIFYING_ASSET_CACHE_ARG0_ARG1",index+1,records.Length),
                        records.Length==0?1:(index+1)/(float)records.Length,cancellation.Cancel);
                    bool badModel=false,badTextures=false,badThumbnail=false;
                    string cast=null;
                    try
                    {
                        cast=CachedCastForVerification(folder);
                        if(string.IsNullOrEmpty(cast))badModel=true;
                        else CastReader.Read(cast);
                    }
                    catch(Exception exception)when(exception is IOException||exception is UnauthorizedAccessException||
                        exception is ArgumentException||exception is InvalidDataException)
                    {badModel=true;}

                    if(!badModel)
                    {
                        string[] completion;
                        try{completion=File.ReadAllLines(Path.Combine(folder,"complete.txt"));}
                        catch{completion=Array.Empty<string>();}
                        bool geometry=completion.Length>2&&string.Equals(completion[2].Trim(),"geometry",StringComparison.OrdinalIgnoreCase);
                        if(!geometry)
                        {
                            badTextures=!SharedTextureCache.VerifyManifestTextures(folder,
                                SharedTextureCache.PreviewMaximumSize,verifiedTextures,true);
                            if(!badTextures)try{badTextures=SharedTextureCache.InspectAlbedos(cast).NeedsFallback;}
                            catch(Exception exception)when(exception is IOException||exception is UnauthorizedAccessException||
                                exception is ArgumentException||exception is InvalidDataException){badTextures=true;}
                        }
                    }

                    if(!badModel&&!badTextures)
                        cast=assetLibrary.CachedModel(record)??assetLibrary.TryAdoptExistingModel(record)??cast;

                    string thumbnail=Path.Combine(folder,"thumbnail.png");
                    badThumbnail=!ThumbnailPngIsReadable(thumbnail)||NeedsThumbnailRefresh(record);
                    if(badModel){invalidModels++;RemoveCacheMarker(folder,"complete.txt");}
                    if(badTextures)invalidTextures++;
                    if(badThumbnail)invalidThumbnails++;
                    if(badModel||badTextures||badThumbnail)
                    {
                        RemoveCacheMarker(folder,"thumbnail-info.json");
                        RemoveCacheMarker(folder,"thumbnail.error.txt");
                        readyThumbnails.Remove(record.Id);failedThumbnails.Remove(record.Id);previewFailures.Remove(record.Id);
                        repairs.Add(new CacheVerificationRepair{record=record,cast=cast,reimport=badModel||badTextures});
                    }
                    checkedModels++;
                    if(index%8==7)await Task.Yield();
                }
                usesRsx=repairs.Any(repair=>repair.reimport);
                if(usesRsx){CancelManualPreviewSessionRelease();SetAssetBusy(true);}
                var reloadScene=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                string[] extractionTargets=Targets;
                for(int index=0;index<repairs.Count;index++)
                {
                    cancellation.Token.ThrowIfCancellationRequested();
                    var repair=repairs[index];
                    Loading(true,L.F("#REPAIRING_ASSET_CACHE_ARG0_ARG1_ARG2",index+1,repairs.Count,repair.record.Name),
                        repairs.Count==0?1:(index+1)/(float)repairs.Count,cancellation.Cancel);
                    try
                    {
                        string cast=repair.cast;
                        if(repair.reimport)
                        {
                            // Keep repairs deliberately sequential. The embedded session retains the
                            // complete project RPak union, while a bad GUID cannot poison a large batch.
                            var result=await assetLibrary.ExtractBatchAsync(new[]{repair.record},extractionTargets,
                                cancellation.Token,true);
                            if(result.Deferred.Contains(repair.record.Id)&&!result.Paths.ContainsKey(repair.record.Id))
                            {
                                var fallback=await assetLibrary.ExtractBatchAsync(new[]{repair.record},extractionTargets,
                                    cancellation.Token,true);
                                MergeBatchResult(result,fallback);
                            }
                            if(!result.Paths.TryGetValue(repair.record.Id,out cast))
                                throw new IOException(result.Errors.TryGetValue(repair.record.Id,out string error)?error:L.T("#EXPORT_MISSING"));
                        }
                        if(string.IsNullOrEmpty(cast))cast=assetLibrary.CachedModel(repair.record);
                        if(string.IsNullOrEmpty(cast))throw new IOException(L.T("#EXPORT_MISSING"));
                        await SharedTextureCache.Normalize(assetLibrary.ModelDirectory(repair.record),
                            SharedTextureCache.PreviewMaximumSize,cancellation.Token);
                        var work=new StagedThumbnail{record=repair.record,cast=cast,
                            inspection=SharedTextureCache.InspectAlbedos(cast)};
                        if(work.inspection.NeedsFallback)
                        {
                            await assetLibrary.TryRepairOfficialTexturesAsync(repair.record,cast,work.inspection,
                                extractionTargets,cancellation.Token,true);
                            work.repairAttempted=true;
                        }
                        RenderStagedThumbnail(work,reloadScene);
                    }
                    catch(OperationCanceledException){throw;}
                    catch(Exception exception){ThumbnailFailure(repair.record,exception);}
                    await Task.Yield();
                }
                foreach(string id in reloadScene)world.Reload(id);
                if(usesRsx)SetAssetBusy(false);
                var targets=new HashSet<string>(Targets,StringComparer.OrdinalIgnoreCase);
                UpdateThumbnailProgress(AutomaticThumbnailRecords(targets));UpdateThumbnailControls();RefreshCatalog();
                SetStatus(L.F("#ASSET_CACHE_VERIFIED_ARG0_ARG1_ARG2_ARG3",checkedModels,invalidModels,
                    invalidTextures,invalidThumbnails));
            }
            catch(OperationCanceledException){SetStatus(L.T("#ASSET_CACHE_VERIFICATION_CANCELLED"));}
            catch(Exception exception){Debug.LogException(exception);SetStatus(exception.Message);}
            finally
            {
                SetAssetBusy(false);Loading(false);thumbnailPaused=wasPaused;UpdateThumbnailPauseButtons();
                if(usesRsx)ScheduleManualPreviewSessionRelease(assetLibrary.CacheRoot);
                if(!cancellation.IsCancellationRequested&&!thumbnailPaused&&!backgroundStopped)_=PrepareThumbnails();
            }
        }
    }
}
