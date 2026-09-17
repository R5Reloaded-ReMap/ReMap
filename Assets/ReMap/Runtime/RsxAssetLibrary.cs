using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ReMap.Standalone.Core;
using UnityEngine;

namespace ReMap.Standalone
{
    [Serializable]
    public sealed class AssetSourceSettings
    {
        // gameDirectory/platformDirectory/flowstateCast are retained to migrate existing local settings.
        public string gameDirectory = "", platformDirectory = "";
        public bool flowstateCast;
        public string targetGame = GameTargets.R5Reloaded;
        public string r5ReloadedGameDirectory = "", r5ReloadedPlatformDirectory = "";
        public string r5FlowstateGameDirectory = "", r5FlowstatePlatformDirectory = "";
        // Map references are editor-only overlays and are deliberately kept out of map documents.
        public bool showMainBsp;
        public bool showMprtModels;
        // MPRT references are streamed around the camera. The master toggle remains off by default.
        public int mprtPreset = 1;
        public float mprtDistance = 750f;
        public int mprtMaxActive = 15000;
        public float mprtMinimumSize = .5f;
        public int mprtDensityPercent = 100;
        // These fields remain only so older local JSON settings can be read. ReMap always uses its bundled official RSX.
        public string rsxExecutable = "", rsxBackend = "official";
        public string rconAddress = "[::ffff:127.0.0.1]:37015", rconKey = "", rconPassword = "";
        public string assetExportDirectory = "";
        public int textureLimit = 1024;
        public string[] lastTargetMaps = Array.Empty<string>();
        public string[] lastR5ReloadedTargetMaps = Array.Empty<string>();
        public string[] lastR5FlowstateTargetMaps = Array.Empty<string>();
    }
    public sealed class MapSource { public string Id, Name; public override string ToString() => Name; }

