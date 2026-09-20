using ReMap.Standalone.Core;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;
using UnityEngine.UIElements;

namespace ReMap.Standalone
{
    public sealed partial class ReMapApp
    {
        private VisualElement settingsOverlay, indexingOverlay, libraryDock, inspectorPanel;
        private ScrollView settingsScroll;
        private System.Threading.Tasks.Task settingsIndexTask;
        private DropdownField editingMapChoice;
        private TextField settingsAssetFolder;
        private Label missingMapNotice;
        private VisualElement thumbnailCategoryChoices;
        private readonly List<string> editingMapIds = new List<string>();

        private void BuildSettings()
        {
            settingsOverlay = new VisualElement(); settingsOverlay.AddToClassList("modal-overlay"); root.Add(settingsOverlay);
            var panel = new VisualElement(); settingsPanel = panel; panel.AddToClassList("settings-panel"); settingsOverlay.Add(panel);
            var heading = DockTitle(L.T("#SETTINGS")); heading.name = "settings-drag-title"; panel.Add(heading); BindResize(heading, "settings-move");

            heading.Add(Button("×", () => ShowSettings(false), "dock-close"));
            var scroll = settingsScroll = new ScrollView(); scroll.AddToClassList("settings-scroll"); panel.Add(scroll);
            scroll.Add(Label(L.T("#SETTINGS_PAGE_INTRO"), "settings-intro"));
            scroll.Add(Label(L.T("#INTERFACE"), "section-title"));
            var languages = AppLocalization.Languages;
            var language = new DropdownField(L.T("#LANGUAGE"), languages.Select(o => o.Name).ToList(),
                Math.Max(0, languages.ToList().FindIndex(o => o.Code == AppLocalization.SelectedLanguage))) { name = "language-setting" };
            var languageNotice = Label(L.T("#LANGUAGE_CHANGES_TAKE_EFFECT_NEXT"), "note");
            language.RegisterValueChangedCallback(_ => {
                AppLocalization.SelectForNextLaunch(languages[language.index].Code);
                languageNotice.text = L.T(AppLocalization.SelectedLanguage == L.Language
                    ? "Language changes take effect the next time you start the app."
                    : "Language saved. Restart the app to apply it; save your map first.");
            });
            scroll.Add(language); scroll.Add(languageNotice);
            var autoCollapseFolders = new Toggle(L.T("#AUTO_COLLAPSE_OTHER_FOLDERS")) {
                value = autoCollapseOtherFolders, name = "auto-collapse-folders-setting",
                tooltip = L.T("#AUTO_COLLAPSE_OTHER_FOLDERS_HELP")
            };
            autoCollapseFolders.RegisterValueChangedCallback(change => {
                autoCollapseOtherFolders = change.newValue;
                PlayerPrefs.SetInt(AutoCollapseOtherFoldersPreferenceKey, change.newValue ? 1 : 0);
                PlayerPrefs.Save();
                ApplyAutoCollapseHierarchy();
                RefreshObjects();
            });
            scroll.Add(autoCollapseFolders);
            string currentTarget = snapshot?.gameTarget ?? assetLibrary.TargetGame;
            scroll.Add(Label(L.T("#GAME_INSTALLATIONS"), "section-title"));
            var r5rPaths = new Foldout { text = "R5Reloaded", value = currentTarget != GameTargets.R5Flowstate }; r5rPaths.AddToClassList("game-paths-foldout"); scroll.Add(r5rPaths);
            var r5rGame = AddFolderPicker(r5rPaths, L.T("#R5RELOADED_FOLDER"), assetLibrary.Settings.r5ReloadedGameDirectory, () => "");
            var r5fPaths = new Foldout { text = "R5Flowstate", value = currentTarget == GameTargets.R5Flowstate }; r5fPaths.AddToClassList("game-paths-foldout"); scroll.Add(r5fPaths);
            var r5fGame = AddFolderPicker(r5fPaths, L.T("#R5FLOWSTATE_FOLDER"), assetLibrary.Settings.r5FlowstateGameDirectory, () => "");
            var officialPaths = new Foldout { text = "Apex Legends", value = false }; officialPaths.AddToClassList("game-paths-foldout"); scroll.Add(officialPaths);
            var officialApex = AddFolderPicker(officialPaths, L.T("#OFFICIAL_APEX_FOLDER"), assetLibrary.Settings.officialApexGameDirectory, () => "");
            var officialFallback = new Toggle(L.T("#OFFICIAL_TEXTURE_FALLBACK")) { value = assetLibrary.Settings.officialTextureFallback, tooltip = L.T("#OFFICIAL_TEXTURE_FALLBACK_HELP") };
            officialPaths.Add(officialFallback); officialPaths.Add(Label(L.T("#OFFICIAL_TEXTURE_FALLBACK_HELP"), "note"));
            scroll.Add(Label(L.T("#REMAP_DETECTS_PAKS_WIN64_AVAILABLE"), "note"));
            Action applyGameSources = () => {
                string selectedTarget = snapshot?.gameTarget ?? assetLibrary.TargetGame;
                assetLibrary.ConfigureProfiles(selectedTarget, r5rGame.value, "", r5fGame.value, "");
                assetLibrary.ConfigureOfficialTextureFallback(officialApex.value, officialFallback.value);
                CancelPlacement(); previewEntry = null; lastPreviewRequest = null;
                world.models.ForgetPrepared();
                foreach (var id in snapshot.objects.Where(o => o.assetId.StartsWith("apex:", StringComparison.Ordinal)).Select(o => o.assetId).Distinct()) world.Reload(id);
                if (currentThumbnail != null) Destroy(currentThumbnail); currentThumbnail = null; assetPreview.image = null;
                previewText.text = L.T("#SOURCES_CHANGED_RUN_INDEXING_AGAIN"); previewFailures.Clear(); Refresh(); FillMapChoices(); RefreshProjectSelector(slot.value);
                SetStatus(assetLibrary.Configured ? L.T("#PATHS_SAVED_OPEN_INDEXING") : L.T("#PATHS_SAVED"));
            };
            Action<string> setupGame = target => {
                try
                {
                    applyGameSources();
                    bool flowstate = target == GameTargets.R5Flowstate;
                    string game = flowstate ? assetLibrary.Settings.r5FlowstateGameDirectory : assetLibrary.Settings.r5ReloadedGameDirectory;
                    string platform = flowstate ? assetLibrary.Settings.r5FlowstatePlatformDirectory : assetLibrary.Settings.r5ReloadedPlatformDirectory;
                    int count = ReMapEntExporter.EnsureDiskPriority(game, platform).Count;
                    SetStatus(L.F("#FIRST_TIME_SETUP_COMPLETE_ARG0_ARG1", GameTargets.DisplayName(target), count));
                }
                catch (Exception exception)
                {
                    SetStatus(L.F("#FIRST_TIME_SETUP_FAILED_ARG0", exception.Message));
                }
            };
            r5rPaths.Add(Label(L.T("#FIRST_TIME_SETUP_HELP"), "note"));
            r5rPaths.Add(Button(L.T("#SET_UP_REMAP_FIRST_TIME"), () => setupGame(GameTargets.R5Reloaded)));
            r5fPaths.Add(Label(L.T("#FIRST_TIME_SETUP_HELP"), "note"));
            r5fPaths.Add(Button(L.T("#SET_UP_REMAP_FIRST_TIME"), () => setupGame(GameTargets.R5Flowstate)));
            scroll.Add(Button(L.T("#SAVE_PATHS"), applyGameSources));
            scroll.Add(Label(L.T("#ASSET_CACHE"), "section-title"));
            scroll.Add(Label(L.T("#COMPATIBLE_MODELS_TEXTURES_SHARED_BETWEEN"), "note"));
            settingsAssetFolder = AddFolderPicker(scroll, L.T("#ASSET_EXPORT_FOLDER"),
                assetLibrary.AssetExportDirectory, () => assetLibrary.AssetExportDirectory);
            settingsAssetFolder.isDelayed = true;
            scroll.Add(Label(L.T("#ASSET_EXPORT_FOLDER_HELP"), "note"));
            scroll.Add(Button(L.T("#SAVE_ASSET_EXPORT_FOLDER"), () => {
                assetLibrary.ConfigureAssetExportDirectory(settingsAssetFolder.value);
                settingsAssetFolder.SetValueWithoutNotify(assetLibrary.AssetExportDirectory);
                welcomeAssetFolder?.SetValueWithoutNotify(assetLibrary.AssetExportDirectory);
                CancelPlacement(); previewEntry = null; lastPreviewRequest = null; previewFailures.Clear();
                world.models.ForgetPrepared();
                foreach (var id in snapshot.objects.Where(item => item.assetId.StartsWith("apex:", StringComparison.Ordinal)).Select(item => item.assetId).Distinct()) world.Reload(id);
                if (currentThumbnail != null) Destroy(currentThumbnail); currentThumbnail = null; assetPreview.image = null;
                Refresh(); SetStatus(L.T("#ASSET_EXPORT_FOLDER_SAVED"));
            }));
            scroll.Add(Button(L.T("#OPEN_CACHE_FOLDER"), () => {
                Directory.CreateDirectory(assetLibrary.AssetExportDirectory);
                Process.Start(new ProcessStartInfo { FileName = assetLibrary.AssetExportDirectory, UseShellExecute = true });
            }));
            var thumbnailCategories = new Foldout { text = L.T("#THUMBNAIL_CATEGORY_FILTER"), value = false };
            thumbnailCategories.AddToClassList("game-paths-foldout"); scroll.Add(thumbnailCategories);
            thumbnailCategories.Add(Label(L.T("#THUMBNAIL_CATEGORY_FILTER_HELP"), "note"));
            var categoryActions = new VisualElement(); categoryActions.AddToClassList("inspector-actions");
            categoryActions.Add(Button(L.T("#USE_RECOMMENDED_FILTERS"), () => {
                ApplySkippedThumbnailCategories(RsxAssetLibrary.DefaultSkippedThumbnailCategories);
                RefreshThumbnailCategoryChoices();
            }));
            categoryActions.Add(Button(L.T("#EXPORT_ALL_CATEGORIES"), () => {
                ApplySkippedThumbnailCategories(Array.Empty<string>());
                RefreshThumbnailCategoryChoices();
            }));
            thumbnailCategories.Add(categoryActions);
            thumbnailCategoryChoices = new VisualElement(); thumbnailCategoryChoices.AddToClassList("map-choices");
            thumbnailCategories.Add(thumbnailCategoryChoices); RefreshThumbnailCategoryChoices();
            if (LiveMapEnabled)
            {
                scroll.Add(Label(L.T("#GAME_CONNECTION"), "section-title"));
                string savedAddress = string.IsNullOrWhiteSpace(assetLibrary.Settings.rconAddress) ? "[::ffff:127.0.0.1]:37015" : assetLibrary.Settings.rconAddress;
                var rconAddress = new TextField(L.T("#SERVER_ADDRESS")) { value = savedAddress }; rconAddress.AddToClassList("settings-field"); scroll.Add(rconAddress);
                var rconKey = new TextField(L.T("#RCON_AES_KEY")) { value = assetLibrary.Settings.rconKey ?? "" }; rconKey.AddToClassList("settings-field"); scroll.Add(rconKey);
                var rconPassword = new TextField(L.T("#RCON_PASSWORD")) { value = assetLibrary.Settings.rconPassword ?? "", isPasswordField = true }; rconPassword.AddToClassList("settings-field"); scroll.Add(rconPassword);
                scroll.Add(Label(L.T("#R5FLOWSTATE_USE_OPEN_LAUNCHER_CONSOLE"), "note"));
                scroll.Add(Button(L.T("#SAVE_GAME_CONNECTION"), () => {
                    assetLibrary.Settings.rconAddress = rconAddress.value.Trim(); assetLibrary.Settings.rconKey = rconKey.value.Trim(); assetLibrary.Settings.rconPassword = rconPassword.value;
                    assetLibrary.SaveSettings(); SetStatus(L.T("#GAME_CONNECTION_SAVED"));
                }));
            }
            scroll.Add(Label(L.T("#MAP_REFERENCE_DISPLAY"), "section-title"));
            var showMainBsp = new Toggle(L.T("#SHOW_MAIN_BSP")) { value = assetLibrary.Settings.showMainBsp };
            showMainBsp.tooltip = L.T("#SHOW_MAIN_BSP_HELP");
            showMainBsp.RegisterValueChangedCallback(change => SetMapReferenceOptions(showBsp: change.newValue));
            scroll.Add(showMainBsp);
            var showMprtModels = new Toggle(L.T("#SHOW_MPRT_MODELS")) {
                value = assetLibrary.Settings.showMprtModels, name = MprtVisibilityToggleName };
            showMprtModels.tooltip = L.T("#SHOW_MPRT_MODELS_HELP");
            showMprtModels.RegisterValueChangedCallback(change => SetMapReferenceOptions(showMprt: change.newValue));
            scroll.Add(showMprtModels);
            AddMprtStreamingSettings(scroll);
            scroll.Add(Button(L.T("#RELOAD_MAP_REFERENCE"), () => RequestMapReferenceReload()));
            var actions = new VisualElement(); actions.AddToClassList("settings-actions"); panel.Add(actions);
            actions.Add(Button(L.T("#CLOSE"), () => ShowSettings(false)));
            var grip = ResizeHandle("settings", false); grip.AddToClassList("settings-resize-grip"); panel.Add(grip);
            ShowSettings(false);
            BuildIndexingPage();
        }

