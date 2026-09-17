using System;

using System.Globalization;

using System.IO;

using System.Linq;

using ReMap.Standalone.Core;

using UnityEngine;

using UnityEngine.InputSystem;

using UnityEngine.UIElements;



namespace ReMap.Standalone

{

    public sealed class UnityMapCodec : IMapCodec

    {

        public string Encode(MapDocument document) => JsonUtility.ToJson(document, true);

        public MapDocument Decode(string json)

        {

            // JsonUtility fills missing fields with defaults. Require the format marker explicitly.

            if (string.IsNullOrWhiteSpace(json) || !json.Contains("\"schemaVersion\"") ||

                !json.Contains("\"coordinateSystem\"") || !json.Contains("\"objects\""))

                throw new ArgumentException(L.T("#NOT_REMAP_MAP"));

            var document = JsonUtility.FromJson<MapDocument>(json);
            // Saves created before classic prop settings existed do not contain these
            // fields. JsonUtility reports zero values for them, so restore the legacy
            // ReMap defaults explicitly during decoding.
            bool hasMantle = json.Contains("\"allowMantle\"");
            bool hasFadeDistance = json.Contains("\"fadeDistance\"");
            bool hasRealmId = json.Contains("\"realmId\"");
            foreach (var item in document?.objects ?? new System.Collections.Generic.List<MapObject>())
            {
                if (!hasMantle) item.allowMantle = true;
                if (!hasFadeDistance) item.fadeDistance = 50000f;
                if (!hasRealmId) item.realmId = -1;
            }
            return document;

        }

    }



    [RequireComponent(typeof(UIDocument))]

    public sealed partial class ReMapApp : MonoBehaviour

    {

        [SerializeField] private Shader objectShader;

        [SerializeField] private Shader gridShader;

        [SerializeField] private Shader lineShader;

        [SerializeField] private StyleSheet stylesheet;

        private readonly MapSession session = new MapSession();

        private DemoCatalog catalog;

        private readonly UnityMapCodec codec = new UnityMapCodec();

        private WorldView world;

        private MapFiles files;

        private MapDocument snapshot;

        private VisualElement root, viewport, inspector;

        private ScrollView catalogList, objectList;

        private Label status, count, mode, fileStatus;

        private Button undoButton, redoButton;

        private DropdownField slot;
        private GenericDropdownMenu activeDropdownMenu;
        private TextField search;


        private long lastSavedRevision = -1;

        private CatalogEntry placing;

        private const string SnapPreference = "ReMap.Snapping.v1";
        private const string CentrePivotPreference = "ReMap.Gizmo.CentrePivot.v1";
        private static string CentrePivotPreferenceKey => CentrePivotPreference +
            (Environment.GetCommandLineArgs().AnySmokeFlag() ? ".qa" : "");
        private bool snap = true;



        public void Configure(Shader objectShader, Shader gridShader, Shader lineShader, StyleSheet stylesheet)

        { this.objectShader = objectShader; this.gridShader = gridShader; this.lineShader = lineShader; this.stylesheet = stylesheet; }



        private void Awake() { Application.runInBackground = true; if(Environment.GetCommandLineArgs().Any(a=>a.StartsWith("-remap")))InputSystem.settings.backgroundBehavior=InputSettings.BackgroundBehavior.IgnoreFocus; }



        private void Start()

        {

            Application.targetFrameRate = 60;

            world = new WorldView(objectShader, gridShader, lineShader);

            catalog = new DemoCatalog();
            files = new MapFiles(PersistentDirectory("Maps"), codec);

            root = GetComponent<UIDocument>().rootVisualElement;

            root.styleSheets.Add(stylesheet);

            if (!Environment.GetCommandLineArgs().AnySmokeFlag()) snap = PlayerPrefs.GetInt(SnapPreference, 1) != 0;
            centrePivot = PlayerPrefs.GetInt(CentrePivotPreferenceKey, 1) != 0;
            autoCollapseOtherFolders = PlayerPrefs.GetInt(AutoCollapseOtherFoldersPreferenceKey, 0) != 0;
            LoadLayout(); BuildInterface();

            session.Edit(doc => {

                doc.name = L.T("#STARTER_WORKSPACE");
                doc.gameTarget = assetLibrary.TargetGame;

                doc.objects.Add(new MapObject { assetId = "demo:floor", displayName = L.T("#PLATFORM"), position = new Float3(0,.125f,0), scale = new Float3(6,.25f,6) });

                doc.objects.Add(new MapObject { assetId = "demo:wall", displayName = L.T("#WALL"), position = new Float3(0,1.75f,2.8f), scale = new Float3(6,3,.3f) });

                doc.objects.Add(new MapObject { assetId = "demo:cylinder", displayName = L.T("#PILLAR"), position = new Float3(-2,1.25f,0), scale = new Float3(.65f,1,.65f) });

            });

            Refresh(); RefreshProjectSelector(DraftSlot(assetLibrary.TargetGame));

            bool regularLaunch = !Environment.GetCommandLineArgs().Any(a => a.StartsWith("-remap"));
            bool restoredWorkspace = regularLaunch && TryRestoreWorkspace();
            if (regularLaunch) assetLibrary.SelectTarget(snapshot.gameTarget, false);
            if (regularLaunch && assetLibrary.Configured)
            {
                if (snapshot.targetMaps.Count == 0)
                {
                    session.Edit(d => d.targetMaps = assetLibrary.LastTargetMaps.Where(m => assetLibrary.Maps.Any(x => x.Id == m)).ToList());
                    Refresh();
                }
                _ = IndexAssets();
            }

            if (Environment.GetCommandLineArgs().Contains("-remapThumbnailPrioritySmoke")) StartCoroutine(ThumbnailPrioritySmoke());
            if (Environment.GetCommandLineArgs().Contains("-remapPlacementGestureSmoke")) StartCoroutine(PlacementGestureSmoke());
            if (Environment.GetCommandLineArgs().Contains("-remapAlbedoSmoke")) StartCoroutine(AlbedoSmoke());
            if (Environment.GetCommandLineArgs().Contains("-remapContinuousSmoke")) StartCoroutine(ContinuousPreviewSmoke());
            if (Environment.GetCommandLineArgs().Contains("-remapLocalizationSmoke")) StartCoroutine(LocalizationSmoke());
            if (Environment.GetCommandLineArgs().Contains("-remapApexCoordinatesSmoke")) StartCoroutine(ApexCoordinatesSmoke());
            if (Environment.GetCommandLineArgs().Contains("-remapCameraOrbitSmoke")) StartCoroutine(CameraOrbitSmoke());
            if (Environment.GetCommandLineArgs().Contains("-remapDropWheelSmoke")) StartCoroutine(DropWheelSmoke());
            if (Environment.GetCommandLineArgs().Contains("-remapArchiveUnionSmoke")) StartCoroutine(ArchiveUnionSmoke());
            if (Environment.GetCommandLineArgs().Contains("-remapAssetQueueSmoke")) StartCoroutine(AssetQueueSmoke());
            if (Environment.GetCommandLineArgs().Contains("-remapEditorSmoke")) StartCoroutine(EditorFeaturesSmoke());
            if (Environment.GetCommandLineArgs().Contains("-remapSmoke")) StartCoroutine(SmokeCheck());

            if (Environment.GetCommandLineArgs().Contains("-remapAssetSmoke")) StartCoroutine(AssetSmokeCheck());

            if (Environment.GetCommandLineArgs().Contains("-remapInteractionSmoke")) StartCoroutine(InteractionSmoke());

            if (Environment.GetCommandLineArgs().Contains("-remapWorkspaceSmoke")) StartCoroutine(WorkspaceFeaturesSmoke());
            if (Environment.GetCommandLineArgs().Contains("-remapHierarchySmoke")) StartCoroutine(HierarchySmoke());
            if (Environment.GetCommandLineArgs().Contains("-remapLayoutSmoke")) StartCoroutine(LayoutSmoke());
            if (Environment.GetCommandLineArgs().Contains("-remapToolsSmoke")) StartCoroutine(ConstructionSmoke());
            if (Environment.GetCommandLineArgs().Contains("-remapLiveBridgeSmoke")) StartCoroutine(LiveBridgeSmoke());
            if (Environment.GetCommandLineArgs().Contains("-remapCollisionSmoke")) StartCoroutine(CollisionSmoke());
            if (Environment.GetCommandLineArgs().Contains("-remapPerfSmoke")) StartCoroutine(PerformanceSmoke());
            if (Environment.GetCommandLineArgs().Contains("-remapMapReferenceSmoke")) StartCoroutine(MapReferenceComparisonSmoke());
            SetStatus(restoredWorkspace ? L.T("#LAST_WORKSPACE_RESTORED") : L.T("#CHOOSE_SHAPE_LIBRARY_CLICK_SCENE"));

        }

