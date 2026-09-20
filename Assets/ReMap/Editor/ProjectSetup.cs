using System;
using System.IO;
using System.Security.Cryptography;
using ReMap.Standalone;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;

namespace ReMap.Standalone.Editor
{
    public static class ProjectSetup
    {
        public const string ScenePath = "Assets/ReMap/Workspace.unity";
        private const string LogoPath = "Assets/ReMap/Resources/Branding/remap-logo.png";
        [MenuItem("ReMap/Prepare and open workspace")]
        public static void Prepare()
        {
            if (File.Exists(ScenePath))
            {
                if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
                EditorSceneManager.OpenScene(ScenePath);
                ApplyBranding();
                AssetDatabase.SaveAssets();
                return;
            }
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var settings = ScriptableObject.CreateInstance<PanelSettings>();
            settings.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            settings.referenceResolution = new Vector2Int(1600, 900);
            settings.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
            settings.match = .5f;
            settings.themeStyleSheet = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>("Assets/ReMap/Runtime/WorkspaceTheme.tss");
            AssetDatabase.CreateAsset(settings, "Assets/ReMap/Runtime/WorkspacePanel.asset");
            var app = new GameObject("ReMap", typeof(UIDocument));
            app.GetComponent<UIDocument>().panelSettings = settings;
            app.AddComponent<ReMapApp>().Configure(Shader.Find("Universal Render Pipeline/Lit"),
                Shader.Find("ReMap/WorkspaceGrid"), Shader.Find("Universal Render Pipeline/Unlit"),
                AssetDatabase.LoadAssetAtPath<StyleSheet>("Assets/ReMap/Runtime/Workspace.uss"));
            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            PlayerSettings.companyName = "ReMap";
            PlayerSettings.productName = "ReMap";
            PlayerSettings.defaultScreenWidth = 1600;
            PlayerSettings.defaultScreenHeight = 900;
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.allowFullscreenSwitch = false; // F11 / Alt+Enter are handled by the app so Windows cannot toggle a second mode underneath it.
            PlayerSettings.resizableWindow = true;
            PlayerSettings.runInBackground = true;
            ApplyBranding();
            AssetDatabase.SaveAssets();
            Debug.Log("REMAP_SETUP_OK");
        }

        private static void ApplyBranding()
        {
            var logo = AssetDatabase.LoadAssetAtPath<Texture2D>(LogoPath);
            if (logo == null) throw new FileNotFoundException("ReMap logo is missing.", LogoPath);
            var target = UnityEditor.Build.NamedBuildTarget.Standalone;
            int[] sizes = PlayerSettings.GetIconSizes(target, IconKind.Application);
            if (sizes.Length == 0) throw new InvalidDataException("Unity exposes no Standalone application icon slots.");
            var applicationIcons = new Texture2D[sizes.Length];
            for (int index = 0; index < applicationIcons.Length; index++) applicationIcons[index] = logo;
            PlayerSettings.SetIcons(target, applicationIcons, IconKind.Application);
            foreach (PlatformIconKind kind in PlayerSettings.GetSupportedIconKinds(target))
            {
                PlatformIcon[] icons = PlayerSettings.GetPlatformIcons(target, kind);
                foreach (PlatformIcon icon in icons)
                    for (int layer = 0; layer < icon.maxLayerCount; layer++) icon.SetTexture(logo, layer);
                PlayerSettings.SetPlatformIcons(target, kind, icons);
            }
        }

