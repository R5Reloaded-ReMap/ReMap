using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ReMap.Standalone.Core;

namespace ReMap.Standalone
{
    public readonly struct MapReferenceProgress
    {
        public readonly string Message;
        public readonly float Value;
        public MapReferenceProgress(string message, float value) { Message = message; Value = value; }
    }

    public sealed class MapReferenceData
    {
        public string BspPath;
        public string MprtPath;
        public string ExtractionRoot;
        public ApexBspMap Map;
    }

    // Locates and unpacks only the selected map VPK, then hands the BSP to our own readers.
    public static class MapReferenceExtractor
    {
        private static readonly string[] EntityLumpKinds = { "script", "snd", "spawn", "env", "fx" };

        public static async Task<MapReferenceData> LoadAsync(RsxAssetLibrary library, string mapId, bool force,
            IProgress<MapReferenceProgress> progress, CancellationToken cancellation)
        {
            if (library == null) throw new ArgumentNullException(nameof(library));
            mapId = (mapId ?? "").Trim();
            if (!ValidMapId(mapId)) throw new ArgumentException(L.T("#SELECT_VALID_EDITED_MAP_BSP"));

            string root = Path.Combine(library.CacheDirectory, "MapReferences", library.TargetGame, mapId);
            Directory.CreateDirectory(root);
            string bsp = force ? null : FindExtractedBsp(root, mapId);
            if (bsp == null)
            {
                await ExtractMapArchiveAsync(library, mapId, root, progress, cancellation);
                bsp = FindExtractedBsp(root, mapId);
                if (bsp == null) throw new InvalidDataException("ReVPK completed but no BSP was produced for " + mapId + ".");
            }

            progress?.Report(new MapReferenceProgress("Reading BSP geometry…", .28f));
            var map = await Task.Run(() => ApexBspReader.Read(bsp, true), cancellation);
            cancellation.ThrowIfCancellationRequested();
            string mprt = Path.Combine(root, mapId + ".mprt");
            await Task.Run(() => MprtReader.Write(mprt, map.StaticProps), cancellation);
            progress?.Report(new MapReferenceProgress("BSP and MPRT data ready.", .42f));
            return new MapReferenceData { BspPath = bsp, MprtPath = mprt, ExtractionRoot = root, Map = map };
        }

        public static async Task<string> ExtractEntityLumpsAsync(RsxAssetLibrary library, string mapId, bool force, IProgress<MapReferenceProgress> progress, CancellationToken cancellation)
        {
            if (library == null) throw new ArgumentNullException(nameof(library));
            mapId = (mapId ?? "").Trim();
            if (!ValidMapId(mapId)) throw new ArgumentException(L.T("#SELECT_VALID_EDITED_MAP_ENTITIES"));
            string root = Path.Combine(library.CacheDirectory, "MapReferences", library.TargetGame, mapId);
            Directory.CreateDirectory(root);
            if (force || EntityLumpKinds.Any(kind => FindExtractedEntityLump(root, mapId, kind) == null))
                await ExtractMapArchiveAsync(library, mapId, root, progress, cancellation);
            foreach (string kind in EntityLumpKinds)
                if (FindExtractedEntityLump(root, mapId, kind) == null)
                    throw new FileNotFoundException(L.F("#REVPK_ENTITY_LUMP_MISSING_ARG0_ARG1", kind, mapId));
            return FindExtractedEntityLump(root, mapId, "script");
        }

        private static async Task ExtractMapArchiveAsync(RsxAssetLibrary library, string mapId, string root, IProgress<MapReferenceProgress> progress, CancellationToken cancellation)
        {
            string archive = FindMapArchive(library.GameDirectory, mapId);
            if (archive == null) throw new FileNotFoundException(L.F("#BSP_VPK_NOT_FOUND_ARG0", mapId));
            string revpkSource = FindRevpk(library);
            if (revpkSource == null) throw new FileNotFoundException(L.T("#REVPK_NOT_FOUND_CONFIGURED_GAMES"));
            string revpk = StageRevpk(revpkSource);
            progress?.Report(new MapReferenceProgress("Extracting " + mapId + " BSP and entity lumps…", .08f));
            await RunProcessAsync(revpk, "-unpack " + Quote(Path.GetFileName(archive)) + " " + Quote(CommandPath(root)) + " 0", cancellation, Path.GetDirectoryName(archive));
        }

        private static string FindExtractedEntityLump(string root, string mapId, string kind)
        {
            if (!Directory.Exists(root)) return null;
            return Directory.EnumerateFiles(root, mapId + "_" + kind + ".ent", SearchOption.AllDirectories).FirstOrDefault();
        }

