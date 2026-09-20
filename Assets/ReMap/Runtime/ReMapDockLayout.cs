using ReMap.Standalone.Core;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
namespace ReMap.Standalone
{
    [Serializable]
    public sealed class WorkspaceLayout
    {
        public float sideWidth = 380, libraryHeight = 250, treeHeight = 240, libraryDetailsWidth = 260;
        public bool library = true, hierarchy = true, properties = true;
        public bool libraryDetails;
        public float settingsWidth = 700, settingsHeight = 600, settingsX = -1, settingsY = -1;
    }
    public sealed partial class ReMapApp
    {
        private const string LayoutPreference = "ReMap.WorkspaceLayout.v1";
        private bool LayoutQA => Array.IndexOf(Environment.GetCommandLineArgs(), "-remapLayoutSmoke") >= 0;
        private string LayoutKey => LayoutPreference + (LayoutQA ? ".qa" : "");
        private WorkspaceLayout layout;
        private VisualElement editorBody, workspaceColumn, hierarchyPane, propertiesPane, settingsPanel;
        private VisualElement sideSplitter, librarySplitter, treeSplitter, viewMenu;
        private readonly Dictionary<string, Toggle> panelToggles = new Dictionary<string, Toggle>();
        private Toggle libraryDetailsToggle;
        private bool layoutResizing, maximizedScene;
        private VisualElement activeResizeHandle;
        private int resizePointer;
        private Button maximizeSceneButton,fullscreenMenuButton;
        private void LoadLayout()
        {
            try { layout = JsonUtility.FromJson<WorkspaceLayout>(PlayerPrefs.GetString(LayoutKey, "")); } catch { layout = null; }
            layout = layout ?? new WorkspaceLayout();
            if (Environment.GetCommandLineArgs().AnySmokeFlag() && !LayoutQA) layout = new WorkspaceLayout();
            if (!float.IsFinite(layout.sideWidth) || !float.IsFinite(layout.libraryHeight) || !float.IsFinite(layout.treeHeight) || !float.IsFinite(layout.libraryDetailsWidth) || !float.IsFinite(layout.settingsWidth) || !float.IsFinite(layout.settingsHeight) || !float.IsFinite(layout.settingsX) || !float.IsFinite(layout.settingsY)) layout = new WorkspaceLayout();
        }
        private void SaveLayout()
        {
            if (Environment.GetCommandLineArgs().AnySmokeFlag() && !LayoutQA) return;
            PlayerPrefs.SetString(LayoutKey, JsonUtility.ToJson(layout)); PlayerPrefs.Save();
        }
        private VisualElement DockTitle(string text, Action close = null)
        {
            var tab = new VisualElement(); tab.AddToClassList("dock-title");
            var label = Label(text, "dock-title-label"); label.style.flexGrow = 1; tab.Add(label);
            if (close != null) { var button = Button("×", close, "dock-close"); button.tooltip = L.T("#CLOSE_PANEL_REOPEN_VIEW"); tab.Add(button); }
            return tab;
        }
        private void BuildDockLayout(VisualElement body, VisualElement workspace)
        {
            editorBody = body; workspaceColumn = workspace;
            body.RegisterCallback<GeometryChangedEvent>(_ => AdaptLayout());
            var sceneTab = DockTitle(L.T("#SCENE")); workspace.Insert(0, sceneTab);
            maximizeSceneButton = Button(L.T("#MAXIMIZE"), ToggleSceneMaximized, "dock-tab-action"); sceneTab.Add(maximizeSceneButton);
            sceneTab.RegisterCallback<PointerDownEvent>(e => { if (e.button == 0 && e.clickCount == 2) ToggleSceneMaximized(); });
            var libraryTitle=DockTitle(L.T("#LIBRARY"), () => SetPanelVisible("library", false));
            int libraryStatusIndex=Math.Max(0,libraryTitle.childCount-1);
            if(rsxSessionIdleStatus!=null)libraryTitle.Insert(libraryStatusIndex++,rsxSessionIdleStatus);
            if(pageState!=null)libraryTitle.Insert(libraryStatusIndex,pageState);
            libraryDock.Insert(0,libraryTitle);
            librarySplitter = ResizeHandle("library", false); workspace.Insert(workspace.IndexOf(libraryDock), librarySplitter);
            sideSplitter = ResizeHandle("side", true); body.Insert(body.IndexOf(inspectorPanel), sideSplitter);
            treeSplitter = ResizeHandle("tree", false); inspectorPanel.Insert(inspectorPanel.IndexOf(propertiesPane), treeSplitter);
            viewMenu = new VisualElement(); viewMenu.AddToClassList("view-menu"); viewMenu.style.display = DisplayStyle.None; root.Add(viewMenu);
            void Panel(string id, string name) { var toggle = new Toggle(name); toggle.RegisterValueChangedCallback(e => SetPanelVisible(id, e.newValue)); viewMenu.Add(toggle); panelToggles[id] = toggle; }
            Panel("library", L.T("#LIBRARY")); Panel("hierarchy", L.T("#HIERARCHY")); Panel("properties", L.T("#PROPERTIES"));
            libraryDetailsToggle = new Toggle(L.T("#LIBRARY_DETAILS")) { value = layout.libraryDetails };
            libraryDetailsToggle.RegisterValueChangedCallback(e => SetLibraryDetails(e.newValue)); viewMenu.Add(libraryDetailsToggle);
            viewMenu.Add(Button(L.T("#SETTINGS_7A0408"), () => { viewMenu.style.display = DisplayStyle.None; ShowSettings(true); }));
            fullscreenMenuButton=Button("",()=>{viewMenu.style.display=DisplayStyle.None;ToggleFullscreen();});viewMenu.Add(fullscreenMenuButton);UpdateFullscreenButton();
            viewMenu.Add(Button(L.T("#MAXIMIZE_RESTORE_SCENE"), () => { viewMenu.style.display = DisplayStyle.None; ToggleSceneMaximized(); }));
            viewMenu.Add(Button(L.T("#RESET_LAYOUT"), ResetLayout));
            root.RegisterCallback<PointerDownEvent>(e => { if (viewMenu.style.display.value != DisplayStyle.None && !viewMenu.worldBound.Contains(e.position) && !root.Q<Button>("view-menu-button").worldBound.Contains(e.position)) viewMenu.style.display = DisplayStyle.None; }, TrickleDown.TrickleDown);
            root.RegisterCallback<KeyDownEvent>(e => { if (e.keyCode == KeyCode.Escape) { viewMenu.style.display = DisplayStyle.None; if (SettingsOpen) { ShowSettings(false); e.StopPropagation(); } else if (IndexingOpen) { ShowIndexing(false); e.StopPropagation(); } } }, TrickleDown.TrickleDown);
            AdaptLayout();
        }
        private void ShowViewMenu()
        {
            if (viewMenu == null) return;
            HideCommandMenu();
            bool open = viewMenu.style.display.value == DisplayStyle.None;
            viewMenu.style.display = open ? DisplayStyle.Flex : DisplayStyle.None;
            if (open) { libraryDetailsToggle.SetValueWithoutNotify(layout.libraryDetails);UpdateFullscreenButton(); var anchor = root.Q<Button>("view-menu-button"); viewMenu.style.left = Mathf.Clamp(anchor.worldBound.x, 0, root.worldBound.width - 240); viewMenu.style.top = anchor.worldBound.yMax; viewMenu.BringToFront(); }
        }
        private void SetPanelVisible(string id, bool visible)
        {
            CommitInspectorEdit(); EndLibraryDrag();
            if (id == "library") layout.library = visible;
            if (id == "hierarchy") layout.hierarchy = visible;
            if (id == "properties") layout.properties = visible;
            maximizedScene = false; AdaptLayout(); SaveLayout();
        }
        private void ToggleSceneMaximized() { CommitInspectorEdit(); maximizedScene = !maximizedScene; EndLibraryDrag(); CancelGizmoDrag(); AdaptLayout(); }
        private void ResetLayout()
        {
            EndLayoutResize(); layout = new WorkspaceLayout(); maximizedScene = false; viewMenu.style.display = DisplayStyle.None; AdaptLayout(); SaveLayout();
            RefreshAssetCatalog();
        }
        private VisualElement ResizeHandle(string kind, bool vertical)
        {
            var handle = new VisualElement { name = "resize-" + kind }; handle.AddToClassList("dock-splitter"); handle.AddToClassList(vertical ? "splitter-vertical" : "splitter-horizontal");
            handle.tooltip = L.T("#DRAG_RESIZE_DOUBLE_CLICK_DEFAULT");
            BindResize(handle, kind); return handle;
        }
        private void BindResize(VisualElement handle, string kind)
        {
            Vector2 start = default; WorkspaceLayout initial = null;
            handle.RegisterCallback<PointerDownEvent>(e => {
                if (e.button != 0 || (e.target is VisualElement target && target != handle && target.GetFirstAncestorOfType<Button>() != null) || e.target is Button) return;
                CommitInspectorEdit(); CancelGizmoDrag(); EndLibraryDrag();
                if (e.clickCount == 2 && kind != "settings-move")
                {
                    var defaults = new WorkspaceLayout();
                    if (kind == "side") layout.sideWidth = defaults.sideWidth;
                    if (kind == "library") layout.libraryHeight = defaults.libraryHeight;
                    if (kind == "library-details") layout.libraryDetailsWidth = defaults.libraryDetailsWidth;
                    if (kind == "tree") layout.treeHeight = defaults.treeHeight;
                    if (kind == "settings") { layout.settingsWidth = defaults.settingsWidth; layout.settingsHeight = defaults.settingsHeight; }
                    AdaptLayout(); SaveLayout(); e.StopPropagation(); return;
                }
                if (kind.StartsWith("settings")) { layout.settingsX = settingsPanel.worldBound.x; layout.settingsY = settingsPanel.worldBound.y; }
                initial = JsonUtility.FromJson<WorkspaceLayout>(JsonUtility.ToJson(layout)); start = e.position;
                // Start from the displayed dimensions when a smaller window has clamped the saved layout.
                initial.sideWidth = inspectorPanel.resolvedStyle.width; initial.libraryHeight = libraryDock.resolvedStyle.height; initial.treeHeight = hierarchyPane.resolvedStyle.height;
                if(assetDetail!=null&&assetDetail.resolvedStyle.width>0)initial.libraryDetailsWidth=assetDetail.resolvedStyle.width;
                if (kind.StartsWith("settings")) { initial.settingsWidth = settingsPanel.resolvedStyle.width; initial.settingsHeight = settingsPanel.resolvedStyle.height; }
                layoutResizing = true; activeResizeHandle = handle; resizePointer = e.pointerId; handle.CapturePointer(e.pointerId); handle.AddToClassList("resizing"); e.StopPropagation();
            });
            handle.RegisterCallback<PointerMoveEvent>(e => {
                if (!layoutResizing || activeResizeHandle != handle || initial == null) return;
                var delta = (Vector2)e.position - start; ApplyResize(kind, initial, delta); e.StopPropagation();
            });
            handle.RegisterCallback<PointerUpEvent>(e => { if (activeResizeHandle == handle) { EndLayoutResize(); e.StopPropagation(); } });
            handle.RegisterCallback<PointerCaptureOutEvent>(_ => { if (activeResizeHandle == handle) EndLayoutResize(); });
        }
        private void ApplyResize(string kind, WorkspaceLayout initial, Vector2 delta)
        {
            float width = root.worldBound.width, height = root.worldBound.height;
            if (kind == "side") layout.sideWidth = Mathf.Clamp(initial.sideWidth - delta.x, 300, Math.Max(300, width - 320));
            if (kind == "library") layout.libraryHeight = Mathf.Clamp(initial.libraryHeight - delta.y, 190, Math.Max(190, editorBody.worldBound.height - 160));
            if (kind == "library-details") layout.libraryDetailsWidth = Mathf.Clamp(initial.libraryDetailsWidth - delta.x, 180, Math.Max(180, libraryContent.worldBound.width - 320));
            if (kind == "tree") layout.treeHeight = Mathf.Clamp(initial.treeHeight + delta.y, 150, Math.Max(150, inspectorPanel.worldBound.height - 170));
            if (kind == "settings") { layout.settingsWidth = Mathf.Clamp(initial.settingsWidth + delta.x, 360, Math.Max(360, width - layout.settingsX - 8)); layout.settingsHeight = Mathf.Clamp(initial.settingsHeight + delta.y, 300, Math.Max(300, height - layout.settingsY - 8)); }
            if (kind == "settings-move") { layout.settingsX = Mathf.Clamp(initial.settingsX + delta.x, 0, Math.Max(0, width - settingsPanel.resolvedStyle.width)); layout.settingsY = Mathf.Clamp(initial.settingsY + delta.y, 0, Math.Max(0, height - settingsPanel.resolvedStyle.height)); }
            AdaptLayout();
        }
        private void EndLayoutResize()
        {
            if (!layoutResizing) return;
            layoutResizing = false; var handle = activeResizeHandle; activeResizeHandle = null;
            handle?.RemoveFromClassList("resizing"); if (handle?.HasPointerCapture(resizePointer) == true) handle.ReleasePointer(resizePointer); SaveLayout();
        }
        private void AdaptLayout()
        {
            if (layout == null || editorBody == null || settingsPanel == null) return;
            float width = root.worldBound.width, height = root.worldBound.height;
            if (width < 1 || height < 1) return;
            root.EnableInClassList("compact", width < 1100);
            bool library = layout.library && !maximizedScene, hierarchy = layout.hierarchy && !maximizedScene, properties = layout.properties && !maximizedScene;
            void Show(VisualElement element, bool visible) { if (element != null) element.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None; }
            Show(libraryDock, library); Show(librarySplitter, library); Show(inspectorPanel, hierarchy || properties); Show(sideSplitter, hierarchy || properties);
            Show(hierarchyPane, hierarchy); Show(propertiesPane, properties); Show(treeSplitter, hierarchy && properties);
            float bodyHeight = editorBody.resolvedStyle.height > 0 ? editorBody.resolvedStyle.height : height - 80;
            inspectorPanel.style.width = Mathf.Clamp(layout.sideWidth, 300, Math.Max(300, width - 320));
            libraryDock.style.height = Mathf.Clamp(layout.libraryHeight, 190, Math.Max(190, bodyHeight - 160));
            ApplyLibraryDetailLayout();
            hierarchyPane.style.height = hierarchy && properties ? Mathf.Clamp(layout.treeHeight, 150, Math.Max(150, bodyHeight - 170)) : StyleKeyword.Auto;
            hierarchyPane.style.flexGrow = properties ? 0 : 1;
            objectList.style.height = StyleKeyword.Auto; objectList.style.flexGrow = 1; objectList.style.flexBasis = 0;
            foreach (var toggle in panelToggles) toggle.Value.SetValueWithoutNotify(toggle.Key == "library" ? layout.library : toggle.Key == "hierarchy" ? layout.hierarchy : layout.properties);
            if (maximizeSceneButton != null) maximizeSceneButton.text = maximizedScene ? L.T("#RESTORE") : L.T("#MAXIMIZE");
            float sw = Mathf.Clamp(layout.settingsWidth, Math.Min(360, width - 16), Math.Max(1, width - 16));
            float sh = Mathf.Clamp(layout.settingsHeight, Math.Min(300, height - 16), Math.Max(1, height - 16));
            settingsPanel.style.width = sw; settingsPanel.style.height = sh;
            settingsPanel.style.left = Mathf.Clamp(layout.settingsX < 0 ? (width - sw) / 2 : layout.settingsX, 0, Math.Max(0, width - sw));
            settingsPanel.style.top = Mathf.Clamp(layout.settingsY < 0 ? (height - sh) / 2 : layout.settingsY, 0, Math.Max(0, height - sh));
        }

        private void ApplyLibraryDetailLayout()
        {
            if(assetDetail==null||assetDetailSplitter==null||catalogMode==null)return;
            bool visible=layout.libraryDetails&&(catalogMode.index==0||catalogMode.index==2);
            assetDetail.style.display=visible?DisplayStyle.Flex:DisplayStyle.None;
            assetDetailSplitter.style.display=visible?DisplayStyle.Flex:DisplayStyle.None;
            if(!visible)return;
            float available=libraryContent?.resolvedStyle.width??0;
            float maximum=available>1?Math.Max(180,available-320):Math.Max(180,layout.libraryDetailsWidth);
            assetDetail.style.width=Mathf.Clamp(layout.libraryDetailsWidth,180,maximum);
        }
    }
    internal static class WorkspaceArguments
    {
        internal static bool AnySmokeFlag(this string[] args) { foreach (string arg in args) if (arg.StartsWith("-remap", StringComparison.OrdinalIgnoreCase)) return true; return false; }
    }
}