        [MenuItem("ReMap/Build Windows app")]
        public static void BuildWindows()
        {
            string configuredOutput = Environment.GetEnvironmentVariable("REMAP_BUILD_OUTPUT");
            BuildWindowsAt(string.IsNullOrWhiteSpace(configuredOutput)
                ? "Builds/Windows/ReMap.exe"
                : configuredOutput);
        }
        private static void BuildWindowsAt(string output)
        {
            Prepare();
            Unity.CodeEditor.CodeEditor.CurrentEditor.SyncAll();
            string configuredVersion = Environment.GetEnvironmentVariable("REMAP_BUILD_VERSION");
            string originalVersion = PlayerSettings.bundleVersion;
            try
            {
                if (!string.IsNullOrWhiteSpace(configuredVersion))
                    PlayerSettings.bundleVersion = configuredVersion.Trim();
                bool development = Environment.GetEnvironmentVariable("REMAP_DEVELOPMENT_BUILD") == "1";
                bool developerTools = Environment.GetEnvironmentVariable("REMAP_DEVELOPER_TOOLS") == "1";
                BuildOptions options = development
                    ? BuildOptions.Development | BuildOptions.AllowDebugging
                    : BuildOptions.None;
                var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions {
                    scenes = new[] { ScenePath }, locationPathName = output,
                    target = BuildTarget.StandaloneWindows64, options = options,
                    extraScriptingDefines = developerTools
                        ? new[] { "REMAP_DEVELOPER_TOOLS" }
                        : Array.Empty<string>()
                });
                if (report.summary.result != BuildResult.Succeeded)
                    throw new Exception("Windows build failed: " + report.summary.result);
                string outputDirectory = Path.GetDirectoryName(Path.GetFullPath(report.summary.outputPath));
                BuildLiveBridge(outputDirectory);
                BundleGameScripts(outputDirectory);
                BundleOfficialRsx(outputDirectory);
                Debug.Log("REMAP_BUILD_OK: " + report.summary.outputPath);
            }
            finally
            {
                if (PlayerSettings.bundleVersion != originalVersion)
                {
                    PlayerSettings.bundleVersion = originalVersion;
                    AssetDatabase.SaveAssets();
                }
            }
        }

