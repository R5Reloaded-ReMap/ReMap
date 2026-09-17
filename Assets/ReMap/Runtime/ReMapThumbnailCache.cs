using System;
using System.IO;
using System.Collections.Generic;
using ReMap.Standalone.Core;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UIElements;
namespace ReMap.Standalone
{
    public sealed partial class ReMapApp
    {
        private bool backgroundStopped, thumbnailLoopRunning, thumbnailPaused, indexRequested;
        private CancellationTokenSource thumbnailExport;
        private int thumbnailIdleRevision;
        private string visibleThumbnailPage;
        private int visibleThumbnailRevision;
        private void InterruptBackgroundFor(GameAssetRecord requested=null) {
            if(thumbnailExport!=null&&(requested==null||!extractingThumbnails.Contains(requested.Id)))thumbnailExport.Cancel();
        }
        private void PrioritizeVisibleThumbnails() {
            string page=string.Join("|",visibleAssets.Select(r=>r.Id));
            if(page==visibleThumbnailPage)return;
            visibleThumbnailPage=page;int revision=++visibleThumbnailRevision;
            root.schedule.Execute(()=> {
                if(revision!=visibleThumbnailRevision||this==null||backgroundStopped)return;
                var targetSet=new HashSet<string>(Targets,StringComparer.OrdinalIgnoreCase);
                var pending=visibleAssets.Where(r=>r.Supports(targetSet)&&!readyThumbnails.Contains(r.Id)&&!failedThumbnails.Contains(r.Id)).ToArray();
                if(pending.Length>0&&!pending.Any(r=>extractingThumbnails.Contains(r.Id)))InterruptBackgroundFor();
                if(!Environment.GetCommandLineArgs().Any(a=>a.StartsWith("-remap")))_=PrepareThumbnails();
            }).StartingIn(350);
        }
        private VisualElement loadingOverlay;
        private Label loadingMessage, thumbnailProgress;
        private ProgressBar loadingProgress;
        private Button loadingCancelButton;
        private Action loadingCancelAction;
        private VisualElement thumbnailStatusRow;
        private Button thumbnailPauseButton;
        private int thumbnailDone, thumbnailFailed, thumbnailTotal, thumbnailAvailable;
        private void BuildLoadingUI()
        {
            loadingOverlay = new VisualElement(); loadingOverlay.AddToClassList("loading-overlay"); root.Add(loadingOverlay);
            var card = new VisualElement(); card.AddToClassList("loading-card"); loadingOverlay.Add(card);
            card.Add(Label(L.T("#LOADING_ASSETS"), "section-title"));
            loadingMessage = Label(L.T("#PREPARING"), "note"); card.Add(loadingMessage);
            loadingProgress = new ProgressBar { lowValue = 0, highValue = 1, value = 0 }; card.Add(loadingProgress);
            loadingCancelButton = Button(L.T("#CANCEL"), () => loadingCancelAction?.Invoke()); card.Add(loadingCancelButton);
            loadingProgress.style.display = DisplayStyle.None; loadingCancelButton.style.display = DisplayStyle.None;
            loadingOverlay.style.display = DisplayStyle.None;
            var row = new VisualElement(); thumbnailStatusRow=row; row.AddToClassList("thumbnail-progress-row"); row.style.flexDirection = FlexDirection.Row;
            libraryFooter.Add(row);
            thumbnailProgress = Label(L.T("#THUMBNAILS_LOCAL_CACHE"), "library-state"); thumbnailProgress.style.flexGrow = 1; row.Add(thumbnailProgress);
            var pause = Button(L.T("#PAUSE_THUMBNAILS"), () => { thumbnailPaused = !thumbnailPaused; if(thumbnailPaused){InterruptBackgroundFor();_=assetLibrary.ReleasePreviewSessionAsync();} else _=PrepareThumbnails(); });
            thumbnailPauseButton=pause;pause.schedule.Execute(() => pause.text = thumbnailPaused ? L.T("#RESUME_THUMBNAILS") : L.T("#PAUSE_THUMBNAILS")).Every(500); row.Add(pause);
            row.style.display=DisplayStyle.None;
        }
        private void Loading(bool show, string message = null, float? progress = null, Action cancel = null)
        {
            if (loadingOverlay == null) return;
            loadingOverlay.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
            if (message != null) loadingMessage.text = message;
            loadingProgress.style.display = show && progress.HasValue ? DisplayStyle.Flex : DisplayStyle.None;
            loadingCancelButton.style.display = show && cancel != null ? DisplayStyle.Flex : DisplayStyle.None;
            if (progress.HasValue) loadingProgress.value = Mathf.Clamp01(progress.Value);
            loadingCancelAction = show ? cancel : null;
        }
        private readonly HashSet<string> readyThumbnails=new HashSet<string>(), failedThumbnails=new HashSet<string>(), extractingThumbnails=new HashSet<string>();
        private readonly HashSet<string> availableThumbnails=new HashSet<string>();
        private readonly List<GameAssetRecord> visibleAssets=new List<GameAssetRecord>();
        private string thumbnailStateRoot;
        private int thumbnailStateCount=-1;
        private void ReadThumbnailState() {
            if(thumbnailStateRoot==assetLibrary.CacheRoot&&thumbnailStateCount==assetLibrary.Records.Count)return;
            if(thumbnailStateRoot!=assetLibrary.CacheRoot)preparedPlacementEntries.Clear();
            thumbnailStateRoot=assetLibrary.CacheRoot;thumbnailStateCount=assetLibrary.Records.Count;readyThumbnails.Clear();availableThumbnails.Clear();failedThumbnails.Clear();thumbnailAlbedo.Clear();
            if(thumbnailStateRoot==null)return;
            foreach(var record in assetLibrary.Records) {
                string thumbnail=Path.Combine(assetLibrary.ModelDirectory(record),"thumbnail.png");
                if(File.Exists(thumbnail))
                {
                    availableThumbnails.Add(record.Id);
                    if(!NeedsThumbnailRefresh(record))readyThumbnails.Add(record.Id);
                }
                // Previous failures get one fresh attempt per launch; current failures stay out of the loop.
            }
        }
        private void UpdateThumbnailProgress(GameAssetRecord[] eligible,string detail=null)
        {
            thumbnailTotal=eligible.Length;
            thumbnailDone=eligible.Count(r=>readyThumbnails.Contains(r.Id));
            thumbnailFailed=eligible.Count(r=>failedThumbnails.Contains(r.Id));
            thumbnailAvailable=eligible.Count(r=>availableThumbnails.Contains(r.Id));
            string status=thumbnailAvailable>thumbnailDone?L.F("#THUMBNAILS_AVAILABLE_ARG0_UPDATED_ARG1",thumbnailAvailable,thumbnailDone,thumbnailTotal):L.F("#THUMBNAILS_ARG0_ARG1",thumbnailDone,thumbnailTotal);
            if(thumbnailFailed>0)status+=L.F("#ARG0_FAILED",thumbnailFailed);
            if(thumbnailPaused)status+=L.T("#PAUSED");
            if(!string.IsNullOrEmpty(detail))status+=detail;
            thumbnailProgress.text=status;
        }
        [Serializable] private sealed class ThumbnailInfo { public int missingAlbedo; public int rendererVersion; }
        private const int ThumbnailRendererVersion=3;
        private bool NeedsThumbnailRefresh(GameAssetRecord record)
        {
            try
            {
                string path=Path.Combine(assetLibrary.ModelDirectory(record),"thumbnail-info.json");
                if(!File.Exists(path))return true;
                var info=JsonUtility.FromJson<ThumbnailInfo>(File.ReadAllText(path));
                return info==null||info.rendererVersion<ThumbnailRendererVersion;
            }
            catch{return true;}
        }
        private readonly Dictionary<string,int> thumbnailAlbedo=new Dictionary<string,int>();
        private void SaveThumbnailInfo(GameAssetRecord record,int missing) {
            thumbnailAlbedo[record.Id]=missing;File.WriteAllText(Path.Combine(assetLibrary.ModelDirectory(record),"thumbnail-info.json"),JsonUtility.ToJson(new ThumbnailInfo{missingAlbedo=missing,rendererVersion=ThumbnailRendererVersion}));
        }
        private bool ThumbnailMissingAlbedo(GameAssetRecord record) {
            if(!thumbnailAlbedo.TryGetValue(record.Id,out var missing)) {
                string path=Path.Combine(assetLibrary.ModelDirectory(record),"thumbnail-info.json");
                try {missing=File.Exists(path)?JsonUtility.FromJson<ThumbnailInfo>(File.ReadAllText(path)).missingAlbedo:0;}catch {missing=0;}
                thumbnailAlbedo[record.Id]=missing;
            }
            return missing>0;
        }
        private string ThumbnailStatus(GameAssetRecord record) => extractingThumbnails.Contains(record.Id)?L.T("#EXTRACTING_B5C67A"):failedThumbnails.Contains(record.Id)||previewFailures.ContainsKey(record.Id)?L.T("#FAILED_RETRY"):L.T("#QUEUED");
        private sealed class ThumbnailExtraction {
            public GameAssetRecord[] batch;
            public CancellationTokenSource cancellation;
            public Task<AssetBatchResult> work;
            public IVisualElementScheduledItem progress;
        }
        private bool ThumbnailExternalWorkBlocked() => indexRequested||pendingAssetDrops>0||thumbnailPaused||SettingsOpen||IndexingOpen||inspectorDirty||libraryDragging||draggingGizmo||sceneSelectionPending||assemblyDragging;
        private bool ThumbnailWorkBlocked() => assetBusy||ThumbnailExternalWorkBlocked();
        private GameAssetRecord[] SceneThumbnailPriorities(GameAssetRecord[] eligible) {
            if(snapshot==null)return Array.Empty<GameAssetRecord>();
            var byId=eligible.ToDictionary(record=>record.Id,StringComparer.OrdinalIgnoreCase);
            var byPath=eligible.GroupBy(record=>GameAssetIndex.NormalizeModelPath(record.modelPath),StringComparer.OrdinalIgnoreCase).ToDictionary(group=>group.Key,group=>group.First(),StringComparer.OrdinalIgnoreCase);
            var found=new HashSet<string>(StringComparer.OrdinalIgnoreCase);var result=new List<GameAssetRecord>();
            foreach(var item in snapshot.objects) {
                if(item.isGroup)continue;
                GameAssetRecord record=null;
                if(!string.IsNullOrEmpty(item.assetId))byId.TryGetValue(item.assetId,out record);
                if(record==null&&!string.IsNullOrWhiteSpace(item.gameModelPath))byPath.TryGetValue(GameAssetIndex.NormalizeModelPath(item.gameModelPath),out record);
                if(record!=null&&!world.models.IsPrepared(record.Id)&&found.Add(record.Id))result.Add(record);
            }
            return result.ToArray();
        }
        private static IEnumerable<string> CustomThumbnailModelPaths(string type) {
            switch(type) {
                case "zipline":case "curved-zipline":return ReMapZiplineProfiles.RequiredModelPaths;
                case "ziprail":return new[]{"mdl/props/zip_rail/zip_rail_building_claw_01.rmdl","mdl/props/zip_rail/zip_rail_cord_end_01.rmdl","mdl/props/zip_rail/zip_rail_ground_base_01.rmdl","mdl/props/zip_rail/zip_rail_ground_post_01.rmdl","mdl/props/zip_rail/zip_rail_ground_post_top_01.rmdl"};
                case "door":return ReMapDoorProfiles.RequiredModelPaths;
                case "loot-bin":return new[]{LootBinModelPath};
                case "jump-pad":return new[]{JumpPadModelPath};
                case "spawn-point":return new[]{SpawnPointModelPath};
                case "jump-tower":return new[]{JumpTowerBaseModelPath,JumpTowerBalloonModelPath};
                case "weapon-rack":return new[]{WeaponRackModelPath};
                case "respawn-heal":return RespawnHealProfiles.Select(profile=>profile.ModelPath);
                case "button":return new[]{ButtonPanelModelPath,ButtonArrowModelPath};
                case "speed-boost":return new[]{SpeedBoostBaseModelPath,SpeedBoostOrbModelPath};
                case "bubble-shield":return new[]{BubbleShieldModelPath};
                case "animated-camera":return new[]{AnimatedCameraBaseModelPath,AnimatedCameraHeadModelPath};
                default:return Array.Empty<string>();
            }
        }
        private GameAssetRecord[] CustomThumbnailPriorities(GameAssetRecord[] eligible) {
            if(catalogMode==null||catalogMode.index!=2||snapshot==null)return Array.Empty<GameAssetRecord>();
            string term=search.value??"";
            var paths=new HashSet<string>(CustomCatalogEntries().Where(entry=>entry.SupportsGame(snapshot.gameTarget)&&(entry.Name.IndexOf(term,StringComparison.OrdinalIgnoreCase)>=0||entry.Category.IndexOf(term,StringComparison.OrdinalIgnoreCase)>=0)).SelectMany(entry=>CustomThumbnailModelPaths(entry.CustomType)).Select(GameAssetIndex.NormalizeModelPath),StringComparer.OrdinalIgnoreCase);
            return eligible.Where(record=>paths.Contains(GameAssetIndex.NormalizeModelPath(record.modelPath))).ToArray();
        }
        private GameAssetRecord[] NextThumbnailBatch(GameAssetRecord[] eligible,HashSet<string> targetSet,string[] targets,int batchSize) {
            var unavailable=new HashSet<string>(readyThumbnails,StringComparer.OrdinalIgnoreCase);unavailable.UnionWith(extractingThumbnails);
            var scene=SceneThumbnailPriorities(eligible);var custom=CustomThumbnailPriorities(eligible);
            var batch=ThumbnailQueue.Next(eligible,scene,visibleAssets,custom,unavailable,failedThumbnails,search.value,targets,batchSize,assetLibrary.PreferredPreviewArchive);
            // Render already exported visible models before waiting for any further archive decompression.
            bool scenePending=scene.Any(r=>!unavailable.Contains(r.Id)&&!failedThumbnails.Contains(r.Id));
            var cached=scenePending?Array.Empty<GameAssetRecord>():visibleAssets.Where(r=>r.Supports(targetSet)&&!unavailable.Contains(r.Id)&&!failedThumbnails.Contains(r.Id)&&assetLibrary.CachedModel(r)!=null).Take(batchSize).ToArray();
            return cached.Length>0?cached:batch;
        }
        private ThumbnailExtraction StartThumbnailExtraction(GameAssetRecord[] batch,string[] targets,GameAssetRecord[] eligible) {
            var extraction=new ThumbnailExtraction{batch=batch,cancellation=new CancellationTokenSource()};
            extractingThumbnails.UnionWith(batch.Select(r=>r.Id));RefreshCatalog();
            bool multiple=batch.Length>1;float started=Time.realtimeSinceStartup;
            UpdateThumbnailProgress(eligible,multiple?L.F("#EXTRACTING_ARG0_MODELS",batch.Length):L.F("#PREPARING_ARG0",batch[0].Name));
            thumbnailExport=extraction.cancellation;
            extraction.work=assetLibrary.ExtractBatchAsync(batch,targets,extraction.cancellation.Token);
            extraction.progress=thumbnailProgress.schedule.Execute(()=> {
                if(extraction.work.IsCompleted)return;
                UpdateThumbnailProgress(eligible,multiple?L.F("#ARG0_S_READING_ARG1_MODELS",(int)(Time.realtimeSinceStartup-started),batch.Length):L.F("#ARG0_S_ARG1",(int)(Time.realtimeSinceStartup-started),batch[0].Name));
            }).Every(1000);
            return extraction;
        }
        private async Task<AssetBatchResult> FinishThumbnailExtraction(ThumbnailExtraction extraction) {
            try{return await extraction.work;}
            finally {
                extraction.progress.Pause();
                if(ReferenceEquals(thumbnailExport,extraction.cancellation))thumbnailExport=null;
                extraction.cancellation.Dispose();
            }
        }
        private async Task PrepareThumbnails()
        {
            if(thumbnailLoopRunning||assetLibrary.CacheRoot==null)return;
            thumbnailIdleRevision++;
            thumbnailLoopRunning=true;string generation=assetLibrary.CacheRoot;ReadThumbnailState();
            ThumbnailExtraction prefetched=null;
            try {
                while(this!=null&&!backgroundStopped&&generation==assetLibrary.CacheRoot) {
                    string[] targets=Targets;var targetSet=new HashSet<string>(targets,StringComparer.OrdinalIgnoreCase);
                    var eligible=assetLibrary.Records.Where(r=>r.Supports(targetSet)).ToArray();
                    UpdateThumbnailProgress(eligible);
                    UpdateThumbnailControls();
                    if(thumbnailDone+thumbnailFailed>=thumbnailTotal&&prefetched==null)break;
                    if(ThumbnailWorkBlocked()){await Task.Delay(200);continue;}
                    bool continuous=assetLibrary.ContinuousPreviewsSupported;
                    int batchSize=assetLibrary.BatchPreviewsSupported?8:continuous?1:assetLibrary.UsesForkFeatures?8:1;
                    var current=prefetched;prefetched=null;
                    if(current==null) {
                        var nextBatch=NextThumbnailBatch(eligible,targetSet,targets,batchSize);
                        if(nextBatch.Length==0)break;
                        current=StartThumbnailExtraction(nextBatch,targets,eligible);
                    }
                    var batch=current.batch;SetAssetBusy(true);
                    try {
                        AssetBatchResult result=await FinishThumbnailExtraction(current);
                        if(this==null||backgroundStopped||generation!=assetLibrary.CacheRoot)return;
                        // Keep RSX busy with one bounded look-ahead batch while Unity renders this batch.
                        if(!ThumbnailExternalWorkBlocked()) {
                            var nextBatch=NextThumbnailBatch(eligible,targetSet,targets,batchSize);
                            if(nextBatch.Length>0)prefetched=StartThumbnailExtraction(nextBatch,targets,eligible);
                        }
                        var reloadScene=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        foreach(var next in batch) {
                            GameObject model=null;Texture2D thumbnail=null;
                            try {
                                if(!result.Paths.TryGetValue(next.Id,out var cast))throw new IOException(result.Errors.TryGetValue(next.Id,out var error)?error:L.T("#EXPORT_MISSING"));
                                await SharedTextureCache.Normalize(assetLibrary.ModelDirectory(next),assetLibrary.Settings.textureLimit);
                                if(this==null||backgroundStopped||generation!=assetLibrary.CacheRoot)return;
                                world.models.Prepare(next.Id,cast);model=world.models.Create(next.Id,false);RememberPlacementEntry(next,model);thumbnail=ModelThumbnail.Render(model);
                                if(snapshot.objects.Any(item=>!item.isGroup&&(string.Equals(item.assetId,next.Id,StringComparison.OrdinalIgnoreCase)||GameAssetIndex.SameModelPath(item.gameModelPath,next.modelPath))))reloadScene.Add(next.Id);
                                File.WriteAllBytes(Path.Combine(assetLibrary.ModelDirectory(next),"thumbnail.png"),thumbnail.EncodeToPNG());
                                availableThumbnails.Add(next.Id);
                                SaveThumbnailInfo(next,world.models.MissingAlbedo(next.Id));
                                readyThumbnails.Add(next.Id);failedThumbnails.Remove(next.Id);previewFailures.Remove(next.Id);
                            }catch(Exception ex){if(this==null||backgroundStopped||generation!=assetLibrary.CacheRoot)return;ThumbnailFailure(next,ex);}
                            finally {if(this!=null&&!backgroundStopped){if(model!=null)world.models.Release(next.Id,model);if(thumbnail!=null)Destroy(thumbnail);extractingThumbnails.Remove(next.Id);UpdateThumbnailProgress(eligible);UpdateThumbnailControls();RefreshCatalog();}}
                            await Task.Yield();
                        }
                        if(reloadScene.Count>0){foreach(string id in reloadScene)world.Reload(id);Refresh();}
                    }catch(OperationCanceledException) { Debug.Log("REMAP_THUMBNAIL_PREEMPTED"); /* Requeued without failure. */ }
                    catch(Exception ex){if(this!=null&&!backgroundStopped)foreach(var entry in batch)if(!readyThumbnails.Contains(entry.Id))ThumbnailFailure(entry,ex);}
                    finally {
                        if(this!=null&&!backgroundStopped){foreach(var entry in batch)extractingThumbnails.Remove(entry.Id);SetAssetBusy(false);RefreshCatalog();if(!indexRequested&&queuedPreview!=null){var next=queuedPreview;queuedPreview=null;_=PreviewGameAsset(next);}}
                    }
                    if(continuous)await Task.Yield();else await Task.Delay(150);
                }
            }finally{
                try{
                    if(prefetched!=null) {
                        prefetched.cancellation.Cancel();
                        try{await FinishThumbnailExtraction(prefetched);}catch(Exception ex){Debug.LogWarning("REMAP_THUMBNAIL_PREFETCH_STOP: "+ex.Message);}
                        foreach(var entry in prefetched.batch)extractingThumbnails.Remove(entry.Id);
                    }
                    if(backgroundStopped||thumbnailDone+thumbnailFailed>=thumbnailTotal)await assetLibrary.ReleasePreviewSessionAsync();
                }
                finally{thumbnailLoopRunning=false;UpdateThumbnailControls();if(!backgroundStopped&&thumbnailDone+thumbnailFailed<thumbnailTotal)_=ReleaseThumbnailSessionAfterIdle(generation);}
            }
        }
        private async Task ReleaseThumbnailSessionAfterIdle(string generation) {
            int revision=++thumbnailIdleRevision;
            await Task.Delay(15000);
            if(this==null||backgroundStopped||thumbnailLoopRunning||revision!=thumbnailIdleRevision||generation!=assetLibrary.CacheRoot)return;
            await assetLibrary.ReleasePreviewSessionAsync();
        }
        private void UpdateThumbnailControls() {
            if(thumbnailStatusRow==null)return;
            bool complete=thumbnailTotal<=0||thumbnailDone+thumbnailFailed>=thumbnailTotal;
            var targetSet=new HashSet<string>(Targets,StringComparer.OrdinalIgnoreCase);
            bool visiblePending=visibleAssets.Any(r=>r.Supports(targetSet)&&!readyThumbnails.Contains(r.Id)&&!failedThumbnails.Contains(r.Id));
            bool show=!complete&&(thumbnailLoopRunning||visiblePending)||thumbnailFailed>0;
            thumbnailStatusRow.style.display=show?DisplayStyle.Flex:DisplayStyle.None;
            if(libraryFooter!=null)libraryFooter.style.display=show?DisplayStyle.Flex:DisplayStyle.None;
            if(thumbnailPauseButton!=null)thumbnailPauseButton.style.display=!complete&&(thumbnailLoopRunning||visiblePending)?DisplayStyle.Flex:DisplayStyle.None;
        }
        private void ThumbnailFailure(GameAssetRecord entry,Exception ex) {
            string folder=assetLibrary.ModelDirectory(entry);Directory.CreateDirectory(folder);File.WriteAllText(Path.Combine(folder,"thumbnail.error.txt"),ex.Message);
            failedThumbnails.Add(entry.Id);previewFailures[entry.Id]=ex.Message;Debug.LogWarning(L.T("#THUMBNAIL")+entry.Name+" : "+ex.Message);
        }
    }
}
