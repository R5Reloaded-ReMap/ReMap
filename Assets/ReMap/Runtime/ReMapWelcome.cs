using ReMap.Standalone.Core;
using UnityEngine.UIElements;

namespace ReMap.Standalone
{
    public sealed partial class ReMapApp
    {
        private VisualElement welcomeOverlay;
        private TextField welcomeAssetFolder;

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
            content.Add(Label(L.T("#DETECTED_GAME_INSTALLATIONS"), "section-title"));
            AddWelcomeTarget(content, GameTargets.R5Reloaded);
            AddWelcomeTarget(content, GameTargets.R5Flowstate);
            content.Add(Label(L.T("#ASSET_CACHE"), "section-title"));
            welcomeAssetFolder = AddFolderPicker(content, L.T("#ASSET_EXPORT_FOLDER"),
                assetLibrary.AssetExportDirectory, () => assetLibrary.AssetExportDirectory);
            welcomeAssetFolder.isDelayed = true;
            content.Add(Label(L.T("#ASSET_EXPORT_FOLDER_HELP"), "note"));

            var actions = new VisualElement();
            actions.AddToClassList("dialog-actions");
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