        private static string PersistentDirectory(string name)
        {
            string destination = Path.Combine(Application.persistentDataPath, name);
            try
            {
                var product = Directory.GetParent(Application.persistentDataPath);
                var localLow = product?.Parent;
                string legacy = localLow == null ? "" : Path.Combine(localLow.FullName, "ReMapFlowstate", "ReMapFlowstate", name);
                if (Directory.Exists(legacy))
                {
                    Directory.CreateDirectory(destination);
                    foreach (string source in Directory.EnumerateFiles(legacy, "*", SearchOption.TopDirectoryOnly))
                    {
                        string target = Path.Combine(destination, Path.GetFileName(source));
                        if (!File.Exists(target)) File.Copy(source, target);
                    }
                }
            }
            catch (Exception ex) { Debug.LogWarning("ReMap legacy data migration: " + ex.Message); }
            return destination;
        }



        private Button Button(string text, Action action, string className = null)

        {

            var button = new Button(() => Run(action)) { text = text };

            if (className != null) button.AddToClassList(className);

            return button;

        }



        private static Label Label(string text, string className)

        { var label = new Label(text); label.AddToClassList(className); return label; }



        private void BuildInterface()

        {

            root.AddToClassList("app");

            root.RegisterCallback<GeometryChangedEvent>(_ => AdaptLayout());
            root.RegisterCallback<PointerDownEvent>(OpenContentSizedDropdown, TrickleDown.TrickleDown);
            root.RegisterCallback<NavigationSubmitEvent>(OpenContentSizedDropdown, TrickleDown.TrickleDown);

            var header = new VisualElement(); header.AddToClassList("header"); root.Add(header);

            header.Add(Label("ReMap", "brand"));

            BuildTopMenuButtons(header);

            var spacer = new VisualElement(); spacer.style.flexGrow = 1; header.Add(spacer);

            workspaceGameButton = Button("", () => ShowWorkspaceGameDialog(true), "workspace-game-button"); header.Add(workspaceGameButton);

            header.Add(Label(L.T("#PROJECT"), "current-map-label"));

            slot = new DropdownField { tooltip = L.T("#CHOOSE_SAVED_PROJECT") }; slot.AddToClassList("slot"); header.Add(slot); InitializeProjectSelector();

            var toolbar = new VisualElement(); toolbar.AddToClassList("toolbar"); root.Add(toolbar);

            toolbar.Add(Button(L.T("#MOVE_W"), () => SetGizmoMode(false)));

            toolbar.Add(Button(L.T("#ROTATE_E"), () => SetGizmoMode(true)));
            BuildTransformToolbar(toolbar);

            var snapToggle = new Toggle(L.T("#SNAPPING")) { value = snap };
            snapToggle.RegisterValueChangedCallback(e =>
            {
                snap = e.newValue;
                if (!Environment.GetCommandLineArgs().AnySmokeFlag())
                {
                    PlayerPrefs.SetInt(SnapPreference, snap ? 1 : 0);
                    PlayerPrefs.Save();
                }
            });
            toolbar.Add(snapToggle);

            toolbar.Add(Button(L.T("#FRAME_F"), () => FocusSelection()));

            mode = Label(L.T("#SELECTION"), "mode"); toolbar.Add(mode);

            fileStatus = Label("", "file-status"); header.Add(fileStatus);

            var body = new VisualElement(); body.AddToClassList("body"); root.Add(body);

            var workspace = new VisualElement(); workspace.AddToClassList("workspace-column"); body.Add(workspace);

            viewport = new VisualElement { focusable = true }; viewport.AddToClassList("viewport"); workspace.Add(viewport);

            viewport.RegisterCallback<PointerDownEvent>(_ => viewport.Focus());

            var viewportTitle = Label(L.T("#BUILD_AREA"), "viewport-title"); viewportTitle.pickingMode = PickingMode.Ignore; viewport.Add(viewportTitle);

            gizmoVisual = new GizmoOverlay(); viewport.Add(gizmoVisual);

            var hint = Label(L.T("#APEX_X_Y_HORIZONTAL_Z_415865"), "viewport-hint");

            hint.pickingMode = PickingMode.Ignore; viewport.Add(hint);

            libraryDock = new VisualElement(); libraryDock.AddToClassList("library-dock"); workspace.Add(libraryDock); BuildAssetLibrary(libraryDock);

            inspectorPanel = new VisualElement(); inspectorPanel.AddToClassList("right-panel"); body.Add(inspectorPanel);

            hierarchyPane = new VisualElement(); hierarchyPane.AddToClassList("dock-pane"); hierarchyPane.Add(DockTitle(L.T("#HIERARCHY"), () => SetPanelVisible("hierarchy", false))); inspectorPanel.Add(hierarchyPane);

            BuildHierarchy(hierarchyPane);

            propertiesPane = new VisualElement(); propertiesPane.AddToClassList("dock-pane"); propertiesPane.style.flexGrow = 1; propertiesPane.style.flexBasis = 0; propertiesPane.Add(DockTitle(L.T("#PROPERTIES"), () => SetPanelVisible("properties", false))); inspectorPanel.Add(propertiesPane);

            inspector = new ScrollView(); inspector.AddToClassList("inspector"); propertiesPane.Add(inspector);

            var footer = new VisualElement(); footer.AddToClassList("footer"); root.Add(footer);

            status = Label("", "status"); footer.Add(status); count = Label("", "count"); footer.Add(count);

            BuildSettings(); BuildAbout(); BuildWorkspaceGameDialog(); BuildNewMapDialog(); BuildLoadingUI(); BuildDragUI(); BuildDockLayout(body, workspace); BuildConstructionTools(); BuildCodePreviewWindow(); BuildLiveConsoleWindow(); RefreshCatalog();

        }

