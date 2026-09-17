using System;
using System.Collections.Generic;
using System.Linq;
using ReMap.Standalone.Core;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
namespace ReMap.Standalone
{
    public sealed partial class ReMapApp
    {
        private const string AutoCollapseOtherFoldersPreference = "ReMap.Hierarchy.AutoCollapseOtherFolders.v1";
        private static string AutoCollapseOtherFoldersPreferenceKey => AutoCollapseOtherFoldersPreference + (Environment.GetCommandLineArgs().AnySmokeFlag() ? ".qa" : "");
        private bool autoCollapseOtherFolders;
        private readonly HashSet<string> collapsedGroups = new HashSet<string>();
        private readonly Dictionary<VisualElement, string> folderDropTargets = new Dictionary<VisualElement, string>();
        private readonly Dictionary<string, VisualElement> hierarchyRows = new Dictionary<string, VisualElement>();
        private readonly List<string> visibleHierarchyIds = new List<string>();
        private Button childFolderButton, foldAllFoldersButton;
        private Label hierarchyPath;
        private VisualElement hierarchyRootRow, hierarchyMenu, hierarchyDropRow;
        private TextField hierarchyName;
        private enum HierarchyDropMode { None, Inside, Before, After }
        private HierarchyDropMode hierarchyDropMode;
        private string hierarchyDropBeforeId, hierarchyDropTargetId;
        private string renamingHierarchyId, hoverFolderId;
        private bool rebuildingHierarchy, hierarchyScrollQueued;
        private MapDocument hierarchyLookupDocument;
        private Dictionary<string, MapObject> hierarchyLookup;
        private List<MapObject> hierarchyPrevious;
        private readonly HashSet<string> hierarchyPreviousCollapsed = new HashSet<string>();
        private string hierarchyPreviousRename;
        private int hierarchyWindowStart = -1, hierarchyWindowEnd;
        private Dictionary<string, MapObject> HierarchyLookup()
        {
            if (!ReferenceEquals(hierarchyLookupDocument, snapshot)) { hierarchyLookupDocument = snapshot; hierarchyLookup = snapshot.objects.ToDictionary(o => o.id); }
            return hierarchyLookup;
        }
        private static bool HiddenHierarchyObject(MapObject item) =>
            item != null && (item.customType == "zipline-component" || item.customType == "door-component" ||
                item.customType == "curved-zipline-component" || item.customType == "ziprail-component" ||
                (item.customType == "zipline-endpoint" && item.customRole == "start"));
        private static int VisibleChildCount(IEnumerable<MapObject> items) =>
            items == null ? 0 : items.Count(item => !HiddenHierarchyObject(item));

        private bool HierarchyContentUnchanged()
        {
            if (hierarchyPrevious == null || hierarchyPrevious.Count != snapshot.objects.Count || hierarchyPreviousRename != renamingHierarchyId || !hierarchyPreviousCollapsed.SetEquals(collapsedGroups)) return false;
            for (int i = 0; i < hierarchyPrevious.Count; i++) {
                var a = hierarchyPrevious[i]; var b = snapshot.objects[i];
                if (a.id != b.id || a.parentId != b.parentId || a.displayName != b.displayName || a.isGroup != b.isGroup || a.disabled != b.disabled || a.customType != b.customType || a.customRole != b.customRole) return false;
            }
            return true;
        }
        private void QueueHierarchyViewport()
        {
            if (rebuildingHierarchy || hierarchyScrollQueued || snapshot == null || snapshot.objects.Count <= 200) return;
            hierarchyScrollQueued = true;
            objectList.schedule.Execute(() => {
                hierarchyScrollQueued = false;
                if (renamingHierarchyId != null && hierarchyName != null) { Run(() => CommitHierarchyName(renamingHierarchyId, hierarchyName.value)); return; }
                RefreshObjects();
            });
        }
        private float hoverFolderSince;
        private CatalogEntry dragEntry;
        private GameAssetRecord dragRecord;
        private string dragObjectId;
        private Vector2 libraryDragStart;
        private bool dragCandidate, libraryDragging, suppressCardClick, deferAutoCollapseForDrag, preserveOpenFoldersForSceneSelection;
        private Label dragLabel;