    // The worker owns RSX calls. Unity only receives metadata or one exported model at a time.
    public sealed partial class RsxAssetLibrary : IDisposable
    {
        public readonly string LocalRoot;
        public AssetSourceSettings Settings { get; private set; }
        public List<MapSource> Maps { get; private set; } = new List<MapSource>();
        public List<GameAssetRecord> Records { get; private set; } = new List<GameAssetRecord>();
        public string CacheRoot { get; private set; }
        public string CacheDirectory
        {
            get
            {
                try { return ResolveAssetExportDirectory(LocalRoot, Settings.assetExportDirectory); }
                catch (Exception exception) when (exception is ArgumentException || exception is NotSupportedException || exception is PathTooLongException)
                { return Path.GetFullPath(Path.Combine(LocalRoot, "AssetCache")); }
            }
        }
        public string AssetExportDirectory => CacheDirectory;
        public string TargetGame => GameTargets.Normalize(Settings.targetGame);
        public string GameDirectory => Settings.gameDirectory ?? "";
        public string PlatformDirectory => Settings.platformDirectory ?? "";
        public string PakDirectory { get; private set; }
        public bool UsesForkFeatures => false;
        public string[] LastTargetMaps
        {
            get => TargetGame == GameTargets.R5Flowstate ? Settings.lastR5FlowstateTargetMaps ?? Array.Empty<string>() : Settings.lastR5ReloadedTargetMaps ?? Array.Empty<string>();
            set
            {
                if (TargetGame == GameTargets.R5Flowstate) Settings.lastR5FlowstateTargetMaps = value ?? Array.Empty<string>();
                else Settings.lastR5ReloadedTargetMaps = value ?? Array.Empty<string>();
                Settings.lastTargetMaps = value ?? Array.Empty<string>();
            }
        }
        private readonly CancellationTokenSource shutdown = new CancellationTokenSource();
        private readonly SemaphoreSlim worker = new SemaphoreSlim(1, 1);
        private string[] Common => (TargetGame == GameTargets.R5Flowstate
            ? new[] { "common_early.rpak", "common.rpak", "common_mp.rpak", "common_roots.rpak", "common_flowstate.rpak" }
            : new[] { "common_early.rpak", "common.rpak", "common_mp.rpak", "common_roots.rpak", "common_sdk.rpak" });
        public bool Configured => File.Exists(Settings.rsxExecutable) && !string.IsNullOrEmpty(PakDirectory) && Directory.Exists(PakDirectory);
        public RsxAssetLibrary(string root)
        {
            LocalRoot = root;
            string settingsPath = Path.Combine(root, "asset-source.local.json");
            Settings = File.Exists(settingsPath) ? JsonUtility.FromJson<AssetSourceSettings>(File.ReadAllText(settingsPath)) : new AssetSourceSettings();
            Settings = Settings ?? new AssetSourceSettings();
            MigrateSettings();
            AutoDetectInstallations();
            SelectTarget(Settings.targetGame, false);
        }
        public static string FindLocalRoot()
        {
            var directory = new DirectoryInfo(Path.GetFullPath(Path.Combine(Application.dataPath, "..")));
            string fallback = directory.FullName;
            for (int i = 0; directory != null && i < 4; i++, directory = directory.Parent)
                if (File.Exists(Path.Combine(directory.FullName, "asset-source.local.json"))) return directory.FullName;
            return fallback;
        }
        public void ConfigureProfiles(string targetGame, string r5ReloadedGame, string r5ReloadedPlatform,
            string r5FlowstateGame, string r5FlowstatePlatform)
        {
            if (worker.CurrentCount == 0) throw new InvalidOperationException(L.T("#WAIT_RSX_OPERATION_FINISH"));
            targetGame = GameTargets.Normalize(targetGame);
            r5ReloadedGame = CleanPath(r5ReloadedGame); r5FlowstateGame = CleanPath(r5FlowstateGame);
            if (!string.IsNullOrEmpty(r5ReloadedGame) && FindPakDirectory(r5ReloadedGame) == null)
                throw new ArgumentException(L.T("#R5RELOADED_FOLDER_CONTAIN_RPAK_ARCHIVES"));
            if (!string.IsNullOrEmpty(r5FlowstateGame) && FindPakDirectory(r5FlowstateGame) == null)
                throw new ArgumentException(L.T("#R5FLOWSTATE_FOLDER_CONTAIN_RPAK_ARCHIVES"));
            string selected = targetGame == GameTargets.R5Flowstate ? r5FlowstateGame : r5ReloadedGame;
            if (string.IsNullOrEmpty(selected))
                throw new ArgumentException(L.F("#CONFIGURE_ARG0_GAME_FOLDER", GameTargets.DisplayName(targetGame)));
            SetProfile(GameTargets.R5Reloaded, r5ReloadedGame,
                string.IsNullOrWhiteSpace(r5ReloadedPlatform) ? FindPlatformDirectory(r5ReloadedGame) : CleanPath(r5ReloadedPlatform));
            SetProfile(GameTargets.R5Flowstate, r5FlowstateGame,
                string.IsNullOrWhiteSpace(r5FlowstatePlatform) ? FindPlatformDirectory(r5FlowstateGame) : CleanPath(r5FlowstatePlatform));
            Settings.targetGame = targetGame;
            ResolveOfficialRsx();
            SyncActiveProfile();
            PakDirectory = FindPakDirectory(Settings.gameDirectory);
            previewSession?.Dispose(); previewSession = null; Records.Clear(); CacheRoot = null; DiscoverMaps();
            SaveSettings();
        }
        public void ConfigureAssetExportDirectory(string directory)
        {
            if (worker.CurrentCount == 0) throw new InvalidOperationException(L.T("#WAIT_RSX_OPERATION_FINISH"));
            string previous = CacheDirectory;
            string resolved = ResolveAssetExportDirectory(LocalRoot, directory);
            Directory.CreateDirectory(resolved);
            string defaultDirectory = ResolveAssetExportDirectory(LocalRoot, "");
            Settings.assetExportDirectory = string.Equals(resolved, defaultDirectory, StringComparison.OrdinalIgnoreCase) ? "" : resolved;
            if (!string.Equals(previous, resolved, StringComparison.OrdinalIgnoreCase))
            {
                previewSession?.Dispose(); previewSession = null; Records.Clear(); CacheRoot = null;
            }
            SaveSettings();
        }
        public static string ResolveAssetExportDirectory(string localRoot, string directory)
        {
            string root = Path.GetFullPath(localRoot ?? throw new ArgumentNullException(nameof(localRoot)));
            string value = CleanPath(directory);
            return value.Length == 0
                ? Path.GetFullPath(Path.Combine(root, "AssetCache"))
                : Path.GetFullPath(Path.IsPathRooted(value) ? value : Path.Combine(root, value));
        }
        public void SelectTarget(string targetGame, bool save = true)
        {
            if (worker.CurrentCount == 0) throw new InvalidOperationException(L.T("#WAIT_RSX_OPERATION_FINISH"));
            targetGame = GameTargets.Normalize(targetGame);
            if (TargetGame != targetGame) { previewSession?.Dispose(); previewSession = null; Records.Clear(); CacheRoot = null; }
            Settings.targetGame = targetGame;
            SyncActiveProfile();
            PakDirectory = FindPakDirectory(Settings.gameDirectory);
            DiscoverMaps();
            if (save) SaveSettings();
        }
        public void SaveSettings()
        {
            SyncActiveProfile();
            File.WriteAllText(Path.Combine(LocalRoot, "asset-source.local.json"), JsonUtility.ToJson(Settings, true));
        }