        private static DropdownField DropdownFromTarget(object target)
        {
            for (var current = target as VisualElement; current != null; current = current.parent)
                if (current is DropdownField dropdown) return dropdown;
            return null;
        }

        private void OpenContentSizedDropdown(PointerDownEvent evt)
        {
            if (evt.button != 0) return;
            if (OpenContentSizedDropdown(DropdownFromTarget(evt.target))) evt.StopPropagation();
        }

        private void OpenContentSizedDropdown(NavigationSubmitEvent evt)
        {
            if (OpenContentSizedDropdown(DropdownFromTarget(evt.target))) evt.StopPropagation();
        }

        private bool OpenContentSizedDropdown(DropdownField field)
        {
            if (field == null || !field.enabledInHierarchy || field.choices == null ||
                field.choices.Count == 0 || field.panel == null) return false;
            var menu = new GenericDropdownMenu();
            foreach (string choice in field.choices)
            {
                string selectedChoice = choice;
                string label = field.formatListItemCallback?.Invoke(choice) ?? choice;
                menu.AddItem(label, field.value == choice, () => field.value = selectedChoice);
            }
            activeDropdownMenu = menu;
            menu.onClose += () => {
                if (ReferenceEquals(activeDropdownMenu, menu)) activeDropdownMenu = null;
            };
            menu.DropDown(field.worldBound, field, DropdownMenuSizeMode.Content);
            return true;
        }

        private void RefreshCatalog() => RefreshAssetCatalog();



        private void Update()

        {

            if (world == null || viewport?.panel == null || Mouse.current == null) return;

            if (HandleGlobalKeyboardShortcuts()) return;

            world.TickCamera(Time.unscaledDeltaTime);
            world.TickMprtReference(Time.unscaledDeltaTime);
            var bounds = viewport.worldBound;

            float sx = Screen.width / root.worldBound.width, sy = Screen.height / root.worldBound.height;

            if (!float.IsFinite(sx) || !float.IsFinite(sy)) return;

            world.SetViewport(new Rect(bounds.x * sx, bounds.y * sy, bounds.width * sx, bounds.height * sy));

            var mousePosition = Mouse.current.position.ReadValue();

            var panelPosition = RuntimePanelUtils.ScreenToPanel(root.panel, new Vector2(mousePosition.x, Screen.height - mousePosition.y));

            if (layoutResizing || BlockingDialogOpen || loadingOverlay?.style.display.value == DisplayStyle.Flex) { world.CancelNavigation(); world.ClearPreview(); return; }

            bool inside = bounds.Contains(panelPosition) && !PointerOverFloatingPanel(panelPosition);

            if(!inside&&Mouse.current.middleButton.wasReleasedThisFrame)world.CancelMiddleGesture();
            if(world.Orbiting||(inside&&WorldView.OrbitShortcut&&Mouse.current.leftButton.wasPressedThisFrame&&!libraryDragging&&!assemblyDragging)) {
                CommitInspectorEdit();CancelSceneSelection();CancelGizmoDrag();viewport.Focus();world.ClearPreview();
                world.Navigate(Time.unscaledDeltaTime);UpdateGizmoVisual();return;
            }
            if (HandleAssemblyInput(panelPosition, mousePosition, inside)) return;
            if (HandleLibraryDrag(panelPosition, mousePosition)) return;

            if (inside && (Mouse.current.rightButton.isPressed || Mouse.current.middleButton.wasPressedThisFrame)) { CommitInspectorEdit(); viewport.Focus(); }

            UpdateGizmoVisual();

            if (sceneSelectionPending) { HandleSceneSelection(mousePosition, inside); return; }

            if (HandleGizmoInput(mousePosition, inside)) return;

            if (inside)

            {

                world.Navigate(Time.unscaledDeltaTime); UpdateGizmoVisual();

                if (placing != null && world.Placement(mousePosition, placing, snap, out var point))

                {

                    world.Preview(placing, point);

                    if (Mouse.current.leftButton.wasPressedThisFrame)

                    {

                        var entry = placing;

                        Run(() => {

                            if (entry.CustomType != null)
                            {
                                InsertCustomObject(entry, point);
                            }
                            else
                            {
                                var item = new MapObject { assetId = entry.Id, displayName = entry.Name,

                                    position = WorldView.ToData(point), scale = WorldView.ToData(entry.Size),

                                    gameModelPath = entry.GameAsset?.modelPath ?? "", commonAsset = entry.GameAsset?.IsCommon ?? false,

                                    availableMaps = entry.GameAsset?.origins.Select(o => o.mapId).Where(m => m != "").Distinct().ToList() ?? new System.Collections.Generic.List<string>() };

                                session.Edit(doc => doc.objects.Add(item));

                                selectedId = item.id;

                                Refresh();
                            }

                        });

                    }

                }

                else

                {

                    world.ClearPreview();

                    HandleSceneSelection(mousePosition, inside);

                }

            }

            else world.ClearPreview();

            var keyboard = Keyboard.current;

            if (keyboard == null || root.panel.focusController.focusedElement != viewport) return;

            if (keyboard.escapeKey.wasPressedThisFrame) CancelPlacement();

            if (!Mouse.current.rightButton.isPressed) {

                if (keyboard.fKey.wasPressedThisFrame) FocusSelection();

                if (keyboard.wKey.wasPressedThisFrame) SetGizmoMode(false);

                if (keyboard.eKey.wasPressedThisFrame) SetGizmoMode(true);
                if (keyboard.rKey.wasPressedThisFrame) SetScaleGizmo();

            }

            bool control = keyboard.ctrlKey.isPressed;

            if (selectedId != null && !control && !Mouse.current.rightButton.isPressed)

            {

                var delta = Vector3.zero;

                float step = snap ? moveSnap : ApexCoordinates.MetersPerUnit;

                if (keyboard.leftArrowKey.wasPressedThisFrame) delta.x -= step;

                if (keyboard.rightArrowKey.wasPressedThisFrame) delta.x += step;

                if (keyboard.upArrowKey.wasPressedThisFrame) delta.z += step;

                if (keyboard.downArrowKey.wasPressedThisFrame) delta.z -= step;

                if (keyboard.pageUpKey.wasPressedThisFrame) delta.y += step;

                if (keyboard.pageDownKey.wasPressedThisFrame) delta.y -= step;

                if (delta != Vector3.zero) Run(() => {

                    MoveSelection(delta);

                });

            }

        }



        private void Select(string id)

        {

            SelectModified(id, false, false);

        }

        private void CancelPlacement() { placementRequestVersion++; placingAssembly=null;placingSelection=null;placingSelectionRoots=null;assemblyDragSlot=null;assemblyDragging=false; CancelSceneSelection(); CancelGizmoDrag(); placing = null; world.ClearPreview(); mode.RemoveFromClassList("mode-warning"); mode.text = L.T("#SELECTION"); }

        private void Refresh()

        {

            snapshot = session.Snapshot();

            NormalizeSelection();

            world.Sync(snapshot, selectedId); world.HighlightSelection(SelectionRoots()); SyncMapReference();

            undoButton?.SetEnabled(session.CanUndo); redoButton?.SetEnabled(session.CanRedo);

            fileStatus.text = session.Revision == lastSavedRevision ? L.T("#SAVED") : L.T("#UNSAVED_CHANGES");

            RefreshWorkspaceGameButton();

            count.text = L.F("#ARG0_OBJECTS_ARG1_LOADED_MODELS", snapshot.objects.Count, world.LoadedModelCount);

            RefreshObjects(); RefreshInspector(); RefreshAssetTargets(); QueueWorkspaceRecovery();

        }