        private void BuildHierarchy(VisualElement panel)
        {
            var toolbar = new VisualElement(); toolbar.AddToClassList("tree-toolbar"); panel.Add(toolbar);
            var create = Button(L.T("#GROUP_861228"), () => CreateGroupAt("")); create.tooltip = L.T("#CREATE_GROUP_SCENE_ROOT"); toolbar.Add(create);
            childFolderButton = Button(L.T("#SUBGROUP"), () => CreateGroupAt(selectedId)); toolbar.Add(childFolderButton);
            foldAllFoldersButton = Button("−", ToggleAllFolders); foldAllFoldersButton.AddToClassList("tree-fold-all"); toolbar.Add(foldAllFoldersButton);
            hierarchyPath = Label(L.T("#WORLD_SPAWN"), "tree-path"); panel.Add(hierarchyPath);
            objectList = new ScrollView(ScrollViewMode.VerticalAndHorizontal); objectList.AddToClassList("objects"); panel.Add(objectList);
            objectList.verticalScroller.valueChanged += _ => QueueHierarchyViewport();
            objectList.contentViewport.RegisterCallback<GeometryChangedEvent>(_ => QueueHierarchyViewport());
            objectList.RegisterCallback<KeyDownEvent>(HierarchyKeys, TrickleDown.TrickleDown);
            objectList.RegisterCallback<PointerDownEvent>(e => {
                if (e.button == 1 && e.target == objectList.contentContainer) { ShowHierarchyMenu(null, e.position); e.StopPropagation(); }
            });
            var hint = Label(L.T("#DRAG_ORGANIZE_RIGHT_CLICK_ACTIONS"), "tree-hint"); panel.Add(hint);
            hierarchyMenu = new VisualElement(); hierarchyMenu.AddToClassList("tree-menu"); hierarchyMenu.style.display = DisplayStyle.None; root.Add(hierarchyMenu);
        }
        private void BuildDragUI()
        {
            dragLabel = Label("", "drag-label"); dragLabel.pickingMode = PickingMode.Ignore; root.Add(dragLabel); dragLabel.style.display = DisplayStyle.None;
            root.RegisterCallback<PointerDownEvent>(e => {
                if (hierarchyMenu.style.display.value != DisplayStyle.None && !hierarchyMenu.worldBound.Contains(e.position)) hierarchyMenu.style.display = DisplayStyle.None;
                if (viewport.worldBound.Contains(e.position) && e.button != 0) { CommitInspectorEdit(); viewport.Focus(); }
            }, TrickleDown.TrickleDown);
            root.RegisterCallback<PointerUpEvent>(e => { if (libraryDragging) e.StopPropagation(); }, TrickleDown.TrickleDown);
            root.RegisterCallback<KeyDownEvent>(e => { if (e.keyCode == KeyCode.Escape && hierarchyMenu.style.display.value != DisplayStyle.None) { hierarchyMenu.style.display = DisplayStyle.None; e.StopPropagation(); } if (Mouse.current?.rightButton.isPressed == true && CameraPointerInside()) e.StopPropagation(); }, TrickleDown.TrickleDown);
        }
        private bool CameraPointerInside()
        {
            if (Mouse.current == null || root.panel == null || SettingsOpen || IndexingOpen || loadingOverlay?.style.display.value == DisplayStyle.Flex) return false;
            var point = Mouse.current.position.ReadValue();
            return viewport.worldBound.Contains(RuntimePanelUtils.ScreenToPanel(root.panel, new Vector2(point.x, Screen.height - point.y)));
        }
        private string HierarchyPath(string id)
        {
            var names = new List<string>(); var byId = HierarchyLookup();
            while (!string.IsNullOrEmpty(id) && byId.TryGetValue(id, out var item)) { names.Add(item.displayName); id = item.parentId; }
            names.Reverse(); return L.T("#WORLD_SPAWN") + (names.Count == 0 ? "" : " / " + string.Join(" / ", names));
        }
        private void RefreshObjects(bool force = false)
        {
            if (objectList == null || snapshot == null) return;
            bool virtualized = snapshot.objects.Count > 200;
            int first = virtualized ? Math.Max(0, (int)((objectList.scrollOffset.y - 24) / 23) - 8) : 0;
            int last = virtualized ? first + Math.Max(20, (int)(objectList.contentViewport.worldBound.height / 23) + 18) : int.MaxValue;
            if (!force && first == hierarchyWindowStart && last == hierarchyWindowEnd && HierarchyContentUnchanged()) { UpdateHierarchySelection(); return; }
            hierarchyWindowStart = first; hierarchyWindowEnd = last;
            hierarchyPrevious = snapshot.objects; hierarchyPreviousRename = renamingHierarchyId;
            hierarchyPreviousCollapsed.Clear(); hierarchyPreviousCollapsed.UnionWith(collapsedGroups);
            rebuildingHierarchy = true;
            try
            {
                var scroll = objectList.scrollOffset;
                objectList.Clear(); folderDropTargets.Clear(); hierarchyRows.Clear(); visibleHierarchyIds.Clear(); hierarchyDropRow = null;
                var selected = snapshot.objects.Find(o => o.id == selectedId);
                childFolderButton.SetEnabled(selected != null);
                childFolderButton.tooltip = selected != null ? L.T("#CREATE_INSIDE") + selected.displayName + " »" : L.T("#SELECT_GROUP_CREATE_SUBGROUP");
                UpdateFoldAllFoldersButton();
                hierarchyPath.text = HierarchyPath(selectedId); hierarchyPath.tooltip = hierarchyPath.text;
                hierarchyRootRow = new VisualElement { focusable = true }; hierarchyRootRow.AddToClassList("tree-row"); hierarchyRootRow.AddToClassList("tree-root"); objectList.Add(hierarchyRootRow);
                hierarchyRootRow.Add(Label(L.T("#WORLD_SPAWN"), "tree-name")); hierarchyRootRow.Add(Label(snapshot.objects.Count(item => !HiddenHierarchyObject(item)).ToString(), "tree-count")); folderDropTargets[hierarchyRootRow] = "";
                hierarchyRootRow.EnableInClassList("selected", sceneRootSelected);
                hierarchyRootRow.tooltip = L.T("#DROP_HERE_MOVE_OBJECT_GROUP");
                hierarchyRootRow.RegisterCallback<PointerDownEvent>(e => {
                    if (e.button == 1) { ShowHierarchyMenu(null, e.position); e.StopPropagation(); }
                    else if (e.button == 0) { SelectSceneRoot(); hierarchyRootRow.Focus(); }
                });
                // Children are ordered like the document; each visual row occupies the entire tree width.
                var children = snapshot.objects.GroupBy(o => o.parentId ?? "").ToDictionary(g => g.Key, g => g.ToArray());
                int deepest = 0;
                void Spacer(int rows) { if (rows <= 0) return; var spacer = new VisualElement { pickingMode = PickingMode.Ignore }; spacer.style.height = rows * 23; spacer.style.flexShrink = 0; objectList.Add(spacer); }
                // Spacer heights preserve the full scroll range; only the visible rows and a small margin have UI elements.
                var above = new VisualElement { pickingMode = PickingMode.Ignore }; above.style.flexShrink = 0; objectList.Add(above);
                void AddChildren(string parent, int depth, bool parentEnabled)
                {
                    if (!children.TryGetValue(parent, out var items)) return;
                    foreach (var item in items)
                    {
                        if (HiddenHierarchyObject(item)) continue;
                        string id = item.id; deepest = Math.Max(deepest, depth); bool active = parentEnabled && !item.disabled;
                        bool hasChildren = children.TryGetValue(id, out var nested) && VisibleChildCount(nested) > 0;
                        int logicalIndex = visibleHierarchyIds.Count; visibleHierarchyIds.Add(id);
                        if (logicalIndex < first || logicalIndex >= last) { if (hasChildren && !collapsedGroups.Contains(id)) AddChildren(id, depth + 1, active); continue; }
                        var row = new VisualElement { focusable = true, name = "tree-" + id }; row.AddToClassList("tree-row");
                        row.EnableInClassList("selected", selectedIds.Contains(id)); row.EnableInClassList("tree-inactive", !active);
                        row.tooltip = HierarchyPath(id) + (!parentEnabled ? L.T("#DISABLED_PARENT_GROUP") : "");
                        objectList.Add(row); hierarchyRows[id] = row;
                        for (int i = 0; i < depth; i++) { var guide = new VisualElement(); guide.AddToClassList("tree-guide"); guide.pickingMode = PickingMode.Ignore; row.Add(guide); }
                        var fold = new Button(() => { ToggleFolder(id); }); fold.AddToClassList("tree-fold");
                        if (hasChildren)
                        {
                            bool closed = collapsedGroups.Contains(id);
                            var arrow = new VisualElement { pickingMode = PickingMode.Ignore }; arrow.style.width = 10; arrow.style.height = 10; arrow.style.flexShrink = 0;
                            arrow.generateVisualContent += context => {
                                var painter = context.painter2D; painter.fillColor = new Color(.66f, .79f, .82f); painter.BeginPath();
                                painter.MoveTo(closed ? new Vector2(3, 1) : new Vector2(1, 3));
                                painter.LineTo(closed ? new Vector2(8, 5) : new Vector2(9, 3));
                                painter.LineTo(closed ? new Vector2(3, 9) : new Vector2(5, 8)); painter.ClosePath(); painter.Fill();
                            }; fold.Add(arrow);
                        }
                        if (!hasChildren) fold.style.visibility = Visibility.Hidden;
                        fold.tooltip = L.T("#EXPAND_COLLAPSE_GROUP"); row.Add(fold);
                        var icon = new VisualElement(); icon.AddToClassList(item.isGroup ? "tree-folder-icon" : hasChildren ? "tree-model-folder-icon" : "tree-object-icon"); icon.pickingMode = PickingMode.Ignore; row.Add(icon);
                        if (renamingHierarchyId == id)
                        {
                            hierarchyName = new TextField { value = item.displayName }; hierarchyName.AddToClassList("tree-rename"); row.Add(hierarchyName);
                            var field = hierarchyName;
                            field.RegisterCallback<KeyDownEvent>(e => {
                                if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) { Run(() => CommitHierarchyName(id, field.value)); e.StopPropagation(); }
                                if (e.keyCode == KeyCode.Escape) { renamingHierarchyId = null; RefreshObjects(); FocusHierarchy(id); e.StopPropagation(); }
                            }, TrickleDown.TrickleDown);
                            field.RegisterCallback<FocusOutEvent>(_ => { if (!rebuildingHierarchy && renamingHierarchyId == id) Run(() => CommitHierarchyName(id, field.value)); });
                            field.schedule.Execute(() => { if (renamingHierarchyId == id) { field.Focus(); field.SelectAll(); } });
                        }
                        else { var label = Label(item.displayName, "tree-name"); label.pickingMode = PickingMode.Ignore; row.Add(label); }
                        if (hasChildren) { var countLabel = Label(VisibleChildCount(nested).ToString(), "tree-count"); countLabel.pickingMode = PickingMode.Ignore; row.Add(countLabel); }
                        var enabled = new Toggle { value = !item.disabled, tooltip = L.T("#INCLUDE_ITEM_CHILDREN_SCENE_GAME") }; enabled.AddToClassList("tree-enabled"); row.Add(enabled);
                        enabled.RegisterValueChangedCallback(e => Run(() => { CommitInspectorEdit(); session.Edit(d => d.objects.Find(o => o.id == id).disabled = !e.newValue); Refresh(); }));
                        row.RegisterCallback<PointerDownEvent>(e => {
                            if (e.button == 1) { ShowHierarchyMenu(id, e.position); e.StopPropagation(); return; }
                            if (e.button != 0 || IsHierarchyControl(e.target as VisualElement, row)) return;
                            suppressCardClick = false;
                            // Keep every manually opened destination visible until this click is known to be a click or a drag.
                            // Otherwise selecting the source closes the destination before it can receive the drop.
                            dragEntry = null; dragRecord = null; dragObjectId = id; libraryDragStart = e.position;
                            dragCandidate = true; deferAutoCollapseForDrag = autoCollapseOtherFolders;
                            // Selection does not rebuild the row underneath the pressed pointer.
                            if (!selectedIds.Contains(id) || e.ctrlKey || e.shiftKey) SelectModified(id, e.ctrlKey, e.shiftKey, true);
                            else { CommitInspectorEdit(); CancelPlacement(); activeSelectionId=id; RefreshInspector(); UpdateHierarchySelection(); }
                            FocusHierarchy(id);
                            if (e.clickCount == 2 && hasChildren)
                            {
                                deferAutoCollapseForDrag = false; dragCandidate = false;
                                ToggleFolder(id); e.StopPropagation(); return;
                            }
                        });
                        folderDropTargets[row] = id;
                        if (hasChildren && !collapsedGroups.Contains(id)) AddChildren(id, depth + 1, active);
                    }
                }
                AddChildren("", 0, true);
                above.style.height = Math.Min(first, visibleHierarchyIds.Count) * 23;
                Spacer(Math.Max(0, visibleHierarchyIds.Count - last));
                if (!snapshot.objects.Any(item => !HiddenHierarchyObject(item))) { var empty = Label(L.T("#NO_OBJECTS_CREATE_GROUP_DROP"), "tree-empty"); objectList.Add(empty); folderDropTargets[empty] = ""; }
                objectList.contentContainer.style.minWidth = Math.Max(210, 170 + deepest * 16);
                objectList.scrollOffset = scroll;
            }
            finally { rebuildingHierarchy = false; }
        }
        private static bool IsHierarchyControl(VisualElement element, VisualElement row)
        {
            for (var current = element; current != null && current != row; current = current.parent)
                if (current is Button || current is Toggle || current is TextField) return true;
            return false;
        }
        private void UpdateHierarchySelection()
        {
            hierarchyRootRow?.EnableInClassList("selected", sceneRootSelected);
            foreach (var row in hierarchyRows) row.Value.EnableInClassList("selected", selectedIds.Contains(row.Key));
            hierarchyPath.text = HierarchyPath(selectedId); hierarchyPath.tooltip = hierarchyPath.text;
            childFolderButton.SetEnabled(snapshot.objects.Find(o => o.id == selectedId) != null);
        }
        private void ApplyAutoCollapseHierarchy()
        {
            if(!autoCollapseOtherFolders||snapshot==null||deferAutoCollapseForDrag)return;
            var containers=new HashSet<string>(snapshot.objects.Where(item=>!HiddenHierarchyObject(item)&&!string.IsNullOrEmpty(item.parentId)).Select(item=>item.parentId));
            var open=MapHierarchy.SelectionContainers(snapshot,selectedIds);
            collapsedGroups.RemoveWhere(id=>!containers.Contains(id));
            foreach(var id in containers)
            {
                if(open.Contains(id))collapsedGroups.Remove(id);
                else if(!preserveOpenFoldersForSceneSelection)collapsedGroups.Add(id);
            }
        }
        private void RevealHierarchy(string id)
        {
            var current = session.Snapshot();
            if(autoCollapseOtherFolders){ApplyAutoCollapseHierarchy();return;}
            var item = current.objects.Find(o => o.id == id);
            while (item != null && !string.IsNullOrEmpty(item.parentId)) { collapsedGroups.Remove(item.parentId); item = current.objects.Find(o => o.id == item.parentId); }
        }
        private int hierarchyFocusRequest;
        private void FocusHierarchy(string id, bool focusRow = true)
        {
            int request = ++hierarchyFocusRequest;
            void FocusRow(int retry)
            {
                if (request != hierarchyFocusRequest || objectList.panel == null) return;
                if (id == null) { hierarchyRootRow?.Focus(); return; }
                if (!hierarchyRows.ContainsKey(id) && snapshot.objects.Count > 200) {
                    int index = visibleHierarchyIds.IndexOf(id);
                    if (index < 0) return;
                    objectList.scrollOffset = new Vector2(objectList.scrollOffset.x, 24 + index * 23);
                    RefreshObjects(true);
                }
                if (hierarchyRows.TryGetValue(id, out var row)) { if(focusRow)row.Focus(); objectList.ScrollTo(row); }
                // Expanding a collapsed tree updates its scroll range during the following UI layout.
                // Cancel superseded requests so an older folder focus cannot pull a later selection back up.
                if (retry < 3 && (retry == 0 || !hierarchyRows.ContainsKey(id))) objectList.schedule.Execute(() => FocusRow(retry + 1)).StartingIn(16);
            }
            FocusRow(0);
        }
        private void ToggleFolder(string id)
        {
            if (!collapsedGroups.Add(id)) collapsedGroups.Remove(id);
            RefreshObjects(); FocusHierarchy(id);
        }
        private HashSet<string> HierarchyContainers()
        {
            if (snapshot == null) return new HashSet<string>();
            var visibleIds = snapshot.objects.Where(item => !HiddenHierarchyObject(item)).Select(item => item.id).ToHashSet();
            return snapshot.objects.Where(item => !HiddenHierarchyObject(item) && !string.IsNullOrEmpty(item.parentId) && visibleIds.Contains(item.parentId))
                .Select(item => item.parentId).ToHashSet();
        }
        private void UpdateFoldAllFoldersButton()
        {
            if (foldAllFoldersButton == null) return;
            var containers = HierarchyContainers();
            bool canCollapse = containers.Any(id => !collapsedGroups.Contains(id));
            foldAllFoldersButton.text = canCollapse ? "−" : "+";
            foldAllFoldersButton.tooltip = L.T(canCollapse ? "#COLLAPSE_ALL_FOLDERS" : "#EXPAND_ALL_FOLDERS");
            foldAllFoldersButton.SetEnabled(containers.Count > 0);
        }
        private void ToggleAllFolders()
        {
            var containers = HierarchyContainers();
            if (containers.Any(id => !collapsedGroups.Contains(id))) collapsedGroups.UnionWith(containers);
            else collapsedGroups.Clear();
            hierarchyWindowStart = -1;
            RefreshObjects(true);
        }
        private void CreateGroup()
        {
            var selected = snapshot.objects.Find(o => o.id == selectedId);
            CreateGroupAt(selected?.id ?? "");
        }
        private void CreateGroupAt(string parent)
        {
            CommitInspectorEdit(); parent = parent ?? "";
            if (parent != "" && snapshot.objects.Find(o => o.id == parent) == null) throw new InvalidOperationException(L.T("#SELECT_PARENT_GROUP"));
            var names = new HashSet<string>(snapshot.objects.Where(o => (o.parentId ?? "") == parent).Select(o => o.displayName), StringComparer.OrdinalIgnoreCase);
            string name = L.T("#GROUP_34CA0E"); int suffix = 2; while (names.Contains(name)) name = L.T("#GROUP_B5548E") + suffix++ + ")";
            var group = new MapObject { assetId = "group:empty", displayName = name, isGroup = true, parentId = parent };
            session.Edit(doc => doc.objects.Add(group)); selectedId = group.id; renamingHierarchyId=group.id; RevealHierarchy(group.id); Refresh(); FocusHierarchy(group.id,false);
        }
        private void BeginHierarchyRename(string id)
        {
            if (string.IsNullOrEmpty(id)) return;
            CommitInspectorEdit(); selectedId = id; RevealHierarchy(id); renamingHierarchyId = id; RefreshInspector(); RefreshObjects();
        }
        private void CommitHierarchyName(string id, string name)
        {
            name = name.Trim(); if (name.Length == 0 || name.Length > 128) { SetStatus(L.T("#NAME_CONTAIN_1_128_CHARACTERS")); hierarchyName?.Focus(); return; }
            renamingHierarchyId = null;
            if (snapshot.objects.Find(o => o.id == id)?.displayName != name) session.Edit(doc => doc.objects.Find(o => o.id == id).displayName = name);
            Refresh(); FocusHierarchy(id);
        }
        private void ShowHierarchyMenu(string id, Vector2 point)
        {
            var contextSelectionRoots = SelectionRoots().ToArray();
            if (id != null) { if(!selectedIds.Contains(id)) Select(id); else { CommitInspectorEdit(); activeSelectionId=id; RefreshInspector(); UpdateHierarchySelection(); } }
            hierarchyMenu.Clear();
            void Action(string title, System.Action action) { var button = Button(title, () => { hierarchyMenu.style.display = DisplayStyle.None; action(); }); hierarchyMenu.Add(button); }
            var item = snapshot.objects.Find(o => o.id == id);
            if (item == null)
            {
                Action(L.T("#CREATE_ROOT_GROUP"), () => CreateGroupAt(""));
                Action((assetLibrary.Settings.showMainBsp ? "✓ " : "") + L.T("#SHOW_MAIN_BSP"),
                    () => SetMapReferenceOptions(showBsp: !assetLibrary.Settings.showMainBsp));
                Action((assetLibrary.Settings.showMprtModels ? "✓ " : "") + L.T("#SHOW_MPRT_MODELS"),
                    () => SetMapReferenceOptions(showMprt: !assetLibrary.Settings.showMprtModels));
                Action(L.T("#RELOAD_MAP_REFERENCE"), () => RequestMapReferenceReload());
            }
            else
            {
                if (string.IsNullOrEmpty(item.customType))
                {
                    Action(L.T("#CREATE_SUBGROUP"), () => CreateGroupAt(id));
                    if (item.isGroup)
                    {
                        var pivotReferences = ConstructionTools.PivotReferences(snapshot, id, contextSelectionRoots);
                        Action(L.T("#PIVOT_FROM_SELECTION_BOTTOM_CENTER"), () => RepositionGroupPivot(id, GroupPivotAnchor.BottomCenter, pivotReferences));
                        Action(L.T("#PIVOT_FROM_SELECTION_CENTER"), () => RepositionGroupPivot(id, GroupPivotAnchor.Center, pivotReferences));
                        Action(L.T("#PIVOT_FROM_SELECTION_TOP_CENTER"), () => RepositionGroupPivot(id, GroupPivotAnchor.TopCenter, pivotReferences));
                    }
                }
                Action(L.T("#GROUP_CTRL_G"), GroupSelection);
                Action(L.T("#RENAME_F2"), () => BeginHierarchyRename(id));
                if (!string.IsNullOrEmpty(item.parentId)) Action(L.T("#MOVE_ROOT"), () => ReparentObject(id, ""));
                Action(item.disabled ? L.T("#ENABLE") : L.T("#DISABLE"), () => SetSelectionEnabled(item.disabled));
                Action(L.T("#DUPLICATE"), () => { selectedId = id; Duplicate(); RevealHierarchy(selectedId); RefreshObjects(); FocusHierarchy(selectedId); });
                Action(L.T("#CONSTRUCTION_TOOLS"), () => ShowConstructionTools(true));
                Action(L.T("#DELETE"), () => { selectedId = id; Delete(); });
            }
            hierarchyMenu.style.left = Mathf.Clamp(point.x, 0, Math.Max(0, root.worldBound.width - 240));
            hierarchyMenu.style.top = Mathf.Clamp(point.y, 0, Math.Max(0, root.worldBound.height - (item?.isGroup == true ? 380 : 250)));
            hierarchyMenu.style.display = DisplayStyle.Flex; hierarchyMenu.BringToFront();
        }
        private void HierarchyKeys(KeyDownEvent e)
        {
            if (renamingHierarchyId != null) return;
            var item = snapshot.objects.Find(o => o.id == selectedId); string next = null;
            switch (e.keyCode)
            {
                case KeyCode.RightArrow:
                    if (item != null && snapshot.objects.Any(o => o.parentId == item.id && !HiddenHierarchyObject(o))) { if (collapsedGroups.Remove(item.id)) { RefreshObjects(); FocusHierarchy(item.id); } else next = snapshot.objects.FirstOrDefault(o => o.parentId == item.id && !HiddenHierarchyObject(o))?.id; } break;
                case KeyCode.LeftArrow:
                    if (item != null && snapshot.objects.Any(o => o.parentId == item.id && !HiddenHierarchyObject(o)) && !collapsedGroups.Contains(item.id)) { collapsedGroups.Add(item.id); RefreshObjects(); FocusHierarchy(item.id); }
                    else next = item?.parentId; break;
                case KeyCode.DownArrow: case KeyCode.UpArrow:
                    int index = visibleHierarchyIds.IndexOf(selectedId); index = Mathf.Clamp(index + (e.keyCode == KeyCode.DownArrow ? 1 : -1), 0, visibleHierarchyIds.Count - 1);
                    if (index >= 0 && index < visibleHierarchyIds.Count) next = visibleHierarchyIds[index]; break;
                default: return;
            }
            if (next != null) { SelectModified(string.IsNullOrEmpty(next) ? null : next, e.ctrlKey, e.shiftKey, true); FocusHierarchy(next); }
            e.StopPropagation();
        }
        private void ReparentObject(string id, string parent, string beforeSiblingId = null)
        {
            if(selectedIds.Contains(id) && selectedIds.Count>1) { ReparentSelection(parent, beforeSiblingId); return; }
            CommitInspectorEdit(); var check = snapshot.Copy(); MapHierarchy.Reorder(check, id, parent, beforeSiblingId);
            var current = snapshot.objects.Find(o => o.id == id);
            bool parentChanged = (current.parentId ?? "") != (parent ?? "");
            var pose = parentChanged ? world.ReparentPose(id, parent) : default;
            session.Edit(doc => {
                MapHierarchy.Reorder(doc, id, parent, beforeSiblingId);
                if (parentChanged) { var item = doc.objects.Find(o => o.id == id); item.position = pose.position; item.rotation = pose.rotation; item.scale = pose.scale; }
            });
            selectedId = id; RevealHierarchy(id); Refresh(); FocusHierarchy(id);
        }
        private void RegisterDragSource(VisualElement element, CatalogEntry entry, GameAssetRecord record, string id = null)
        {
            element.RegisterCallback<PointerDownEvent>(e => {
                if (e.button != 0) return;
                if (e.clickCount == 2 && id == null)
                {
                    EndLibraryDrag(); suppressCardClick = true;
                    if (record != null) _ = RequestModelPlacement(record);
                    else if (entry != null) { CancelPlacement(); BeginPlacement(entry); }
                    e.StopImmediatePropagation(); return;
                }
                suppressCardClick = false; dragEntry = entry; dragRecord = record; dragObjectId = id; libraryDragStart = e.position; dragCandidate = true;
            }, TrickleDown.TrickleDown);
        }
        private MapDocument checkedDropSnapshot;
        private string checkedDropParent, checkedDropBefore, checkedDropId;
        private bool checkedDropValid;
        private void ClearHierarchyDropStyle()
        {
            hierarchyDropRow?.RemoveFromClassList("tree-drop");
            hierarchyDropRow?.RemoveFromClassList("tree-drop-before");
            hierarchyDropRow?.RemoveFromClassList("tree-drop-after");
            hierarchyDropRow?.RemoveFromClassList("tree-drop-invalid");
            hierarchyDropRow = null;
        }
        private bool ValidHierarchyDrop(string parent) => ValidHierarchyDrop(parent, null);
        private bool ValidHierarchyDrop(string parent, string beforeSiblingId)
        {
            if (parent == null) return false;
            if (dragObjectId == null) return true;
            if (checkedDropSnapshot == snapshot && checkedDropParent == parent && checkedDropBefore == beforeSiblingId && checkedDropId == dragObjectId) return checkedDropValid;
            checkedDropSnapshot = snapshot; checkedDropParent = parent; checkedDropBefore = beforeSiblingId; checkedDropId = dragObjectId;
            try
            {
                var roots = selectedIds.Contains(dragObjectId) ? SelectionRoots() : new List<string>{dragObjectId};
                if (beforeSiblingId != null && roots.Contains(beforeSiblingId)) return checkedDropValid = false;
                var copy = snapshot.Copy(); foreach(var id in roots) MapHierarchy.Reorder(copy, id, parent, beforeSiblingId); checkedDropValid = true;
            }
            catch (ArgumentException) { checkedDropValid = false; }
            return checkedDropValid;
        }
        private string NextHierarchySibling(MapObject item)
        {
            bool found = false;
            foreach (var candidate in snapshot.objects)
            {
                if (candidate.id == item.id) { found = true; continue; }
                if (found && (candidate.parentId ?? "") == (item.parentId ?? "")) return candidate.id;
            }
            return null;
        }
        private string HoverHierarchyDrop(Vector2 point)
        {
            ClearHierarchyDropStyle(); hierarchyDropMode = HierarchyDropMode.None; hierarchyDropBeforeId = hierarchyDropTargetId = null;
            string parent = null;
            if (objectList.worldBound.Contains(point))
            {
                var hit = hierarchyRows.FirstOrDefault(pair => pair.Value.worldBound.Contains(point));
                if (hit.Key != null)
                {
                    var target = snapshot.objects.Find(o => o.id == hit.Key); hierarchyDropRow = hit.Value; hierarchyDropTargetId = hit.Key;
                    float ratio = (point.y - hit.Value.worldBound.yMin) / Math.Max(1, hit.Value.worldBound.height);
                    if (ratio < .28f) { parent = target.parentId ?? ""; hierarchyDropBeforeId = target.id; hierarchyDropMode = HierarchyDropMode.Before; }
                    else if (ratio > .72f) { parent = target.parentId ?? ""; hierarchyDropBeforeId = NextHierarchySibling(target); hierarchyDropMode = HierarchyDropMode.After; }
                    else { parent = target.id; hierarchyDropMode = HierarchyDropMode.Inside; }
                }
                else if (hierarchyRootRow.worldBound.Contains(point) || point.y < objectList.worldBound.yMax - 15)
                { parent = ""; hierarchyDropRow = hierarchyRootRow; hierarchyDropMode = HierarchyDropMode.Inside; }
            }
            bool valid = ValidHierarchyDrop(parent, hierarchyDropBeforeId);
            if (hierarchyDropRow != null) hierarchyDropRow.AddToClassList(!valid ? "tree-drop-invalid" : hierarchyDropMode == HierarchyDropMode.Before ? "tree-drop-before" : hierarchyDropMode == HierarchyDropMode.After ? "tree-drop-after" : "tree-drop");
            string hoveredFolder = hierarchyDropMode == HierarchyDropMode.Inside ? parent : null;
            if (hoveredFolder != hoverFolderId) { hoverFolderId = hoveredFolder; hoverFolderSince = Time.unscaledTime; }
            if (valid && !string.IsNullOrEmpty(hoveredFolder) && collapsedGroups.Contains(hoveredFolder) && Time.unscaledTime - hoverFolderSince > .6f)
            { collapsedGroups.Remove(hoveredFolder); RefreshObjects(); hoverFolderSince = Time.unscaledTime; }
            return parent;
        }
        private bool HandleLibraryDrag(Vector2 panelPosition, Vector2 screenPosition)
        {
            if (!dragCandidate) return false;
            if (Keyboard.current?.escapeKey.wasPressedThisFrame == true) { EndLibraryDrag(); return true; }
            if (!libraryDragging && Vector2.Distance(panelPosition, libraryDragStart) > 6 && Mouse.current.leftButton.isPressed)
            { CommitInspectorEdit(); CancelPlacement(); libraryDragging = true; suppressCardClick = true; dragLabel.style.display = DisplayStyle.Flex; dragLabel.BringToFront(); StartDragPreview(); }
            if (libraryDragging)
            {
                if (objectList.worldBound.Contains(panelPosition))
                {
                    float amount = panelPosition.y < objectList.worldBound.yMin + 28 ? -1 : panelPosition.y > objectList.worldBound.yMax - 28 ? 1 : 0;
                    objectList.scrollOffset += Vector2.up * amount * 220 * Time.unscaledDeltaTime;
                }
                string parent = HoverHierarchyDrop(panelPosition); string beforeSiblingId = hierarchyDropBeforeId;
                bool valid = ValidHierarchyDrop(parent, beforeSiblingId), scene = viewport.worldBound.Contains(panelPosition);
                string name = dragEntry?.Name ?? dragRecord?.Name ?? snapshot.objects.Find(o => o.id == dragObjectId)?.displayName ?? L.T("#OBJECT");
                string targetName = snapshot.objects.Find(o => o.id == hierarchyDropTargetId)?.displayName ?? L.T("#WORLD_SPAWN");
                string destination = hierarchyDropMode == HierarchyDropMode.Before ? L.T("#BEFORE") + targetName :
                    hierarchyDropMode == HierarchyDropMode.After ? L.T("#AFTER") + targetName : L.T("#INSIDE") + HierarchyPath(parent);
                dragLabel.text = name + "\n" + (parent != null ? valid ? destination : L.T("#CANNOT_MOVE_GROUP") : scene ? L.T("#DROP_SCENE") : L.T("#DROP_GROUP_SCENE"));
                UpdateDragPreview(scene, screenPosition);
                if (scene && dragRecord != null && dragEntry == null && !dragPreviewFailed)
                    dragLabel.text = name + "\n" + L.T("#LOADING_PLACEMENT_PREVIEW");
                dragLabel.style.left = Mathf.Min(panelPosition.x + 16, root.worldBound.width - 280); dragLabel.style.top = Mathf.Min(panelPosition.y + 16, root.worldBound.height - 65);
                if (Mouse.current.leftButton.wasReleasedThisFrame)
                {
                    var entry = dragEntry; var record = dragRecord; string id = dragObjectId;
                    // Keep the ghost's shared mesh/textures alive through a ready drop.
                    if (valid || scene) _ = DropLibraryItem(entry, record, id, parent ?? "", beforeSiblingId, screenPosition, scene);
                    else SetStatus(L.T("#MOVE_CANCELED_CHOOSE_VALID_GROUP"));
                    EndLibraryDrag();
                }
                else if (!Mouse.current.leftButton.isPressed) EndLibraryDrag();
                return true;
            }
            if (!Mouse.current.leftButton.isPressed) EndLibraryDrag();
            return false;
        }
        private void EndLibraryDrag()
        {
            bool applyDeferredCollapse=deferAutoCollapseForDrag;
            deferAutoCollapseForDrag=false;
            dragPreviewVersion++;
            if (libraryDragging) world?.ClearPreview();
            dragCandidate = libraryDragging = false; if (dragLabel != null) dragLabel.style.display = DisplayStyle.None;
            ClearHierarchyDropStyle(); hierarchyDropMode = HierarchyDropMode.None; hierarchyDropBeforeId = hierarchyDropTargetId = null; hoverFolderId = null;
            if(applyDeferredCollapse){ApplyAutoCollapseHierarchy();RefreshObjects();}
        }
        private System.Threading.Tasks.Task DropLibraryItem(CatalogEntry entry, GameAssetRecord record, string id, string parent, Vector2 point, bool scene) =>
            DropLibraryItem(entry, record, id, parent, null, point, scene);
        private async System.Threading.Tasks.Task DropLibraryItem(CatalogEntry entry, GameAssetRecord record, string id, string parent, string beforeSiblingId, Vector2 point, bool scene)
        {
            try
            {
                if (id != null) {
                    if(scene) {
                        if(!selectedIds.Contains(id))Select(id);
                        if(!world.SurfacePoint(point,snap?moveSnap:0,out var surface))throw new InvalidOperationException(L.T("#DROP_ONTO_SURFACE"));
                        var bounds=world.CombinedBounds(SelectionRoots());var anchor=bounds.HasValue?new Vector3(bounds.Value.center.x,bounds.Value.min.y,bounds.Value.center.z):SelectionPivot();
                        MoveSelection(surface-anchor);
                    } else ReparentObject(id,parent,beforeSiblingId);return;
                }
                Vector3? dropSurface=null;
                if(scene) {if(!world.SurfacePoint(point,0,out var surface))throw new InvalidOperationException(L.T("#DROP_ONTO_CONSTRUCTION_SURFACE"));dropSurface=surface;}
                if (record != null)
                {
                    entry = await PrepareDropEntry(record);
                    if (this == null || entry?.Id != record.Id) return;
                    if (!record.Supports(Targets)) throw new InvalidOperationException(L.T("#ARCHIVES_CHANGED_DROP_CANCELED"));
                }
                if (entry == null) return;
                var position = Vector3.zero;
                if(scene)position=WorldView.PlacementPosition(dropSurface.Value,entry,snap,moveSnap);
                CommitInspectorEdit();
                if (entry.CustomType != null) { InsertCustomObject(entry, position, parent); return; }
                var item = new MapObject { assetId = entry.Id, displayName = entry.Name, parentId = parent, position = WorldView.ToData(position), scale = WorldView.ToData(entry.Size),
                    gameModelPath = entry.GameAsset?.modelPath ?? "", commonAsset = entry.GameAsset?.IsCommon ?? false,
                    availableMaps = entry.GameAsset?.origins.Select(o => o.mapId).Where(m => m != "").Distinct().ToList() ?? new List<string>() };
                session.Edit(doc => { doc.objects.Add(item); if (beforeSiblingId != null) MapHierarchy.Reorder(doc, item.id, parent, beforeSiblingId); }); selectedId = item.id; RevealHierarchy(item.id); Refresh(); FocusHierarchy(item.id);
            }
            catch (Exception ex) { if (this != null) SetStatus(ex.Message); }
        }
    }
}