        private void MigrateSettings()
        {
            Settings.targetGame = GameTargets.Normalize(Settings.targetGame);
            Settings.rsxBackend = "official";
            Settings.flowstateCast = false;
            if (!string.IsNullOrWhiteSpace(Settings.gameDirectory))
            {
                string inferred = LooksLikeFlowstate(Settings.gameDirectory) ? GameTargets.R5Flowstate : Settings.targetGame;
                if (inferred == GameTargets.R5Flowstate && string.IsNullOrWhiteSpace(Settings.r5FlowstateGameDirectory))
                {
                    Settings.r5FlowstateGameDirectory = Settings.gameDirectory;
                    Settings.r5FlowstatePlatformDirectory = Settings.platformDirectory;
                    Settings.targetGame = inferred;
                }
                else if (inferred == GameTargets.R5Reloaded && string.IsNullOrWhiteSpace(Settings.r5ReloadedGameDirectory))
                {
                    Settings.r5ReloadedGameDirectory = Settings.gameDirectory;
                    Settings.r5ReloadedPlatformDirectory = Settings.platformDirectory;
                }
            }
            if ((Settings.lastR5ReloadedTargetMaps?.Length ?? 0) == 0 && Settings.targetGame == GameTargets.R5Reloaded)
                Settings.lastR5ReloadedTargetMaps = Settings.lastTargetMaps ?? Array.Empty<string>();
            if ((Settings.lastR5FlowstateTargetMaps?.Length ?? 0) == 0 && Settings.targetGame == GameTargets.R5Flowstate)
                Settings.lastR5FlowstateTargetMaps = Settings.lastTargetMaps ?? Array.Empty<string>();
            if (Settings.mprtDistance <= 0 || Settings.mprtMaxActive <= 0 || Settings.mprtDensityPercent <= 0)
            {
                Settings.mprtPreset = 1;
                Settings.mprtDistance = 750f;
                Settings.mprtMaxActive = 15000;
                Settings.mprtMinimumSize = .5f;
                Settings.mprtDensityPercent = 100;
            }
        }

