using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ReMap.Standalone.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace ReMap.Standalone
{
    public sealed partial class ReMapApp
    {
        private RsxAssetLibrary assetLibrary;
        private readonly Dictionary<string, Toggle> mapToggles = new Dictionary<string, Toggle>();
        private readonly List<Texture2D> pageThumbnails = new List<Texture2D>();
        private VisualElement mapChoices, assetDetail, assetDetailSplitter, libraryContent, libraryFooter;
        private DropdownField catalogMode;
        private Label pageState, previewText;
        private Image assetPreview;
        private Texture2D currentThumbnail;
        private Button indexButton, placeAssetButton;
        private Button libraryDetailsButton;
        private bool assetBusy;
        private const float CatalogCardWidth = 98, CatalogCardStride = 102, CatalogRowHeight = 112;
        private GameAssetRecord[] catalogRecords = Array.Empty<GameAssetRecord>();
        private readonly HashSet<string> selectedLibraryAssetIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private string librarySelectionAnchorId;
        private int renderedCatalogFirst = -1, renderedCatalogLast = -1, renderedCatalogColumns = -1;
        private string catalogQuery;
        private bool catalogRendering;
        private CatalogEntry previewEntry;
        private Button retryPreviewButton;
        private GameAssetRecord lastPreviewRequest, queuedPreview;
        private bool queuedPreviewForceRefresh;
        private int previewRequestVersion;
        private readonly Dictionary<string, string> previewFailures = new Dictionary<string, string>();
        private void BuildAssetLibrary(VisualElement library)
        {
            Debug.Log("REMAP_STARTUP_ASSET_LIBRARY_BEGIN");
            assetLibrary = new RsxAssetLibrary(RsxAssetLibrary.FindLocalRoot(), RsxAssetLibrary.FindSettingsRoot());
            Debug.Log("REMAP_STARTUP_ASSET_LIBRARY_READY");
            var controls = new VisualElement(); controls.AddToClassList("library-controls"); library.Add(controls);
            var bar = controls; bar.style.flexDirection = FlexDirection.Row;
            bar.Add(Button(L.T("#LOADED_ARCHIVES"), () => ShowIndexing(true)));
            indexButton = Button(L.T("#INDEX"), () => { _ = IndexAssets(); }); bar.Add(indexButton);
            catalogMode = new DropdownField(new List<string> { L.T("#GAME_MODELS"), L.T("#ASSEMBLIES_228174"), L.T("#CUSTOM") }, 0);
            catalogMode.RegisterValueChangedCallback(_ => ResetCatalog()); bar.Add(catalogMode);
            search = new TextField { value = "", tooltip = L.T("#SEARCH_MODELS_NAME_PATH") }; search.AddToClassList("search");
            search.RegisterValueChangedCallback(_ => ResetCatalog()); bar.Add(search); BuildAssemblyControls(bar);
            libraryDetailsButton = Button("", () => SetLibraryDetails(!layout.libraryDetails));libraryDetailsButton.name="library-details-button";bar.Add(libraryDetailsButton);
            var content = new VisualElement(); libraryContent=content; content.AddToClassList("library-content"); library.Add(content);
            catalogList = new ScrollView(ScrollViewMode.Vertical); catalogList.AddToClassList("catalog");
            content.Add(catalogList);
            catalogList.verticalScroller.valueChanged += _ => RenderVisibleCatalog();
            catalogList.RegisterCallback<GeometryChangedEvent>(_ => RenderVisibleCatalog());
            assetDetailSplitter = ResizeHandle("library-details", true); assetDetailSplitter.AddToClassList("library-detail-splitter"); content.Add(assetDetailSplitter);
            var preview = new VisualElement(); assetDetail=preview; preview.AddToClassList("asset-detail"); content.Add(preview);
            assetPreview = new Image { scaleMode = ScaleMode.ScaleToFit }; assetPreview.AddToClassList("asset-preview"); preview.Add(assetPreview);
            var textScroll = new ScrollView(); textScroll.AddToClassList("preview-message"); preview.Add(textScroll);
            previewText = Label(L.T("#CLICK_MODEL_LOAD_PREVIEW"), "library-state"); textScroll.Add(previewText);
            var actions = new VisualElement(); actions.style.flexDirection = FlexDirection.Row; preview.Add(actions);
            placeAssetButton = Button(L.T("#PLACE"), PlaceSelectedLibraryAsset, "primary");
            placeAssetButton.SetEnabled(false); actions.Add(placeAssetButton);
            retryPreviewButton = Button(L.T("#IMPORT_MODEL"), () => { _ = ImportSelectedLibraryAssets(); }); retryPreviewButton.SetEnabled(false); actions.Add(retryPreviewButton);
            pageState = Label("", "library-count");
            libraryFooter=new VisualElement();libraryFooter.AddToClassList("library-footer");libraryFooter.style.display=DisplayStyle.None;library.Add(libraryFooter);
        }
        private void FillMapChoices()
        {
            FillEditingMapChoice();
            string missingMessage = MissingMapSourcesMessage();
            if (missingMapNotice != null)
            {
                missingMapNotice.text = missingMessage;
                missingMapNotice.style.display = string.IsNullOrEmpty(missingMessage) ? DisplayStyle.None : DisplayStyle.Flex;
            }
            mapChoices.Clear(); mapToggles.Clear();
            foreach (var map in assetLibrary.Maps)
            {
                string id = map.Id;
                var toggle = new Toggle(map.Name.Split('·')[0].Trim()) { value = snapshot?.targetMaps.Contains(id) == true, tooltip = id };
                toggle.RegisterValueChangedCallback(e => {
                    CancelPlacement();
                    session.Edit(doc => { if (e.newValue) { if (!doc.targetMaps.Contains(id)) doc.targetMaps.Add(id); } else { doc.targetMaps.Remove(id); if (doc.editingMap == id) doc.editingMap = ""; } });
                    assetLibrary.LastTargetMaps = session.Snapshot().targetMaps.ToArray(); assetLibrary.SaveSettings();
                    Refresh();
                    SetStatus(L.T("#ARCHIVES_CHANGED_APPLY_INDEXING"));
                });
                mapChoices.Add(toggle); mapToggles.Add(id, toggle);
            }
        }
        private string[] Targets => assetLibrary.ResolveMapTargets(snapshot?.targetMaps);
        private string MissingMapSourcesMessage()
        {
            string[] missing = RsxAssetLibrary.FindMissingTargets(snapshot?.targetMaps, assetLibrary.Maps.Select(map => map.Id));
            if (missing.Length == 0) return "";
            string target = GameTargets.DisplayName(snapshot?.gameTarget ?? assetLibrary.TargetGame);
            return L.F("#SOME_MAP_SOURCES_SAVED_SCENE", target, string.Join(", ", missing));
        }
        private async Task IndexAssets(CancellationToken cancellation = default, Action cancel = null)
        {
            if (indexRequested) return;
            indexRequested=true;InterruptBackgroundFor();
            try
            {
                while(assetBusy||pendingAssetDrops>0)
                {
                    await Task.Delay(50, cancellation);
                    if(this==null||backgroundStopped) { indexRequested=false; return; }
                }
            }
            catch
            {
                indexRequested=false;
                throw;
            }
            indexRequested=false;
            cancellation.ThrowIfCancellationRequested();
            SetAssetBusy(true); Loading(true, L.T("#READING_ARCHIVE_INDEXES"), cancel: cancel);
            try
            {
                await assetLibrary.IndexAsync(Targets, new Progress<string>(message => {
                    if (this != null) { pageState.text = message; Loading(true, message, cancel: cancel); SetStatus(message); }
                }), cancellation);
                if (this == null) return;
                RestoreCachedModels(); ResetCatalog(); RefreshThumbnailCategoryChoices();
                string missingMessage = MissingMapSourcesMessage();
                SetStatus(string.IsNullOrEmpty(missingMessage) ? L.T("#INDEX_READY_PREPARING_CACHED_THUMBNAILS") : missingMessage);
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { throw; }
            catch (Exception ex) { if (this != null) { pageState.text = L.T("#INDEX_INCOMPLETE_UNAVAILABLE"); SetStatus(ex.Message); Debug.LogWarning(ex); } }
            finally { if (this != null) { Loading(false); SetAssetBusy(false); if (!cancellation.IsCancellationRequested && !Environment.GetCommandLineArgs().Any(a => a.StartsWith("-remap"))) _ = PrepareThumbnails(); RunQueuedPreview(); } }
        }
        private void SetAssetBusy(bool value)
        {
            assetBusy = value; indexButton?.SetEnabled(!indexRequested); UpdateLibraryAssetActions();
        }
        private void RefreshAssetCatalog()
        {
            if (catalogList == null || catalogMode == null) return;
            string[] targets = Targets;
            string query = catalogMode.index + "|" + search.value + "|" + string.Join(";", targets);
            if (query != catalogQuery) {
                catalogQuery = query; catalogList.scrollOffset = Vector2.zero;
                selectedLibraryAssetIds.Clear(); librarySelectionAnchorId = null;
            }
            ApplyLibraryDetailLayout();
            if(libraryDetailsButton!=null)libraryDetailsButton.text=layout.libraryDetails?L.T("#HIDE_DETAILS"):L.T("#DETAILS");
            ReadThumbnailState();
            if (catalogMode.index == 1) { catalogRecords=Array.Empty<GameAssetRecord>(); ClearRenderedCatalog(); ConfigureFlowCatalog(); RefreshAssemblies(); return; }
            if (catalogMode.index == 2) { catalogRecords=Array.Empty<GameAssetRecord>(); RenderCustomCatalog(); return; }
            string term = search.value ?? "";
            var targetSet = new HashSet<string>(targets, StringComparer.OrdinalIgnoreCase);
            catalogRecords = assetLibrary.Records.Where(r => r.Supports(targetSet) &&
                GameAssetIndex.ShowInCatalog(r,term,assetLibrary.ShouldAutomaticallyPrepareThumbnail(r),
                    availableThumbnails.Contains(r.Id))).ToArray();
            var visibleIds = new HashSet<string>(catalogRecords.Select(record => record.Id), StringComparer.OrdinalIgnoreCase);
            selectedLibraryAssetIds.RemoveWhere(id => !visibleIds.Contains(id));
            if (librarySelectionAnchorId != null && !visibleIds.Contains(librarySelectionAnchorId)) librarySelectionAnchorId = null;
            UpdateLibraryModelCount();
            UpdateLibraryAssetActions();
            renderedCatalogFirst=renderedCatalogLast=renderedCatalogColumns=-1;
            RenderVisibleCatalog(true);
            if (catalogRecords.Length == 0) catalogList.Add(Label(L.T("#INDEX_SOURCES_CHANGE_FILTERS"), "note"));
        }

        private void ClearRenderedCatalog()
        {
            visibleAssets.Clear(); catalogList.Clear();
            foreach (var thumbnail in pageThumbnails) Destroy(thumbnail);
            pageThumbnails.Clear();
        }

        private void ConfigureFlowCatalog()
        {
            catalogList.contentContainer.style.height=StyleKeyword.Auto;
            catalogList.contentContainer.style.minHeight=StyleKeyword.Auto;
            catalogList.contentContainer.style.flexDirection=FlexDirection.Row;
            catalogList.contentContainer.style.flexWrap=Wrap.Wrap;
        }

        private void RenderVisibleCatalog(bool force=false)
        {
            if(catalogRendering||catalogMode==null||catalogMode.index!=0||catalogRecords==null)return;
            float width=catalogList.contentViewport.resolvedStyle.width;
            float height=catalogList.contentViewport.resolvedStyle.height;
            if(width<=1||height<=1)return;
            int columns=Math.Max(1,Mathf.FloorToInt((width-2)/CatalogCardStride));
            int rows=Mathf.CeilToInt(catalogRecords.Length/(float)columns);
            float offset=Mathf.Max(0,catalogList.scrollOffset.y);
            int firstRow=Math.Max(0,Mathf.FloorToInt(offset/CatalogRowHeight)-1);
            int lastRow=Math.Min(rows,Mathf.CeilToInt((offset+height)/CatalogRowHeight)+1);
            int first=firstRow*columns,last=Math.Min(catalogRecords.Length,lastRow*columns);
            if(!force&&first==renderedCatalogFirst&&last==renderedCatalogLast&&columns==renderedCatalogColumns)return;
            catalogRendering=true;
            try
            {
                ClearRenderedCatalog();
                catalogList.contentContainer.style.position=Position.Relative;
                catalogList.contentContainer.style.flexWrap=Wrap.NoWrap;
                catalogList.contentContainer.style.height=Math.Max(height,rows*CatalogRowHeight);
                catalogList.contentContainer.style.minHeight=Math.Max(height,rows*CatalogRowHeight);
                renderedCatalogFirst=first;renderedCatalogLast=last;renderedCatalogColumns=columns;
                foreach (int index in Enumerable.Range(first,Math.Max(0,last-first)))
                {
                    var record=catalogRecords[index]; visibleAssets.Add(record);
                    int row=index/columns,column=index%columns;
                var card = Button("", () => { }, "game-card");
                RegisterDragSource(card, null, record);
                card.RegisterCallback<PointerDownEvent>(e => {
                    if(e.button!=0||e.clickCount!=1||suppressCardClick)return;
                    SelectGameAsset(record,e.ctrlKey||e.commandKey,e.shiftKey);
                },TrickleDown.TrickleDown);
                card.style.position=Position.Absolute;card.style.left=2+column*CatalogCardStride;card.style.top=2+row*CatalogRowHeight;card.style.width=CatalogCardWidth;card.style.height=CatalogRowHeight-4;
                card.style.marginLeft=0;card.style.marginRight=0;card.style.marginTop=0;card.style.marginBottom=0;
                card.tooltip = L.T("#DOUBLE_CLICK_PLACE_DRAG_SCENE") + "\n" + record.modelPath + "\nGUID : " + record.guid + "\n" + string.Join("\n", record.origins.Select(o => o.archive));
                var image = new Image { scaleMode = ScaleMode.ScaleToFit }; image.AddToClassList("card-image"); card.Add(image);
                bool hasImage = false;
                if (assetLibrary.CacheRoot != null)
                {
                    string thumbnailPath = Path.Combine(assetLibrary.ModelDirectory(record), "thumbnail.png");
                    // Keep an existing preview visible even when it is due for regeneration.
                    // Category exclusions stop automatic work; they must not turn a previously
                    // usable library card blank while the user decides whether to retry it.
                    if (File.Exists(thumbnailPath))
                    {
                        var texture = new Texture2D(2, 2); pageThumbnails.Add(texture);
                        try { if (ImageConversion.LoadImage(texture, File.ReadAllBytes(thumbnailPath), true)) { image.image = texture; hasImage = true; } }
                        catch (Exception ex) { Debug.LogWarning(L.T("#THUMBNAIL_CACHE") + ex.Message); }
                    }
                }
                if (!hasImage) image.Add(Label(ThumbnailStatus(record), "card-placeholder"));
                if(hasImage&&ThumbnailMissingAlbedo(record))image.Add(Label("!", "card-warning"));
                var name=Label(record.Name,"card-name");EnableNameMarquee(name,record.Name);card.Add(name);
                card.Add(Label(record.Category, "card-category"));
                if (selectedLibraryAssetIds.Contains(record.Id)) card.AddToClassList("selected");
                if (lastPreviewRequest?.Id == record.Id) card.AddToClassList("active-preview");
                catalogList.Add(card);
                }
                PrioritizeVisibleThumbnails();
            }
            finally{catalogRendering=false;}
        }

        private void ResetCatalog()
        {
            catalogQuery = null; if (catalogList != null) catalogList.scrollOffset = Vector2.zero; RefreshCatalog();
        }

        private static void EnableNameMarquee(Label label,string fullName)
        {
            IVisualElementScheduledItem animation=null;int offset=0;
            label.RegisterCallback<PointerEnterEvent>(_=> {
                int visible=Math.Max(10,Mathf.FloorToInt(label.resolvedStyle.width/5.7f));if(fullName.Length<=visible||animation!=null)return;
                string loop=fullName+"   •   ";animation=label.schedule.Execute(()=> {string doubled=loop+loop;label.text=doubled.Substring(offset,Math.Min(visible,doubled.Length-offset));offset=(offset+1)%loop.Length;}).StartingIn(500).Every(140);
            });
            label.RegisterCallback<PointerLeaveEvent>(_=> {animation?.Pause();animation=null;offset=0;label.text=fullName;});
        }

        private void UpdateLibraryModelCount()
        {
            if(pageState==null)return;
            pageState.text=selectedLibraryAssetIds.Count>0
                ? L.F("#ARG0_MODELS_ARG1_SELECTED",catalogRecords.Length,selectedLibraryAssetIds.Count)
                : L.F("#ARG0_MODELS",catalogRecords.Length);
        }

        private GameAssetRecord[] SelectedLibraryRecords() => catalogRecords
            .Where(record=>selectedLibraryAssetIds.Contains(record.Id)).ToArray();

        private void UpdateLibraryAssetActions()
        {
            if(retryPreviewButton==null||placeAssetButton==null)return;
            if(catalogMode==null||catalogMode.index!=0) {
                retryPreviewButton.SetEnabled(false);
                placeAssetButton.SetEnabled(!assetBusy&&CanPlace(previewEntry));
                return;
            }
            var selected=SelectedLibraryRecords();
            if(lastPreviewRequest==null||!selectedLibraryAssetIds.Contains(lastPreviewRequest.Id))previewEntry=null;
            else previewEntry=ReadyPlacementEntry(lastPreviewRequest);
            bool allImported=selected.Length>0&&selected.All(record=>assetLibrary.CachedModel(record)!=null);
            bool allPresent=selected.Length>0&&selected.All(assetLibrary.HasExistingModelExport);
            retryPreviewButton.text=L.T(allImported||allPresent?"#REIMPORT_MODELS":"#IMPORT_MODEL");
            retryPreviewButton.tooltip=selected.Length>1?L.F("#IMPORT_SELECTED_MODELS_ARG0",selected.Length):"";
            retryPreviewButton.SetEnabled(!assetBusy&&!indexRequested&&selected.Length>0);
            placeAssetButton.SetEnabled(CanPlaceSelectedLibraryAsset()||(!assetBusy&&CanPlace(previewEntry)));
        }

        private GameAssetRecord SelectedLibraryPlacementRecord() => catalogMode?.index==0&&lastPreviewRequest!=null&&
            selectedLibraryAssetIds.Contains(lastPreviewRequest.Id)?lastPreviewRequest:null;
        private bool CanPlaceSelectedLibraryAsset()
        {
            var record=SelectedLibraryPlacementRecord();
            return record!=null&&record.Supports(Targets)&&assetLibrary.CachedModel(record)!=null;
        }
        private void PlaceSelectedLibraryAsset()
        {
            var record=SelectedLibraryPlacementRecord();
            if(record!=null&&record.Supports(Targets)&&assetLibrary.CachedModel(record)!=null)
            { _=RequestModelPlacement(record);return; }
            if(CanPlace(previewEntry))BeginPlacement(previewEntry);
        }

        private void SelectLibraryAsset(GameAssetRecord record,bool additive,bool range)
        {
            int anchorIndex=librarySelectionAnchorId==null?-1:Array.FindIndex(catalogRecords,item=>item.Id.Equals(librarySelectionAnchorId,StringComparison.OrdinalIgnoreCase));
            int currentIndex=Array.FindIndex(catalogRecords,item=>item.Id.Equals(record.Id,StringComparison.OrdinalIgnoreCase));
            if(range&&anchorIndex>=0&&currentIndex>=0) {
                if(!additive)selectedLibraryAssetIds.Clear();
                int first=Math.Min(anchorIndex,currentIndex),last=Math.Max(anchorIndex,currentIndex);
                for(int index=first;index<=last;index++)selectedLibraryAssetIds.Add(catalogRecords[index].Id);
            } else if(additive) {
                if(!selectedLibraryAssetIds.Add(record.Id))selectedLibraryAssetIds.Remove(record.Id);
                librarySelectionAnchorId=record.Id;
            } else {
                selectedLibraryAssetIds.Clear();selectedLibraryAssetIds.Add(record.Id);librarySelectionAnchorId=record.Id;
            }
            UpdateLibraryModelCount();RenderVisibleCatalog(true);UpdateLibraryAssetActions();
        }

        private void SelectGameAsset(GameAssetRecord record,bool additive=false,bool range=false)
        {
            SelectLibraryAsset(record,additive,range);
            var active=selectedLibraryAssetIds.Contains(record.Id)?record:SelectedLibraryRecords().LastOrDefault();
            SetLibraryDetails(true);
            if(active==null) {
                lastPreviewRequest=null;previewEntry=null;
                if(currentThumbnail!=null)Destroy(currentThumbnail);currentThumbnail=null;assetPreview.image=null;
                previewText.text=L.T("#CLICK_MODEL_LOAD_PREVIEW");UpdateLibraryAssetActions();return;
            }
            lastPreviewRequest=active;
            string cached=assetLibrary.CachedModel(active)??assetLibrary.TryAdoptExistingModel(active);
            if(cached!=null) {
                if(assetBusy||indexRequested) {
                    queuedPreview=null;queuedPreviewForceRefresh=false;
                    previewEntry=ReadyPlacementEntry(active);ShowPreviewLoading(active,true);
                    previewText.text=ModelDetails(active);previewText.tooltip=active.modelPath;
                    RefreshCatalog();UpdateLibraryAssetActions();return;
                }
                _=PreviewGameAsset(active);return;
            }
            previewEntry=null;ShowSelectedUnimportedAsset(active);RefreshCatalog();UpdateLibraryAssetActions();
        }

        private void ShowSelectedUnimportedAsset(GameAssetRecord record)
        {
            if(currentThumbnail!=null)Destroy(currentThumbnail);currentThumbnail=null;assetPreview.image=null;
            string thumbnailPath=Path.Combine(assetLibrary.ModelDirectory(record),"thumbnail.png");
            if(File.Exists(thumbnailPath))try {
                var image=new Texture2D(2,2);
                if(ImageConversion.LoadImage(image,File.ReadAllBytes(thumbnailPath),true)){currentThumbnail=image;assetPreview.image=image;}
                else Destroy(image);
            }catch(Exception ex){Debug.LogWarning(L.T("#THUMBNAIL_CACHE")+ex.Message);}
            previewText.text=ModelDetails(record)+"\n\n"+L.T(assetLibrary.HasExistingModelExport(record)?
                "#MODEL_CACHE_NEEDS_REIMPORT":"#MODEL_NOT_IMPORTED_USE_IMPORT");
            previewText.tooltip=record.modelPath;
        }

        private string ModelDetails(GameAssetRecord record, int missingAlbedo = 0, Vector3? apexDimensions = null)
        {
            var archives = record.origins.Select(o => o.archive).Where(a => !string.IsNullOrWhiteSpace(a)).Distinct().OrderBy(a => a).ToArray();
            string details = record.Name + "\n" + L.T("#CATEGORY") + record.Category + "\n" + record.modelPath + "\n\n" + L.T("#RPAK") + "\n• " + string.Join("\n• ", archives);
            if(apexDimensions.HasValue)details += "\n\n"+L.T("#DIMENSIONS_X_Y_Z")+FormatVector(apexDimensions.Value);
            if (missingAlbedo > 0) details += "\n\n" + L.F("#ARG0_MATERIAL_S_UNRESOLVED_ALBEDO", missingAlbedo);
            return details;
        }
        private async Task PreviewGameAsset(GameAssetRecord record,bool forceRefresh=false)
        {
            string cachedPath=forceRefresh?null:assetLibrary.CachedModel(record);
            bool needsExtraction=forceRefresh||cachedPath==null;
            if(needsExtraction){CancelManualPreviewSessionRelease();InterruptBackgroundFor(record,true);}
            int requestVersion=++previewRequestVersion;
            if (assetBusy||indexRequested) {
                queuedPreview=record;queuedPreviewForceRefresh=forceRefresh;lastPreviewRequest=record;
                if(needsExtraction)InterruptBackgroundFor(record);ShowPreviewLoading(record,cachedPath!=null);
                previewText.text=ModelDetails(record)+"\n\n"+L.F("#PREVIEW_QUEUED_ARG0",record.Name);RefreshCatalog();return;
            }
            lastPreviewRequest = record; string previewGeneration=assetLibrary.CacheRoot;
            CommitInspectorEdit(); SetAssetBusy(true); SetStatus(needsExtraction?L.F("#EXTRACTING_ARG0_EDITING_REMAINS_AVAILABLE",record.Name):L.F("#LOADING_CACHED_ARG0",record.Name)); previewEntry = null; placeAssetButton.SetEnabled(false); CancelPlacement();
            ShowPreviewLoading(record,!needsExtraction);
            GameObject model = null;
            try
            {
                string path=cachedPath??await assetLibrary.ExtractAsync(record,Targets,forceRefresh:forceRefresh);
                if (this == null || requestVersion!=previewRequestVersion || previewGeneration!=assetLibrary.CacheRoot) return;
                if (!record.Supports(Targets)) throw new InvalidOperationException(L.T("#SELECTED_ARCHIVES_CHANGED_DURING_EXTRACTION"));
                if(needsExtraction) {
                    previewText.text=L.T("#PREPARING_TEXTURES")+record.Name;
                    await SharedTextureCache.Normalize(assetLibrary.ModelDirectory(record),SharedTextureCache.PreviewMaximumSize);
                    if(this==null||requestVersion!=previewRequestVersion||previewGeneration!=assetLibrary.CacheRoot)return;
                    var albedos=SharedTextureCache.InspectAlbedos(path);
                    if(albedos.NeedsFallback)
                        await assetLibrary.TryRepairOfficialTexturesAsync(record,path,albedos,Targets,forceRefresh:forceRefresh);
                    if(this==null||requestVersion!=previewRequestVersion||previewGeneration!=assetLibrary.CacheRoot)return;
                }
                world.models.Prepare(record.Id, path); model = world.models.Create(record.Id, false);
                var bounds = model.GetComponent<MeshFilter>().sharedMesh.bounds;
                if(requestVersion!=previewRequestVersion)return;
                var renderedThumbnail=ModelThumbnail.Render(model);
                if(currentThumbnail!=null)Destroy(currentThumbnail);
                currentThumbnail=renderedThumbnail;assetPreview.image=currentThumbnail;
                previewFailures.Remove(record.Id); readyThumbnails.Add(record.Id); failedThumbnails.Remove(record.Id);
                string errorMarker = Path.Combine(assetLibrary.ModelDirectory(record), "thumbnail.error.txt"); if (File.Exists(errorMarker)) File.Delete(errorMarker);
                int missing = world.models.MissingAlbedo(record.Id); SaveThumbnailInfo(record,missing);
                previewEntry = RememberPlacementEntry(record,model);
                previewText.text = ModelDetails(record, missing, ApexDisplay.Position(bounds.size));
                previewText.tooltip = record.modelPath+"\n"+world.models.AlbedoDiagnostics(record.Id);
                if(needsExtraction)File.WriteAllBytes(Path.Combine(assetLibrary.ModelDirectory(record),"thumbnail.png"),currentThumbnail.EncodeToPNG());
                await WaitForAssetUiIdle(); if(this==null||requestVersion!=previewRequestVersion)return; CommitInspectorEdit(); if(needsExtraction)world.Reload(record.Id); Refresh(); RefreshCatalog();
                SetStatus(missing > 0 ? L.F("#ARG0_ARG1_MATERIAL_S_UNRESOLVED", record.Name, missing) : L.T("#SELECTED_MODEL") + record.Name);
            }
            catch (Exception ex) { if (this != null&&requestVersion==previewRequestVersion) { previewEntry = null; previewFailures[record.Id] = ex.Message; failedThumbnails.Add(record.Id); previewText.text = record.Name + " : " + ex.Message; previewText.tooltip = ex.ToString(); SetStatus(ex.Message); Debug.LogWarning(ex); } }
            finally { await WaitForAssetUiIdle(); if (this != null) { Run(CommitInspectorEdit); if (model != null) world.models.Release(record.Id, model); Refresh(); SetAssetBusy(false); if(needsExtraction)ScheduleManualPreviewSessionRelease(previewGeneration); RunQueuedPreview(); } }
        }
        private void ShowPreviewLoading(GameAssetRecord record,bool cached) {
            if(currentThumbnail!=null)Destroy(currentThumbnail);currentThumbnail=null;assetPreview.image=null;
            if(cached) {
                string thumbnailPath=Path.Combine(assetLibrary.ModelDirectory(record),"thumbnail.png");
                if(File.Exists(thumbnailPath))try {
                    var image=new Texture2D(2,2);
                    if(ImageConversion.LoadImage(image,File.ReadAllBytes(thumbnailPath),true)){currentThumbnail=image;assetPreview.image=image;}
                    else Destroy(image);
                }catch(Exception ex){Debug.LogWarning(L.T("#THUMBNAIL_CACHE")+ex.Message);}
            }
            previewText.text=ModelDetails(record)+"\n\n"+L.T(cached?"#LOADING_CACHED_PREVIEW":"#LOADING_PREVIEW");
        }
        private void RunQueuedPreview() {
            if(queuedPreview==null||assetBusy||indexRequested)return;
            var next=queuedPreview;bool force=queuedPreviewForceRefresh;
            queuedPreview=null;queuedPreviewForceRefresh=false;_=PreviewGameAsset(next,force);
        }
        private async Task WaitForAssetUiIdle() {
            while(this!=null&&(draggingGizmo||libraryDragging||sceneSelectionPending||assemblyDragging||layoutResizing))await Task.Delay(50);
        }
        private void RestoreCachedModels()
        {
            if (snapshot == null) return;
            foreach (var id in snapshot.objects.Select(o => o.assetId).Distinct())
            {
                var entry = assetLibrary.Records.FirstOrDefault(r => r.Id == id);
                if (entry == null) continue;
                string path = assetLibrary.CachedModel(entry);
                if (path != null) { world.models.Prepare(id, path); world.Reload(id); }
            }
            Refresh();
        }
        private void BeginPlacement(CatalogEntry entry)
        {
            placementRequestVersion++; CommitInspectorEdit(); world.ClearPreview(); placing = entry; selectedId = null; world.Highlight(null);
            mode.text = L.F("#PLACING_ARG0_ESCAPE_FINISH", entry.Name); RefreshInspector();
            SetStatus(L.F("#CLICK_SCENE_PLACE_ARG0", entry.Name));
        }
        private bool Incompatible(MapObject item) => item.assetId.StartsWith("apex:", StringComparison.Ordinal) && !AssetCompatibility.Supports(item.commonAsset, item.availableMaps, Targets);
        private bool CanPlace(CatalogEntry entry) => entry != null && entry.SupportsGame(snapshot.gameTarget) &&
            (entry.CustomType != null ? CustomObjectAvailable(entry) : entry.GameAsset?.Supports(Targets) == true);
        private string[] displayedTargets;
        private void RefreshAssetTargets()
        {
            foreach (var toggle in mapToggles) toggle.Value.SetValueWithoutNotify(snapshot.targetMaps.Contains(toggle.Key));
            RefreshEditingMapChoice();
            if (placing?.GameAsset != null && !placing.GameAsset.Supports(Targets)) CancelPlacement();
            UpdateLibraryAssetActions();
            var currentTargets = Targets;
            if (displayedTargets == null || !displayedTargets.SequenceEqual(currentTargets)) { displayedTargets = currentTargets; RefreshCatalog(); }
        }

        private void SetLibraryDetails(bool visible)
        {
            layout.libraryDetails = visible; libraryDetailsToggle?.SetValueWithoutNotify(visible); SaveLayout(); RefreshAssetCatalog();
        }
    }
}