        private VectorInput positionInput, rotationInput, scaleInput;

        private TextField objectNameInput, worldPositionInfo;

        private string inspectorEditingId;
        private const string SceneRootInspectorId = "__scene_root__";

        private bool inspectorDirty;

        private static T CompactInspectorField<T>(T field) where T : VisualElement
        {
            field.AddToClassList("inspector-field-row");
            return field;
        }

        private VisualElement InspectorSection(string title, string name)
        {
            var section = new VisualElement { name = name };
            section.AddToClassList("inspector-section");
            section.Add(Label(title, "inspector-section-title"));
            return section;
        }

        private TextField CopyableInspectorValue(string label, string value)
        {
            var field = new TextField(label) { value = value ?? "", isReadOnly = true };
            field.AddToClassList("inspector-info-field");
            field.tooltip = value ?? "";
            return field;
        }

        private sealed class EndlessFloatField : FloatField
        {
            private bool dragging;
            private int pointerId=-1;
            private VisualElement activeDragHandle;
            private float startValue;
            private CursorLockMode previousLockState;
            private bool previousCursorVisible;
            private bool previousDelayed;
            private bool restorePointer;
            private Vector2 restorePosition;
            private bool clickCandidate;
            public event Action Clicked;

            public EndlessFloatField(string label):base(label)
            {
                AddDragHandle(labelElement);
                RegisterCallback<DetachFromPanelEvent>(_=>Stop());
            }

            public void AddDragHandle(VisualElement handle)
            {
                handle.RegisterCallback<PointerDownEvent>(Begin,TrickleDown.TrickleDown);
                handle.RegisterCallback<PointerMoveEvent>(Move,TrickleDown.TrickleDown);
                handle.RegisterCallback<PointerUpEvent>(End,TrickleDown.TrickleDown);
                handle.RegisterCallback<PointerCancelEvent>(_=>Stop());
                handle.RegisterCallback<PointerCaptureOutEvent>(_=>Stop());
            }

            private void Begin(PointerDownEvent e)
            {
                if(e.button!=0||dragging)return;
                activeDragHandle=e.currentTarget as VisualElement??labelElement;
                dragging=true;clickCandidate=true;pointerId=e.pointerId;startValue=value;
                previousDelayed=isDelayed;isDelayed=false;
                previousLockState=UnityEngine.Cursor.lockState;previousCursorVisible=UnityEngine.Cursor.visible;
                var mouse=Mouse.current;restorePointer=mouse!=null;
                if(restorePointer)restorePosition=mouse.position.ReadValue();
                ((IValueField<float>)this).StartDragging();
                activeDragHandle.CapturePointer(pointerId);
                UnityEngine.Cursor.lockState=CursorLockMode.Locked;UnityEngine.Cursor.visible=false;
                e.StopImmediatePropagation();
            }

            private void Move(PointerMoveEvent e)
            {
                if(!dragging||e.pointerId!=pointerId)return;
                if((e.pressedButtons&1)==0){Stop();return;}
                Vector3 delta=e.deltaPosition;
                Vector2 mouseDelta=Mouse.current?.delta.ReadValue()??Vector2.zero;
                if(mouseDelta.sqrMagnitude>0)delta=new Vector3(mouseDelta.x,-mouseDelta.y,0);
                var speed=e.shiftKey?DeltaSpeed.Fast:e.altKey?DeltaSpeed.Slow:DeltaSpeed.Normal;
                if(delta.sqrMagnitude>.25f)clickCandidate=false;
                ((IValueField<float>)this).ApplyInputDeviceDelta(delta,speed,startValue);
                e.StopImmediatePropagation();
            }

            private void End(PointerUpEvent e)
            {
                if(dragging&&e.pointerId==pointerId&&e.button==0)
                {
                    bool clicked=clickCandidate;
                    Stop();
                    if(clicked)Clicked?.Invoke();
                }
            }

            private void Stop()
            {
                if(!dragging)return;
                dragging=false;clickCandidate=false;
                int releasedPointer=pointerId;pointerId=-1;
                if(activeDragHandle!=null&&activeDragHandle.HasPointerCapture(releasedPointer))activeDragHandle.ReleasePointer(releasedPointer);
                activeDragHandle=null;
                ((IValueField<float>)this).StopDragging();
                isDelayed=previousDelayed;
                UnityEngine.Cursor.lockState=previousLockState;UnityEngine.Cursor.visible=previousCursorVisible;
                if(restorePointer&&previousLockState!=CursorLockMode.Locked&&Mouse.current!=null)
                    Mouse.current.WarpCursorPosition(restorePosition);
                restorePointer=false;
            }

            public void FinishDragging()=>Stop();
        }

        private sealed class VectorInput : VisualElement

        {

            private readonly EndlessFloatField x, y, z;
            private readonly VisualElement fieldsRow;
            private readonly VisualElement titleRow;
            private bool linking;
            private Vector3 previousDisplayed;
            private readonly Func<float> angleStep;
            private readonly bool uniform;

            public event Action Changed;

            private readonly int displayKind; // 0 raw, 1 position, 2 Source angles, 3 scale axes
            public Vector3 DisplayedValue=>uniform?Vector3.one*x.value:new Vector3(x.value,y.value,z.value);
            private Vector3 Display(Vector3 v)=>displayKind==1?ApexDisplay.Position(v):displayKind==2?ApexDisplay.Angles(v):displayKind==3?ApexDisplay.Axes(v):v;
            public Vector3 value => displayKind==1?ApexDisplay.UnityPosition(DisplayedValue):displayKind==2?ApexDisplay.UnityAngles(DisplayedValue):displayKind==3?ApexDisplay.Axes(DisplayedValue):DisplayedValue;

            public void Set(Vector3 v) { v=Display(v);if(displayKind==2)v=NormalizeAngles(v);SetDisplayed(v); }
            public void SetAxisEnabled(int axis, bool enabled)
            {
                var field=axis==0?x:axis==1?y:z;
                field?.SetEnabled(enabled);
            }

            public void SetDelayed(bool delayed)
            {
                x.isDelayed = delayed;
                if (!uniform) { y.isDelayed = delayed; z.isDelayed = delayed; }
            }

            public void Change(Vector3 v) { Set(v); Changed?.Invoke(); }

            public VectorInput(string title, Vector3 initial, int kind=0, Func<float> angleStep=null)

