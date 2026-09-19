using ReMap.Standalone.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace ReMap.Standalone
{
    public sealed partial class ReMapApp
    {
        private VisualElement aboutOverlay;

        private void BuildAbout()
        {
            aboutOverlay = new VisualElement();
            aboutOverlay.AddToClassList("modal-overlay");
            root.Add(aboutOverlay);

            var panel = new VisualElement();
            panel.AddToClassList("about-panel");
            aboutOverlay.Add(panel);

            var heading = DockTitle(L.T("#ABOUT_REMAP"));
            panel.Add(heading);
            heading.Add(Button("×", () => ShowAbout(false), "dock-close"));

            var content = new ScrollView();
            content.AddToClassList("about-content");
            panel.Add(content);

            content.Add(Label("ReMap", "about-brand"));
            content.Add(Label(L.F("#VERSION_ARG0", Application.version), "about-version"));
            content.Add(Label(L.T(Debug.isDebugBuild ? "#DEVELOPMENT_BUILD" : "#RELEASE_BUILD"), "about-build-type"));
            content.Add(Label(L.T("#ABOUT_TAGLINE"), "about-tagline"));

            content.Add(Label(L.T("#CREDITS"), "section-title"));
            content.Add(Label(L.T("#ABOUT_ORIGINAL_EDITOR"), "about-copy"));
            content.Add(Label(L.T("#ABOUT_STANDALONE"), "about-copy"));
            content.Add(Label(L.T("#ABOUT_FOUNDATION"), "about-copy"));
            content.Add(Label(L.T("#ABOUT_REVPK"), "about-copy"));

            content.Add(Label(L.T("#LICENSES"), "section-title"));
            content.Add(Label(L.T("#ABOUT_LICENSES"), "about-copy"));

            var links = new VisualElement();
            links.AddToClassList("about-links");
            links.Add(Button(L.T("#PROJECT_SOURCE"), () => Application.OpenURL("https://github.com/R5Reloaded-ReMap/ReMap")));
            links.Add(Button(L.T("#RSX_SOURCE"), () => Application.OpenURL("https://github.com/R5Reloaded-ReMap/rsx")));
            links.Add(Button(L.T("#REVPK_SOURCE"), () => Application.OpenURL("https://github.com/R5Reloaded/r5sdk")));
            content.Add(links);

            content.Add(Label(L.T("#ABOUT_DISCLAIMER"), "about-disclaimer"));

            var actions = new VisualElement();
            actions.AddToClassList("dialog-actions");
            actions.Add(Button(L.T("#CLOSE"), () => ShowAbout(false), "primary"));
            panel.Add(actions);

            ShowAbout(false);
        }

        private void ShowAbout(bool show)
        {
            if (aboutOverlay == null) return;
            if (show)
            {
                CommitInspectorEdit();
                CancelGizmoDrag();
                world.ClearPreview();
            }
            aboutOverlay.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
            if (show) aboutOverlay.BringToFront();
        }

        private bool AboutOpen => aboutOverlay != null &&
            aboutOverlay.style.display.value != DisplayStyle.None;
    }
}