        private TextField AddFolderPicker(VisualElement parent, string label, string value, Func<string> fallback)
        {
            var row = new VisualElement(); row.AddToClassList("folder-picker"); parent.Add(row);
            var field = new TextField(label) { value = value ?? "" }; field.AddToClassList("settings-field"); row.Add(field);
            row.Add(Button(L.T("#BROWSE_FOLDER"), () => {
                string initial = Directory.Exists(field.value) ? field.value : fallback?.Invoke();
                string selected = WindowsFolderPicker.Open(L.T("#SELECT_FOLDER"), initial);
                if (!string.IsNullOrWhiteSpace(selected)) field.value = selected;
            }));
            return field;
        }

        private void ApplySkippedThumbnailCategories(IEnumerable<string> categories)
        {
            assetLibrary.ConfigureSkippedThumbnailCategories(categories);
            visibleThumbnailPage = null;
            var targets = new HashSet<string>(Targets, StringComparer.OrdinalIgnoreCase);
            UpdateThumbnailProgress(AutomaticThumbnailRecords(targets));
            UpdateThumbnailControls(); RefreshCatalog();
            if (!thumbnailPaused && !backgroundStopped) _ = PrepareThumbnails();
        }

        private void RefreshThumbnailCategoryChoices()
        {
            if (thumbnailCategoryChoices == null || assetLibrary == null) return;
            thumbnailCategoryChoices.Clear();
            var skipped = new HashSet<string>(assetLibrary.Settings.skippedThumbnailCategories ??
                Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
            var categories = assetLibrary.Records.GroupBy(record => record.Category,
                StringComparer.OrdinalIgnoreCase).Select(group => new { Name = group.Key, Count = group.Count() })
                .OrderBy(group => group.Name, StringComparer.OrdinalIgnoreCase).ToArray();
            if (categories.Length == 0)
            {
                thumbnailCategoryChoices.Add(Label(L.T("#INDEX_FIRST_TO_LIST_MODEL_CATEGORIES"), "note"));
                return;
            }
            foreach (var category in categories)
            {
                var toggle = new Toggle(L.F("#THUMBNAIL_CATEGORY_ARG0_ARG1", category.Name, category.Count)) {
                    value = skipped.Contains(category.Name), tooltip = "mdl/" + category.Name + "/"
                };
                toggle.RegisterValueChangedCallback(change => {
                    var selected = new HashSet<string>(assetLibrary.Settings.skippedThumbnailCategories ??
                        Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
                    if (change.newValue) selected.Add(category.Name); else selected.Remove(category.Name);
                    ApplySkippedThumbnailCategories(selected);
                });
                thumbnailCategoryChoices.Add(toggle);
            }
        }

        private void BuildIndexingPage()
        {
            indexingOverlay = new VisualElement(); indexingOverlay.AddToClassList("modal-overlay"); root.Add(indexingOverlay);
            var panel = new VisualElement(); panel.AddToClassList("settings-panel"); indexingOverlay.Add(panel);
            var heading = DockTitle(L.T("#INDEXING_PAGE")); panel.Add(heading);
            heading.Add(Button("×", () => ShowIndexing(false), "dock-close"));
            var scroll = new ScrollView(); scroll.AddToClassList("settings-scroll"); panel.Add(scroll);
            scroll.Add(Label(L.T("#INDEXING_PAGE_INTRO"), "settings-intro"));
            scroll.Add(Label(L.F("#PROJECT_TARGET_LOCKED_ARG0", GameTargets.DisplayName(snapshot?.gameTarget ?? assetLibrary.TargetGame)), "project-target-lock"));
            scroll.Add(Label(L.T("#PROJECT_TARGET_LOCKED_HELP"), "note"));
            scroll.Add(Label(L.T("#MAP_CONFIGURATION"), "section-title"));
            scroll.Add(Label(L.T("#CHOOSE_APEX_MAP_BUILDING_AUTOMATICALLY"), "library-state"));
            missingMapNotice = Label("", "map-source-warning"); scroll.Add(missingMapNotice);
            editingMapChoice = new DropdownField(L.T("#EDITED_MAP"), new List<string> { L.T("#SPECIFIED") }, 0) { name = "editing-map-setting" };
            editingMapChoice.RegisterValueChangedCallback(e =>
            {
                int index = editingMapChoice.choices.IndexOf(e.newValue);
                string id = index >= 0 && index < editingMapIds.Count ? editingMapIds[index] : "";
                session.Edit(doc => { doc.editingMap = id; if (!string.IsNullOrEmpty(id) && !doc.targetMaps.Contains(id)) doc.targetMaps.Add(id); });
                assetLibrary.LastTargetMaps = session.Snapshot().targetMaps.ToArray(); assetLibrary.SaveSettings(); Refresh();
                SetStatus(string.IsNullOrEmpty(id) ? L.T("#NO_EDITED_MAP_SELECTED") : L.T("#EDITED_MAP_SELECTED_ARCHIVE_ENABLED"));
            });
            scroll.Add(editingMapChoice);
            scroll.Add(Label(L.T("#ADDITIONAL_MODEL_SOURCES"), "section-title"));
            scroll.Add(Label(L.T("#ENABLE_OTHER_MAPS_NEED_ASSETS"), "library-state"));
            mapChoices = new VisualElement(); mapChoices.AddToClassList("map-choices"); scroll.Add(mapChoices);
            var actions = new VisualElement(); actions.AddToClassList("settings-actions"); panel.Add(actions);
            actions.Add(Button(L.T("#CLOSE_WITHOUT_INDEXING"), () => ShowIndexing(false)));
            actions.Add(Button(L.T("#APPLY_INDEX_CLOSE"), ApplyIndexingChanges, "primary"));
            indexingOverlay.style.display = DisplayStyle.None;
        }

        private void AddMprtStreamingSettings(VisualElement parent)
        {
            AssetSourceSettings settings = assetLibrary.Settings;
            var choices = new List<string> { L.T("#MPRT_PRESET_PERFORMANCE"), L.T("#MPRT_PRESET_BALANCED"),
                L.T("#MPRT_PRESET_QUALITY"), L.T("#MPRT_PRESET_CUSTOM") };
            var preset = new DropdownField(L.T("#MPRT_PRESET"), choices, Mathf.Clamp(settings.mprtPreset, 0, 3));
            var distance = new FloatField(L.T("#MPRT_DISTANCE_METERS")) { value = settings.mprtDistance };
            var maximum = new IntegerField(L.T("#MPRT_MAXIMUM_ACTIVE")) { value = settings.mprtMaxActive };
            var minimumSize = new FloatField(L.T("#MPRT_MINIMUM_SIZE_METERS")) { value = settings.mprtMinimumSize };
            var density = new IntegerField(L.T("#MPRT_DENSITY_PERCENT")) { value = settings.mprtDensityPercent };
            foreach (var field in new VisualElement[] { preset, distance, maximum, minimumSize, density })
                field.AddToClassList("settings-field");
            foreach (var field in new VisualElement[] { distance, maximum, minimumSize, density })
                field.AddToClassList("settings-number-field");
            void RefreshValues()
            {
                preset.SetValueWithoutNotify(choices[Mathf.Clamp(settings.mprtPreset, 0, 3)]);
                distance.SetValueWithoutNotify(settings.mprtDistance);
                maximum.SetValueWithoutNotify(settings.mprtMaxActive);
                minimumSize.SetValueWithoutNotify(settings.mprtMinimumSize);
                density.SetValueWithoutNotify(settings.mprtDensityPercent);
            }
            preset.RegisterValueChangedCallback(change => {
                SetMprtReferenceTuning(preset: choices.IndexOf(change.newValue)); RefreshValues();
            });
            distance.RegisterValueChangedCallback(change => {
                SetMprtReferenceTuning(distance: change.newValue); RefreshValues();
            });
            maximum.RegisterValueChangedCallback(change => {
                SetMprtReferenceTuning(maximum: change.newValue); RefreshValues();
            });
            minimumSize.RegisterValueChangedCallback(change => {
                SetMprtReferenceTuning(minimumSize: change.newValue); RefreshValues();
            });
            density.RegisterValueChangedCallback(change => {
                SetMprtReferenceTuning(density: change.newValue); RefreshValues();
            });
            parent.Add(preset); parent.Add(distance); parent.Add(maximum); parent.Add(minimumSize); parent.Add(density);
            parent.Add(Label(L.T("#MPRT_STREAMING_HELP"), "note"));
        }
        private void ShowSettings(bool show)
        {
            if (settingsOverlay == null) return;
            if (show)
            {
                CommitInspectorEdit(); CancelGizmoDrag(); world.ClearPreview();
                settingsAssetFolder?.SetValueWithoutNotify(assetLibrary.AssetExportDirectory);
                RefreshThumbnailCategoryChoices();
                settingsScroll?.schedule.Execute(() => settingsScroll.scrollOffset = Vector2.zero);
            }
            settingsOverlay.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
            if (show) { AdaptLayout(); settingsOverlay.BringToFront(); } else { EndLayoutResize(); SaveLayout(); }
        }
        private bool SettingsOpen => settingsOverlay != null && settingsOverlay.style.display.value != DisplayStyle.None;

        private void ShowIndexing(bool show)
        {
            if (indexingOverlay == null) return;
            if (show)
            {
                CommitInspectorEdit(); CancelGizmoDrag(); world.ClearPreview(); FillMapChoices();
                var target = indexingOverlay.Q<Label>(className: "project-target-lock");
                if (target != null) target.text = L.F("#PROJECT_TARGET_LOCKED_ARG0", GameTargets.DisplayName(snapshot?.gameTarget ?? assetLibrary.TargetGame));
            }
            indexingOverlay.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
            if (show) { AdaptLayout(); indexingOverlay.BringToFront(); }
        }
        private void ApplyIndexingChanges()
        {
            ShowIndexing(false);
            settingsIndexTask = IndexAssets();
        }
        private bool IndexingOpen => indexingOverlay != null && indexingOverlay.style.display.value != DisplayStyle.None;

        private void FillEditingMapChoice()
        {
            if (editingMapChoice == null) return;
            editingMapIds.Clear(); editingMapIds.Add("");
            var choices = new List<string> { L.T("#SPECIFIED") };
            foreach (var map in assetLibrary.Maps) { editingMapIds.Add(map.Id); choices.Add(map.Name.Split('·')[0].Trim()); }
            editingMapChoice.choices = choices; RefreshEditingMapChoice();
        }

        private void RefreshEditingMapChoice()
        {
            if (editingMapChoice == null || snapshot == null || editingMapChoice.choices.Count == 0) return;
            int index = editingMapIds.IndexOf(snapshot.editingMap ?? "");
            if (index < 0 || index >= editingMapChoice.choices.Count) index = 0;
            editingMapChoice.SetValueWithoutNotify(editingMapChoice.choices[index]);
        }
    }

    internal static class WindowsFolderPicker
    {
        private const uint BrowseInitialized = 1;
        private const uint SetSelection = 0x467;
        private const uint ReturnOnlyFileSystemDirectories = 0x0001;
        private const uint UseNewUserInterface = 0x0050;

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int BrowseCallback(IntPtr window, uint message, IntPtr parameter, IntPtr data);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct BrowseInfo
        {
            public IntPtr owner, root, displayName;
            [MarshalAs(UnmanagedType.LPWStr)] public string title;
            public uint flags;
            [MarshalAs(UnmanagedType.FunctionPtr)] public BrowseCallback callback;
            public IntPtr data;
            public int image;
        }

        public static string Open(string title, string initialDirectory)
        {
            if (Application.platform != RuntimePlatform.WindowsEditor && Application.platform != RuntimePlatform.WindowsPlayer)
                return "";
            IntPtr initial = IntPtr.Zero;
            IntPtr item = IntPtr.Zero;
            try
            {
                if (!string.IsNullOrWhiteSpace(initialDirectory) && Directory.Exists(initialDirectory))
                    initial = Marshal.StringToHGlobalUni(Path.GetFullPath(initialDirectory));
                BrowseCallback callback = (window, message, parameter, data) => {
                    if (message == BrowseInitialized && data != IntPtr.Zero)
                        SendMessage(window, SetSelection, new IntPtr(1), data);
                    return 0;
                };
                var info = new BrowseInfo
                {
                    owner = GetActiveWindow(), title = title, flags = ReturnOnlyFileSystemDirectories | UseNewUserInterface,
                    callback = callback, data = initial
                };
                item = SHBrowseForFolder(ref info);
                if (item == IntPtr.Zero) return "";
                var path = new StringBuilder(32768);
                return SHGetPathFromIDList(item, path) ? path.ToString() : "";
            }
            finally
            {
                if (item != IntPtr.Zero) Marshal.FreeCoTaskMem(item);
                if (initial != IntPtr.Zero) Marshal.FreeHGlobal(initial);
            }
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SHBrowseForFolder(ref BrowseInfo info);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SHGetPathFromIDList(IntPtr item, StringBuilder path);

        [DllImport("user32.dll")]
        private static extern IntPtr GetActiveWindow();

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SendMessage(IntPtr window, uint message, IntPtr parameter, IntPtr data);
    }
}