            {

                displayKind=kind;uniform=kind==3;this.angleStep=angleStep;initial=Display(initial);if(displayKind==2)initial=NormalizeAngles(initial);
                if(uniform)AddToClassList("uniform-property");
                titleRow = new VisualElement(); titleRow.AddToClassList("property-title-row");
                var titleLabel = new Label(title) { tooltip = title }; titleLabel.AddToClassList("property-title-label"); titleLabel.style.flexGrow = 1; titleRow.Add(titleLabel); Add(titleRow);
                fieldsRow = new VisualElement(); fieldsRow.AddToClassList("property-fields-row"); fieldsRow.style.flexDirection = FlexDirection.Row; Add(fieldsRow);

                x = Field(uniform?"":kind==2?"P":"X", initial.x, fieldsRow);
                if(uniform)
                {
                    x.labelElement.style.display=DisplayStyle.None;x.AddToClassList("uniform-scale-value");
                    x.AddDragHandle(titleLabel);
                    y=z=null;previousDisplayed=Vector3.one*initial.x;
                }
                else
                {
                    y = Field("Y", initial.y, fieldsRow); z = Field(kind==2?"R":"Z", initial.z, fieldsRow);previousDisplayed=initial;
                }
                if(kind==2){x.tooltip=L.T("#PITCH");y.tooltip=L.T("#YAW");z.tooltip=L.T("#ROLL");x.Clicked+=()=>StepAngleAxis(0);y.Clicked+=()=>StepAngleAxis(1);z.Clicked+=()=>StepAngleAxis(2);}
                if(kind==1)tooltip=L.T("#APEX_UNITS_X_Y_HORIZONTAL");

                x.RegisterValueChangedCallback(_=>AxisChanged(0));
                if(!uniform){y.RegisterValueChangedCallback(_=>AxisChanged(1));z.RegisterValueChangedCallback(_=>AxisChanged(2));}

            }

            public void AddToFieldsRow(VisualElement element) => fieldsRow.Add(element);
            public void AddToTitleRow(VisualElement element) => titleRow.Add(element);
            public VisualElement FieldsRow => fieldsRow;
            public VisualElement TitleRow => titleRow;
            public void FinishDragging() { x.FinishDragging(); y?.FinishDragging(); z?.FinishDragging(); }

            private static Vector3 NormalizeAngles(Vector3 value) => new Vector3(
                GizmoPlacement.NormalizeAngle(value.x), GizmoPlacement.NormalizeAngle(value.y),
                GizmoPlacement.NormalizeAngle(value.z));

            private void SetDisplayed(Vector3 displayed)
            {
                if(uniform)
                {
                    linking=true;x.SetValueWithoutNotify(displayed.x);previousDisplayed=Vector3.one*displayed.x;linking=false;return;
                }
                linking=true;x.SetValueWithoutNotify(displayed.x);y.SetValueWithoutNotify(displayed.y);z.SetValueWithoutNotify(displayed.z);previousDisplayed=displayed;linking=false;
            }

            private void StepAngleAxis(int axis)
            {
                float step=angleStep?.Invoke()??0f;
                if(!float.IsFinite(step)||step<=0f)return;
                var current=NormalizeAngles(DisplayedValue);
                current[axis]=GizmoPlacement.StepAngle(current[axis],step);
                SetDisplayed(current);Changed?.Invoke();
            }

            private void AxisChanged(int axis)
            {
                if(linking)return;
                if(uniform){previousDisplayed=Vector3.one*x.value;Changed?.Invoke();return;}
                var current=DisplayedValue;
                if(displayKind==2)
                {
                    current=NormalizeAngles(current);
                    SetDisplayed(current);
                }
                previousDisplayed=DisplayedValue;Changed?.Invoke();
            }

            private static EndlessFloatField Field(string axis, float initial, VisualElement row)

            {

                var field = new EndlessFloatField(axis) { value = initial, isDelayed = true }; field.style.flexGrow = 1; field.style.flexBasis = 0; field.style.minWidth = 0;

                field.style.marginLeft = 0; field.style.marginRight = 4; field.labelElement.style.minWidth = 12; field.labelElement.style.width = 12;

                field.labelElement.style.marginRight = 2; row.Add(field); return field;

            }

        }

        private void RefreshInspector()

