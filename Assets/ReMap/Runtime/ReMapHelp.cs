using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using ReMap.Standalone.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace ReMap.Standalone
{
    public sealed partial class ReMapApp
    {
        private static readonly string[] HelpTitleKeys = {
            "#HELP_SETUP_TITLE", "#HELP_PROJECT_TITLE", "#HELP_EDIT_TITLE",
            "#HELP_EXPORT_TITLE", "#HELP_SHORTCUTS_TITLE"
        };
        private static readonly string[] HelpBodyKeys = {
            "#HELP_SETUP_BODY", "#HELP_PROJECT_BODY", "#HELP_EDIT_BODY",
            "#HELP_EXPORT_BODY", "#HELP_SHORTCUTS_BODY"
        };

        private VisualElement helpOverlay;
        private Label helpPageTitle, helpPageBody, helpPageCounter;
        private Button helpPrevious, helpNext;
        private int helpPageIndex;

        private void BuildHelpGuide()
        {
            helpOverlay = new VisualElement();
            helpOverlay.AddToClassList("modal-overlay");
            root.Add(helpOverlay);

            var panel = new VisualElement();
            panel.AddToClassList("help-panel");
            helpOverlay.Add(panel);

            var heading = DockTitle(L.T("#GETTING_STARTED"));
            panel.Add(heading);
            heading.Add(Button("×", () => ShowHelpGuide(false), "dock-close"));

            var content = new ScrollView();
            content.AddToClassList("help-content");
            panel.Add(content);

            helpPageTitle = Label("", "help-page-title");
            helpPageBody = Label("", "help-page-body");
            content.Add(helpPageTitle);
            content.Add(helpPageBody);

            var actions = new VisualElement();
            actions.AddToClassList("dialog-actions");
            helpPageCounter = Label("", "help-page-counter");
            actions.Add(helpPageCounter);
            helpPrevious = Button(L.T("#PREVIOUS"), () => SetHelpPage(helpPageIndex - 1));
            helpNext = Button(L.T("#NEXT"), () => SetHelpPage(helpPageIndex + 1), "primary");
            actions.Add(helpPrevious);
            actions.Add(helpNext);
            actions.Add(Button(L.T("#CLOSE"), () => ShowHelpGuide(false)));
            panel.Add(actions);

            helpOverlay.RegisterCallback<KeyDownEvent>(e => {
                if (e.keyCode == KeyCode.Escape && HelpOpen) { ShowHelpGuide(false); e.StopPropagation(); }
            }, TrickleDown.TrickleDown);
            ShowHelpGuide(false);
        }

        private void ShowHelpGuide(int page)
        {
            SetHelpPage(page);
            ShowHelpGuide(true);
        }

        private void ShowHelpGuide(bool show)
        {
            if (helpOverlay == null) return;
            if (show)
            {
                CommitInspectorEdit();
                CancelGizmoDrag();
                world.ClearPreview();
            }
            helpOverlay.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
            if (show) helpOverlay.BringToFront();
        }

        private void SetHelpPage(int page)
        {
            helpPageIndex = Mathf.Clamp(page, 0, HelpTitleKeys.Length - 1);
            if (helpPageTitle == null) return;
            helpPageTitle.text = L.T(HelpTitleKeys[helpPageIndex]);
            helpPageBody.text = L.T(HelpBodyKeys[helpPageIndex]);
            helpPageCounter.text = L.F("#HELP_PAGE_ARG0_ARG1", helpPageIndex + 1, HelpTitleKeys.Length);
            helpPrevious.SetEnabled(helpPageIndex > 0);
            helpNext.SetEnabled(helpPageIndex + 1 < HelpTitleKeys.Length);
        }

        private void OpenLogFolder()
        {
            string log = Application.consoleLogPath;
            string directory = string.IsNullOrWhiteSpace(log) ? Application.persistentDataPath : Path.GetDirectoryName(log);
            if (string.IsNullOrWhiteSpace(directory)) throw new DirectoryNotFoundException(L.T("#LOG_FOLDER_NOT_FOUND"));
            Directory.CreateDirectory(directory);
            Process.Start(new ProcessStartInfo { FileName = directory, UseShellExecute = true });
        }

        private void CopyDiagnostics()
        {
            var info = new StringBuilder();
            info.AppendLine("ReMap " + Application.version);
            info.AppendLine("Build: " + (UnityEngine.Debug.isDebugBuild ? "Development" : "Release"));
            info.AppendLine("OS: " + SystemInfo.operatingSystem);
            info.AppendLine("Unity: " + Application.unityVersion);
            info.AppendLine("Target: " + (assetLibrary == null ? "-" : assetLibrary.TargetGame));
            info.AppendLine("Settings: " + (assetLibrary == null ? "-" : Path.Combine(assetLibrary.SettingsRoot, "asset-source.local.json")));
            info.AppendLine("Asset cache: " + (assetLibrary == null ? "-" : assetLibrary.AssetExportDirectory));
            info.AppendLine("Game: " + (assetLibrary == null ? "-" : assetLibrary.GameDirectory));
            info.AppendLine("Platform: " + (assetLibrary == null ? "-" : assetLibrary.PlatformDirectory));
            info.AppendLine("Log: " + Application.consoleLogPath);
            GUIUtility.systemCopyBuffer = info.ToString().TrimEnd();
            SetStatus(L.T("#DIAGNOSTICS_COPIED"));
        }

        private bool HelpOpen => helpOverlay != null && helpOverlay.style.display.value != DisplayStyle.None;
    }
}