        private void AutoDetectInstallations()
        {
            var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string value in new[] { Settings.gameDirectory, Settings.r5ReloadedGameDirectory, Settings.r5FlowstateGameDirectory, LocalRoot })
            {
                if (string.IsNullOrWhiteSpace(value)) continue;
                var directory = new DirectoryInfo(Path.GetFullPath(value));
                for (int i = 0; directory != null && i < 5; i++, directory = directory.Parent) roots.Add(directory.FullName);
            }
            foreach (var drive in DriveInfo.GetDrives().Where(d => d.IsReady)) roots.Add(drive.RootDirectory.FullName);
            if (string.IsNullOrWhiteSpace(Settings.r5ReloadedGameDirectory))
                Settings.r5ReloadedGameDirectory = FindInstallation(GameTargets.R5Reloaded, roots);
            if (string.IsNullOrWhiteSpace(Settings.r5FlowstateGameDirectory))
                Settings.r5FlowstateGameDirectory = FindInstallation(GameTargets.R5Flowstate, roots);
            if (string.IsNullOrWhiteSpace(Settings.r5ReloadedPlatformDirectory))
                Settings.r5ReloadedPlatformDirectory = FindPlatformDirectory(Settings.r5ReloadedGameDirectory);
            if (string.IsNullOrWhiteSpace(Settings.r5FlowstatePlatformDirectory))
                Settings.r5FlowstatePlatformDirectory = FindPlatformDirectory(Settings.r5FlowstateGameDirectory);
            ResolveOfficialRsx(roots);
        }

        private void ResolveOfficialRsx(IEnumerable<string> roots = null)
        {
            string applicationRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            if (roots == null)
            {
                var nearby = new List<string>();
                foreach (string start in new[] { applicationRoot, Path.GetFullPath(LocalRoot) })
                {
                    var directory = new DirectoryInfo(start);
                    for (int i = 0; directory != null && i < 5; i++, directory = directory.Parent) nearby.Add(directory.FullName);
                }
                roots = nearby;
            }
            string bundled = Path.Combine(applicationRoot, "rsx.exe");
            Settings.rsxExecutable = File.Exists(bundled)
                ? Path.GetFullPath(bundled)
                : FindOfficialRsx(roots);
            Settings.rsxBackend = "official";
            Settings.flowstateCast = false;
        }

        private static string FindOfficialRsx(IEnumerable<string> roots)
        {
            foreach (string root in roots)
                foreach (string relative in new[] {
                    "rsx.exe",
                    Path.Combine("bin", "Release", "rsx.exe"),
                    Path.Combine("rsx", "bin", "Release", "rsx.exe"),
                    Path.Combine("Github", "rsx", "bin", "Release", "rsx.exe") })
                {
                    string candidate = Path.Combine(root, relative);
                    if (File.Exists(candidate)) return Path.GetFullPath(candidate);
                }
            return "";
        }

        private static string FindInstallation(string target, IEnumerable<string> roots)
        {
            string[] relatives = target == GameTargets.R5Flowstate
                ? new[] { "R5Flowstate", Path.Combine("r5_mods", "R5Flowstate") }
                : new[] { Path.Combine("R5Reloaded", "R5R Library", "LIVE"), Path.Combine("r5_mods", "R5Reloaded", "R5R Library", "LIVE") };
            foreach (string root in roots)
            {
                if (LooksLikeTarget(root, target)) return root;
                foreach (string relative in relatives)
                {
                    string candidate = Path.Combine(root, relative);
                    if (LooksLikeTarget(candidate, target)) return candidate;
                }
            }
            return "";
        }

