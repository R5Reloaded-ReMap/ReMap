using ReMap.Standalone.Core;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace ReMap.Standalone
{
    internal sealed class OfficialTextureRepairRequest
    {
        public GameAssetRecord Entry;
        public string LegacyCast;
        public SharedTextureCache.AlbedoInspection Inspection;
    }

    public sealed partial class RsxAssetLibrary
    {
        [Serializable]
        private sealed class OfficialTextureAttempt
        {
            public string fingerprint;
            public int requested;
            public int replaced;
        }

        private sealed class OfficialArchivePlan
        {
            public string primaryArchive;
            public string[] archives;
        }

        private static readonly string[] OfficialMapArchiveSuffixes =
            { ".rpak", "_client_perm.rpak", "_client_temp.rpak", "_loadscreen.rpak" };
        private static readonly Regex OfficialMapVariantSuffix = new Regex(
            @"(?:_64k_x_64k|_mu\d+|_hu|_uh|_nx\d*|_night\d*|_tt|_avt|_snapback|_landscape|_staging)$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private readonly HashSet<string> officialPreviewMisses = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> failedOfficialArchivePlans = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, OfficialArchivePlan> officialArchivePlans =
            new Dictionary<string, OfficialArchivePlan>(StringComparer.OrdinalIgnoreCase);
        private bool? officialPreviewAvailable;
        public bool OfficialTextureFallbackAvailable
        {
            get
            {
                if (!officialPreviewAvailable.HasValue)
                    officialPreviewAvailable = Settings.officialTextureFallback &&
                        IsOfficialApexInstallation(Settings.officialApexGameDirectory);
                return officialPreviewAvailable.Value;
            }
        }
        internal bool ShouldTryOfficialPreview(GameAssetRecord entry) =>
            entry != null && OfficialTextureFallbackAvailable && !officialPreviewMisses.Contains(entry.Id);

        public void ConfigureOfficialTextureFallback(string directory, bool enabled)
        {
            if (worker.CurrentCount == 0) throw new InvalidOperationException(L.T("#WAIT_RSX_OPERATION_FINISH"));
            directory = CleanPath(directory);
            if (!string.IsNullOrEmpty(directory) && !IsOfficialApexInstallation(directory))
                throw new ArgumentException(L.T("#OFFICIAL_APEX_FOLDER_INVALID"));
            Settings.officialApexGameDirectory = directory;
            Settings.officialTextureFallback = enabled;
            officialPreviewMisses.Clear();
            failedOfficialArchivePlans.Clear();
            officialArchivePlans.Clear();
            officialPreviewAvailable = null;
            SaveSettings();
        }

        public static bool IsOfficialApexInstallation(string path)
        {
            if (string.IsNullOrWhiteSpace(path) ||
                !File.Exists(Path.Combine(path, "start_protected_game.exe")) ||
                !File.Exists(Path.Combine(path, "r5apex_dx12.exe")) ||
                File.Exists(Path.Combine(path, "r5apex_ds.exe"))) return false;
            string paks = FindPakDirectory(path);
            return paks != null && File.Exists(Path.Combine(paks, "common.rpak"));
        }

        private void DetectOfficialApex(IEnumerable<string> nearbyRoots)
        {
            if (!string.IsNullOrWhiteSpace(Settings.officialApexGameDirectory) &&
                !IsOfficialApexInstallation(Settings.officialApexGameDirectory))
                Settings.officialApexGameDirectory = "";
            if (!string.IsNullOrWhiteSpace(Settings.officialApexGameDirectory)) return;

            var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            AddWindowsShortcutCandidates(candidates);
            foreach (string root in nearbyRoots ?? Array.Empty<string>()) AddOfficialCandidates(candidates, root);
            string found = candidates.FirstOrDefault(IsOfficialApexInstallation);
            if (!string.IsNullOrEmpty(found))
            {
                Settings.officialApexGameDirectory = found;
                return;
            }
            foreach (var drive in DriveInfo.GetDrives())
            {
                if (!drive.IsReady || drive.DriveType == DriveType.Network || drive.DriveType == DriveType.CDRom ||
                    drive.DriveType == DriveType.NoRootDirectory) continue;
                string root = drive.RootDirectory.FullName;
                AddOfficialCandidates(candidates, root);
                AddOfficialCandidates(candidates, Path.Combine(root, "Games"));
                AddOfficialCandidates(candidates, Path.Combine(root, "SteamLibrary"));
                AddSteamLibraries(candidates, Path.Combine(root, "Program Files (x86)", "Steam"));
                AddSteamLibraries(candidates, Path.Combine(root, "Steam"));
                AddSteamLibraries(candidates, Path.Combine(root, "Games", "steamapps"));
                found = candidates.FirstOrDefault(IsOfficialApexInstallation);
                if (!string.IsNullOrEmpty(found))
                {
                    Settings.officialApexGameDirectory = found;
                    return;
                }
            }
            Settings.officialApexGameDirectory = "";
        }

        private static void AddWindowsShortcutCandidates(HashSet<string> candidates)
        {
            if (Environment.OSVersion.Platform != PlatformID.Win32NT) return;
            foreach (Environment.SpecialFolder folder in new[]
            {
                Environment.SpecialFolder.DesktopDirectory,
                Environment.SpecialFolder.CommonDesktopDirectory,
                Environment.SpecialFolder.StartMenu,
                Environment.SpecialFolder.CommonStartMenu
            })
            {
                string root = Environment.GetFolderPath(folder);
                if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) continue;
                try
                {
                    foreach (string shortcut in Directory.EnumerateFiles(root, "Apex Legends.lnk",
                        SearchOption.AllDirectories))
                        AddShortcutTarget(candidates, shortcut);
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }

        private static void AddShortcutTarget(HashSet<string> candidates, string shortcutPath)
        {
            object shell = null;
            object shortcut = null;
            try
            {
                Type shellType = Type.GetTypeFromProgID("WScript.Shell");
                if (shellType == null) return;
                shell = Activator.CreateInstance(shellType);
                shortcut = shellType.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod,
                    null, shell, new object[] { shortcutPath });
                string target = shortcut?.GetType().InvokeMember("TargetPath", BindingFlags.GetProperty,
                    null, shortcut, null) as string;
                string workingDirectory = shortcut?.GetType().InvokeMember("WorkingDirectory", BindingFlags.GetProperty,
                    null, shortcut, null) as string;
                if (!string.IsNullOrWhiteSpace(target)) AddOfficialCandidates(candidates, Path.GetDirectoryName(target));
                if (!string.IsNullOrWhiteSpace(workingDirectory)) AddOfficialCandidates(candidates, workingDirectory);
            }
            catch (Exception exception) when (exception is COMException || exception is TargetInvocationException ||
                exception is ArgumentException || exception is NotSupportedException) { }
            finally
            {
                if (shortcut != null && Marshal.IsComObject(shortcut)) Marshal.FinalReleaseComObject(shortcut);
                if (shell != null && Marshal.IsComObject(shell)) Marshal.FinalReleaseComObject(shell);
            }
        }
        private static void AddOfficialCandidates(HashSet<string> candidates, string root)
        {
            if (string.IsNullOrWhiteSpace(root)) return;
            try
            {
                candidates.Add(Path.GetFullPath(root));
                candidates.Add(Path.GetFullPath(Path.Combine(root, "Apex Legends")));
                candidates.Add(Path.GetFullPath(Path.Combine(root, "steamapps", "common", "Apex Legends")));
                candidates.Add(Path.GetFullPath(Path.Combine(root, "steamapps", "steamapps", "common", "Apex Legends")));
            }
            catch (Exception exception) when (exception is ArgumentException || exception is NotSupportedException || exception is PathTooLongException) { }
        }

        private static void AddSteamLibraries(HashSet<string> candidates, string steamRoot)
        {
            AddOfficialCandidates(candidates, steamRoot);
            string vdf = Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf");
            if (!File.Exists(vdf)) return;
            try
            {
                foreach (Match match in Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s*\"([^\"]+)\"",
                    RegexOptions.IgnoreCase))
                    AddOfficialCandidates(candidates, match.Groups[1].Value.Replace("\\\\", "\\"));
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        private static string MapIdFromArchive(string archive)
        {
            string name = Path.GetFileName(archive) ?? "";
            foreach (string suffix in new[] { "_client_perm.rpak", "_client_temp.rpak", "_loadscreen.rpak", ".rpak" })
                if (name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                    return name.Substring(0, name.Length - suffix.Length);
            return "";
        }

        private static string OfficialMapFamily(string mapId)
        {
            string family = mapId ?? "";
            while (true)
            {
                string parent = OfficialMapVariantSuffix.Replace(family, "");
                if (parent.Length == family.Length) return family;
                family = parent;
            }
        }

        private static int OfficialMapRevision(string mapId)
        {
            int revision = 0;
            foreach (Match match in Regex.Matches(mapId ?? "", @"_mu(\d+)(?:_|$)",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                if (int.TryParse(match.Groups[1].Value, out int value)) revision = Math.Max(revision, value);
            return revision;
        }

        public static string[] SelectOfficialMapArchives(string originArchive,
            IEnumerable<string> availableArchives)
        {
            string sourceMap = MapIdFromArchive(originArchive);
            if (!sourceMap.StartsWith("mp_rr_", StringComparison.OrdinalIgnoreCase)) return Array.Empty<string>();

            string[] names = (availableArchives ?? Array.Empty<string>()).Select(Path.GetFileName)
                .Where(name => !string.IsNullOrEmpty(name)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            var available = new HashSet<string>(names, StringComparer.OrdinalIgnoreCase);
            string sourceFamily = OfficialMapFamily(sourceMap);
            string selectedMap = names
                .Where(name => name.EndsWith(".rpak", StringComparison.OrdinalIgnoreCase) &&
                    !name.EndsWith("_client_perm.rpak", StringComparison.OrdinalIgnoreCase) &&
                    !name.EndsWith("_client_temp.rpak", StringComparison.OrdinalIgnoreCase) &&
                    !name.EndsWith("_loadscreen.rpak", StringComparison.OrdinalIgnoreCase))
                .Select(name => name.Substring(0, name.Length - ".rpak".Length))
                .Where(map => string.Equals(OfficialMapFamily(map), sourceFamily,
                    StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(map => string.Equals(map, sourceMap, StringComparison.OrdinalIgnoreCase))
                .ThenByDescending(map => map.StartsWith(sourceMap + "_", StringComparison.OrdinalIgnoreCase))
                .ThenByDescending(map => sourceMap.StartsWith(map + "_", StringComparison.OrdinalIgnoreCase))
                .ThenByDescending(OfficialMapRevision)
                .ThenByDescending(map => map.Length)
                .ThenBy(map => map, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
            if (string.IsNullOrEmpty(selectedMap)) return Array.Empty<string>();

            return OfficialMapArchiveSuffixes.Select(suffix => selectedMap + suffix)
                .Where(available.Contains).ToArray();
        }

        public static string[] SelectOfficialProjectArchives(IEnumerable<string> targets,
            IEnumerable<string> availableArchives)
        {
            string[] available = (availableArchives ?? Array.Empty<string>()).Select(Path.GetFileName)
                .Where(name => !string.IsNullOrEmpty(name)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            string[] common = new[] { "common_early.rpak", "common.rpak", "common_mp.rpak", "common_roots.rpak" }
                .Concat(available.Where(name => name.StartsWith("common", StringComparison.OrdinalIgnoreCase))
                    .OrderBy(name => name, StringComparer.OrdinalIgnoreCase))
                .Where(name => available.Contains(name, StringComparer.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            string[] maps = (targets ?? Array.Empty<string>()).Where(target =>
                    !string.IsNullOrWhiteSpace(target)).Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(target => target, StringComparer.OrdinalIgnoreCase)
                .SelectMany(target => SelectOfficialMapArchives(target + ".rpak", available))
                .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            return common.Concat(maps).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        }

        private OfficialArchivePlan ResolveOfficialArchivePlan(string officialPaks, string origin,
            IEnumerable<string> targets)
        {
            string[] selectedTargets = (targets ?? Array.Empty<string>()).Where(target =>
                !string.IsNullOrWhiteSpace(target)).Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(target => target, StringComparer.OrdinalIgnoreCase).ToArray();
            string cacheKey = Path.GetFullPath(officialPaks) + "|" + origin + "|" +
                string.Join(";", selectedTargets);
            if (officialArchivePlans.TryGetValue(cacheKey, out OfficialArchivePlan cached))
                return cached != null && !failedOfficialArchivePlans.Contains(cached.primaryArchive) ? cached : null;

            string[] available = Directory.EnumerateFiles(officialPaks, "*.rpak", SearchOption.TopDirectoryOnly)
                .Select(Path.GetFileName).ToArray();
            string[] projectArchives = SelectOfficialProjectArchives(selectedTargets, available);
            string[] required;
            string mapId = MapIdFromArchive(origin);
            if (mapId.StartsWith("mp_rr_", StringComparison.OrdinalIgnoreCase))
            {
                required = SelectOfficialMapArchives(origin, available);
                // The requested map may not be active in the installed season. Never spend time
                // loading an unrelated project union in that case: use R5R/R5F immediately.
                if (required.Length == 0) return officialArchivePlans[cacheKey] = null;
            }
            else
            {
                string exact = available.FirstOrDefault(name => string.Equals(name, origin,
                    StringComparison.OrdinalIgnoreCase));
                if (exact == null) return officialArchivePlans[cacheKey] = null;
                required = new[] { exact };
            }

            string primary = required[0];
            var plan = new OfficialArchivePlan
            {
                primaryArchive = primary,
                // Mirror the project's in-game archive union. The requested model must not change
                // this list, otherwise common <-> map imports make RSX parse every RPAK again.
                archives = projectArchives.Concat(required)
                    .Distinct(StringComparer.OrdinalIgnoreCase).Select(name =>
                    Path.Combine(officialPaks, name)).Where(File.Exists).ToArray()
            };
            officialArchivePlans[cacheKey] = plan;
            Debug.Log("REMAP_OFFICIAL_ARCHIVES: " + string.Join(", ",
                plan.archives.Select(Path.GetFileName)));
            return failedOfficialArchivePlans.Contains(primary) ? null : plan;
        }

        private bool TryExtractOfficialPreviews(GameAssetRecord[] entries, string[] targets, AssetBatchResult result,
            CancellationToken cancellation)
        {
            cancellation.ThrowIfCancellationRequested();
            if (entries == null || entries.Length == 0 || !OfficialTextureFallbackAvailable ||
                SessionExecutable == null || entries.Any(entry => !ShouldTryOfficialPreview(entry))) return false;
            string officialPaks = FindPakDirectory(Settings.officialApexGameDirectory);
            if (officialPaks == null) return false;
            string origin = OriginArchive(entries[0], targets);
            OfficialArchivePlan plan = ResolveOfficialArchivePlan(officialPaks, origin, targets);
            if (plan == null)
            {
                foreach (var entry in entries) officialPreviewMisses.Add(entry.Id);
                return false;
            }

            result.OfficialAttempts.UnionWith(entries.Select(entry => entry.Id));

            try
            {
                // Reuse the same embedded process. LOAD can switch between official and legacy
                // archive sets without paying for a process restart for every thumbnail batch.
                SetExtractionActivity(AssetExtractionSource.OfficialApex,
                    AssetExtractionOperation.LoadingArchives, plan.primaryArchive, entries.Length);
                EnsurePreviewSession(plan.archives, false, true);
                previewSession.Load(plan.archives, origin);
                SetExtractionActivity(AssetExtractionSource.OfficialApex,
                    AssetExtractionOperation.ExportingModels, plan.primaryArchive, entries.Length);
                string output = previewSession.ExportMany(entries.Select(entry => entry.guid).ToArray());
                int completedBefore = result.Paths.Count;
                CommitOfficialPreviews(entries, output, previewSession.Root, plan.primaryArchive, result);
                Debug.Log("REMAP_OFFICIAL_PREVIEW_BATCH: " + (result.Paths.Count - completedBefore) + "/" +
                    entries.Length + " from " + plan.primaryArchive);
                foreach (var entry in entries)
                    if (!result.Paths.ContainsKey(entry.Id)) officialPreviewMisses.Add(entry.Id);
            }
            catch (Exception exception) when (exception is IOException || exception is TimeoutException)
            {
                if (cancellation.IsCancellationRequested)
                {
                    ResetPreviewSession(); cancellation.ThrowIfCancellationRequested();
                }
                Debug.LogWarning("REMAP_OFFICIAL_PREVIEW_FALLBACK: " + exception.Message);
                foreach (var entry in entries) officialPreviewMisses.Add(entry.Id);
                if (exception is TimeoutException || previewSession == null || !previewSession.Alive)
                {
                    failedOfficialArchivePlans.Add(plan.primaryArchive);
                    ResetPreviewSession();
                }
            }
            cancellation.ThrowIfCancellationRequested();
            foreach (var entry in entries)
                if (!result.Paths.ContainsKey(entry.Id)) result.Deferred.Add(entry.Id);
            return true;
        }

        private void CommitOfficialPreviews(GameAssetRecord[] entries, string output, string sessionRoot,
            string origin, AssetBatchResult result)
        {
            string[] exported = Directory.Exists(output)
                ? Directory.GetFiles(output, "*_LOD0.cast", SearchOption.AllDirectories) : Array.Empty<string>();
            foreach (var entry in entries)
            {
                string[] matches = exported.Where(path => string.Equals(Path.GetFileName(path),
                    entry.Name + "_LOD0.cast", StringComparison.OrdinalIgnoreCase)).ToArray();
                if (matches.Length != 1) continue;
                try
                {
                    CastReader.Read(matches[0]);
                    string source = Path.GetFullPath(Path.GetDirectoryName(matches[0]));
                    string modelRoot = ModelDirectory(entry);
                    string destination = Path.GetFullPath(Path.Combine(modelRoot,
                        "o" + Guid.NewGuid().ToString("N").Substring(0, 8)));
                    if (!source.StartsWith(Path.GetFullPath(sessionRoot) + Path.DirectorySeparatorChar,
                        StringComparison.OrdinalIgnoreCase) ||
                        !destination.StartsWith(Path.GetFullPath(modelRoot) + Path.DirectorySeparatorChar,
                        StringComparison.OrdinalIgnoreCase)) continue;
                    Directory.CreateDirectory(modelRoot);
                    Directory.Move(source, destination);
                    string cast = Path.Combine(destination, Path.GetFileName(matches[0]));
                    File.WriteAllText(Path.Combine(modelRoot, "complete.txt"),
                        Path.GetRelativePath(modelRoot, cast) + "\n" + "official:" + origin +
                        "\ntextured\n" + ActiveCacheGeneration());
                    result.Paths[entry.Id] = cast;
                    result.Errors.Remove(entry.Id);
                }
                catch (Exception exception) when (exception is IOException || exception is ArgumentException)
                {
                    Debug.LogWarning("REMAP_OFFICIAL_PREVIEW_COMMIT_FAILED: " + entry.modelPath + ": " + exception.Message);
                }
            }
        }

        private sealed class PreparedOfficialRepair
        {
            public OfficialTextureRepairRequest request;
            public OfficialArchivePlan plan;
            public string marker;
            public string fingerprint;
        }

        internal async Task<HashSet<string>> TryRepairOfficialTexturesBatchAsync(
            IEnumerable<OfficialTextureRepairRequest> requests, string[] targets,
            CancellationToken cancellation = default, bool forceRefresh = false)
        {
            var repaired = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var candidates = (requests ?? Enumerable.Empty<OfficialTextureRepairRequest>())
                .Where(request => request?.Entry != null && request.Inspection?.NeedsFallback == true &&
                    !string.IsNullOrWhiteSpace(request.LegacyCast) && !officialPreviewMisses.Contains(request.Entry.Id))
                .ToArray();
            if (candidates.Length == 0 || !OfficialTextureFallbackAvailable || SessionExecutable == null) return repaired;
            if (candidates.Length > 8 && !BulkTextureRepairsSupported)
                throw new ArgumentException(L.T("#BATCH_1_8_MODELS_REQUIRED"));

            string officialPaks = FindPakDirectory(Settings.officialApexGameDirectory);
            if (officialPaks == null) return repaired;
            var pending = new List<PreparedOfficialRepair>();
            foreach (var request in candidates)
            {
                string origin = OriginArchive(request.Entry, targets);
                OfficialArchivePlan plan = ResolveOfficialArchivePlan(officialPaks, origin, targets);
                if (plan == null) continue;
                string modelRoot = ModelDirectory(request.Entry);
                string marker = Path.Combine(modelRoot, "official-texture-fallback.json");
                string fingerprint = OfficialTextureFingerprint(plan.archives, request.Inspection.materialHashes);
                try
                {
                    if (!forceRefresh && File.Exists(marker))
                    {
                        var previous = JsonUtility.FromJson<OfficialTextureAttempt>(File.ReadAllText(marker));
                        if (previous != null && previous.fingerprint == fingerprint)
                        {
                            if (previous.replaced > 0) repaired.Add(request.Entry.Id);
                            continue;
                        }
                    }
                }
                catch (IOException) { }
                pending.Add(new PreparedOfficialRepair
                    { request = request, plan = plan, marker = marker, fingerprint = fingerprint });
            }
            if (pending.Count == 0) return repaired;
            if (pending.Select(item => item.plan.primaryArchive).Distinct(StringComparer.OrdinalIgnoreCase).Count() != 1 ||
                pending.Select(item => item.request.Entry.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != pending.Count)
                throw new ArgumentException(L.T("#BATCH_SHARE_ARCHIVE_HAVE_DISTINCT"));

            using var linked = CancellationTokenSource.CreateLinkedTokenSource(shutdown.Token, cancellation);
            await worker.WaitAsync(linked.Token);
            string output = null, sessionRoot = null;
            try
            {
                previewArchivePlan = null;
                OfficialArchivePlan plan = pending[0].plan;
                string origin = OriginArchive(pending[0].request.Entry, targets);
                string[] guids = pending.Select(item => item.request.Entry.guid).ToArray();
                var export = await Task.Run(() =>
                {
                    SetExtractionActivity(AssetExtractionSource.OfficialApex,
                        AssetExtractionOperation.LoadingArchives, plan.primaryArchive, pending.Count);
                    EnsurePreviewSession(plan.archives, false, true);
                    previewSession.Load(plan.archives, origin);
                    SetExtractionActivity(AssetExtractionSource.OfficialApex,
                        AssetExtractionOperation.ExportingModels, plan.primaryArchive, pending.Count);
                    string exportedRoot = guids.Length == 1 ? previewSession.Export(guids[0]) :
                        guids.Length > 8 ? previewSession.ExportBulk(guids) : previewSession.ExportBatch(guids);
                    string[] paths = Directory.Exists(exportedRoot)
                        ? Directory.GetFiles(exportedRoot, "*_LOD0.cast", SearchOption.AllDirectories)
                        : Array.Empty<string>();
                    return (Output: exportedRoot, Root: previewSession.Root, Paths: paths);
                }, linked.Token);
                output = export.Output; sessionRoot = export.Root; string[] exported = export.Paths;
                SetExtractionActivity(AssetExtractionSource.OfficialApex,
                    AssetExtractionOperation.RepairingTextures, plan.primaryArchive, pending.Count);
                foreach (var item in pending)
                {
                    string[] matches = exported.Where(path => string.Equals(Path.GetFileName(path),
                        item.request.Entry.Name + "_LOD0.cast", StringComparison.OrdinalIgnoreCase)).ToArray();
                    if (matches.Length == 0 && pending.Count == 1 && exported.Length == 1) matches = exported;
                    int replaced = 0;
                    if (matches.Length == 1)
                    {
                        CastReader.Read(matches[0]);
                        replaced = SharedTextureCache.ReplaceAlbedosFromOfficial(
                            ModelDirectory(item.request.Entry), item.request.LegacyCast, matches[0],
                            sessionRoot, item.request.Inspection.materialHashes);
                    }
                    Directory.CreateDirectory(ModelDirectory(item.request.Entry));
                    File.WriteAllText(item.marker, JsonUtility.ToJson(new OfficialTextureAttempt
                        { fingerprint = item.fingerprint, requested = item.request.Inspection.materialHashes.Count,
                            replaced = replaced }, true));
                    if (replaced <= 0) continue;
                    repaired.Add(item.request.Entry.Id);
                    Debug.Log("REMAP_OFFICIAL_TEXTURE_FALLBACK: " + item.request.Entry.modelPath +
                        " (" + replaced + ")");
                }
            }
            catch (Exception exception) when (exception is IOException || exception is TimeoutException ||
                exception is OperationCanceledException || exception is InvalidDataException)
            {
                Debug.LogWarning("REMAP_OFFICIAL_TEXTURE_BATCH_FAILED: " + exception.Message);
                if (exception is TimeoutException || previewSession == null || !previewSession.Alive) ResetPreviewSession();
                if (exception is OperationCanceledException) throw;
            }
            finally
            {
                previewArchivePlan = null;
                if (!string.IsNullOrWhiteSpace(output))
                {
                    try
                    {
                        string root = Path.GetFullPath(sessionRoot ?? "") + Path.DirectorySeparatorChar;
                        string candidate = Path.GetFullPath(output);
                        if (candidate.StartsWith(root, StringComparison.OrdinalIgnoreCase) && Directory.Exists(candidate))
                            Directory.Delete(candidate, true);
                    }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                }
                worker.Release();
            }
            return repaired;
        }

        public async Task<bool> TryRepairOfficialTexturesAsync(GameAssetRecord entry, string legacyCast,
            SharedTextureCache.AlbedoInspection inspection, string[] targets, CancellationToken cancellation = default,
            bool forceRefresh = false)
        {
            var request = new OfficialTextureRepairRequest
                { Entry = entry, LegacyCast = legacyCast, Inspection = inspection };
            var repaired = await TryRepairOfficialTexturesBatchAsync(new[] { request }, targets,
                cancellation, forceRefresh);
            return entry != null && repaired.Contains(entry.Id);
        }

        private string OfficialTextureFingerprint(IEnumerable<string> archives, IEnumerable<ulong> materials)
        {
            var values = new List<string>();
            foreach (string path in new[] { SessionExecutable }.Concat(archives))
            {
                var info = new FileInfo(path);
                values.Add(info.FullName + "|" + info.Length + "|" + info.LastWriteTimeUtc.Ticks);
            }
            values.AddRange(materials.OrderBy(value => value).Select(value => value.ToString("x16")));
            return string.Join("\n", values);
        }
    }
}