        {

            inspector.Clear();worldPositionInfo=null; positionInput = rotationInput = scaleInput = null; objectNameInput = null; inspectorLockedZiplineEnd=null; jumpTowerHeightInput=null;

            var item = snapshot?.objects.Find(o => o.id == selectedId); inspectorEditingId = item?.id;

            if (item == null)
            {
                if (sceneRootSelected) BuildSceneRootInspector();
                else inspector.Add(Label(L.T("#SELECT_OBJECT_SCENE_HIERARCHY"), "note"));
                return;
            }

            if (selectedIds.Count > 1) { BuildMultipleInspector(); return; }
            inspectorOriginals = null;
            var transformSection = InspectorSection(L.T("#TRANSFORM"), "inspector-transform");
            objectNameInput = CompactInspectorField(new TextField(L.T("#NAME")) { value = item.displayName });
            objectNameInput.AddToClassList("property-field"); transformSection.Add(objectNameInput);

            positionInput = new VectorInput(L.T("#LOCAL_APEX_POSITION_U"), WorldView.ToVector(item.position),1);

            if (IsVerticalOnlyMoveTarget(item))
            {
                positionInput.SetAxisEnabled(0, false);
                positionInput.SetAxisEnabled(1, false);
            }
            rotationInput = new VectorInput(L.T("#LOCAL_APEX_ANGLES"), WorldView.ToVector(item.rotation),2,()=>rotateSnap);

            scaleInput = new VectorInput(L.T("#SCALE"), WorldView.ToVector(item.scale),3);

            if (IsJumpTowerBalloon(item))
            {
                positionInput.SetDelayed(false);
                rotationInput.SetEnabled(false);
                scaleInput.SetEnabled(false);
            }
            else if (item.customType == "jump-tower")
            {
                rotationInput.SetAxisEnabled(0, false);
                rotationInput.SetAxisEnabled(2, false);
                scaleInput.SetEnabled(false);
            }

            foreach (var field in new[] { positionInput, rotationInput, scaleInput })

            {

                field.AddToClassList("property-field"); transformSection.Add(field); field.Changed += PreviewInspectorEdit;

                field.RegisterCallback<PointerUpEvent>(_ => root.schedule.Execute(() => Run(CommitInspectorEdit)), TrickleDown.TrickleDown);

                field.RegisterCallback<FocusOutEvent>(_ => root.schedule.Execute(() => Run(CommitInspectorEdit)));

            }

            if (item.positionLocked && (IsLockableZiplinePoint(item) || IsJumpTowerBalloon(item)))
            {
                positionInput.SetEnabled(false);
                positionInput.tooltip = L.T("#LOCK_CONTROL_POINT_POSITION_HELP");
            }
            else if(!string.IsNullOrEmpty(item.parentId))
            {
                var parent=snapshot.objects.Find(o=>o.id==item.parentId);
                positionInput.tooltip=L.F("#RELATIVE_PARENT_ARG0_MOVING_PARENT",parent?.displayName??L.T("#GROUP_34CA0E"));
            }

            inspector.Add(transformSection);
            objectNameInput.RegisterValueChangedCallback(_ => { inspectorDirty = true; root.schedule.Execute(() => Run(CommitInspectorEdit)); });

            objectNameInput.RegisterCallback<FocusOutEvent>(_ => root.schedule.Execute(() => Run(CommitInspectorEdit)));

            if (item.customType == "zipline" || item.customType == "zipline-endpoint" ||
                item.customType == "door" || item.customType == "curved-zipline" ||
                item.customType == "curved-zipline-point" || item.customType == "ziprail" ||
                item.customType == "ziprail-point" || item.customType == "loot-bin" ||
                item.customType == "jump-pad" || item.customType == "spawn-point" ||
                item.customType == "trigger" || item.customType == "jump-tower" ||
                item.customType == "jump-tower-component" ||
                item.customType == "weapon-rack" || item.customType == "respawn-heal" ||
                item.customType == "button" || item.customType == "speed-boost" ||
                item.customType == "bubble-shield" || item.customType == "camera-path" ||
                item.customType == "camera-path-point" || item.customType == "camera-path-target" ||
                item.customType == "animated-camera" || item.customType == "sound" ||
                item.customType == "sound-point" || item.customType == "location-pair" ||
                item.customType == "text-info-panel" || item.customType == "window-hint")
            {
                var remapSettings = InspectorSection(L.T("#REMAP_SETTINGS"), "remap-settings");
                if (item.customType == "zipline") BuildZiplineInspector(item, remapSettings);
                else if (item.customType == "zipline-endpoint") BuildZiplineEndpointInspector(item, remapSettings);
                else if (item.customType == "door") BuildDoorInspector(item, remapSettings);
                else if (item.customType == "curved-zipline") BuildCurvedZiplineInspector(item, remapSettings);
                else if (item.customType == "curved-zipline-point") BuildCurvedZiplinePointInspector(item, remapSettings);
                else if (item.customType == "ziprail") BuildZiprailInspector(item, remapSettings);
                else if (item.customType == "ziprail-point") BuildZiprailPointInspector(item, remapSettings);
                else if (item.customType == "loot-bin") BuildLootBinInspector(item, remapSettings);
                else if (item.customType == "jump-pad") BuildJumpPadInspector(item, remapSettings);
                else if (item.customType == "spawn-point") BuildSpawnPointInspector(item, remapSettings);
                else if (item.customType == "trigger") BuildTriggerInspector(item, remapSettings);
                else if (item.customType == "jump-tower") BuildJumpTowerInspector(item, remapSettings);
                else if (item.customType == "jump-tower-component")
                    BuildJumpTowerBalloonInspector(item, remapSettings);
                else if (item.customType == "weapon-rack") BuildWeaponRackInspector(item, remapSettings);
                else if (item.customType == "respawn-heal") BuildRespawnHealInspector(item, remapSettings);
                else if (item.customType == "button") BuildButtonInspector(item, remapSettings);
                else if (item.customType == "speed-boost") BuildSpeedBoostInspector(item, remapSettings);
                else if (item.customType == "bubble-shield") BuildBubbleShieldInspector(item, remapSettings);
                else if (item.customType == "camera-path") BuildCameraPathInspector(item, remapSettings);
                else if (item.customType == "camera-path-point" || item.customType == "camera-path-target")
                    BuildCameraPathNodeInspector(item, remapSettings);
                else if (item.customType == "animated-camera") BuildAnimatedCameraInspector(item, remapSettings);
                else if (item.customType == "sound") BuildSoundInspector(item, remapSettings);
                else if (item.customType == "sound-point") BuildSoundPointInspector(item, remapSettings);
                else if (item.customType == "location-pair") BuildLocationPairInspector(remapSettings);
                else if (item.customType == "text-info-panel") BuildTextInfoPanelInspector(item, remapSettings);
                else BuildWindowHintInspector(item, remapSettings);
                inspector.Add(remapSettings);
            }
            if (!item.isGroup && string.IsNullOrEmpty(item.customType))
            {
                var gameScripts = InspectorSection(L.T("#GAME_SCRIPTS"), "game-scripts");
                BuildClassicPropSettings(item, gameScripts);
                BuildScriptProperties(item, gameScripts);
                inspector.Add(gameScripts);
            }

            var information = InspectorSection(L.T("#COPYABLE_INFORMATION"), "inspector-information");
            worldPositionInfo = CopyableInspectorValue(L.T("#WORLD_ORIGIN"), "");
            information.Add(worldPositionInfo); RefreshWorldPositionInfo();
            string modelPath = string.IsNullOrWhiteSpace(item.gameModelPath) ? item.assetId : item.gameModelPath;
            string typeDetails = item.customType == "zipline" ? L.T("#CUSTOM_OBJECT_ZIPLINE") :
                item.customType == "door" ? L.T("#CUSTOM_OBJECT_DOOR") :
                item.customType == "curved-zipline" ? L.T("#CUSTOM_OBJECT_CURVED_ZIPLINE") :
                item.customType == "curved-zipline-point" ? L.T("#CURVED_ZIPLINE_CONTROL_POINT") :
                item.customType == "ziprail" ? L.T("#ZIPRAIL") :
                item.customType == "ziprail-point" ? L.T("#ZIPRAIL_CONTROL_POINT") :
                item.customType == "loot-bin" ? L.T("#CUSTOM_OBJECT_LOOT_BIN") :
                item.customType == "jump-pad" ? L.T("#CUSTOM_OBJECT_JUMP_PAD") :
                item.customType == "spawn-point" ? L.T("#CUSTOM_OBJECT_SPAWN_POINT") :
                item.customType == "trigger" ? L.T("#CUSTOM_OBJECT_TRIGGER") :
                item.customType == "jump-tower" ? L.T("#CUSTOM_OBJECT_JUMP_TOWER") :
                item.customType == "jump-tower-component" ? L.T("#JUMP_TOWER_COMPONENT") :
                item.customType == "weapon-rack" ? L.T("#CUSTOM_OBJECT_WEAPON_RACK") :
                item.customType == "respawn-heal" ? L.T("#CUSTOM_OBJECT_RESPAWN_HEAL") :
                item.customType == "button" ? L.T("#CUSTOM_OBJECT_BUTTON") :
                item.customType == "speed-boost" ? L.T("#CUSTOM_OBJECT_SPEED_BOOST") :
                item.customType == "speed-boost-component" ? L.T("#SPEED_BOOST_COMPONENT") :
                item.customType == "bubble-shield" ? L.T("#CUSTOM_OBJECT_BUBBLE_SHIELD") :
                item.customType == "camera-path" ? L.T("#CUSTOM_OBJECT_CAMERA_PATH") :
                item.customType == "camera-path-point" ? L.T("#CAMERA_PATH_POINT") :
                item.customType == "camera-path-target" ? L.T("#CAMERA_PATH_TARGET") :
                item.customType == "animated-camera" ? L.T("#CUSTOM_OBJECT_ANIMATED_CAMERA") :
                item.customType == "animated-camera-component" ? L.T("#ANIMATED_CAMERA_COMPONENT") :
                item.customType == "sound" ? L.T("#CUSTOM_OBJECT_SOUND") :
                item.customType == "sound-point" ? L.T("#SOUND_POLYLINE_POINT") :
                item.customType == "location-pair" ? L.T("#CUSTOM_OBJECT_LOCATION_PAIR") :
                item.customType == "text-info-panel" ? L.T("#CUSTOM_OBJECT_TEXT_INFO_PANEL") :
                item.customType == "window-hint" ? L.T("#CUSTOM_OBJECT_WINDOW_HINT") :
                item.customType == "zipline-endpoint" ? L.T("#ZIPLINE_ATTACHMENT_POINT") :
                item.isGroup ? L.T("#GROUP_CHILDREN_SHARE_TRANSFORM") : L.T("#MODEL");
            information.Add(CopyableInspectorValue(L.T("#OBJECT_TYPE"), typeDetails));
            if (!item.isGroup && !string.IsNullOrWhiteSpace(modelPath))
                information.Add(CopyableInspectorValue(L.T("#MODEL_PATH"), modelPath));
            information.Add(CopyableInspectorValue(L.T("#LOCAL_POSITION"), FormatVector(ApexDisplay.Position(WorldView.ToVector(item.position)))));
            information.Add(CopyableInspectorValue(L.T("#LOCAL_ANGLES"), FormatVector(ApexDisplay.Angles(WorldView.ToVector(item.rotation)))));
            information.Add(CopyableInspectorValue(L.T("#SCALE"), FormatNumber(ApexDisplay.Axes(WorldView.ToVector(item.scale)).x)));
            var detailBounds = world.GeometryBounds(item.id);
            if (detailBounds.HasValue)
                information.Add(CopyableInspectorValue(L.T("#DIMENSIONS"), FormatVector(ApexDisplay.Position(detailBounds.Value.size))));
            inspector.Add(information);

            var actions = new VisualElement(); actions.AddToClassList("inspector-actions"); inspector.Add(actions);
            var duplicate = Button("⧉  " + L.T("#DUPLICATE"), Duplicate); duplicate.tooltip = L.T("#DUPLICATE_SELECTION"); actions.Add(duplicate);
            var delete = Button("⌫  " + L.T("#DELETE"), Delete); delete.tooltip = L.T("#DELETE_SELECTION"); actions.Add(delete);

        }

