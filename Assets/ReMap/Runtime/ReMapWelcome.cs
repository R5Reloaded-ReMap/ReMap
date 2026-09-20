using System;
using System.IO;
using ReMap.Standalone.Core;
using UnityEngine.UIElements;

namespace ReMap.Standalone
{
    public sealed partial class ReMapApp
    {
        private VisualElement welcomeOverlay;
        private VisualElement welcomeTargets;
        private TextField welcomeAssetFolder;
        private Label welcomeImportStatus;

        private void BuildWelcome()
        {
            welcomeOverlay = new VisualElement();
            welcomeOverlay.AddToClassList("modal-overlay");
            root.Add(welcomeOverlay);

            var panel = new VisualElement();
            panel.AddToClassList("welcome-panel");
            welcomeOverlay.Add(panel);

            panel.Add(DockTitle(L.T("#WELCOME_REMAP")));
            var content = new ScrollView();
            content.AddToClassList("welcome-content");
            panel.Add(content);
            content.Add(BrandLogo("welcome-logo"));
            content.Add(Label(L.T("#WELCOME_TAGLINE"), "welcome-tagline"));
            content.Add(Label(L.T("#WELCOME_INTRO"), "welcome-copy"));
            content.Add(Label(L.T("#ASSET_CACHE"), "section-title"));
            welcomeAssetFolder = AddFolderPicker(content, L.T("#ASSET_EXPORT_FOLDER"),
                assetLibrary.AssetExportDirectory, () => assetLibrary.AssetExportDirectory);
            welcomeAssetFolder.isDelayed = true;
            content.Add(Label(L.T("#ASSET_EXPORT_FOLDER_HELP"), "note"));
            var importBeta1 = Button(L.T("#IMPORT_BETA1_SETTINGS"), ImportWelcomeBeta1Settings, "welcome-import");
            importBeta1.name = "import-beta1-settings"; content.Add(importBeta1);
            welcomeImportStatus = Label(L.T("#IMPORT_BETA1_SETTINGS_HELP"), "note");
            welcomeImportStatus.name = "import-beta1-status"; content.Add(welcomeImportStatus);
            content.Add(Label(L.T("#DETECTED_GAME_INSTALLATIONS"), "section-title"));
            welcomeTargets = new VisualElement(); welcomeTargets.AddToClassList("welcome-targets"); content.Add(welcomeTargets);
            RefreshWelcomeTargets();

            var actions = new VisualElement();
            actions.AddToClassList("dialog-actions");
            actions.Add(Button(L.T("#GETTING_STARTED"), () => ShowHelpGuide(0)));
            actions.Add(Button(L.T("#OPEN_SETTINGS"), () => {
                ApplyWelcomeAssetFolder(); ShowWelcome(false); ShowSettings(true);
            }));
            actions.Add(Button(L.T("#CONTINUE_WITHOUT_GAME_FILES"), () => {
                ApplyWelcomeAssetFolder();
                ShowWelcome(false);
                SetStatus(L.T("#GAME_FILES_CAN_BE_CONFIGURED_LATER"));
            }));
            panel.Add(actions);
            ShowWelcome(false);
        }

        private void ImportWelcomeBeta1Settings()
        {
            string initial = Directory.GetParent(assetLibrary.LocalRoot)?.FullName ?? assetLibrary.LocalRoot;
            string selected = WindowsFolderPicker.Open(L.T("#SELECT_BETA1_FOLDER"), initial);
            if (string.IsNullOrWhiteSpace(selected)) return;
            try
            {
                string cache = assetLibrary.ImportLegacySettings(selected);
                welcomeAssetFolder.SetValueWithoutNotify(cache);
                settingsAssetFolder?.SetValueWithoutNotify(cache);
                RefreshWelcomeTargets();
                welcomeImportStatus.text = L.F("#BETA1_SETTINGS_IMPORTED_ARG0", cache);
                SetStatus(welcomeImportStatus.text);
            }
            catch (Exception exception)
            {
                welcomeImportStatus.text = L.F("#BETA1_SETTINGS_IMPORT_FAILED_ARG0", exception.Message);
                SetStatus(welcomeImportStatus.text);
            }
        }

        private void RefreshWelcomeTargets()
        {
            if (welcomeTargets == null) return;
            welcomeTargets.Clear();
            AddWelcomeTarget(welcomeTargets, GameTargets.R5Reloaded);
            AddWelcomeTarget(welcomeTargets, GameTargets.R5Flowstate);
        }

        private void AddWelcomeTarget(VisualElement parent, string target)
        {
            bool flowstate = GameTargets.Normalize(target) == GameTargets.R5Flowstate;
            string path = flowstate ? assetLibrary.Settings.r5FlowstateGameDirectory : assetLibrary.Settings.r5ReloadedGameDirectory;
            bool detected = assetLibrary.HasInstallationFor(target);
            var row = new VisualElement(); row.AddToClassList("welcome-target"); parent.Add(row);
            var text = new VisualElement(); text.AddToClassList("welcome-target-copy"); row.Add(text);
            text.Add(Label(GameTargets.DisplayName(target), "welcome-target-name"));
            text.Add(Label(detected ? L.F("#INSTALLATION_DETECTED_ARG0", path) : L.T("#INSTALLATION_NOT_DETECTED"), "welcome-target-path"));
            var use = Button(L.F("#USE_ARG0", GameTargets.DisplayName(target)), () => UseWelcomeTarget(target), "primary");
            use.SetEnabled(detected); row.Add(use);
        }

        private void UseWelcomeTarget(string target)
        {
            ApplyWelcomeAssetFolder();
            assetLibrary.SelectTarget(target);
            session.Edit(document => document.gameTarget = GameTargets.Normalize(target));
            RefreshProjectSelector(DraftSlot(target));
            RefreshWorkspaceGameButton();
            Refresh();
            ShowWelcome(false);
            SetStatus(L.T("#GAME_SELECTED_INDEXING_ASSETS"));
            _ = IndexAssets();
        }

        private void ApplyWelcomeAssetFolder()
        {
            assetLibrary.ConfigureAssetExportDirectory(welcomeAssetFolder?.value);
            welcomeAssetFolder?.SetValueWithoutNotify(assetLibrary.AssetExportDirectory);
            settingsAssetFolder?.SetValueWithoutNotify(assetLibrary.AssetExportDirectory);
        }

        private void ShowWelcome(bool show)
        {
            if (welcomeOverlay == null) return;
            welcomeOverlay.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
            if (show) welcomeOverlay.BringToFront();
        }

        private bool WelcomeOpen => welcomeOverlay != null &&
            welcomeOverlay.style.display.value != DisplayStyle.None;
    }
}