        public static string FindMapArchive(string gameDirectory, string mapId)
        {
            if (!ValidMapId(mapId) || string.IsNullOrWhiteSpace(gameDirectory)) return null;
            foreach (string directory in new[] {
                Path.Combine(gameDirectory, "vpk"), Path.Combine(gameDirectory, "vpk_"), gameDirectory })
            {
                if (!Directory.Exists(directory)) continue;
                string suffix = "server_" + mapId + ".bsp.pak000_dir.vpk";
                string found = Directory.EnumerateFiles(directory, "*" + suffix, SearchOption.TopDirectoryOnly)
                    .OrderBy(path => path.StartsWith(Path.Combine(directory, "english"), StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                    .FirstOrDefault();
                if (found != null) return Path.GetFullPath(found);
            }
            return null;
        }

        public static string FindRevpk(RsxAssetLibrary library)
        {
            string applicationRoot = Path.GetFullPath(Path.Combine(UnityEngine.Application.dataPath, ".."));
            foreach (string game in new[] {
                applicationRoot, library.GameDirectory, library.Settings.r5ReloadedGameDirectory, library.Settings.r5FlowstateGameDirectory })
            {
                if (string.IsNullOrWhiteSpace(game)) continue;
                foreach (string relative in new[] { "revpk.exe", Path.Combine("bin", "revpk.exe") })
                {
                    string candidate = Path.Combine(game, relative);
                    if (File.Exists(candidate)) return Path.GetFullPath(candidate);
                }
            }
            return null;
        }

        private static string FindExtractedBsp(string root, string mapId)
        {
            if (!Directory.Exists(root)) return null;
            string exact = Directory.EnumerateFiles(root, mapId + ".bsp", SearchOption.AllDirectories).FirstOrDefault();
            return exact ?? Directory.EnumerateFiles(root, "*.bsp", SearchOption.AllDirectories).FirstOrDefault();
        }

        private static bool ValidMapId(string value) =>
            value.StartsWith("mp_", StringComparison.Ordinal) && value.Length <= 128 &&
            value.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 && value.IndexOfAny(new[] { '/', '\\' }) < 0;

        private static async Task RunProcessAsync(string executable, string arguments, CancellationToken cancellation,
            string workingDirectory = null)
        {
            var errors = new StringBuilder();
            using (var process = new Process())
            {
                process.StartInfo = new ProcessStartInfo {
                    FileName = executable, Arguments = arguments, UseShellExecute = false,
                    WorkingDirectory = string.IsNullOrWhiteSpace(workingDirectory)
                        ? Path.GetDirectoryName(executable) : workingDirectory,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true, RedirectStandardError = true
                };
                process.OutputDataReceived += (_, e) => { };
                process.ErrorDataReceived += (_, e) => { if (!string.IsNullOrEmpty(e.Data) && errors.Length < 16000) errors.AppendLine(e.Data); };
                if (!process.Start()) throw new InvalidOperationException(L.T("#COULD_NOT_START_REVPK"));
                process.BeginOutputReadLine(); process.BeginErrorReadLine();
                using (cancellation.Register(() => { try { if (!process.HasExited) process.Kill(); } catch { } }))
                    await Task.Run(() => process.WaitForExit(), cancellation);
                cancellation.ThrowIfCancellationRequested();
                if (process.ExitCode != 0) throw new InvalidOperationException(L.F("#REVPK_FAILED_ARG0", errors.ToString().Trim()));
            }
        }

        private static string Quote(string value)
        {
            string quote = ((char)34).ToString();
            return quote + value.Replace(quote, "") + quote;
        }

        // ReVPK rebuilds its command line without preserving quotes. A staged executable keeps argv[0]
        // space-free; 8.3 paths cover configured installations whose folders contain spaces.
        private static string StageRevpk(string source)
        {
            string directory = Path.Combine(Path.GetTempPath(), "ReMapTools");
            Directory.CreateDirectory(directory);
            string destination = Path.Combine(directory, "revpk.exe");
            if (!File.Exists(destination) || File.GetLastWriteTimeUtc(destination) != File.GetLastWriteTimeUtc(source) ||
                new FileInfo(destination).Length != new FileInfo(source).Length)
            {
                File.Copy(source, destination, true);
                File.SetLastWriteTimeUtc(destination, File.GetLastWriteTimeUtc(source));
            }
            return CommandPath(destination);
        }

        private static string CommandPath(string path)
        {
            path = Path.GetFullPath(path);
            if (path.IndexOf(' ') < 0) return path;
            var shortPath = new StringBuilder(1024);
            uint length = GetShortPathName(path, shortPath, (uint)shortPath.Capacity);
            if (length > 0 && length < shortPath.Capacity && shortPath.ToString().IndexOf(' ') < 0)
                return shortPath.ToString();
            throw new IOException(L.F("#REVPK_PATH_WITHOUT_SPACES_ARG0", path));
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern uint GetShortPathName(string longPath, StringBuilder shortPath, uint bufferLength);
    }
}