        private void BuildSceneRootInspector()
        {
            inspectorEditingId=SceneRootInspectorId; inspectorOriginals=null; inspectorDirty=false;
            inspector.Add(Label(L.T("#WORLD_SPAWN"), "tool-selection"));
            positionInput=new VectorInput(L.T("#STARTING_APEX_POSITION_U"),WorldView.ToVector(snapshot.originOffset),1);
            positionInput.AddToClassList("property-field"); inspector.Add(positionInput);
            positionInput.Changed+=PreviewInspectorEdit;
            positionInput.RegisterCallback<PointerUpEvent>(_=>root.schedule.Execute(()=>Run(CommitInspectorEdit)),TrickleDown.TrickleDown);
            positionInput.RegisterCallback<FocusOutEvent>(_=>root.schedule.Execute(()=>Run(CommitInspectorEdit)));
            inspector.Add(Label(L.T("#OFFSET_APPLIED_GAME_EDITOR_KEEPS"),"note"));
            var showMainBsp = new Toggle(L.T("#SHOW_MAIN_BSP")) { value = assetLibrary.Settings.showMainBsp };
            showMainBsp.RegisterValueChangedCallback(change => SetMapReferenceOptions(showBsp: change.newValue));
            inspector.Add(showMainBsp);
            var showMprtModels = new Toggle(L.T("#SHOW_MPRT_MODELS")) { value = assetLibrary.Settings.showMprtModels };
            showMprtModels.RegisterValueChangedCallback(change => SetMapReferenceOptions(showMprt: change.newValue));
            inspector.Add(showMprtModels);
            AddMprtStreamingSettings(inspector);
            inspector.Add(Button(L.T("#RELOAD_MAP_REFERENCE"), () => RequestMapReferenceReload()));
        }

        private void PreviewInspectorEdit()

        {

            if (inspectorEditingId == null || positionInput == null) return;

            if(inspectorEditingId==SceneRootInspectorId) { inspectorDirty=true; return; }

            if (selectedIds.Count > 1) { PreviewMultipleInspector(); return; }
            var p = positionInput.value; var r = rotationInput.value; var s = scaleInput.value; inspectorDirty = true;
            if (!WorldView.ToData(p).IsFinite || !WorldView.ToData(r).IsFinite || !WorldView.ToData(s).IsFinite || s.x < .01f || s.y < .01f || s.z < .01f) return;

            if(inspectorLockedZiplineEnd==null)inspectorLockedZiplineEnd=CaptureLockedZiplineEnd(SelectionRoots());
            inspectorDirty = true; if(!world.SetLocalPreview(inspectorEditingId, p, r, s, inspectorLockedZiplineEnd))SetStatus(ApexCoordinates.LimitMessage);RefreshWorldPositionInfo();SyncJumpTowerHeightInput();UpdateGizmoVisual();

        }

        private void CommitInspectorEdit()
        {
            if (!inspectorDirty || inspectorEditingId == null || positionInput == null) return;
            if(inspectorEditingId==SceneRootInspectorId)
            {
                var offset=positionInput.value; inspectorDirty=false;
                try
                {
                    session.Edit(doc=>doc.originOffset=WorldView.ToData(offset)); snapshot=session.Snapshot();
                    undoButton?.SetEnabled(session.CanUndo); redoButton?.SetEnabled(session.CanRedo);
                    fileStatus.text=session.Revision==lastSavedRevision?L.T("#SAVED"):L.T("#UNSAVED_CHANGES");
                    positionInput.Set(WorldView.ToVector(snapshot.originOffset)); RefreshObjects();
                }
                catch { Refresh(); throw; }
                return;
            }
            if (selectedIds.Count > 1) { inspectorDirty = false; try { ValidateMultipleInspector(); if(inspectorOriginals != null) CommitSelectionPreview(inspectorOriginals); } finally { inspectorOriginals=null; Refresh(); } return; }
            string id = inspectorEditingId; var p = positionInput.value; var r = rotationInput.value; var s = scaleInput.value; string name = objectNameInput.value.Trim();
            var lockedEndLocal=inspectorLockedZiplineEnd==null?null:world.LocalPose(inspectorLockedZiplineEnd.Local.id);
            inspectorDirty = false;
            try
            {
                session.Edit(doc => { var item = doc.objects.Find(o => o.id == id); if (item == null) return;
                    item.displayName = name; if (!item.positionLocked) item.position = WorldView.ToData(p); item.rotation = WorldView.ToData(r); item.scale = WorldView.ToData(s);
                    if (IsJumpTowerBalloon(item)) SyncJumpTowerHeightFromBalloon(doc, item);
                    else if (item.customType == "jump-tower") NormalizeJumpTowerRotation(item);
                    if(lockedEndLocal!=null){var end=doc.objects.Find(o=>o.id==lockedEndLocal.id);if(end!=null)end.position=lockedEndLocal.position;} });
                snapshot = session.Snapshot(); world.Sync(snapshot, selectedId); world.HighlightSelection(SelectionRoots());
                undoButton?.SetEnabled(session.CanUndo); redoButton?.SetEnabled(session.CanRedo);
                fileStatus.text = session.Revision == lastSavedRevision ? L.T("#SAVED") : L.T("#UNSAVED_CHANGES");
                inspectorLockedZiplineEnd=null; RefreshObjects(); SyncInspectorValues();
                // Keep the live input elements: rebuilding here steals focus from the next field.
            }
            catch { inspectorLockedZiplineEnd=null; Refresh(); throw; }
        }