        private static void BundleGameScripts(string outputDirectory)
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string sourceRoot = Path.Combine(projectRoot, "scripts", "vscripts");
            string destinationRoot = Path.Combine(outputDirectory, "GameScripts");
            foreach (string variant in new[] { "remap_r5r", "remap_r5f" })
            {
                string source = Path.Combine(sourceRoot, variant);
                if (!Directory.Exists(source)) throw new DirectoryNotFoundException(source);
                string destination = Path.Combine(destinationRoot, variant);
                Directory.CreateDirectory(destination);
                foreach (string path in Directory.EnumerateFiles(source, "*.nut", SearchOption.TopDirectoryOnly))
                    CopyIfChanged(path, Path.Combine(destination, Path.GetFileName(path)));
            }
        }

        private static void BundleOfficialRsx(string outputDirectory)
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string configuredRsxRoot = Environment.GetEnvironmentVariable("REMAP_RSX_ROOT");
            string rsxRoot = string.IsNullOrWhiteSpace(configuredRsxRoot)
                ? Path.GetFullPath(Path.Combine(projectRoot, "..", "rsx"))
                : Path.GetFullPath(configuredRsxRoot);
            string rsxLicense = Path.Combine(projectRoot, "ThirdParty", "RSX", "LICENSE");
            string executable = Path.Combine(rsxRoot, "bin", "Release", "rsx.exe");
            string sessionMarker = executable + ".remap-session-v1";
            string batchMarker = executable + ".remap-session-v2";
            string geometryMarker = executable + ".remap-session-v3";
            if (!File.Exists(executable))
                throw new FileNotFoundException("Build official RSX in Release/x64 before building ReMap.", executable);
            if (!File.Exists(sessionMarker))
                throw new FileNotFoundException("Build the ReMap RSX session fork in Release/x64 before building ReMap.", sessionMarker);
            if (!File.Exists(batchMarker))
                throw new FileNotFoundException("Build the ReMap RSX batch session fork in Release/x64 before building ReMap.", batchMarker);
            if (!File.Exists(geometryMarker))
                throw new FileNotFoundException("Build the ReMap RSX geometry fallback fork in Release/x64 before building ReMap.", geometryMarker);
            Directory.CreateDirectory(outputDirectory);
            CopyIfChanged(executable, Path.Combine(outputDirectory, "rsx.exe"));
            CopyIfChanged(sessionMarker, Path.Combine(outputDirectory, "rsx.exe.remap-session-v1"));
            CopyIfChanged(batchMarker, Path.Combine(outputDirectory, "rsx.exe.remap-session-v2"));
            CopyIfChanged(geometryMarker, Path.Combine(outputDirectory, "rsx.exe.remap-session-v3"));
            if (!File.Exists(rsxLicense))
                throw new FileNotFoundException("The bundled RSX license is missing from ThirdParty/RSX.", rsxLicense);
            File.Copy(rsxLicense, Path.Combine(outputDirectory, "RSX-LICENSE.txt"), true);
            CopyRequired(Path.Combine(rsxRoot, "thirdpartylegalnotices.txt"), Path.Combine(outputDirectory, "RSX-THIRD-PARTY-NOTICES.txt"));
        }

        private static void CopyIfChanged(string source, string destination)
        {
            if (File.Exists(destination) && new FileInfo(source).Length == new FileInfo(destination).Length)
            {
                byte[] sourceHash, destinationHash;
                using (var hash = SHA256.Create())
                using (var stream = File.OpenRead(source)) sourceHash = hash.ComputeHash(stream);
                using (var hash = SHA256.Create())
                using (var stream = File.OpenRead(destination)) destinationHash = hash.ComputeHash(stream);
                bool equal = sourceHash.Length == destinationHash.Length;
                for (int i = 0; equal && i < sourceHash.Length; i++) equal = sourceHash[i] == destinationHash[i];
                if (equal) return;
            }
            File.Copy(source, destination, true);
        }

        private static void CopyRequired(string source, string destination)
        {
            if (!File.Exists(source))
                throw new FileNotFoundException("A required third-party notice is missing.", source);
            File.Copy(source, destination, true);
        }

        private static void BuildLiveBridge(string outputDirectory)
        {
            string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            string framework = Path.Combine(windows, "Microsoft.NET", "Framework64", "v4.0.30319");
            string compiler = Path.Combine(framework, "csc.exe");
            string wpf = Path.Combine(framework, "WPF");
            string source = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Tools", "LiveBridge", "Program.cs"));
            string output = Path.Combine(outputDirectory, "ReMapLiveBridge.exe");
            string legacyOutput = Path.Combine(outputDirectory, "ReMapLauncherConsole.exe");
            if (File.Exists(legacyOutput)) File.Delete(legacyOutput);
            if (!File.Exists(compiler) || !File.Exists(source)) throw new FileNotFoundException("ReMap live bridge compiler or source is missing.");
            Directory.CreateDirectory(outputDirectory);
            string arguments = "/nologo /target:exe /platform:anycpu /optimize+ /out:" + Quote(output) +
                " /reference:" + Quote(Path.Combine(wpf, "UIAutomationClient.dll")) +
                " /reference:" + Quote(Path.Combine(wpf, "UIAutomationTypes.dll")) +
                " /reference:" + Quote(Path.Combine(wpf, "WindowsBase.dll")) +
                " /reference:" + Quote(Path.Combine(wpf, "PresentationFramework.dll")) + " " + Quote(source);
            using (var process = new System.Diagnostics.Process())
            {
                process.StartInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = compiler, Arguments = arguments, UseShellExecute = false, CreateNoWindow = true,
                    RedirectStandardOutput = true, RedirectStandardError = true
                };
                process.Start();
                string outputText = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
                process.WaitForExit();
                if (process.ExitCode != 0) throw new Exception("ReMap live bridge build failed:\n" + outputText);
            }
        }

        private static string Quote(string value) => "\"" + value.Replace("\"", "\\\"") + "\"";
    }
}