        private static bool LooksLikeTarget(string path, string target)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(Path.Combine(path, "r5apex.exe")) || FindPakDirectory(path) == null) return false;
            return target == GameTargets.R5Flowstate ? LooksLikeFlowstate(path) : !LooksLikeFlowstate(path);
        }

        private static bool LooksLikeFlowstate(string path)
        {
            string paks = FindPakDirectory(path);
            return paks != null && File.Exists(Path.Combine(paks, "common_flowstate.rpak")) &&
                (File.Exists(Path.Combine(path, "platform", "r5f_map_names.txt")) || File.Exists(Path.Combine(path, "platform_", "r5f_map_names.txt")) ||
                 path.IndexOf("flowstate", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        public static string FindPakDirectory(string game)
        {
            if (string.IsNullOrWhiteSpace(game)) return null;
            foreach (string candidate in new[] { game, Path.Combine(game, "paks", "Win64"), Path.Combine(game, "paks", "Win64_server") })
                if (Directory.Exists(candidate) && Directory.EnumerateFiles(candidate, "*.rpak", SearchOption.TopDirectoryOnly).Any()) return Path.GetFullPath(candidate);
            return null;
        }

        public static string FindPlatformDirectory(string game)
        {
            if (string.IsNullOrWhiteSpace(game)) return "";
            foreach (string name in new[] { "platform", "platform_" })
            {
                string candidate = Path.Combine(game, name);
                if (Directory.Exists(Path.Combine(candidate, "scripts"))) return Path.GetFullPath(candidate);
            }
            return Path.GetFullPath(Path.Combine(game, "platform"));
        }

        private static string CleanPath(string value) => (value ?? "").Trim().Trim('"');

        private void SetProfile(string target, string game, string platform)
        {
            if (target == GameTargets.R5Flowstate)
            { Settings.r5FlowstateGameDirectory = game; Settings.r5FlowstatePlatformDirectory = platform; }
            else
            { Settings.r5ReloadedGameDirectory = game; Settings.r5ReloadedPlatformDirectory = platform; }
        }

        private void SyncActiveProfile()
        {
            if (TargetGame == GameTargets.R5Flowstate)
            { Settings.gameDirectory = Settings.r5FlowstateGameDirectory ?? ""; Settings.platformDirectory = Settings.r5FlowstatePlatformDirectory ?? ""; }
            else
            { Settings.gameDirectory = Settings.r5ReloadedGameDirectory ?? ""; Settings.platformDirectory = Settings.r5ReloadedPlatformDirectory ?? ""; }
        }

        private void DiscoverMaps()
        {
            Maps.Clear();
            if (string.IsNullOrEmpty(PakDirectory)) return;
            string names = new[] { Path.Combine(GameDirectory, "platform", "r5f_map_names.txt"), Path.Combine(GameDirectory, "platform_", "r5f_map_names.txt") }.FirstOrDefault(File.Exists);
            if (names != null)
            {
                foreach (var line in File.ReadLines(names))
                {
                    string value = line.Trim();
                    if (value.StartsWith("#") || !value.Contains("=")) continue;
                    var parts = value.Split(new[] { '=' }, 2); string id = parts[0].Trim();
                    if (ValidMapArchive(id)) Maps.Add(new MapSource { Id = id, Name = parts[1].Trim() + " · " + id });
                }
            }
            if (Maps.Count == 0)
            {
                string levels = Path.Combine(PlatformDirectory, "scripts", "levels");
                IEnumerable<string> ids = Directory.Exists(levels)
                    ? Directory.EnumerateFiles(levels, "mp_*.rson", SearchOption.TopDirectoryOnly).Select(Path.GetFileNameWithoutExtension)
                    : Directory.EnumerateFiles(PakDirectory, "mp_*.rpak", SearchOption.TopDirectoryOnly).Select(Path.GetFileNameWithoutExtension);
                foreach (string id in ids.Where(ValidMapArchive).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
                    Maps.Add(new MapSource { Id = id, Name = FriendlyMapName(id) + " · " + id });
            }
        }

        private bool ValidMapArchive(string id) =>
            !string.IsNullOrWhiteSpace(id) && id.StartsWith("mp_", StringComparison.Ordinal) &&
            id.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 && File.Exists(Path.Combine(PakDirectory, id + ".rpak"));

        private static string FriendlyMapName(string id)
        {
            string value = id.StartsWith("mp_rr_", StringComparison.Ordinal) ? id.Substring(6) : id.StartsWith("mp_", StringComparison.Ordinal) ? id.Substring(3) : id;
            return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(value.Replace('_', ' '));
        }
        private string Fingerprint()
        {
            // Any RPak/streaming dependency change invalidates the cache, including patch packs.
            var signature = new StringBuilder("remap-static-cast-v2|" + Path.GetFullPath(PakDirectory) + "|" + Settings.rsxBackend);
            foreach (string file in Directory.EnumerateFiles(PakDirectory).Where(p => p.EndsWith(".rpak", StringComparison.OrdinalIgnoreCase) || p.EndsWith(".starpak", StringComparison.OrdinalIgnoreCase)).OrderBy(p => p, StringComparer.Ordinal))
            { var info = new FileInfo(file); signature.Append('|').Append(info.Name).Append(':').Append(info.Length).Append(':').Append(info.LastWriteTimeUtc.Ticks); }
            var tool = new FileInfo(Settings.rsxExecutable); signature.Append('|').Append(tool.Length).Append(':').Append(tool.LastWriteTimeUtc.Ticks);
            using (var hash = SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(signature.ToString()))).Replace("-", "").Substring(0, 24).ToLowerInvariant();
        }
        public async Task IndexAsync(string[] targets, IProgress<string> progress,
            CancellationToken cancellation = default)
        {
            if (!Configured) throw new InvalidOperationException(L.T("#CONFIGURE_SELECTED_GAME_FOLDER_SETTINGS"));
            targets = ResolveMapTargets(targets);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(shutdown.Token, cancellation);
            await worker.WaitAsync(linked.Token);
            try
            {
                previewSession?.Dispose(); previewSession=null; // Release the CLI mutex before index subprocesses.
                string root = await Task.Run(() => ActivateCacheGeneration(CacheDirectory, Fingerprint()), linked.Token);
                var records = await Task.Run(() => {
                    var result = new List<GameAssetRecord>();
                    var sources = Common.Where(n => File.Exists(Path.Combine(PakDirectory, n))).Select(n => new AssetOrigin { archive = n }).ToList();
                    foreach (var map in targets.Distinct())
                        foreach (var suffix in new[] { ".rpak", "_client_perm.rpak", "_client_temp.rpak" })
                            if (File.Exists(Path.Combine(PakDirectory, map + suffix))) sources.Add(new AssetOrigin { mapId = map, archive = map + suffix });
                    Directory.CreateDirectory(Path.Combine(root, "index"));
                    for (int i = 0; i < sources.Count; i++)
                    {
                        linked.Token.ThrowIfCancellationRequested();
                        var source = sources[i]; progress?.Report(L.T("#INDEX_A7F8DD") + (i + 1) + "/" + sources.Count + " : " + source.archive);
                        string csv = Path.Combine(root, "index", source.archive + ".csv");
                        if (!File.Exists(csv))
                        {
                            string temporary = csv + ".pending";
                            RunRsx(new[] { "--list", temporary, "--listformat", "csv",
                                Path.Combine(PakDirectory, source.archive) }, root, cancellation: linked.Token);
                            if (!File.Exists(temporary) || !GameAssetIndex.IsSupportedCsvHeader(File.ReadLines(temporary).FirstOrDefault())) throw new IOException(L.T("#MISSING_INVALID_RSX_INDEX") + source.archive);
                            File.Move(temporary, csv);
                        }
                        result.AddRange(GameAssetIndex.ReadCsv(csv, source.mapId, source.archive));
                    }
                    return GameAssetIndex.Merge(result);
                }, linked.Token);
                linked.Token.ThrowIfCancellationRequested();
                CacheRoot = root; Records = records;
            }
            finally { worker.Release(); }
        }
        public static string[] KeepAvailableTargets(IEnumerable<string> targets, IEnumerable<string> available)
        {
            var known = new HashSet<string>(available ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
            return (targets ?? Array.Empty<string>()).Where(known.Contains).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        }
        public static string[] ExpandAvailableTargets(IEnumerable<string> targets, IEnumerable<string> available) =>
            AssetCompatibility.ExpandTargets(targets, available);
        public string[] ResolveMapTargets(IEnumerable<string> targets)
        {
            IEnumerable<string> available = Maps.Select(map => map.Id);
            if (!string.IsNullOrEmpty(PakDirectory) && Directory.Exists(PakDirectory))
                available = available.Concat(MapIdsFromArchives(Directory.EnumerateFiles(PakDirectory,
                    "*.rpak", SearchOption.TopDirectoryOnly).Select(Path.GetFileName)));
            return ExpandAvailableTargets(targets, available);
        }
        public static string[] FindMissingTargets(IEnumerable<string> targets, IEnumerable<string> available)
        {
            var known = new HashSet<string>(available ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
            return (targets ?? Array.Empty<string>()).Where(target => !known.Contains(target)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        }

        public string[] SelectedMapArchives(IEnumerable<string> targets)
        {
            if (string.IsNullOrEmpty(PakDirectory) || !Directory.Exists(PakDirectory))
                return Array.Empty<string>();
            return SelectMapArchives(targets, Directory.EnumerateFiles(PakDirectory, "*.rpak",
                SearchOption.TopDirectoryOnly).Select(Path.GetFileName));
        }

        public static string[] SelectMapArchives(IEnumerable<string> targets,
            IEnumerable<string> availableArchives)
        {
            string[] archiveNames = (availableArchives ?? Array.Empty<string>()).Select(Path.GetFileName).ToArray();
            var available = new HashSet<string>(archiveNames,
                StringComparer.OrdinalIgnoreCase);
            var result = new List<string>();
            foreach (string map in ExpandAvailableTargets(targets, MapIdsFromArchives(archiveNames)))
                foreach (string suffix in new[] { ".rpak", "_client_perm.rpak", "_client_temp.rpak" })
                {
                    string archive = map + suffix;
                    if (available.Contains(archive)) result.Add(archive);
                }
            return result.ToArray();
        }
        public static string[] MapIdsFromArchives(IEnumerable<string> availableArchives)
        {
            var result = new List<string>();
            foreach (string archive in availableArchives ?? Array.Empty<string>())
            {
                string name = Path.GetFileName(archive) ?? "";
                foreach (string suffix in new[] { "_client_perm.rpak", "_client_temp.rpak", ".rpak" })
                    if (name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                    {
                        result.Add(name.Substring(0, name.Length - suffix.Length));
                        break;
                    }
            }
            return result.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        }
        public string ModelDirectory(GameAssetRecord entry) => Path.Combine(CacheRoot, "Models", entry.guid);
        public string CachedModel(GameAssetRecord entry)
        {
            if (CacheRoot == null) return null;
            var folder = ModelDirectory(entry); string marker = Path.Combine(folder, "complete.txt");
            if (!File.Exists(marker)) return null;
            string relative = File.ReadLines(marker).FirstOrDefault();
            if (relative != null && relative.EndsWith(".cast", StringComparison.OrdinalIgnoreCase))
            {
                string path = Path.GetFullPath(Path.Combine(folder, relative));
                if (path.StartsWith(Path.GetFullPath(folder) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) && (File.Exists(path) || (relative.StartsWith("batch-", StringComparison.Ordinal) && Directory.Exists(Path.GetDirectoryName(path))))) return CompactCachedModel(folder, path);
                return null;
            }
            return Directory.EnumerateFiles(folder, "*_LOD0.cast", SearchOption.AllDirectories).SingleOrDefault();
        }
        public async Task<string> ExtractAsync(GameAssetRecord entry, string[] targets, CancellationToken cancellation = default)
        {
            if (!entry.Supports(targets)) throw new InvalidOperationException(L.T("#MODEL_SELECTED_ARCHIVES"));
            if (CacheRoot == null) throw new InvalidOperationException(L.T("#INDEX_ARCHIVES_EXTRACTION"));
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(shutdown.Token, cancellation);
            await worker.WaitAsync(linked.Token);
            try
            {
                var cached = CachedModel(entry); if (cached != null) return cached;
                if(ContinuousPreviewsSupported)return await Task.Run(()=>ExtractContinuous(entry,targets),shutdown.Token);
                var origin = entry.origins.First(o => o.mapId == "" || targets.Contains(o.mapId));
                string folder = ModelDirectory(entry);
                return await Task.Run(() => {
                    Directory.CreateDirectory(folder);
                    string filter = Path.Combine(folder, "guid.txt"); File.WriteAllText(filter, entry.guid);
                    string TryExport(bool geometryOnly)
                    {
                        string output = Path.Combine(folder, geometryOnly ? "geometry" : "textured"); Directory.CreateDirectory(output);
                        var args = new List<string> { "-export", "--exporttypes", "mdl_", "--exportdir", output };
                        if (!geometryOnly) args.Add("-matltextures");
                        if (UsesForkFeatures) { args.Add("--exportguids"); args.Add(filter); }
                        else { args.Add("--exportfilter"); args.Add("0x" + entry.guid); }
                        foreach (string dependency in geometryOnly ? new[] { "common_early.rpak" } : Common)
                            if (dependency != origin.archive && File.Exists(Path.Combine(PakDirectory, dependency))) args.Add(Path.Combine(PakDirectory, dependency));
                        args.Add(Path.Combine(PakDirectory, origin.archive));
                        RunRsx(args, folder, geometryOnly, linked.Token);
                        var paths = Directory.GetFiles(output, "*_LOD0.cast", SearchOption.AllDirectories);
                        if (paths.Length != 1) throw new IOException(L.T("#RSX_DID_PRODUCE_SINGLE_CAST"));
                        CastReader.Read(paths[0]); return paths[0];
                    }
                    string model;
                    try { model = TryExport(false); }
                    catch (IOException)
                    {
                        // Flowstate's viewer can export geometry while unresolved streamed textures crash Oodle.
                        model = TryExport(true);
                    }
                    File.WriteAllText(Path.Combine(folder, "complete.txt"), model.Substring(folder.Length + 1) + "\n" + origin.archive);
                    return model;
                }, linked.Token);
            }
            finally { worker.Release(); }
        }
        private void RunRsx(IEnumerable<string> operation, string root, bool geometryOnly = false, CancellationToken cancellation = default)
        {
            shutdown.Token.ThrowIfCancellationRequested(); cancellation.ThrowIfCancellationRequested();
            Directory.CreateDirectory(root);
            string logPath = Path.Combine(root, geometryOnly ? "rsx-geometry.log" : "rsx-worker.log");
            var args = new[] { "-nogui", "--loadwhitelist", geometryOnly ? "mdl_,Ptch" : "mdl_,matl,txtr,shdr,shds,Ptch", "--parsethreads", "1", "--exportthreads", "1" }
                .Concat(File.Exists(Settings.rsxExecutable + ".remap-session-v1") ? new[] { "-embedded" } : Array.Empty<string>()).Concat(operation);
            string workerRoot = Path.Combine(CacheDirectory, "Worker"); Directory.CreateDirectory(workerRoot);
            using (var log = new StreamWriter(logPath, false))
            using (var process = new Process())
            {
                process.StartInfo = new ProcessStartInfo { FileName = Settings.rsxExecutable, Arguments = string.Join(" ", args.Select(Quote)),
                    WorkingDirectory = workerRoot, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
                long logged = 0; object gate = new object();
                DataReceivedEventHandler write = (_, e) => { if (e.Data == null) return; lock (gate) { if (logged < 8 * 1024 * 1024) { log.WriteLine(e.Data); logged += e.Data.Length; } } };
                process.OutputDataReceived += write; process.ErrorDataReceived += write;
                if (!process.Start()) throw new IOException(L.T("#UNABLE_START_RSX"));
                process.BeginOutputReadLine(); process.BeginErrorReadLine();
                var timer = Stopwatch.StartNew();
                while (!process.WaitForExit(150))
                    if (shutdown.IsCancellationRequested || cancellation.IsCancellationRequested || timer.Elapsed.TotalMinutes > 3)
                    { process.Kill(); process.WaitForExit(); shutdown.Token.ThrowIfCancellationRequested(); cancellation.ThrowIfCancellationRequested(); throw new TimeoutException(L.T("#RSX_EXCEEDED_THREE_MINUTES_OPERATION")); }
                process.WaitForExit();
                shutdown.Token.ThrowIfCancellationRequested(); cancellation.ThrowIfCancellationRequested();
                if (process.ExitCode != 0) throw new IOException(L.T("#RSX_FAILED") + process.ExitCode + L.T("#LOG") + logPath);
            }
        }
        private static string Quote(string value)
        {
            if (value.Contains("\"") || value.Contains("\n") || value.Contains("\r")) throw new ArgumentException(L.T("#INVALID_RSX_ARGUMENT"));
            return "\"" + value.TrimEnd('\\') + "\"";
        }
        public void Dispose() { shutdown.Cancel(); previewSession?.Dispose(); previewSession=null; }
    }
}