        private void FinishInspectorInteraction()
        {
            var focused=root?.panel?.focusController.focusedElement as VisualElement;
            for(var current=focused;current!=null&&current!=inspector;current=current.parent)
            {
                if(current is EndlessFloatField endless){endless.FinishDragging();break;}
                if(current is FloatField floating){((IValueField<float>)floating).StopDragging();break;}
                if(current is IntegerField integer){((IValueField<int>)integer).StopDragging();break;}
            }
            positionInput?.FinishDragging(); rotationInput?.FinishDragging(); scaleInput?.FinishDragging();
            viewport?.Focus();
            CommitInspectorEdit();
        }
        private void RefreshWorldPositionInfo() {
            if(worldPositionInfo!=null&&selectedId!=null)
            {
                string value=FormatVector(ApexDisplay.GamePosition(WorldView.ToVector(world.WorldPose(selectedId).position),snapshot.originOffset));
                worldPositionInfo.SetValueWithoutNotify(value);worldPositionInfo.tooltip=value;
            }
        }
        private void SyncInspectorValues()

        {

            if (selectedId == null || selectedId != inspectorEditingId || positionInput == null) return;

            if (selectedIds.Count > 1) return;
            RefreshWorldPositionInfo();var pose = world.LocalPose(selectedId); positionInput.Set(WorldView.ToVector(pose.position)); rotationInput.Set(WorldView.ToVector(pose.rotation)); scaleInput.Set(WorldView.ToVector(pose.scale));SyncJumpTowerHeightInput();

        }

        private void Duplicate()

        {

            CommitInspectorEdit(); if (selectedId == null) return;

            var roots = SelectionRoots().ToArray(); var next = new System.Collections.Generic.List<string>();
            session.Edit(doc => { foreach(var id in roots) next.Add(MapHierarchy.Duplicate(doc,id)); }); snapshot = session.Snapshot(); SetSelection(next); Refresh();

        }

        private void Delete()

        {

            CommitInspectorEdit(); if (selectedId == null) return;

            var ids = MapSelection.Branches(snapshot, SelectionRoots());
            foreach (var zipline in snapshot.objects.Where(item => item.customType == "curved-zipline" && !ids.Contains(item.id)))
            {
                var points = snapshot.objects.Where(item => item.parentId == zipline.id &&
                    item.customType == "curved-zipline-point").Select(item => item.id).ToArray();
                if (points.Count(id => !ids.Contains(id)) < 2)
                    ids.RemoveWhere(points.Contains);
            }
            foreach (var ziprail in snapshot.objects.Where(item => item.customType == "ziprail" && !ids.Contains(item.id)))
            {
                var points = snapshot.objects.Where(item => item.parentId == ziprail.id &&
                    item.customType == "ziprail-point").Select(item => item.id).ToArray();
                if (points.Count(id => !ids.Contains(id)) < 2)
                    ids.RemoveWhere(points.Contains);
            }
            session.Edit(doc => {
                doc.objects.RemoveAll(o => ids.Contains(o.id));
                foreach (var zipline in doc.objects.Where(item => item.customType == "curved-zipline").ToArray())
                    NormalizeCurvedZiplinePoints(doc, zipline.id);
                foreach (var ziprail in doc.objects.Where(item => item.customType == "ziprail").ToArray())
                    NormalizeZiprailPoints(doc, ziprail.id);
            });
            selectedId = null; Refresh();

        }

        private void NewMap()

        {

            ShowNewMapDialog(true);

        }

        private void Save()

        {

            CommitInspectorEdit(); files.Save(slot.value, snapshot); lastSavedRevision = session.Revision; RememberWorkspace(); RefreshProjectSelector(slot.value); Refresh();

            SetStatus(L.T("#SAVED_4D9F39") + files.PathFor(slot.value));

        }

        private void Load()

        {

            CommitInspectorEdit(); var loaded = files.Load(slot.value); // Validate before touching the current session.
            string activeTarget = assetLibrary.TargetGame;
            if (GameTargets.Normalize(loaded.gameTarget) != activeTarget)
                throw new InvalidOperationException(L.F("#PROJECT_CREATED_ARG0_BUT_ACTIVE", GameTargets.DisplayName(loaded.gameTarget), GameTargets.DisplayName(activeTarget)));
            OpenProject(slot.value, loaded, true);

            SetStatus(assetLibrary.Configured ? L.T("#MAP_OPENED_RELOADING_SELECTED_RPAKS") : L.T("#MAP_OPENED_CONFIGURE_GAME_SOURCES"));

        }

        private void Run(Action action)

        {

            try { action(); }

            catch (Exception ex) { SetStatus(ex.Message); Debug.LogException(ex); }

        }

        private void SetStatus(string message) { status.text = message; status.tooltip = message; }

        private System.Collections.IEnumerator SmokeCheck()

        {

            for (int i = 0; i < 8; i++) yield return null;

            bool success = false;

            try

            {

                if (viewport.worldBound.width < 200 || world.Camera.pixelWidth < 200)

                    throw new Exception("Viewport layout is invalid.");

                Select(snapshot.objects[0].id);

                int countBefore = snapshot.objects.Count;

                Duplicate();

                if (snapshot.objects.Count != countBefore + 1 || world.LoadedModelCount != 3)

                    throw new Exception("Duplicate failed or shared model resources were duplicated.");

                session.Undo(); Refresh();

                if (snapshot.objects.Count != countBefore) throw new Exception("Undo failed in player.");

                var smokeFiles = new MapFiles(Path.Combine(Application.temporaryCachePath, "SmokeChecks"), codec);

                smokeFiles.Save("player", snapshot);

                var loaded = smokeFiles.Load("player");

                if (codec.Encode(snapshot) != codec.Encode(loaded)) throw new Exception("Player save round trip failed.");

                var screenCentre = world.Camera.pixelRect.center;

                if (!world.Placement(screenCentre, catalog.Entries[0], true, out _))

                    throw new Exception("Placement ray did not hit the construction surface.");

                Select(snapshot.objects[0].id);

                if (string.IsNullOrEmpty(world.Pick(world.Camera.WorldToScreenPoint(new Vector3(1, .25f, -1)))))

                    throw new Exception("Object selection ray failed.");

                SetStatus(L.T("#WORKSPACE_READY_SELECTION_DUPLICATION_UNDO"));

                success = true;

            }

            catch (Exception exception) { Debug.LogException(exception); }

            for (int i = 0; i < 3; i++) yield return null;

            if (success)

            {

                yield return new WaitForEndOfFrame();

                try

                {

                    var capture = ScreenCapture.CaptureScreenshotAsTexture();

                    if (capture == null) throw new Exception(L.T("#CAPTURE_REQUIRES_VISIBLE_WINDOW"));

                    var path = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "workspace-preview.png"));

                    try { File.WriteAllBytes(path, capture.EncodeToPNG()); }

                    finally { Destroy(capture); }

                    Debug.Log("REMAP_PLAYER_SMOKE_OK: " + path);

                }

                catch (Exception exception) { success = false; Debug.LogException(exception); }

            }

            Application.Quit(success ? 0 : 1);

        }

        private void OnApplicationPause(bool paused) { if (paused) SaveWorkspaceRecovery(); }

        private void OnApplicationQuit() { SaveWorkspaceRecovery(); }

        private void OnDestroy() { SaveWorkspaceRecovery(); backgroundStopped = true; mapReferenceCancellation?.Cancel(); assetLibrary?.Dispose(); world?.Dispose(); if (currentThumbnail != null) Destroy(currentThumbnail); foreach (var thumbnail in pageThumbnails) Destroy(thumbnail); }

    }

}
