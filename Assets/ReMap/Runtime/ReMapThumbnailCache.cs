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
        private CancellationTokenSource manualPreviewIdleRelease;
        private int? rsxSessionIdleRemainingSeconds;
        private const int ManualPreviewSessionIdleMilliseconds=60000;
        private string visibleThumbnailPage;
        private int visibleThumbnailRevision;
        private void InterruptBackgroundFor(GameAssetRecord requested=null,bool force=false) {
            if(thumbnailExport==null||(!force&&requested!=null&&extractingThumbnails.Contains(requested.Id)))return;
            thumbnailExport.Cancel();
            // Explicit foreground requests must not sit behind a 64-model automatic export.
            // Killing only the embedded background session makes its cancellation observable;
            // the foreground request immediately starts a fresh/reusable session afterward.
            assetLibrary?.CancelActivePreviewOperation();
        }
        private GameAssetRecord[] AutomaticThumbnailRecords(ISet<string> targets) {
            // Category exclusions only trim speculative background work. Models already used by
            // the open map remain required, otherwise reopening a project could leave real props
            // unavailable merely because their category is normally prepared on demand.
            IEnumerable<MapObject> objects=snapshot?.objects??Enumerable.Empty<MapObject>();
            var requiredIds=new HashSet<string>(objects.Where(item=>!item.isGroup&&
                !string.IsNullOrWhiteSpace(item.assetId)).Select(item=>item.assetId),StringComparer.OrdinalIgnoreCase);
            var requiredPaths=new HashSet<string>(objects.Where(item=>!item.isGroup&&
                !string.IsNullOrWhiteSpace(item.gameModelPath)).Select(item=>GameAssetIndex.NormalizeModelPath(item.gameModelPath)),
                StringComparer.OrdinalIgnoreCase);
            return assetLibrary.Records.Where(record=>record.Supports(targets)&&
                (assetLibrary.ShouldAutomaticallyPrepareThumbnail(record)||requiredIds.Contains(record.Id)||
                 requiredPaths.Contains(GameAssetIndex.NormalizeModelPath(record.modelPath)))).ToArray();
        }
        private void PrioritizeVisibleThumbnails() {
            string page=string.Join("|",visibleAssets.Select(r=>r.Id));
            if(page==visibleThumbnailPage)return;
            visibleThumbnailPage=page;int revision=++visibleThumbnailRevision;
            root.schedule.Execute(()=> {
                if(revision!=visibleThumbnailRevision||this==null||backgroundStopped)return;
                var targetSet=new HashSet<string>(Targets,StringComparer.OrdinalIgnoreCase);
                var pending=visibleAssets.Where(r=>r.Supports(targetSet)&&assetLibrary.ShouldAutomaticallyPrepareThumbnail(r)&&
                    !readyThumbnails.Contains(r.Id)&&!failedThumbnails.Contains(r.Id)).ToArray();
                if(pending.Length>0&&!pending.Any(r=>extractingThumbnails.Contains(r.Id)))InterruptBackgroundFor();
                if(pending.Length>0&&!Environment.GetCommandLineArgs().Any(a=>a.StartsWith("-remap")))
                    _=PrepareThumbnails();
            }).StartingIn(350);
        }
        private VisualElement loadingOverlay;
        private Label loadingMessage, thumbnailProgress;
        private ProgressBar loadingProgress;
        private Button loadingCancelButton;
        private Action loadingCancelAction;
        private VisualElement thumbnailStatusRow, thumbnailDashboard;
        private Label rsxSessionIdleStatus;
        private ProgressBar thumbnailDashboardProgress;
        private Label thumbnailDashboardState, thumbnailDashboardSource, thumbnailDashboardDetail,
            thumbnailDashboardOfficialExtraction, thumbnailDashboardTargetExtraction, thumbnailDashboardGenerated,
            thumbnailDashboardRepairs, thumbnailDashboardFailed, thumbnailDashboardEta,
            thumbnailDashboardQueuedTitle, thumbnailDashboardPerformance;
        private VisualElement thumbnailDashboardActive, thumbnailDashboardQueuedBody, thumbnailDashboardQueuedLeft,
            thumbnailDashboardQueuedRight;
        private Foldout thumbnailDashboardCategoryFilter;
        private VisualElement thumbnailDashboardCategoryChoices;
        private Button thumbnailPauseButton, thumbnailDashboardPauseButton;
        private int thumbnailDone, thumbnailFailed, thumbnailTotal, thumbnailAvailable;
        private bool thumbnailDashboardShownForRun, thumbnailRendering, thumbnailCheckingTextures;
        private float thumbnailPerformanceStamp, thumbnailActiveSeconds, thumbnailLastExtractionSeconds,
            thumbnailLastGenerationSeconds;
        private int thumbnailRunStartDone;
        private const int ThumbnailRenderBatchSize=8;
        private readonly HashSet<string> thumbnailOfficialAttempts=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> thumbnailTargetAttempts=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> thumbnailRepairCandidates=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> thumbnailRepairsCompleted=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private ThumbnailExtraction activeThumbnailExtraction;
        private int activeThumbnailRepairs;
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
            row.Add(Button(L.T("#THUMBNAIL_DETAILS"), () => ShowThumbnailDashboard(true)));
            thumbnailPauseButton=ImmediateThumbnailPauseButton();row.Add(thumbnailPauseButton);
            row.style.display=DisplayStyle.None;
            rsxSessionIdleStatus=Label("","library-state");
            rsxSessionIdleStatus.AddToClassList("rsx-session-status");
            rsxSessionIdleStatus.style.display=DisplayStyle.None;
            rsxSessionIdleStatus.schedule.Execute(RefreshRsxSessionStatus).Every(250);

            thumbnailDashboard=new VisualElement();thumbnailDashboard.AddToClassList("thumbnail-dashboard");root.Add(thumbnailDashboard);
            var dashboard=new VisualElement();dashboard.AddToClassList("thumbnail-dashboard-panel");thumbnailDashboard.Add(dashboard);
            var heading=new VisualElement();heading.AddToClassList("thumbnail-dashboard-heading");dashboard.Add(heading);
            var headingText=new VisualElement();headingText.style.flexGrow=1;heading.Add(headingText);
            headingText.Add(Label(L.T("#THUMBNAIL_WORK_TITLE"),"thumbnail-dashboard-title"));
            headingText.Add(Label(L.T("#THUMBNAIL_WORK_SUBTITLE"),"thumbnail-dashboard-subtitle"));
            heading.Add(Button(L.T("#MINIMIZE_TO_BACKGROUND"),()=>ShowThumbnailDashboard(false),"thumbnail-dashboard-minimize"));
            thumbnailDashboardState=Label(L.T("#THUMBNAIL_STAGE_PREPARING"),"thumbnail-dashboard-state");dashboard.Add(thumbnailDashboardState);
            thumbnailDashboardSource=Label("","thumbnail-dashboard-source");dashboard.Add(thumbnailDashboardSource);
            thumbnailDashboardDetail=Label("","thumbnail-dashboard-detail");dashboard.Add(thumbnailDashboardDetail);
            thumbnailDashboardProgress=new ProgressBar{lowValue=0,highValue=1,value=0};thumbnailDashboardProgress.AddToClassList("thumbnail-dashboard-progress");dashboard.Add(thumbnailDashboardProgress);
            var metrics=new VisualElement();metrics.AddToClassList("thumbnail-dashboard-metrics");dashboard.Add(metrics);
            metrics.Add(ThumbnailMetric(L.T("#THUMBNAIL_APEX_EXTRACTION"),out thumbnailDashboardOfficialExtraction));
            metrics.Add(ThumbnailMetric(L.T("#THUMBNAIL_TARGET_EXTRACTION"),out thumbnailDashboardTargetExtraction));
            metrics.Add(ThumbnailMetric(L.T("#THUMBNAIL_GENERATION_COUNTER"),out thumbnailDashboardGenerated));
            metrics.Add(ThumbnailMetric(L.T("#THUMBNAIL_TEXTURE_REPAIR_COUNTER"),out thumbnailDashboardRepairs));
            metrics.Add(ThumbnailMetric(L.T("#THUMBNAIL_FAILED"),out thumbnailDashboardFailed));
            metrics.Add(ThumbnailMetric(L.T("#THUMBNAIL_ESTIMATED_TIME"),out thumbnailDashboardEta));
            thumbnailDashboardCategoryFilter=new Foldout{value=false};
            thumbnailDashboardCategoryFilter.AddToClassList("thumbnail-dashboard-category-filter");dashboard.Add(thumbnailDashboardCategoryFilter);
            thumbnailDashboardCategoryFilter.Add(Label(L.T("#THUMBNAIL_CATEGORY_FILTER_HELP"),"note"));
            var categoryScroll=new ScrollView(ScrollViewMode.Vertical);categoryScroll.AddToClassList("thumbnail-dashboard-category-scroll");
            thumbnailDashboardCategoryFilter.Add(categoryScroll);
            thumbnailDashboardCategoryChoices=new VisualElement();thumbnailDashboardCategoryChoices.AddToClassList("thumbnail-category-choices");
            categoryScroll.Add(thumbnailDashboardCategoryChoices);
            thumbnailDashboardCategoryFilter.Add(ThumbnailCategoryActions());RefreshThumbnailCategoryChoices();
            var queues=new VisualElement();queues.AddToClassList("thumbnail-dashboard-queues");dashboard.Add(queues);
            queues.Add(ThumbnailQueuePanel(L.T("#THUMBNAIL_CURRENT_MODELS"),out thumbnailDashboardActive));
            queues.Add(ThumbnailQueueColumns(L.T("#THUMBNAIL_NEXT_MODELS"),out thumbnailDashboardQueuedTitle,
                out thumbnailDashboardQueuedLeft,out thumbnailDashboardQueuedRight,out thumbnailDashboardQueuedBody));
            thumbnailDashboardPerformance=Label("","thumbnail-dashboard-performance");dashboard.Add(thumbnailDashboardPerformance);
            thumbnailDashboardCategoryFilter.RegisterValueChangedCallback(change => {
                queues.style.display=change.newValue?DisplayStyle.None:DisplayStyle.Flex;
                thumbnailDashboardPerformance.style.display=change.newValue?DisplayStyle.None:DisplayStyle.Flex;
                thumbnailDashboardCategoryFilter.EnableInClassList("expanded",change.newValue);
            });
            var actions=new VisualElement();actions.AddToClassList("thumbnail-dashboard-actions");dashboard.Add(actions);
            thumbnailDashboardPauseButton=ImmediateThumbnailPauseButton();thumbnailDashboardPauseButton.AddToClassList("primary");actions.Add(thumbnailDashboardPauseButton);
            actions.Add(Button(L.T("#MINIMIZE_TO_BACKGROUND"),()=>ShowThumbnailDashboard(false)));
            thumbnailDashboard.style.display=DisplayStyle.None;
        }
        private VisualElement ThumbnailMetric(string title,out Label value) {
            var metric=new VisualElement();metric.AddToClassList("thumbnail-dashboard-metric");
            metric.Add(Label(title,"thumbnail-dashboard-metric-title"));value=Label("—","thumbnail-dashboard-metric-value");metric.Add(value);return metric;
        }
        private VisualElement ThumbnailQueuePanel(string title,out VisualElement content) {
            var panel=new VisualElement();panel.AddToClassList("thumbnail-dashboard-queue");panel.Add(Label(title,"thumbnail-dashboard-queue-title"));
            content=new VisualElement();content.AddToClassList("thumbnail-dashboard-queue-list");panel.Add(content);return panel;
        }
        private VisualElement ThumbnailQueueColumns(string title,out Label titleLabel,out VisualElement left,out VisualElement right,
            out VisualElement columns) {
            var panel=new VisualElement();panel.AddToClassList("thumbnail-dashboard-queue");
            titleLabel=Label(title,"thumbnail-dashboard-queue-title");panel.Add(titleLabel);
            columns=new VisualElement();columns.AddToClassList("thumbnail-dashboard-queue-columns");panel.Add(columns);
            left=new VisualElement();left.AddToClassList("thumbnail-dashboard-queue-column");columns.Add(left);
            right=new VisualElement();right.AddToClassList("thumbnail-dashboard-queue-column");right.AddToClassList("thumbnail-dashboard-queue-column-right");columns.Add(right);
            return panel;
        }
        private Button ImmediateThumbnailPauseButton() {
            var button=new Button{focusable=true,text=L.T(thumbnailPaused?"#RESUME_THUMBNAILS":"#PAUSE_THUMBNAILS")};
            button.RegisterCallback<PointerDownEvent>(evt=> {
                if(evt.button!=0)return;ToggleThumbnailPause();evt.StopImmediatePropagation();
            },TrickleDown.TrickleDown);
            button.RegisterCallback<NavigationSubmitEvent>(evt=> {ToggleThumbnailPause();evt.StopImmediatePropagation();});
            UpdateThumbnailPauseButtons();return button;
        }
        private void ToggleThumbnailPause() {
            SampleThumbnailPerformance();thumbnailPaused=!thumbnailPaused;UpdateThumbnailPauseButtons();
            var targets=new HashSet<string>(Targets,StringComparer.OrdinalIgnoreCase);
            UpdateThumbnailProgress(AutomaticThumbnailRecords(targets));
            // Do not cancel an RSX batch which is already in flight. The look-ahead batch owns
            // entries in extractingThumbnails; cancelling it here used to leave those entries
            // permanently displayed as "Loading" while the queue itself was empty.
            if(!thumbnailPaused)_=PrepareThumbnails();
            else if(thumbnailExport==null&&extractingThumbnails.Count==0)ScheduleManualPreviewSessionRelease(assetLibrary.CacheRoot);
        }
        private void UpdateThumbnailPauseButtons() {
            string text=L.T(thumbnailPaused?"#RESUME_THUMBNAILS":"#PAUSE_THUMBNAILS");
            if(thumbnailPauseButton!=null)thumbnailPauseButton.text=text;
            if(thumbnailDashboardPauseButton!=null)thumbnailDashboardPauseButton.text=text;
        }
        private void ShowThumbnailDashboard(bool show) {
            if(thumbnailDashboard==null)return;
            thumbnailDashboard.style.display=show?DisplayStyle.Flex:DisplayStyle.None;
            if(world?.Camera!=null)world.Camera.enabled=!show;
            if(show){world?.CancelNavigation();world?.ClearPreview();thumbnailDashboard.BringToFront();RefreshThumbnailCategoryChoices();UpdateThumbnailDashboard();}
        }
        private bool ThumbnailDashboardOpen=>thumbnailDashboard?.style.display.value==DisplayStyle.Flex;
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
        private sealed class StagedThumbnail
        {
            public GameAssetRecord record;
            public string cast;
            public SharedTextureCache.AlbedoInspection inspection;
            public bool repairAttempted;
        }
        private readonly Dictionary<string,StagedThumbnail> stagedThumbnails=
            new Dictionary<string,StagedThumbnail>(StringComparer.OrdinalIgnoreCase);
        private readonly List<GameAssetRecord> visibleAssets=new List<GameAssetRecord>();
        private string thumbnailStateRoot;
        private int thumbnailStateCount=-1;
        private void ReadThumbnailState() {
            if(thumbnailStateRoot==assetLibrary.CacheRoot&&thumbnailStateCount==assetLibrary.Records.Count)return;
            if(thumbnailStateRoot!=assetLibrary.CacheRoot)preparedPlacementEntries.Clear();
            thumbnailDashboardShownForRun=false;thumbnailPerformanceStamp=0;
            thumbnailActiveSeconds=0;thumbnailLastExtractionSeconds=0;thumbnailLastGenerationSeconds=0;thumbnailRunStartDone=0;
            if(thumbnailDashboard!=null){thumbnailDashboard.style.display=DisplayStyle.None;if(world?.Camera!=null)world.Camera.enabled=true;}
            thumbnailStateRoot=assetLibrary.CacheRoot;thumbnailStateCount=assetLibrary.Records.Count;readyThumbnails.Clear();availableThumbnails.Clear();failedThumbnails.Clear();stagedThumbnails.Clear();thumbnailAlbedo.Clear();
            thumbnailOfficialAttempts.Clear();thumbnailTargetAttempts.Clear();thumbnailRepairCandidates.Clear();thumbnailRepairsCompleted.Clear();
            activeThumbnailExtraction=null;activeThumbnailRepairs=0;
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
            UpdateThumbnailDashboard(eligible,detail);
        }
        private void SampleThumbnailPerformance() {
            float now=Time.realtimeSinceStartup;
            if(thumbnailPerformanceStamp>0&&!thumbnailPaused&&thumbnailLoopRunning&&(thumbnailRendering||extractingThumbnails.Count>0))
                thumbnailActiveSeconds+=Mathf.Max(0,now-thumbnailPerformanceStamp);
            thumbnailPerformanceStamp=now;
        }
        private void UpdateThumbnailDashboard(GameAssetRecord[] eligible=null,string detail=null) {
            if(thumbnailDashboardState==null||assetLibrary==null)return;
            if(eligible==null) {
                var targets=new HashSet<string>(Targets,StringComparer.OrdinalIgnoreCase);
                eligible=AutomaticThumbnailRecords(targets);
            }
            SampleThumbnailPerformance();
            int remaining=Mathf.Max(0,thumbnailTotal-thumbnailDone-thumbnailFailed);
            int completedThisRun=Mathf.Max(0,thumbnailDone-thumbnailRunStartDone);
            float perMinute=thumbnailActiveSeconds>0?completedThisRun*60f/thumbnailActiveSeconds:0;
            AssetExtractionActivity activity=assetLibrary.ExtractionActivity;
            bool extracting=extractingThumbnails.Count>0;
            string stage=thumbnailPaused?"#THUMBNAIL_STAGE_PAUSED":remaining==0?"#THUMBNAIL_STAGE_COMPLETE":
                thumbnailCheckingTextures?"#THUMBNAIL_STAGE_CHECKING_TEXTURES":
                activity.Operation==AssetExtractionOperation.RepairingTextures&&extracting?"#THUMBNAIL_STAGE_REPAIRING_TEXTURES":
                thumbnailRendering&&extracting?"#THUMBNAIL_STAGE_GENERATING_AND_EXTRACTING":
                thumbnailRendering?"#THUMBNAIL_STAGE_GENERATING":
                extracting&&activity.Operation==AssetExtractionOperation.LoadingArchives?"#THUMBNAIL_STAGE_LOADING_RPAKS":
                extracting?"#THUMBNAIL_STAGE_EXTRACTING":"#THUMBNAIL_STAGE_PREPARING";
            thumbnailDashboardState.text=L.T(stage);
            AssetExtractionSource source=activity.Source;
            if(source==AssetExtractionSource.None&&remaining>0)
                source=eligible.Any(record=>assetLibrary.ShouldTryOfficialPreview(record))?
                    AssetExtractionSource.OfficialApex:AssetExtractionSource.TargetGame;
            string sourceName=source==AssetExtractionSource.OfficialApex?L.T("#THUMBNAIL_SOURCE_APEX_OFFICIAL"):
                source==AssetExtractionSource.TargetGame?GameTargets.DisplayName(assetLibrary.TargetGame):"";
            thumbnailDashboardSource.text=string.IsNullOrEmpty(sourceName)?"":
                string.IsNullOrEmpty(activity.Archive)?L.F("#THUMBNAIL_SOURCE_ARG0",sourceName):
                L.F("#THUMBNAIL_SOURCE_ARG0_ARCHIVE_ARG1",sourceName,activity.Archive);
            thumbnailDashboardDetail.text=string.IsNullOrWhiteSpace(detail)?L.T("#THUMBNAIL_WORKING_IN_BACKGROUND"):detail.Trim().TrimStart('·').Trim();
            thumbnailDashboardProgress.value=thumbnailTotal<=0?0:(float)(thumbnailDone+thumbnailFailed)/thumbnailTotal;
            thumbnailDashboardProgress.title=thumbnailTotal<=0?"0 %":Mathf.RoundToInt(100f*(thumbnailDone+thumbnailFailed)/thumbnailTotal)+" %";
            bool exportInFlight=activeThumbnailExtraction!=null&&!activeThumbnailExtraction.work.IsCompleted;
            int activeOfficial=exportInFlight&&activity.Source==AssetExtractionSource.OfficialApex?
                activeThumbnailExtraction.batch.Length:0;
            int activeTarget=exportInFlight&&activity.Source==AssetExtractionSource.TargetGame?
                activeThumbnailExtraction.batch.Length:0;
            thumbnailDashboardOfficialExtraction.text=PhaseCounter(thumbnailOfficialAttempts.Count,activeOfficial);
            thumbnailDashboardTargetExtraction.text=PhaseCounter(thumbnailTargetAttempts.Count,activeTarget);
            thumbnailDashboardGenerated.text=thumbnailDone+" / "+thumbnailTotal;
            thumbnailDashboardRepairs.text=thumbnailRepairsCompleted.Count+" / "+thumbnailRepairCandidates.Count+
                (activeThumbnailRepairs>0?L.F("#THUMBNAIL_ACTIVE_SUFFIX_ARG0",activeThumbnailRepairs):"");
            thumbnailDashboardFailed.text=thumbnailFailed.ToString();
            thumbnailDashboardEta.text=remaining==0?"0 s":perMinute>0?FormatThumbnailDuration(remaining/perMinute*60f):L.T("#THUMBNAIL_ETA_UNKNOWN");
            int queueRows=ThumbnailQueueRows();
            var active=eligible.Where(r=>extractingThumbnails.Contains(r.Id)).Take(queueRows).ToArray();
            PopulateThumbnailQueueColumn(thumbnailDashboardActive,active,active.Length==0,L.T("#THUMBNAIL_NO_ACTIVE_MODEL"));
            var pending=eligible.Where(r=>!readyThumbnails.Contains(r.Id)&&!failedThumbnails.Contains(r.Id)&&!extractingThumbnails.Contains(r.Id)).ToArray();
            int visibleCount=Math.Min(pending.Length,queueRows*2),queueOffset=pending.Length>visibleCount?Mathf.FloorToInt(Time.realtimeSinceStartup)%pending.Length:0;
            var queued=Enumerable.Range(0,visibleCount).Select(i=>pending[(queueOffset+i)%pending.Length]).ToArray();
            // Balance a partial page between both columns. Filling the left column up to the
            // theoretical row limit first left a large visual hole in the right column.
            int queuedSplit=Mathf.CeilToInt(queued.Length/2f);
            thumbnailDashboardQueuedTitle.text=L.F("#THUMBNAIL_NEXT_MODELS_ARG0_ARG1",visibleCount,remaining);
            PopulateThumbnailQueueColumn(thumbnailDashboardQueuedLeft,queued.Take(queuedSplit),queued.Length==0);
            PopulateThumbnailQueueColumn(thumbnailDashboardQueuedRight,queued.Skip(queuedSplit),false);
            thumbnailDashboardPerformance.text=L.F("#THUMBNAIL_PERFORMANCE_ARG0_ARG1",thumbnailLastExtractionSeconds.ToString("0.0"),thumbnailLastGenerationSeconds.ToString("0.0"));
            UpdateThumbnailPauseButtons();
        }
        private string PhaseCounter(int completed,int active) => active>0?
            L.F("#THUMBNAIL_PHASE_COUNTER_ACTIVE_ARG0_ARG1",completed,active):completed.ToString();
        private void PopulateThumbnailQueueColumn(VisualElement column,IEnumerable<GameAssetRecord> records,bool empty,string emptyText=null) {
            if(column==null)return;
            column.Clear();
            if(empty) {
                var placeholder=Label(string.IsNullOrEmpty(emptyText)?L.T("#THUMBNAIL_QUEUE_EMPTY"):emptyText,"thumbnail-dashboard-queue-row");
                column.Add(placeholder);return;
            }
            foreach(var record in records) {
                var row=Label("• "+record.Name,"thumbnail-dashboard-queue-row");
                row.tooltip=record.modelPath;column.Add(row);
            }
        }
        private static string FormatThumbnailDuration(float seconds) {
            if(!float.IsFinite(seconds)||seconds<0)return "—";
            int total=Mathf.CeilToInt(seconds),minutes=total/60;
            return minutes>0?minutes+" min "+total%60+" s":total+" s";
        }
        private int ThumbnailQueueRows() {
            // Measure the actual queue body, not the active label whose height depends on how
            // many names it currently contains. The latter made the capacity fluctuate and
            // could reduce a large queue to only a handful of visible entries.
            float height=thumbnailDashboardQueuedBody==null?0:thumbnailDashboardQueuedBody.resolvedStyle.height;
            if(!float.IsFinite(height)||height<32)return 16;
            return Mathf.Clamp(Mathf.FloorToInt(height/16f),8,32);
        }
        [Serializable] private sealed class ThumbnailInfo { public int missingAlbedo; public int rendererVersion; }
        // Version 5 invalidates previews rendered from textures exported by the
        // older RSX decompression path. Those PNGs can decode successfully but
        // contain horizontal/static corruption, so existence alone is not a
        // sufficient cache hit.
        private const int ThumbnailRendererVersion=5;
        private bool NeedsThumbnailRefresh(GameAssetRecord record)
        {
            try
            {
                if(!SharedTextureCache.ManifestMatchesMaximum(assetLibrary.ModelDirectory(record),SharedTextureCache.PreviewMaximumSize))return true;
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
        private string ThumbnailStatus(GameAssetRecord record) => extractingThumbnails.Contains(record.Id)?L.T("#EXTRACTING_B5C67A"):
            failedThumbnails.Contains(record.Id)||previewFailures.ContainsKey(record.Id)?L.T("#FAILED_RETRY"):
            !assetLibrary.ShouldAutomaticallyPrepareThumbnail(record)?L.T("#THUMBNAIL_ON_DEMAND"):L.T("#QUEUED");
        private sealed class ThumbnailExtraction {
            public GameAssetRecord[] batch;
            public CancellationTokenSource cancellation;
            public Task<AssetBatchResult> work;
            public IVisualElementScheduledItem progress;
            public float started;
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
                case "ziprail":return ReMapZiprailProfiles.RequiredModelPaths;
                case "door":return ReMapDoorProfiles.RequiredModelPaths;
                case "loot-bin":return new[]{LootBinModelPath};
                case "jump-pad":return new[]{JumpPadModelPath};
                case "spawn-point":return new[]{SpawnPointModelPath};
                case "jump-tower":return new[]{JumpTowerBaseModelPath,JumpTowerBalloonModelPath};
                case "weapon-rack":return new[]{WeaponRackModelPath};
                case "respawn-heal":return RespawnHealProfiles.Select(profile=>profile.ModelPath);
                case "button":return ReMapButtonProfiles.RequiredModelPaths.Concat(new[]{ButtonArrowModelPath});
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
            unavailable.UnionWith(stagedThumbnails.Keys);
            bool officialPhase=eligible.Any(record=>!unavailable.Contains(record.Id)&&!failedThumbnails.Contains(record.Id)&&assetLibrary.ShouldTryOfficialPreview(record));
            var selectable=officialPhase?eligible.Where(assetLibrary.ShouldTryOfficialPreview).ToArray():eligible;
            var scene=SceneThumbnailPriorities(selectable);var custom=CustomThumbnailPriorities(selectable);
            var batch=ThumbnailQueue.Next(selectable,scene,visibleAssets,custom,unavailable,failedThumbnails,search.value,targets,batchSize,assetLibrary.PreferredPreviewArchive);
            // Render already exported visible models before waiting for any further archive decompression.
            bool scenePending=scene.Any(r=>!unavailable.Contains(r.Id)&&!failedThumbnails.Contains(r.Id));
            var cached=scenePending?Array.Empty<GameAssetRecord>():visibleAssets.Where(r=>r.Supports(targetSet)&&!unavailable.Contains(r.Id)&&!failedThumbnails.Contains(r.Id)&&assetLibrary.CachedModel(r)!=null).Take(batchSize).ToArray();
            return cached.Length>0?cached:batch;
        }
        private ThumbnailExtraction StartThumbnailExtraction(GameAssetRecord[] batch,string[] targets,GameAssetRecord[] eligible) {
            var extraction=new ThumbnailExtraction{batch=batch,cancellation=new CancellationTokenSource(),started=Time.realtimeSinceStartup};
            extractingThumbnails.UnionWith(batch.Select(r=>r.Id));RefreshCatalog();
            bool multiple=batch.Length>1;float started=Time.realtimeSinceStartup;
            UpdateThumbnailProgress(eligible,multiple?L.F("#EXTRACTING_ARG0_MODELS",batch.Length):L.F("#PREPARING_ARG0",batch[0].Name));
            thumbnailExport=extraction.cancellation;
            extraction.work=assetLibrary.ExtractBatchAsync(batch,targets,extraction.cancellation.Token);
            activeThumbnailExtraction=extraction;
            extraction.progress=thumbnailProgress.schedule.Execute(()=> {
                if(extraction.work.IsCompleted)return;
                UpdateThumbnailProgress(eligible,multiple?L.F("#ARG0_S_READING_ARG1_MODELS",(int)(Time.realtimeSinceStartup-started),batch.Length):L.F("#ARG0_S_ARG1",(int)(Time.realtimeSinceStartup-started),batch[0].Name));
            }).Every(1000);
            return extraction;
        }
        private async Task<AssetBatchResult> FinishThumbnailExtraction(ThumbnailExtraction extraction) {
            try{return await extraction.work;}
            finally {
                thumbnailLastExtractionSeconds=Mathf.Max(0,Time.realtimeSinceStartup-extraction.started);
                extraction.progress.Pause();
                if(ReferenceEquals(thumbnailExport,extraction.cancellation))thumbnailExport=null;
                if(ReferenceEquals(activeThumbnailExtraction,extraction))activeThumbnailExtraction=null;
                extraction.cancellation.Dispose();
            }
        }
        private List<StagedThumbnail[]> StagedRepairBatches(IEnumerable<StagedThumbnail> source,string[] targets) {
            var result=new List<StagedThumbnail[]>();
            foreach(var group in source.Where(work=>work.inspection?.NeedsFallback==true&&!work.repairAttempted)
                .GroupBy(work=>assetLibrary.OriginArchive(work.record,targets),StringComparer.OrdinalIgnoreCase)) {
                if(assetLibrary.BulkTextureRepairsSupported) {
                    // File names identify exports inside one RSX job. Put duplicate names in a
                    // separate bulk request, but otherwise send the whole corrupt set at once.
                    var bulk=new List<List<StagedThumbnail>>();
                    foreach(var work in group) {
                        int index=0;
                        while(index<bulk.Count&&bulk[index].Any(item=>string.Equals(
                            item.record.Name,work.record.Name,StringComparison.OrdinalIgnoreCase)))index++;
                        if(index==bulk.Count)bulk.Add(new List<StagedThumbnail>());
                        bulk[index].Add(work);
                    }
                    result.AddRange(bulk.Select(batch=>batch.ToArray()));
                    continue;
                }
                var batch=new List<StagedThumbnail>();
                foreach(var work in group) {
                    if(batch.Count>=8||batch.Any(item=>string.Equals(item.record.Name,work.record.Name,StringComparison.OrdinalIgnoreCase))) {
                        result.Add(batch.ToArray());batch.Clear();
                    }
                    batch.Add(work);
                }
                if(batch.Count>0)result.Add(batch.ToArray());
            }
            return result;
        }
        private void RenderStagedThumbnail(StagedThumbnail work,HashSet<string> reloadScene) {
            GameObject model=null;Texture2D thumbnail=null;
            try {
                world.models.Prepare(work.record.Id,work.cast);model=world.models.Create(work.record.Id,false);
                RememberPlacementEntry(work.record,model);thumbnail=ModelThumbnail.Render(model);
                if(snapshot.objects.Any(item=>!item.isGroup&&(string.Equals(item.assetId,work.record.Id,StringComparison.OrdinalIgnoreCase)||
                    GameAssetIndex.SameModelPath(item.gameModelPath,work.record.modelPath))))reloadScene.Add(work.record.Id);
                File.WriteAllBytes(Path.Combine(assetLibrary.ModelDirectory(work.record),"thumbnail.png"),thumbnail.EncodeToPNG());
                availableThumbnails.Add(work.record.Id);
                SaveThumbnailInfo(work.record,world.models.MissingAlbedo(work.record.Id));
                readyThumbnails.Add(work.record.Id);failedThumbnails.Remove(work.record.Id);previewFailures.Remove(work.record.Id);
            }
            finally {
                if(model!=null)world.models.Release(work.record.Id,model);if(thumbnail!=null)Destroy(thumbnail);
            }
        }
        private async Task FinishStagedThumbnails(GameAssetRecord[] eligible,string[] targets,string generation) {
            var eligibleIds=new HashSet<string>(eligible.Select(record=>record.Id),StringComparer.OrdinalIgnoreCase);
            foreach(string id in stagedThumbnails.Keys.Where(id=>!eligibleIds.Contains(id)).ToArray())stagedThumbnails.Remove(id);
            if(stagedThumbnails.Count==0)return;
            var reloadScene=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try {
                // Export every model first. Only then inspect the complete texture set, so the
                // primary export pipeline is never interrupted by per-model repair sessions.
                thumbnailCheckingTextures=true;UpdateThumbnailDashboard(eligible);
                int checkedTextures=0;
                foreach(var work in stagedThumbnails.Values.Where(work=>work.inspection==null).ToArray()) {
                    if(this==null||backgroundStopped||generation!=assetLibrary.CacheRoot||ThumbnailExternalWorkBlocked())return;
                    try {
                        work.inspection=SharedTextureCache.InspectAlbedos(work.cast);
                        if(work.inspection.NeedsFallback)thumbnailRepairCandidates.Add(work.record.Id);
                    }
                    catch(Exception ex){ThumbnailFailure(work.record,ex);stagedThumbnails.Remove(work.record.Id);}
                    checkedTextures++;
                    if(checkedTextures%8==0)UpdateThumbnailDashboard(eligible);
                    await Task.Yield();
                }
                thumbnailCheckingTextures=false;UpdateThumbnailDashboard(eligible);
                // Repairs reuse one official RSX session. Protocol v4 can repair the whole compatible
                // set in one command; older binaries retain their bounded eight-model fallback.
                foreach(var batch in StagedRepairBatches(stagedThumbnails.Values,targets)) {
                    if(this==null||backgroundStopped||generation!=assetLibrary.CacheRoot||ThumbnailExternalWorkBlocked())return;
                    foreach(var work in batch)extractingThumbnails.Add(work.record.Id);
                    activeThumbnailRepairs=batch.Length;
                    UpdateThumbnailProgress(eligible,L.F("#EXTRACTING_ARG0_MODELS",batch.Length));
                    float repairStarted=Time.realtimeSinceStartup;
                    var repairCancellation=new CancellationTokenSource();
                    thumbnailExport=repairCancellation;
                    var repairTask=assetLibrary.TryRepairOfficialTexturesBatchAsync(batch.Select(work=>
                        new OfficialTextureRepairRequest{Entry=work.record,LegacyCast=work.cast,Inspection=work.inspection}),
                        targets,repairCancellation.Token);
                    var repairProgress=thumbnailProgress.schedule.Execute(()=> {
                        if(!repairTask.IsCompleted)UpdateThumbnailProgress(eligible,
                            L.F("#ARG0_S_READING_ARG1_MODELS",(int)(Time.realtimeSinceStartup-repairStarted),batch.Length));
                    }).Every(1000);
                    bool repairWasCancelled=false;
                    try {
                        await repairTask;
                    }
                    catch(OperationCanceledException)when(repairCancellation.IsCancellationRequested) {
                        repairWasCancelled=true;
                    }
                    catch(Exception ex)when(ex is IOException||ex is TimeoutException||ex is InvalidDataException) {
                        Debug.LogWarning("REMAP_THUMBNAIL_REPAIR_BATCH: "+ex.Message);
                    }
                    finally {
                        repairWasCancelled|=repairCancellation.IsCancellationRequested;
                        repairProgress.Pause();
                        if(ReferenceEquals(thumbnailExport,repairCancellation))thumbnailExport=null;
                        repairCancellation.Dispose();
                        foreach(var work in batch) {
                            if(!repairWasCancelled){work.repairAttempted=true;thumbnailRepairsCompleted.Add(work.record.Id);}
                            extractingThumbnails.Remove(work.record.Id);
                        }
                        activeThumbnailRepairs=0;
                        if(this!=null&&!backgroundStopped)UpdateThumbnailProgress(eligible);
                    }
                    if(repairWasCancelled)return;
                }
                thumbnailRendering=true;
                var renderQueue=stagedThumbnails.Values.ToArray();
                for(int offset=0;offset<renderQueue.Length;offset+=ThumbnailRenderBatchSize) {
                    if(this==null||backgroundStopped||generation!=assetLibrary.CacheRoot||ThumbnailExternalWorkBlocked())return;
                    // Rendering uses Unity objects and therefore stays on the application thread.
                    // Yield after at most eight previews, and let Pause stop before the next UI chunk;
                    // an RSX export already in flight is deliberately not cancelled.
                    foreach(var work in renderQueue.Skip(offset).Take(ThumbnailRenderBatchSize)) {
                        if(this==null||backgroundStopped||generation!=assetLibrary.CacheRoot)return;
                        float generationStarted=Time.realtimeSinceStartup;
                        try {
                            RenderStagedThumbnail(work,reloadScene);
                        }
                        catch(Exception ex){ThumbnailFailure(work.record,ex);}
                        finally {
                            thumbnailLastGenerationSeconds=Mathf.Max(0,Time.realtimeSinceStartup-generationStarted);
                            stagedThumbnails.Remove(work.record.Id);UpdateThumbnailProgress(eligible);UpdateThumbnailControls();
                        }
                        await Task.Yield();
                    }
                }
                if(reloadScene.Count>0){foreach(string id in reloadScene)world.Reload(id);Refresh();}
            }
            finally {
                thumbnailCheckingTextures=false;thumbnailRendering=false;activeThumbnailRepairs=0;RefreshCatalog();UpdateThumbnailDashboard(eligible);
            }
        }
        private async Task PrepareThumbnails()
        {
            if(thumbnailLoopRunning||assetLibrary.CacheRoot==null||thumbnailPaused)return;
            ReadThumbnailState();
            var initialTargets=new HashSet<string>(Targets,StringComparer.OrdinalIgnoreCase);
            var initialEligible=AutomaticThumbnailRecords(initialTargets);
            UpdateThumbnailProgress(initialEligible);UpdateThumbnailControls();
            // Refreshing or scrolling the catalog can request preparation speculatively. A no-op
            // request must not cancel and restart the idle timer of an already loaded RSX session.
            if(thumbnailDone+thumbnailFailed>=thumbnailTotal)return;
            CancelManualPreviewSessionRelease();
            thumbnailLoopRunning=true;string generation=assetLibrary.CacheRoot;
            ThumbnailExtraction prefetched=null;
            try {
                while(this!=null&&!backgroundStopped&&generation==assetLibrary.CacheRoot) {
                    string[] targets=Targets;var targetSet=new HashSet<string>(targets,StringComparer.OrdinalIgnoreCase);
                    var eligible=AutomaticThumbnailRecords(targetSet);
                    if(thumbnailPerformanceStamp<=0)thumbnailRunStartDone=eligible.Count(r=>readyThumbnails.Contains(r.Id));
                    UpdateThumbnailProgress(eligible);
                    UpdateThumbnailControls();
                    if(!thumbnailDashboardShownForRun&&thumbnailDone+thumbnailFailed<thumbnailTotal&&!Environment.GetCommandLineArgs().AnySmokeFlag()) {
                        thumbnailDashboardShownForRun=true;ShowThumbnailDashboard(true);
                    }
                    if(thumbnailDone+thumbnailFailed>=thumbnailTotal&&prefetched==null)break;
                    if(thumbnailPaused&&prefetched!=null) {
                        // A pause requested while Unity rendered the preceding batch can race with
                        // the RSX look-ahead. Let that bounded extraction finish and release every
                        // reservation; its cached result will be reused when preparation resumes.
                        try{await FinishThumbnailExtraction(prefetched);}
                        catch(Exception ex)when(ex is IOException||ex is TimeoutException||ex is OperationCanceledException)
                        {Debug.LogWarning("REMAP_THUMBNAIL_PREFETCH_PAUSE: "+ex.Message);}
                        foreach(var entry in prefetched.batch)extractingThumbnails.Remove(entry.Id);
                        prefetched=null;UpdateThumbnailProgress(eligible);UpdateThumbnailControls();RefreshCatalog();
                        ScheduleManualPreviewSessionRelease(generation);
                        continue;
                    }
                    if(ThumbnailWorkBlocked()){await Task.Delay(200);continue;}
                    bool continuous=assetLibrary.ContinuousPreviewsSupported;
                    int batchSize=assetLibrary.BulkExportsSupported?64:assetLibrary.BatchPreviewsSupported?8:
                        continuous?1:assetLibrary.UsesForkFeatures?8:1;
                    var current=prefetched;prefetched=null;
                    if(current==null) {
                        var nextBatch=NextThumbnailBatch(eligible,targetSet,targets,batchSize);
                        if(nextBatch.Length==0) {
                            if(stagedThumbnails.Count>0) {await FinishStagedThumbnails(eligible,targets,generation);continue;}
                            break;
                        }
                        current=StartThumbnailExtraction(nextBatch,targets,eligible);
                    }
                    var batch=current.batch;
                    try {
                        AssetBatchResult result=await FinishThumbnailExtraction(current);
                        if(this==null||backgroundStopped||generation!=assetLibrary.CacheRoot)return;
                        thumbnailOfficialAttempts.UnionWith(result.OfficialAttempts);
                        thumbnailTargetAttempts.UnionWith(result.TargetAttempts);
                        UpdateThumbnailDashboard(eligible);
                        // Keep RSX busy with one bounded look-ahead batch while this batch is
                        // normalized. Texture inspection deliberately waits until every model has
                        // been exported, avoiding any target-game/Apex session switching here.
                        if(!ThumbnailExternalWorkBlocked()) {
                            var nextBatch=NextThumbnailBatch(eligible,targetSet,targets,batchSize);
                            if(nextBatch.Length>0)prefetched=StartThumbnailExtraction(nextBatch,targets,eligible);
                        }
                        for(int offset=0;offset<batch.Length;offset+=ThumbnailRenderBatchSize) {
                            if(thumbnailPaused)break;
                            foreach(var next in batch.Skip(offset).Take(ThumbnailRenderBatchSize)) {
                                float generationStarted=Time.realtimeSinceStartup;
                                try {
                                    if(result.Deferred.Contains(next.Id))continue;
                                    if(!result.Paths.TryGetValue(next.Id,out var cast))throw new IOException(result.Errors.TryGetValue(next.Id,out var error)?error:L.T("#EXPORT_MISSING"));
                                    await SharedTextureCache.Normalize(assetLibrary.ModelDirectory(next),SharedTextureCache.PreviewMaximumSize);
                                    if(this==null||backgroundStopped||generation!=assetLibrary.CacheRoot)return;
                                    stagedThumbnails[next.Id]=new StagedThumbnail{record=next,cast=cast};
                                }catch(Exception ex){if(this==null||backgroundStopped||generation!=assetLibrary.CacheRoot)return;ThumbnailFailure(next,ex);}
                                finally {thumbnailLastGenerationSeconds=Mathf.Max(0,Time.realtimeSinceStartup-generationStarted);if(this!=null&&!backgroundStopped){extractingThumbnails.Remove(next.Id);UpdateThumbnailProgress(eligible);UpdateThumbnailControls();}}
                                await Task.Yield();
                            }
                        }
                        UpdateThumbnailDashboard(eligible);
                    }catch(OperationCanceledException) { Debug.Log("REMAP_THUMBNAIL_PREEMPTED"); /* Requeued without failure. */ }
                    catch(Exception ex){if(this!=null&&!backgroundStopped)foreach(var entry in batch)if(!readyThumbnails.Contains(entry.Id))ThumbnailFailure(entry,ex);}
                    finally {
                        if(this!=null&&!backgroundStopped){foreach(var entry in batch)extractingThumbnails.Remove(entry.Id);RefreshCatalog();if(thumbnailPaused)ScheduleManualPreviewSessionRelease(generation);}
                    }
                    if(continuous)await Task.Yield();else await Task.Delay(150);
                }
            }finally{
                thumbnailRendering=false;
                try{
                    if(prefetched!=null) {
                        prefetched.cancellation.Cancel();
                        try{await FinishThumbnailExtraction(prefetched);}catch(Exception ex){Debug.LogWarning("REMAP_THUMBNAIL_PREFETCH_STOP: "+ex.Message);}
                        foreach(var entry in prefetched.batch)extractingThumbnails.Remove(entry.Id);
                    }
                    if(backgroundStopped)await assetLibrary.ReleasePreviewSessionAsync();
                    if(!backgroundStopped&&thumbnailDone+thumbnailFailed>=thumbnailTotal)
                    {
                        int removed=await Task.Run(()=>SharedTextureCache.CollectGarbage(assetLibrary.CacheRoot));
                        if(removed>0)Debug.Log("REMAP_TEXTURE_CACHE_CLEANED: "+removed);
                    }
                }
                finally{
                    thumbnailLoopRunning=false;UpdateThumbnailControls();
                    if(!backgroundStopped&&!thumbnailPaused&&thumbnailDone+thumbnailFailed>=thumbnailTotal)
                        ShowThumbnailDashboard(false);
                    if(!backgroundStopped)ScheduleManualPreviewSessionRelease(generation);
                }
            }
        }
        private void CancelManualPreviewSessionRelease() {
            var pending=manualPreviewIdleRelease;
            manualPreviewIdleRelease=null;
            UpdateRsxSessionIdleStatus(null);
            if(pending==null)return;
            try{pending.Cancel();}catch(ObjectDisposedException){}
        }
        private void ScheduleManualPreviewSessionRelease(string generation) {
            CancelManualPreviewSessionRelease();
            if(this==null||backgroundStopped||assetLibrary==null||assetLibrary.PreviewProcessId==0)return;
            var cancellation=new CancellationTokenSource();
            manualPreviewIdleRelease=cancellation;
            UpdateRsxSessionIdleStatus(ManualPreviewSessionIdleMilliseconds/1000);
            _=ReleaseManualPreviewSessionAfterIdle(generation,cancellation);
        }
        private async Task ReleaseManualPreviewSessionAfterIdle(string generation,CancellationTokenSource cancellation) {
            try {
                DateTime deadline=DateTime.UtcNow.AddMilliseconds(ManualPreviewSessionIdleMilliseconds);
                while(true) {
                    int remaining=Math.Max(0,(int)Math.Ceiling((deadline-DateTime.UtcNow).TotalSeconds));
                    UpdateRsxSessionIdleStatus(remaining);
                    if(remaining==0)break;
                    await Task.Delay(Math.Min(1000,remaining*1000),cancellation.Token);
                }
                // A cached preview or placement may briefly use Unity while RSX remains idle.
                // Do not reset the deadline; close the session as soon as that local work ends.
                while(this!=null&&!backgroundStopped&&(assetBusy||pendingAssetDrops>0))
                    await Task.Delay(50,cancellation.Token);
                if(this==null||backgroundStopped||(thumbnailLoopRunning&&!thumbnailPaused)||
                    extractingThumbnails.Count>0||thumbnailExport!=null||generation!=assetLibrary.CacheRoot)return;
                await assetLibrary.ReleasePreviewSessionAsync(cancellation.Token);
            }
            catch(OperationCanceledException)when(cancellation.IsCancellationRequested){}
            finally {
                if(ReferenceEquals(manualPreviewIdleRelease,cancellation)) {
                    manualPreviewIdleRelease=null;
                    UpdateRsxSessionIdleStatus(null);
                }
                cancellation.Dispose();
            }
        }
        private void UpdateRsxSessionIdleStatus(int? remainingSeconds) {
            rsxSessionIdleRemainingSeconds=remainingSeconds;RefreshRsxSessionStatus();
        }
        private void RefreshRsxSessionStatus() {
            if(rsxSessionIdleStatus==null)return;
            bool running=!backgroundStopped&&assetLibrary?.PreviewProcessId!=0;
            bool working=assetBusy||pendingAssetDrops>0||extractingThumbnails.Count>0||thumbnailExport!=null;
            // Real RSX work cancels the idle release. Cached previews do not, so keep displaying
            // their original countdown even while Unity prepares one of those cached models.
            bool countdown=running&&rsxSessionIdleRemainingSeconds.HasValue&&rsxSessionIdleRemainingSeconds.Value>0;
            AssetExtractionActivity activity=assetLibrary?.ExtractionActivity??default;
            bool loading=running&&working&&activity.Operation==AssetExtractionOperation.LoadingArchives;
            bool extracting=running&&working&&activity.Operation==AssetExtractionOperation.ExportingModels;
            rsxSessionIdleStatus.text=countdown?L.F("#RSX_SESSION_CLOSES_IN_ARG0",rsxSessionIdleRemainingSeconds.Value):
                loading?L.T("#RSX_LOADING_RPAKS"):extracting?L.F("#RSX_EXTRACTING_ASSETS_ARG0",activity.ModelCount):
                running?L.T("#RSX_SESSION_RUNNING"):"";
            rsxSessionIdleStatus.tooltip=countdown?L.T("#RSX_SESSION_IDLE_HELP"):
                loading?L.T("#RSX_LOADING_RPAKS_HELP"):running?L.T("#RSX_SESSION_RUNNING_HELP"):"";
            rsxSessionIdleStatus.style.display=running?DisplayStyle.Flex:DisplayStyle.None;
        }
        private void UpdateLibraryFooterVisibility() {
            if(libraryFooter==null)return;
            bool thumbnailVisible=thumbnailStatusRow?.style.display.value==DisplayStyle.Flex;
            libraryFooter.style.display=thumbnailVisible?DisplayStyle.Flex:DisplayStyle.None;
        }
        private void UpdateThumbnailControls() {
            if(thumbnailStatusRow==null)return;
            bool complete=thumbnailTotal<=0||thumbnailDone+thumbnailFailed>=thumbnailTotal;
            var targetSet=new HashSet<string>(Targets,StringComparer.OrdinalIgnoreCase);
            bool visiblePending=visibleAssets.Any(r=>r.Supports(targetSet)&&assetLibrary.ShouldAutomaticallyPrepareThumbnail(r)&&
                !readyThumbnails.Contains(r.Id)&&!failedThumbnails.Contains(r.Id));
            bool show=!complete&&(thumbnailLoopRunning||visiblePending)||thumbnailFailed>0;
            thumbnailStatusRow.style.display=show?DisplayStyle.Flex:DisplayStyle.None;
            UpdateLibraryFooterVisibility();
            if(thumbnailPauseButton!=null)thumbnailPauseButton.style.display=!complete&&(thumbnailLoopRunning||visiblePending)?DisplayStyle.Flex:DisplayStyle.None;
            if(thumbnailDashboardPauseButton!=null)thumbnailDashboardPauseButton.style.display=complete?DisplayStyle.None:DisplayStyle.Flex;
        }
        private void ThumbnailFailure(GameAssetRecord entry,Exception ex) {
            string folder=assetLibrary.ModelDirectory(entry);Directory.CreateDirectory(folder);File.WriteAllText(Path.Combine(folder,"thumbnail.error.txt"),ex.Message);
            failedThumbnails.Add(entry.Id);previewFailures[entry.Id]=ex.Message;Debug.LogWarning(L.T("#THUMBNAIL")+entry.Name+" : "+ex.Message);
        }
    }
}
