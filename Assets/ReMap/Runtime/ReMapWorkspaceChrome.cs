using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using ReMap.Standalone.Core;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace ReMap.Standalone
{
    public sealed partial class ReMapApp
    {
        private const string LegacyLastWorkspacePreference = "ReMap.LastWorkspace.v1";
        private const string LegacyRecoverySlot = "_session_recovery";
        private VisualElement commandMenu;
        private Button fileMenuButton, editMenuButton, toolsMenuButton, helpMenuButton;
        private Button workspaceGameButton;
        private Button activeCommandMenuButton;
        private IVisualElementScheduledItem recoverySave;
        private bool changingProjectSelector;
        private VisualElement newMapOverlay, newMapSources, renameMapOverlay;
        private VisualElement portCompatibilityOverlay, portCompatibilityList;
        private Label portCompatibilitySummary;
        private VisualElement workspaceGameOverlay;
        private DropdownField workspaceGameChoice;
        private TextField newMapName, renameMapName;
        private DropdownField newMapGame, newMapPrimary;
        private Label newMapModeNotice;
        private Button newMapConfirm;
        private bool portingMap;
        private MapDocument portSource;
        private string portSourceSlot;
        private readonly Dictionary<string, Toggle> newMapToggles = new Dictionary<string, Toggle>();
        private int windowedWidth,windowedHeight;
        private bool maximizedWindow;

        private static string LastWorkspacePreference(string gameTarget) => "ReMap.LastWorkspace.v2." + GameTargets.Normalize(gameTarget);
        private static string RecoverySlot(string gameTarget) => "_session_recovery_" + GameTargets.Normalize(gameTarget);
        private static bool IsRecoverySlot(string name) => name != null && name.StartsWith("_session_recovery", StringComparison.OrdinalIgnoreCase);
        private string DraftSlot(string gameTarget)
        {
            string stem = GameTargets.Normalize(gameTarget) == GameTargets.R5Flowstate ? "new-r5f-map" : "new-r5r-map";
            string name = stem; int suffix = 2;
            while (files.Exists(name)) name = stem + "-" + suffix++;
            return name;
        }

        private void BuildTopMenuButtons(VisualElement header)
        {
            fileMenuButton = Button(L.T("#FILE"), () => ShowCommandMenu(fileMenuButton, BuildFileMenu), "menu-button");
            editMenuButton = Button(L.T("#EDIT"), () => ShowCommandMenu(editMenuButton, BuildEditMenu), "menu-button");
            var viewButton = Button(L.T("#VIEW"), () => { HideCommandMenu(); ShowViewMenu(); }, "menu-button");
            viewButton.name = "view-menu-button";
            toolsMenuButton = Button(L.T("#TOOLS"), () => ShowCommandMenu(toolsMenuButton, BuildToolsMenu), "menu-button");
            helpMenuButton = Button(L.T("#HELP"), () => ShowCommandMenu(helpMenuButton, BuildHelpMenu), "menu-button");
            header.Add(fileMenuButton); header.Add(editMenuButton); header.Add(viewButton); header.Add(toolsMenuButton); header.Add(helpMenuButton);

            commandMenu = new VisualElement(); commandMenu.AddToClassList("command-menu"); commandMenu.style.display = DisplayStyle.None; root.Add(commandMenu);
            root.RegisterCallback<PointerDownEvent>(e =>
            {
                if (commandMenu.style.display.value == DisplayStyle.None || commandMenu.worldBound.Contains(e.position) || activeCommandMenuButton?.worldBound.Contains(e.position) == true) return;
                HideCommandMenu();
            }, TrickleDown.TrickleDown);
            root.RegisterCallback<KeyDownEvent>(e => { if (e.keyCode == KeyCode.Escape) HideCommandMenu(); }, TrickleDown.TrickleDown);
        }

        private void ShowCommandMenu(Button anchor, Action<VisualElement> build)
        {
            if (commandMenu == null) return;
            bool open = commandMenu.style.display.value == DisplayStyle.None || activeCommandMenuButton != anchor;
            if (viewMenu != null) viewMenu.style.display = DisplayStyle.None;
            if (!open) { HideCommandMenu(); return; }
            activeCommandMenuButton = anchor; commandMenu.Clear(); build(commandMenu);
            commandMenu.style.display = DisplayStyle.Flex;
            commandMenu.style.left = Mathf.Clamp(anchor.worldBound.x, 0, Math.Max(0, root.worldBound.width - 260));
            commandMenu.style.top = anchor.worldBound.yMax; commandMenu.BringToFront();
        }

        private void HideCommandMenu()
        {
            if (commandMenu != null) commandMenu.style.display = DisplayStyle.None;
            activeCommandMenuButton = null;
        }

        private Button MenuAction(VisualElement menu, string text, Action action, bool enabled = true)
        {
            var button = Button(text, () => { HideCommandMenu(); action(); }, "menu-item");
            button.SetEnabled(enabled); menu.Add(button); return button;
        }

        private static void MenuSeparator(VisualElement menu)
        {
            var separator = new VisualElement(); separator.AddToClassList("menu-separator"); menu.Add(separator);
        }

        private void BuildFileMenu(VisualElement menu)
        {
            menu.Add(Label(L.F("#CURRENT_PROJECT_ARG0", slot.value), "menu-caption"));
            MenuAction(menu, L.T("#NEW_MAP"), NewMap);
            MenuAction(menu, L.T("#IMPORT_SHARED_PROJECT"), ImportSharedProject);
            MenuAction(menu, L.T("#EXPORT_CURRENT_PROJECT"), ExportCurrentProject, snapshot != null);
            MenuAction(menu, L.T("#PORT_CURRENT_MAP_OTHER_GAME"), BeginMapPort, snapshot != null);
            MenuAction(menu, L.T("#RENAME_CURRENT_MAP"), () => ShowRenameMapDialog(true));
            MenuAction(menu, L.T("#SAVE") + "    Ctrl+S", Save);
            MenuSeparator(menu);
            MenuAction(menu, L.T("#BUILD_GAME_SCRIPT"), BuildGameScript, snapshot?.objects.Count > 0);
            MenuAction(menu, L.T("#RESET_INSTALLED_GAME_SCRIPT"), ResetGameScript);
            MenuAction(menu, L.T("#PREVIEW_GAME_CODE"), () => ShowCodePreview(), snapshot?.objects.Count > 0);
            MenuAction(menu, L.T("#LIVE_GAME_F9FC5E"), () => ShowLiveConsole(), snapshot?.objects.Count > 0);
            MenuAction(menu, L.T("#COPY_LIVE_COMMANDS"), CopyLiveCommands, snapshot?.objects.Count > 0);
        }

        private void BuildEditMenu(VisualElement menu)
        {
            undoButton = MenuAction(menu, L.T("#UNDO") + "    Ctrl+Z", () => ApplyHistory(false), session.CanUndo);
            redoButton = MenuAction(menu, L.T("#REDO") + "    Ctrl+Y", () => ApplyHistory(true), session.CanRedo);
            MenuSeparator(menu);
            MenuAction(menu,L.T("#COPY_E21F93")+"    Ctrl+C",()=>CopySelectionToClipboard(),selectedIds.Count>0);
            MenuAction(menu,L.T("#CUT")+"    Ctrl+X",CutSelectionToClipboard,selectedIds.Count>0);
            MenuAction(menu,L.T("#PASTE")+"    Ctrl+V",PasteSelectionFromClipboard,ReadSelectionClipboard()!=null);
            MenuSeparator(menu);
            MenuAction(menu, L.T("#SELECT") + "    Ctrl+A", SelectAllObjects, snapshot?.objects.Count > 0);
            MenuAction(menu, L.T("#PLACE_DUPLICATE") + "    Ctrl+D", BeginSelectionDuplicatePlacement, selectedIds.Count > 0);
            MenuAction(menu, L.T("#GROUP_SELECTION") + "    Ctrl+G", GroupSelection, selectedIds.Count > 0);
            MenuAction(menu, L.T("#DELETE") + "    Del", Delete, selectedIds.Count > 0);
        }

        private void BuildHelpMenu(VisualElement menu)
        {
            MenuAction(menu, L.T("#ABOUT_REMAP"), () => ShowAbout(true));
        }

        private bool HandleGlobalKeyboardShortcuts()
        {
            var keyboard=Keyboard.current;if(keyboard==null)return false;
            if(keyboard.f11Key.wasPressedThisFrame||((keyboard.leftAltKey.isPressed||keyboard.rightAltKey.isPressed)&&(keyboard.enterKey.wasPressedThisFrame||keyboard.numpadEnterKey.wasPressedThisFrame)))
            {
                ToggleFullscreen();return true;
            }
            bool holding=placing!=null||placingAssembly!=null||placingSelection!=null||assemblyDragSlot!=null||assemblyDragging||libraryDragging;
            if(keyboard.escapeKey.wasPressedThisFrame&&holding){EndLibraryDrag();CancelPlacement();SetStatus(L.T("#PLACEMENT_CANCELLED"));return true;}
            var focused=root.panel?.focusController.focusedElement as VisualElement;
            bool historyShortcut=keyboard.ctrlKey.isPressed&&(keyboard.zKey.wasPressedThisFrame||keyboard.yKey.wasPressedThisFrame);
            if(!BlockingDialogOpen&&historyShortcut&&IsInspectorEditingTarget(focused))
            {
                Run(()=>ApplyHistory(keyboard.yKey.wasPressedThisFrame||keyboard.shiftKey.isPressed));return true;
            }
            if(BlockingDialogOpen||IsTextEditingTarget(focused))return false;
            if(keyboard.deleteKey.wasPressedThisFrame){Run(Delete);return true;}
            if(keyboard.f2Key.wasPressedThisFrame){BeginHierarchyRename(selectedId);return true;}
            if(keyboard.fKey.wasPressedThisFrame&&Mouse.current?.rightButton.isPressed!=true){FocusSelection();return true;}
            if(!keyboard.ctrlKey.isPressed)return false;
            Action action=null;
            if(keyboard.cKey.wasPressedThisFrame)action=()=>CopySelectionToClipboard();
            else if(keyboard.vKey.wasPressedThisFrame)action=PasteSelectionFromClipboard;
            else if(keyboard.xKey.wasPressedThisFrame)action=CutSelectionToClipboard;
            else if(keyboard.zKey.wasPressedThisFrame)action=()=>ApplyHistory(keyboard.shiftKey.isPressed);
            else if(keyboard.yKey.wasPressedThisFrame)action=()=>ApplyHistory(true);
            else if(keyboard.sKey.wasPressedThisFrame)action=Save;
            else if(keyboard.dKey.wasPressedThisFrame)action=BeginSelectionDuplicatePlacement;
            else if(keyboard.aKey.wasPressedThisFrame)action=SelectAllObjects;
            else if(keyboard.gKey.wasPressedThisFrame)action=GroupSelection;
            if(action==null)return false;Run(action);return true;
        }
        private static bool IsTextEditingTarget(VisualElement element)
        {
            for(var current=element;current!=null;current=current.parent)if(current is TextField||current is FloatField||current is IntegerField)return true;
            return false;
        }
        private bool IsInspectorEditingTarget(VisualElement element)
        {
            for(var current=element;current!=null;current=current.parent)if(current==inspector)return true;
            return false;
        }
        private void ApplyHistory(bool redo)
        {
            FinishInspectorInteraction();CancelPlacement();
            if(redo)session.Redo();else session.Undo();
            Refresh();
        }
        private bool IsFullscreen=>maximizedWindow;
        private static Vector2Int CurrentDisplaySize()
        {
            var display=Screen.mainWindowDisplayInfo;
            int width=display.width>0?display.width:Screen.currentResolution.width;
            int height=display.height>0?display.height:Screen.currentResolution.height;
            return new Vector2Int(Math.Max(640,width),Math.Max(480,height));
        }
        private void ApplyFullscreenResolution()
        {
            if(!maximizedWindow)return;
            var size=CurrentDisplaySize();
            Screen.fullScreenMode=FullScreenMode.FullScreenWindow;
            Screen.SetResolution(size.x,size.y,FullScreenMode.FullScreenWindow);
            root?.schedule.Execute(AdaptLayout);
        }
        private void ApplyWindowedResolution()
        {
            if(maximizedWindow)return;
            int width=windowedWidth>0?windowedWidth:1280,height=windowedHeight>0?windowedHeight:720;
            Screen.fullScreenMode=FullScreenMode.Windowed;
            Screen.SetResolution(width,height,FullScreenMode.Windowed);
            root?.schedule.Execute(AdaptLayout);
        }
        private void ToggleFullscreen()
        {
            if(maximizedWindow)
            {
                maximizedWindow=false;
                ApplyWindowedResolution();
                root?.schedule.Execute(ApplyWindowedResolution).ExecuteLater(120);
            }
            else
            {
                windowedWidth=Screen.width;windowedHeight=Screen.height;
                maximizedWindow=true;
                ApplyFullscreenResolution();
                root?.schedule.Execute(ApplyFullscreenResolution).ExecuteLater(120);
            }
            root?.schedule.Execute(AdaptLayout).ExecuteLater(160);
            UpdateFullscreenButton();SetStatus(IsFullscreen?L.T("#FULLSCREEN_ENABLED"):L.T("#WINDOWED_MODE_ENABLED"));
        }
        private void UpdateFullscreenButton(){if(fullscreenMenuButton!=null)fullscreenMenuButton.text=(IsFullscreen?L.T("#EXIT_FULLSCREEN"):L.T("#FULLSCREEN"))+"    F11";}

        private void BuildToolsMenu(VisualElement menu)
        {
            MenuAction(menu, L.T("#CONSTRUCTION_TOOLS_6E6587"), () => ShowConstructionTools(true));
            MenuAction(menu, L.T("#TRANSFORM_STEPS"), () => ShowTool("transform-snap"));
            MenuSeparator(menu);
            MenuAction(menu, L.T("#WORKSPACE_GAME"), () => ShowWorkspaceGameDialog(true));
            MenuAction(menu, L.T("#INDEXING_PAGE"), () => ShowIndexing(true), !indexRequested);
            MenuAction(menu, L.T("#SETTINGS_7A0408"), () => ShowSettings(true));
        }

        private void BuildWorkspaceGameDialog()
        {
            workspaceGameOverlay = new VisualElement(); workspaceGameOverlay.AddToClassList("modal-overlay"); root.Add(workspaceGameOverlay);
            var panel = new VisualElement(); panel.AddToClassList("assembly-save-panel"); workspaceGameOverlay.Add(panel);
            var heading = DockTitle(L.T("#WORKSPACE_GAME")); panel.Add(heading);
            heading.Add(Button("×", () => ShowWorkspaceGameDialog(false), "dock-close"));
            var content = new VisualElement(); content.AddToClassList("assembly-save-content"); panel.Add(content);
            content.Add(Label(L.T("#WORKSPACE_GAME_HELP"), "note"));
            workspaceGameChoice = new DropdownField(L.T("#TARGET_GAME"), new List<string> { "R5Reloaded", "R5Flowstate" }, 0);
            content.Add(workspaceGameChoice);
            content.Add(Label(L.T("#WORKSPACE_GAME_PROJECTS_HELP"), "map-source-warning"));
            var actions = new VisualElement(); actions.AddToClassList("dialog-actions"); panel.Add(actions);
            actions.Add(Button(L.T("#CANCEL"), () => ShowWorkspaceGameDialog(false)));
            actions.Add(Button(L.T("#SWITCH_WORKSPACE_GAME"), ApplyWorkspaceGame, "primary"));
            workspaceGameOverlay.style.display = DisplayStyle.None;
            root.RegisterCallback<KeyDownEvent>(e => { if (e.keyCode == KeyCode.Escape && WorkspaceGameDialogOpen) { ShowWorkspaceGameDialog(false); e.StopPropagation(); } }, TrickleDown.TrickleDown);
        }

        private void ShowWorkspaceGameDialog(bool show)
        {
            if (workspaceGameOverlay == null) return;
            if (show)
            {
                HideCommandMenu(); CommitInspectorEdit(); CancelPlacement();
                string target = snapshot?.gameTarget ?? assetLibrary.TargetGame;
                workspaceGameChoice.SetValueWithoutNotify(workspaceGameChoice.choices[target == GameTargets.R5Flowstate ? 1 : 0]);
                workspaceGameOverlay.style.display = DisplayStyle.Flex; workspaceGameOverlay.BringToFront();
            }
            else workspaceGameOverlay.style.display = DisplayStyle.None;
        }

        private void ApplyWorkspaceGame()
        {
            string target = workspaceGameChoice.index == 1 ? GameTargets.R5Flowstate : GameTargets.R5Reloaded;
            if (GameTargets.Normalize(snapshot?.gameTarget) == target) { ShowWorkspaceGameDialog(false); return; }
            bool projectLoaded = SwitchWorkspaceTarget(target);
            workspaceGameOverlay.style.display = DisplayStyle.None;
            CancelPlacement(); previewEntry = null; lastPreviewRequest = null; world.models.ForgetPrepared();
            foreach (var id in snapshot.objects.Where(item => item.assetId.StartsWith("apex:", StringComparison.Ordinal)).Select(item => item.assetId).Distinct()) world.Reload(id);
            if (currentThumbnail != null) Destroy(currentThumbnail); currentThumbnail = null; assetPreview.image = null;
            previewFailures.Clear(); Refresh(); FillMapChoices(); RefreshProjectSelector(slot.value);
            if (projectLoaded)
            {
                SetStatus(assetLibrary.Configured ? L.T("#MAP_OPENED_RELOADING_SELECTED_RPAKS") : L.T("#WORKSPACE_GAME_CHANGED"));
                if (assetLibrary.Configured) settingsIndexTask = IndexAssets();
            }
            else
            {
                SetStatus(L.F("#NO_SAVED_ARG0_PROJECT_FOUND", GameTargets.DisplayName(target)));
                root.schedule.Execute(() => ShowNewMapDialog(true));
            }
        }

        private void RefreshWorkspaceGameButton()
        {
            if (workspaceGameButton == null) return;
            string target = snapshot?.gameTarget ?? assetLibrary?.TargetGame ?? GameTargets.R5Reloaded;
            workspaceGameButton.text = GameTargets.DisplayName(target);
            workspaceGameButton.tooltip = L.T("#CHANGE_WORKSPACE_GAME");
        }

        private void RememberWorkspace()
        {
            if (Environment.GetCommandLineArgs().AnySmokeFlag() || slot == null) return;
            PlayerPrefs.SetString(LastWorkspacePreference(snapshot?.gameTarget ?? assetLibrary.TargetGame), slot.value); PlayerPrefs.Save(); QueueWorkspaceRecovery();
        }

        private void InitializeProjectSelector()
        {
            RefreshProjectSelector("workspace");
            slot.RegisterValueChangedCallback(_ => { if (!changingProjectSelector) Run(Load); });
        }

        private void RefreshProjectSelector(string preferred = null)
        {
            if (slot == null) return;
            string selected = string.IsNullOrWhiteSpace(preferred) ? slot.value : preferred;
            if (string.IsNullOrWhiteSpace(selected)) selected = "workspace";
            string target = snapshot?.gameTarget ?? assetLibrary?.TargetGame ?? GameTargets.R5Reloaded;
            var choices = files.ListSlots(target).Where(name => !IsRecoverySlot(name)).ToList();
            if (!choices.Contains(selected)) choices.Insert(0, selected);
            changingProjectSelector = true;
            try { slot.choices = choices; slot.SetValueWithoutNotify(selected); }
            finally { changingProjectSelector = false; }
        }

        private bool TryRestoreWorkspace()
        {
            string target = assetLibrary.TargetGame;
            var compatible = files.ListSlots(target).Where(name => !IsRecoverySlot(name)).ToList();
            string active = PlayerPrefs.GetString(LastWorkspacePreference(target), "");
            if (string.IsNullOrWhiteSpace(active))
            {
                string legacy = PlayerPrefs.GetString(LegacyLastWorkspacePreference, "");
                if (compatible.Contains(legacy)) active = legacy;
            }
            if (!compatible.Contains(active)) active = compatible.FirstOrDefault() ?? DraftSlot(target);
            try
            {
                MapDocument restored = LoadProjectForTarget(RecoverySlot(target), target) ?? LoadProjectForTarget(LegacyRecoverySlot, target) ?? LoadProjectForTarget(active, target);
                if (restored == null) return false;
                RefreshProjectSelector(active); session.Replace(restored); selectedId = null; lastSavedRevision = -1;
                if (files.Exists(active) && codec.Encode(files.Load(active)) == codec.Encode(restored)) lastSavedRevision = session.Revision;
                Refresh(); RestoreCachedModels(); return true;
            }
            catch (Exception ex) { Debug.LogWarning(L.T("#WORKSPACE_RECOVERY_FAILED") + ex.Message); return false; }
        }

        private void QueueWorkspaceRecovery()
        {
            if (root == null || Environment.GetCommandLineArgs().AnySmokeFlag()) return;
            if (recoverySave == null) recoverySave = root.schedule.Execute(SaveWorkspaceRecovery);
            recoverySave.ExecuteLater(700);
        }

        private void SaveWorkspaceRecovery()
        {
            if (files == null || slot == null || snapshot == null || Environment.GetCommandLineArgs().AnySmokeFlag()) return;
            try
            {
                files.Save(RecoverySlot(snapshot.gameTarget), session.Snapshot());
                PlayerPrefs.SetString(LastWorkspacePreference(snapshot.gameTarget), slot.value); PlayerPrefs.Save();
            }
            catch (Exception ex) { Debug.LogWarning(L.T("#WORKSPACE_RECOVERY_FAILED") + ex.Message); }
        }

        private void BuildNewMapDialog()
        {
            newMapOverlay = new VisualElement(); newMapOverlay.AddToClassList("modal-overlay"); root.Add(newMapOverlay);
            var panel = new VisualElement(); panel.AddToClassList("new-map-panel"); newMapOverlay.Add(panel);
            var heading = DockTitle(L.T("#CREATE_NEW_MAP")); panel.Add(heading); heading.Add(Button("×", () => ShowNewMapDialog(false), "dock-close"));
            var content = new ScrollView(); content.AddToClassList("new-map-content"); panel.Add(content);
            content.Add(Label(L.T("#PROJECT"), "section-title"));
            newMapName = new TextField(L.T("#PROJECT_NAME")) { value = "untitled", isDelayed = true }; content.Add(newMapName);
            newMapGame = new DropdownField(L.T("#TARGET_GAME"), new List<string> { "R5Reloaded", "R5Flowstate" }, 0); content.Add(newMapGame);
            newMapGame.RegisterValueChangedCallback(_ =>
            {
                assetLibrary.SelectTarget(newMapGame.index == 1 ? GameTargets.R5Flowstate : GameTargets.R5Reloaded, false);
                FillNewMapChoices();
            });
            content.Add(Label(L.T("#R5RELOADED_DEFAULT_CHOICE_STORED_PROJECT"), "note"));
            newMapModeNotice = Label("", "map-source-warning"); newMapModeNotice.style.display = DisplayStyle.None; content.Add(newMapModeNotice);
            content.Add(Label(L.T("#EDITED_MAP_RPAKS"), "section-title"));
            content.Add(Label(L.T("#CHOOSE_MAIN_MAP_ADD_OTHER"), "note"));
            newMapPrimary = new DropdownField(L.T("#EDITED_MAP"), new List<string> { L.T("#SPECIFIED") }, 0); content.Add(newMapPrimary);
            newMapPrimary.RegisterValueChangedCallback(_ => SyncNewMapPrimary());
            newMapSources = new VisualElement(); newMapSources.AddToClassList("map-choices"); content.Add(newMapSources);
            var actions = new VisualElement(); actions.AddToClassList("dialog-actions"); panel.Add(actions);
            actions.Add(Button(L.T("#CANCEL"), () => ShowNewMapDialog(false)));
            newMapConfirm = Button(L.T("#CREATE_LOAD_RPAKS"), () => { if (portingMap) PortCurrentMap(); else CreateNewMap(); }, "primary"); actions.Add(newMapConfirm);
            newMapOverlay.style.display = DisplayStyle.None;
            root.RegisterCallback<KeyDownEvent>(e => {
                if (e.keyCode != KeyCode.Escape) return;
                if (PortCompatibilityReportOpen) { ShowPortCompatibilityReport(null, null); e.StopPropagation(); }
                else if (NewMapDialogOpen) { ShowNewMapDialog(false); e.StopPropagation(); }
            }, TrickleDown.TrickleDown);
            BuildRenameMapDialog();
            BuildPortCompatibilityReport();
        }
        private void BuildRenameMapDialog()
        {
            renameMapOverlay = new VisualElement(); renameMapOverlay.AddToClassList("modal-overlay"); root.Add(renameMapOverlay);
            var panel = new VisualElement(); panel.AddToClassList("assembly-save-panel"); renameMapOverlay.Add(panel);
            var heading = DockTitle(L.T("#RENAME_CURRENT_MAP_802B4C")); panel.Add(heading); heading.Add(Button("×", () => ShowRenameMapDialog(false), "dock-close"));
            var content = new VisualElement(); content.AddToClassList("assembly-save-content"); panel.Add(content);
            content.Add(Label(L.T("#CHOOSE_NEW_PROJECT_NAME_CURRENT"), "note"));
            renameMapName = new TextField(L.T("#MAP_NAME")) { isDelayed = true }; content.Add(renameMapName);
            var actions = new VisualElement(); actions.AddToClassList("dialog-actions"); panel.Add(actions);
            actions.Add(Button(L.T("#CANCEL"), () => ShowRenameMapDialog(false)));
            actions.Add(Button(L.T("#RENAME"), RenameCurrentMap, "primary"));
            renameMapOverlay.style.display = DisplayStyle.None;
        }
        private void BuildPortCompatibilityReport()
        {
            portCompatibilityOverlay = new VisualElement(); portCompatibilityOverlay.AddToClassList("modal-overlay"); root.Add(portCompatibilityOverlay);
            var panel = new VisualElement(); panel.AddToClassList("new-map-panel"); portCompatibilityOverlay.Add(panel);
            var heading = DockTitle(L.T("#CONVERSION_BLOCKED")); panel.Add(heading);
            heading.Add(Button("×", () => ShowPortCompatibilityReport(null, null), "dock-close"));
            var content = new VisualElement(); content.AddToClassList("new-map-content"); panel.Add(content);
            portCompatibilitySummary = Label("", "map-source-warning"); content.Add(portCompatibilitySummary);
            content.Add(Label(L.T("#CONVERSION_MISSING_MODELS_HELP"), "note"));
            var scroll = new ScrollView(); scroll.AddToClassList("port-report-scroll"); content.Add(scroll);
            portCompatibilityList = new VisualElement(); portCompatibilityList.AddToClassList("port-report-list"); scroll.Add(portCompatibilityList);
            var actions = new VisualElement(); actions.AddToClassList("dialog-actions"); panel.Add(actions);
            actions.Add(Button(L.T("#BACK_TO_CONVERSION"), () => ShowPortCompatibilityReport(null, null), "primary"));
            portCompatibilityOverlay.style.display = DisplayStyle.None;
        }

        private void ShowPortCompatibilityReport(string target, IEnumerable<string> missing)
        {
            if (portCompatibilityOverlay == null) return;
            string[] models = missing?.Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToArray() ?? Array.Empty<string>();
            if (models.Length == 0) { portCompatibilityOverlay.style.display = DisplayStyle.None; return; }
            portCompatibilitySummary.text = L.F("#CONVERSION_MISSING_MODELS_ARG0_ARG1", models.Length, GameTargets.DisplayName(target));
            portCompatibilityList.Clear();
            foreach (string model in models) portCompatibilityList.Add(Label(model, "port-report-item"));
            portCompatibilityOverlay.style.display = DisplayStyle.Flex; portCompatibilityOverlay.BringToFront();
        }
        private void ShowRenameMapDialog(bool show)
        {
            if (renameMapOverlay == null) return;
            if (show) { HideCommandMenu(); CommitInspectorEdit(); renameMapName.SetValueWithoutNotify(slot.value); renameMapOverlay.style.display = DisplayStyle.Flex; renameMapOverlay.BringToFront(); renameMapName.Focus(); }
            else renameMapOverlay.style.display = DisplayStyle.None;
        }
        private void RenameCurrentMap()
        {
            string current = slot.value, renamed = renameMapName.value.Trim(); files.PathFor(renamed);
            if (string.Equals(current, renamed, StringComparison.OrdinalIgnoreCase)) { ShowRenameMapDialog(false); return; }
            if (files.Exists(renamed)) throw new IOException(L.T("#MAP_ALREADY_USES_NAME"));
            session.Edit(document => document.name = renamed); snapshot = session.Snapshot(); files.Save(current, snapshot); files.Rename(current, renamed);
            slot.SetValueWithoutNotify(renamed); lastSavedRevision = session.Revision; RefreshProjectSelector(renamed); RememberWorkspace(); ShowRenameMapDialog(false); Refresh();
            SetStatus(L.T("#MAP_RENAMED") + renamed);
        }

        private void ExportCurrentProject()
        {
            CommitInspectorEdit();
            if (session.Revision != lastSavedRevision) Save();
            string path = WindowsProjectFileDialog.Save(slot.value + MapFiles.PortableExtension);
            if (string.IsNullOrEmpty(path)) return;
            files.ExportPortable(path, session.Snapshot());
            SetStatus(L.F("#PROJECT_EXPORTED_ARG0", path));
        }

        private void ImportSharedProject()
        {
            string path = WindowsProjectFileDialog.Open();
            if (string.IsNullOrEmpty(path)) return;
            MapDocument document = files.ReadPortable(path);
            string project = UniqueImportedProjectSlot(path, document.name);
            files.Save(project, document);
            string importedTarget = GameTargets.Normalize(document.gameTarget);
            string currentTarget = GameTargets.Normalize(snapshot?.gameTarget ?? assetLibrary.TargetGame);
            if (importedTarget == currentTarget)
            {
                OpenProject(project, document, true);
                SetStatus(L.F("#PROJECT_IMPORTED_OPENED_ARG0", project));
            }
            else
            {
                RefreshProjectSelector(slot.value);
                SetStatus(L.F("#PROJECT_IMPORTED_OTHER_GAME_ARG0_ARG1", project, GameTargets.DisplayName(importedTarget)));
            }
        }

        private string UniqueImportedProjectSlot(string path, string documentName)
        {
            string fileName = Path.GetFileName(path) ?? "";
            if (fileName.EndsWith(MapFiles.PortableExtension, StringComparison.OrdinalIgnoreCase))
                fileName = fileName.Substring(0, fileName.Length - MapFiles.PortableExtension.Length);
            string source = string.IsNullOrWhiteSpace(fileName) ? documentName : fileName;
            var safe = new StringBuilder();
            foreach (char character in source ?? "")
            {
                char normalized = character == ' ' ? '-' : character;
                if ((normalized >= 'a' && normalized <= 'z') || (normalized >= 'A' && normalized <= 'Z') ||
                    (normalized >= '0' && normalized <= '9') || normalized == '-' || normalized == '_') safe.Append(normalized);
            }
            string stem = safe.ToString().Trim('-', '_');
            if (stem.Length == 0) stem = "imported-map";
            if (stem.Length > 52) stem = stem.Substring(0, 52).TrimEnd('-', '_');
            string candidate = stem; int suffix = 2;
            while (files.Exists(candidate)) candidate = stem + "-" + suffix++;
            return candidate;
        }

        private void ShowNewMapDialog(bool show)
        {
            if (newMapOverlay == null) return;
            if (show)
            {
                HideCommandMenu(); CommitInspectorEdit(); CancelPlacement();
                string target = portingMap && portSource != null
                    ? (GameTargets.Normalize(portSource.gameTarget) == GameTargets.R5Flowstate ? GameTargets.R5Reloaded : GameTargets.R5Flowstate)
                    : assetLibrary.TargetGame;
                newMapGame.SetValueWithoutNotify(newMapGame.choices[target == GameTargets.R5Flowstate ? 1 : 0]);
                newMapGame.SetEnabled(!portingMap);
                assetLibrary.SelectTarget(target, false); FillNewMapChoices();
                string stem = portingMap ? portSourceSlot + (target == GameTargets.R5Flowstate ? "-r5f" : "-r5r") : "new-map";
                string name = stem; int suffix = 2; while (files.Exists(name)) name = stem + "-" + suffix++;
                newMapModeNotice.text = portingMap ? L.T("#PORT_CREATED_COPY_EVERY_APEX") : "";
                newMapModeNotice.style.display = portingMap ? DisplayStyle.Flex : DisplayStyle.None;
                newMapConfirm.text = portingMap ? L.T("#VERIFY_MODELS_PORT") : L.T("#CREATE_LOAD_RPAKS");
                newMapName.SetValueWithoutNotify(name); newMapOverlay.style.display = DisplayStyle.Flex; newMapOverlay.BringToFront(); newMapName.Focus();
            }
            else
            {
                newMapOverlay.style.display = DisplayStyle.None;
                newMapGame.SetEnabled(true);
                assetLibrary.SelectTarget(snapshot?.gameTarget ?? GameTargets.R5Reloaded, false);
                portingMap = false; portSource = null; portSourceSlot = null;
            }
        }

        private void FillNewMapChoices()
        {
            var mapNames = new List<string>(); newMapToggles.Clear(); newMapSources.Clear();
            var left = new VisualElement(); left.AddToClassList("map-choice-column");
            var right = new VisualElement(); right.AddToClassList("map-choice-column"); right.AddToClassList("map-choice-column-right");
            newMapSources.Add(left); newMapSources.Add(right); int split = (assetLibrary.Maps.Count + 1) / 2;
            for (int index = 0; index < assetLibrary.Maps.Count; index++)
            {
                var map = assetLibrary.Maps[index]; mapNames.Add(map.Name); string id = map.Id;
                var toggle = new Toggle(map.Name) { tooltip = id }; (index < split ? left : right).Add(toggle); newMapToggles.Add(id, toggle);
            }
            if (mapNames.Count == 0) mapNames.Add(L.T("#SPECIFIED"));
            newMapPrimary.choices = mapNames; newMapPrimary.SetValueWithoutNotify(mapNames[0]); SyncNewMapPrimary();
        }

        private void SyncNewMapPrimary()
        {
            int selectedIndex = newMapPrimary.index; string primary = selectedIndex >= 0 && selectedIndex < assetLibrary.Maps.Count ? assetLibrary.Maps[selectedIndex].Id : "";
            foreach (var pair in newMapToggles)
            {
                bool selected = pair.Key == primary;
                if (selected) pair.Value.SetValueWithoutNotify(true);
                pair.Value.SetEnabled(!selected);
            }
        }

        private void CreateNewMap()
        {
            string name = newMapName.value.Trim(); files.PathFor(name);
            int selectedIndex = newMapPrimary.index; string primary = selectedIndex >= 0 && selectedIndex < assetLibrary.Maps.Count ? assetLibrary.Maps[selectedIndex].Id : "";
            var targets = newMapToggles.Where(pair => pair.Value.value).Select(pair => pair.Key).ToList();
            if (!string.IsNullOrEmpty(primary) && !targets.Contains(primary)) targets.Insert(0, primary);
            if (assetLibrary.Maps.Count > 0 && targets.Count == 0) throw new InvalidOperationException(L.T("#SELECT_LEAST_ONE_RPAK_SOURCE"));
            string gameTarget = newMapGame.index == 1 ? GameTargets.R5Flowstate : GameTargets.R5Reloaded;
            var document = new MapDocument { name = name, gameTarget = gameTarget, editingMap = primary, targetMaps = targets };
            document.Validate(); CommitInspectorEdit(); CancelPlacement(); session.Replace(document); selectedId = null; snapshot = session.Snapshot();
            files.Save(name, snapshot); slot.SetValueWithoutNotify(name); lastSavedRevision = session.Revision; RefreshProjectSelector(name); RememberWorkspace();
            newMapOverlay.style.display = DisplayStyle.None; assetLibrary.SelectTarget(gameTarget); Refresh();
            if (assetLibrary.Configured) _ = IndexAssets();
            SetStatus(assetLibrary.Configured ? L.T("#MAP_CREATED_LOADING_SELECTED_RPAKS") : L.T("#MAP_CREATED_CONFIGURE_GAME_SOURCES"));
        }

        private void BeginMapPort()
        {
            CommitInspectorEdit();
            if (session.Revision != lastSavedRevision) Save();
            portSource = session.Snapshot(); portSourceSlot = slot.value; portingMap = true; ShowNewMapDialog(true);
        }

        private async void PortCurrentMap()
        {
            if (!portingMap || portSource == null) return;
            newMapConfirm.SetEnabled(false);
            try
            {
                string name = newMapName.value.Trim(); files.PathFor(name);
                string target = newMapGame.index == 1 ? GameTargets.R5Flowstate : GameTargets.R5Reloaded;
                if (target == GameTargets.Normalize(portSource.gameTarget)) throw new InvalidOperationException(L.T("#CHOOSE_OTHER_GAME_PORT"));
                int selectedIndex = newMapPrimary.index;
                string primary = selectedIndex >= 0 && selectedIndex < assetLibrary.Maps.Count ? assetLibrary.Maps[selectedIndex].Id : "";
                var targets = newMapToggles.Where(pair => pair.Value.value).Select(pair => pair.Key).ToList();
                if (!string.IsNullOrEmpty(primary) && !targets.Contains(primary)) targets.Insert(0, primary);
                if (assetLibrary.Maps.Count > 0 && targets.Count == 0) throw new InvalidOperationException(L.T("#SELECT_LEAST_ONE_RPAK_SOURCE"));
                Loading(true, L.T("#VERIFYING_MODELS_TARGET_GAME"));
                await assetLibrary.IndexAsync(targets.ToArray(), new Progress<string>(message => { if (this != null) SetStatus(message); }));
                string[] missing = MapPortCompatibility.MissingModels(portSource, assetLibrary.Records.Where(record => record.Supports(targets)));
                if (missing.Length > 0)
                {
                    ShowPortCompatibilityReport(target, missing);
                    SetStatus(L.F("#CONVERSION_MISSING_MODELS_ARG0_ARG1", missing.Length, GameTargets.DisplayName(target)));
                    return;
                }
                var document = codec.Decode(codec.Encode(portSource));
                document.name = name; document.gameTarget = target; document.editingMap = primary; document.targetMaps = targets;
                document.Validate(); files.Save(name, document);
                portingMap = false; portSource = null; portSourceSlot = null; newMapOverlay.style.display = DisplayStyle.None;
                assetLibrary.SelectTarget(target);
                OpenProject(name, document, false);
                SetStatus(L.F("#MAP_PORTED_ARG0_ARG1", GameTargets.DisplayName(target), name));
            }
            catch (Exception exception)
            {
                SetStatus(exception.Message); Debug.LogWarning(exception);
            }
            finally
            {
                Loading(false); newMapConfirm.SetEnabled(true);
            }
        }

        private MapDocument LoadProjectForTarget(string project, string gameTarget)
        {
            if (string.IsNullOrWhiteSpace(project) || !files.Exists(project)) return null;
            var document = files.Load(project);
            return GameTargets.Normalize(document.gameTarget) == GameTargets.Normalize(gameTarget) ? document : null;
        }

        private void OpenProject(string project, MapDocument document, bool index)
        {
            string projectTarget = GameTargets.Normalize(document?.gameTarget);
            string activeTarget = GameTargets.Normalize(assetLibrary.TargetGame);
            if (projectTarget != activeTarget)
                throw new InvalidOperationException(L.F("#PROJECT_CREATED_ARG0_BUT_ACTIVE", GameTargets.DisplayName(projectTarget), GameTargets.DisplayName(activeTarget)));
            CancelPlacement(); session.Replace(document); selectedId = null; lastSavedRevision = session.Revision;
            changingProjectSelector = true;
            try { slot.SetValueWithoutNotify(project); }
            finally { changingProjectSelector = false; }
            RememberWorkspace(); RefreshProjectSelector(project); Refresh(); RestoreCachedModels();
            if (index && assetLibrary.Configured) _ = IndexAssets();
        }

        private bool SwitchWorkspaceTarget(string gameTarget)
        {
            string target = GameTargets.Normalize(gameTarget);
            if (snapshot != null && GameTargets.Normalize(snapshot.gameTarget) == target)
            {
                assetLibrary.SelectTarget(target); RefreshProjectSelector(slot.value); return true;
            }
            CommitInspectorEdit();
            if (snapshot != null && session.Revision != lastSavedRevision && slot != null)
            {
                files.Save(slot.value, snapshot); lastSavedRevision = session.Revision;
            }
            assetLibrary.SelectTarget(target);
            var compatible = files.ListSlots(target).Where(name => !IsRecoverySlot(name)).ToList();
            string preferred = PlayerPrefs.GetString(LastWorkspacePreference(target), "");
            string project = compatible.Contains(preferred) ? preferred : compatible.FirstOrDefault();
            if (project != null)
            {
                OpenProject(project, files.Load(project), false);
                return true;
            }
            string draft = DraftSlot(target);
            OpenProject(draft, new MapDocument { name = L.T("#STARTER_WORKSPACE"), gameTarget = target }, false);
            lastSavedRevision = -1;
            return false;
        }
        private bool NewMapDialogOpen => newMapOverlay != null && newMapOverlay.style.display.value != DisplayStyle.None;
        private bool PortCompatibilityReportOpen => portCompatibilityOverlay != null && portCompatibilityOverlay.style.display.value != DisplayStyle.None;
        private bool WorkspaceGameDialogOpen => workspaceGameOverlay != null && workspaceGameOverlay.style.display.value != DisplayStyle.None;
        private bool BlockingDialogOpen => SettingsOpen || IndexingOpen || AboutOpen || WorkspaceGameDialogOpen || NewMapDialogOpen || PortCompatibilityReportOpen || AssemblySaveOpen || (renameMapOverlay != null && renameMapOverlay.style.display.value != DisplayStyle.None);
    }

    internal static class WindowsProjectFileDialog
    {
        private const int Explorer = 0x00080000;
        private const int PathMustExist = 0x00000800;
        private const int FileMustExist = 0x00001000;
        private const int OverwritePrompt = 0x00000002;
        private const int NoChangeDirectory = 0x00000008;
        private const string Filter = "ReMap project (*.remap-project.json)\0*.remap-project.json\0All files (*.*)\0*.*\0\0";

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct OpenFileName
        {
            public int size;
            public IntPtr owner, instance;
            public string filter, customFilter;
            public int maximumCustomFilter, filterIndex;
            public StringBuilder file;
            public int maximumFile;
            public StringBuilder fileTitle;
            public int maximumFileTitle;
            public string initialDirectory, title;
            public int flags;
            public short fileOffset, fileExtension;
            public string defaultExtension;
            public IntPtr customData, hook;
            public string templateName;
            public IntPtr reserved;
            public int reservedValue, extendedFlags;
        }

        [DllImport("comdlg32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool GetSaveFileNameW(ref OpenFileName dialog);
        [DllImport("comdlg32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool GetOpenFileNameW(ref OpenFileName dialog);
        [DllImport("comdlg32.dll")]
        private static extern uint CommDlgExtendedError();
        [DllImport("user32.dll")]
        private static extern IntPtr GetActiveWindow();
        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr window);

        public static string Save(string suggestedName)
        {
            var dialog = Create(L.T("#EXPORT_CURRENT_PROJECT"), suggestedName,
                Explorer | PathMustExist | OverwritePrompt | NoChangeDirectory);
            bool accepted = GetSaveFileNameW(ref dialog);
            uint error = accepted ? 0 : CommDlgExtendedError();
            RestoreOwner(dialog.owner);
            if (!accepted)
            {
                if (error != 0) throw new InvalidOperationException(
                    "Windows save dialog failed (0x" + error.ToString("X4") + ").");
                return null;
            }
            return MapFiles.EnsurePortableExtension(dialog.file.ToString());
        }

        public static string Open()
        {
            var dialog = Create(L.T("#IMPORT_SHARED_PROJECT"), "",
                Explorer | PathMustExist | FileMustExist | NoChangeDirectory);
            bool accepted = GetOpenFileNameW(ref dialog);
            uint error = accepted ? 0 : CommDlgExtendedError();
            RestoreOwner(dialog.owner);
            if (!accepted)
            {
                if (error != 0) throw new InvalidOperationException(
                    "Windows open dialog failed (0x" + error.ToString("X4") + ").");
                return null;
            }
            return dialog.file.ToString();
        }

        private static IntPtr DialogOwner()
        {
            IntPtr active = GetActiveWindow();
            return active != IntPtr.Zero ? active : GetForegroundWindow();
        }

        private static void RestoreOwner(IntPtr owner)
        {
            if (owner != IntPtr.Zero) SetForegroundWindow(owner);
        }

        private static OpenFileName Create(string title, string initialName, int flags)
        {
            var dialog = new OpenFileName
            {
                owner = DialogOwner(),
                filter = Filter,
                filterIndex = 1,
                file = new StringBuilder(initialName ?? "", 4096),
                maximumFile = 4096,
                fileTitle = new StringBuilder(512),
                maximumFileTitle = 512,
                initialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                title = title,
                flags = flags,
                defaultExtension = MapFiles.PortableExtension.TrimStart('.')
            };
            dialog.size = Marshal.SizeOf(dialog);
            return dialog;
        }
    }
}
