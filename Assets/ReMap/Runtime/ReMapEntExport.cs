using ReMap.Standalone.Core;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

namespace ReMap.Standalone
{
    public sealed class ReMapEntFragments
    {
        public string Script { get; internal set; } = "";
        public string Sound { get; internal set; } = "";
        public string Spawn { get; internal set; } = "";
        public int ScriptEntityCount { get; internal set; }
        public int SoundEntityCount { get; internal set; }
        public int SpawnEntityCount { get; internal set; }
        public string PlayerStart { get; internal set; } = "";
        public IReadOnlyList<string> NutOnlyObjects { get; internal set; } = Array.Empty<string>();
        public IReadOnlyList<string> NutOnlyObjectIds { get; internal set; } = Array.Empty<string>();
        public int EntityCount => ScriptEntityCount + SoundEntityCount + SpawnEntityCount;
    }

    public sealed class ReMapLooseMapInstall
    {
        public string MapName { get; internal set; } = "";
        public string LevelSettingsPath { get; internal set; } = "";
        public IReadOnlyList<string> EntityLumpPaths { get; internal set; } = Array.Empty<string>();
        public IReadOnlyList<string> DiskPriorityConfigPaths { get; internal set; } = Array.Empty<string>();
    }

    // Native level publication serializes objects with a faithful entity-lump representation
    // and merges them into copies of the original map lumps so num_models and Respawn's
    // existing entities stay intact. Unsupported objects remain available through .nut.
    public static class ReMapEntExporter
    {
        private static readonly string[] LumpKinds = { "env", "fx", "script", "snd", "spawn" };
        private const string DiskPriorityBegin = "// ReMap loose ENT disk priority - begin";
        private const string DiskPriorityEnd = "// ReMap loose ENT disk priority - end";
        private static readonly Regex Header = new Regex(@"\AENTITIES02 num_models=\d+(?:\r?\n|\z)",
            RegexOptions.CultureInvariant);
        private static readonly Regex EntityBlock = new Regex(@"(?ms)^\{\r?\n.*?^\}", RegexOptions.CultureInvariant);

        public static ReMapEntFragments Generate(MapDocument document,
            IEnumerable<MapObject> worldObjects)
        {
            if (document == null) throw new ArgumentNullException(nameof(document));
            ValidateMapName(document.editingMap);
            var world = (worldObjects ?? throw new ArgumentNullException(nameof(worldObjects)))
                .Where(item => item != null && !item.disabled).ToList();
            var script = new StringBuilder();
            var sound = new StringBuilder();
            var spawn = new StringBuilder();
            var playerStart = new StringBuilder();
            var nutOnly = new List<string>();
            var nutOnlyIds = new HashSet<string>(StringComparer.Ordinal);
            Vector3 offset = WorldView.ToVector(document.originOffset);
            int scriptCount = 0, soundCount = 0, spawnCount = 0;

            foreach (var item in world.Where(item => !item.isGroup && item.customType == "" && !item.clientSide))
            {
                AppendProp(script, item, offset, true);
                scriptCount++;
            }
            foreach (var item in world.Where(item => !item.isGroup && item.customType == "" && item.clientSide))
            {
                nutOnly.Add(Display(item) + " (client-side prop)");
                nutOnlyIds.Add(item.id);
            }

            foreach (var item in world.Where(item => item.customType == "door"))
            {
                if (item.doorGold || item.doorSpawnOpen)
                {
                    nutOnly.Add(Display(item) + " (door options require .nut)");
                    nutOnlyIds.Add(item.id);
                    continue;
                }
                scriptCount += AppendDoor(script, item, offset);
            }

            var byId = world.ToDictionary(item => item.id, StringComparer.Ordinal);
            foreach (var item in world.Where(item => item.customType == "zipline"))
                scriptCount += AppendZipline(script, item, byId, offset);
            foreach (var item in world.Where(item => item.customType == "curved-zipline"))
                scriptCount += AppendCurvedZipline(script, item, world, offset);
            if (GameTargets.Normalize(document.gameTarget) == GameTargets.R5Flowstate)
            {
                foreach (var item in world.Where(item => item.customType == "ziprail"))
                {
                    scriptCount += AppendZiprail(script, item, world, offset);
                    soundCount += AppendZiprailSounds(sound, item, world, offset);
                }
            }

            foreach (var item in world.Where(item => item.customType == "loot-bin"))
            {
                AppendAnimatedProp(script, item, offset, ReMapApp.LootBinModelPath,
                    "survival_lootbin", item.lootBinSkin);
                scriptCount++;
            }

            foreach (var item in world.Where(item => item.customType == "jump-tower"))
            {
                scriptCount += AppendJumpTowerModels(script, item, offset);
                nutOnly.Add(Display(item) + " (gameplay requires .nut)");
                nutOnlyIds.Add(item.id);
            }

            foreach (var item in world.Where(item => item.customType == "window-hint"))
            {
                Vector3 right = ReMapGameScript.WindowHintRight(WorldView.ToVector(item.rotation));
                AppendEntity(script,
                    Pair("halfheight", Number(item.windowHintHalfHeight)),
                    Pair("halfwidth", Number(item.windowHintHalfWidth)),
                    Pair("right", VectorValue(right)),
                    Pair("origin", Position(item.position, offset)),
                    Pair("classname", "func_window_hint"));
                scriptCount++;
            }

            foreach (var item in world.Where(item => item.customType == "sound"))
            {
                var fields = new List<KeyValuePair<string, string>>();
                var points = world.Where(point => point.customType == "sound-point" && point.parentId == item.id)
                    .OrderBy(PointIndex).ToList();
                for (int index = points.Count - 1; index >= 0; index--)
                {
                    string end = Position(points[index].position, offset);
                    string start = index == 0 ? "0 0 0" : Position(points[index - 1].position, offset);
                    fields.Add(Pair("polyline_segment_" + index.ToString(CultureInfo.InvariantCulture),
                        "(" + start + ") (" + end + ")"));
                }
                fields.Add(Pair("radius", Number(item.soundRadius)));
                fields.Add(Pair("model", "mdl/dev/editor_ambient_generic_node.rmdl"));
                fields.Add(Pair("isWaveAmbient", Bool(item.soundWaveAmbient)));
                fields.Add(Pair("enabled", Bool(item.soundEnabled)));
                fields.Add(Pair("origin", Position(item.position, offset)));
                fields.Add(Pair("soundName", item.soundName));
                fields.Add(Pair("classname", "ambient_generic"));
                AppendEntity(sound, fields.ToArray());
                soundCount++;
            }

            MapObject[] playerStarts = world.Where(ReMapApp.IsWorldSpawnPoint).ToArray();
            if (playerStarts.Length > 1) throw new InvalidDataException(L.F("#REMAP_PLAYER_START_COUNT_ARG0", playerStarts.Length));
            if (playerStarts.Length == 1)
            {
                MapObject item = playerStarts[0];
                AppendEntity(playerStart,
                    Pair("spawnflags", "0"), Pair("scale", "1"), Pair("angles", Angles(item.rotation)),
                    Pair("origin", Position(item.position, offset)), Pair("classname", "info_player_start"));
                scriptCount++;
            }

            foreach (var item in world.Where(item => item.customType == "spawn-point" && !ReMapApp.IsWorldSpawnPoint(item)))
            {
                AppendEntity(spawn,
                    Pair("teamnumber", item.spawnPointTeam.ToString(CultureInfo.InvariantCulture)),
                    Pair("phase_9", "0"), Pair("phase_8", "0"), Pair("phase_7", "0"),
                    Pair("phase_6", "0"), Pair("phase_5", "0"), Pair("phase_4", "0"),
                    Pair("phase_3", "0"), Pair("phase_2", "0"), Pair("phase_1", "0"),
                    Pair("model", "mdl/dev/mp_spawn.rmdl"),
                    Pair("gamemode_tdm", "1"), Pair("gamemode_ffa", "1"),
                    Pair("gamemode_fd", "1"), Pair("gamemode_ctf", "1"),
                    Pair("gamemode_cp", "1"), Pair("gamemode_at", "1"),
                    Pair("control_teamnumber", "-1"), Pair("scale", "1"),
                    Pair("angles", Angles(item.rotation)), Pair("origin", Position(item.position, offset)),
                    Pair("link_guid", LinkGuid(item.id, 0)), Pair("classname", "info_spawnpoint_human"));
                spawnCount++;
            }

            var represented = new HashSet<string>(StringComparer.Ordinal)
            {
                "", "door", "door-component", "loot-bin", "window-hint", "sound", "sound-point", "spawn-point",
                "jump-tower", "jump-tower-component",
                "zipline", "zipline-endpoint", "zipline-component",
                "curved-zipline", "curved-zipline-point", "curved-zipline-component",
                "ziprail", "ziprail-point", "ziprail-component"
            };
            foreach (var item in world.Where(item => item.customType.Length > 0 && !represented.Contains(item.customType) &&
                !item.customType.EndsWith("-component", StringComparison.Ordinal) &&
                !item.customType.EndsWith("-point", StringComparison.Ordinal) &&
                !item.customType.EndsWith("-target", StringComparison.Ordinal)))
            {
                nutOnly.Add(Display(item));
                nutOnlyIds.Add(item.id);
            }

            return new ReMapEntFragments
            {
                Script = script.ToString(), Sound = sound.ToString(), Spawn = spawn.ToString(),
                PlayerStart = playerStart.ToString(),
                ScriptEntityCount = scriptCount, SoundEntityCount = soundCount,
                SpawnEntityCount = spawnCount, NutOnlyObjects = nutOnly,
                NutOnlyObjectIds = nutOnlyIds.ToArray()
            };
        }

        public static IReadOnlyList<MapObject> ScriptFallbackObjects(IEnumerable<MapObject> worldObjects,
            ReMapEntFragments fragments)
        {
            if (fragments == null) throw new ArgumentNullException(nameof(fragments));
            var world = (worldObjects ?? throw new ArgumentNullException(nameof(worldObjects)))
                .Where(item => item != null).ToList();
            var ids = new HashSet<string>(fragments.NutOnlyObjectIds ?? Array.Empty<string>(), StringComparer.Ordinal);
            bool changed;
            do
            {
                changed = false;
                foreach (var item in world)
                    if (!string.IsNullOrEmpty(item.parentId) && ids.Contains(item.parentId) && ids.Add(item.id))
                        changed = true;
            }
            while (changed);
            return world.Where(item => ids.Contains(item.id)).ToArray();
        }

        public static string Preview(MapDocument document, IEnumerable<MapObject> worldObjects)
        {
            if (document == null) throw new ArgumentNullException(nameof(document));
            string map = ValidateMapName(document.editingMap);
            ReMapEntFragments fragments = Generate(document, worldObjects);
            var preview = new StringBuilder();
            preview.AppendLine("// ReMap ENT preview for " + map);
            preview.AppendLine("// Generated additions only. Base entities are merged or excluded when the ENT bundle is exported.");
            AppendPreviewLump(preview, map + "_script.ent", JoinFragments(fragments.PlayerStart, fragments.Script));
            AppendPreviewLump(preview, map + "_snd.ent", fragments.Sound);
            AppendPreviewLump(preview, map + "_spawn.ent", fragments.Spawn);
            if (fragments.NutOnlyObjects.Count > 0)
            {
                preview.AppendLine();
                preview.AppendLine("// Script-only objects");
                foreach (string item in fragments.NutOnlyObjects.Distinct()) preview.AppendLine("// - " + item);
            }
            return preview.ToString().TrimEnd();
        }

        private static void AppendPreviewLump(StringBuilder preview, string fileName, string fragment)
        {
            preview.AppendLine();
            preview.AppendLine("// ============================================================================");
            preview.AppendLine("// " + fileName);
            preview.AppendLine("// ============================================================================");
            preview.AppendLine(string.IsNullOrWhiteSpace(fragment) ? "// No generated entities." : fragment.TrimEnd());
        }

        public static string WriteMergedBundle(string selectedSourceEnt, MapDocument document,
            IEnumerable<MapObject> worldObjects)
        {
            return WriteMergedBundle(selectedSourceEnt, document, worldObjects, out _);
        }

        public static string WriteMergedBundle(string selectedSourceEnt, MapDocument document,
            IEnumerable<MapObject> worldObjects, out ReMapEntFragments fragments)
        {
            return WriteMergedBundle(selectedSourceEnt, document, worldObjects, null, out fragments);
        }

        public static string WriteMergedBundle(string selectedSourceEnt, MapDocument document,
            IEnumerable<MapObject> worldObjects, IEnumerable<string> levelRpaks, out ReMapEntFragments fragments)
        {
            return WriteMergedBundle(selectedSourceEnt, document, worldObjects, levelRpaks, false, true, out fragments);
        }

        public static string WriteMergedBundle(string selectedSourceEnt, MapDocument document,
            IEnumerable<MapObject> worldObjects, IEnumerable<string> levelRpaks, bool publish,
            bool preserveBaseEntities, out ReMapEntFragments fragments)
        {
            if (string.IsNullOrWhiteSpace(selectedSourceEnt)) throw new ArgumentNullException(nameof(selectedSourceEnt));
            string sourceDirectory = Path.GetDirectoryName(Path.GetFullPath(selectedSourceEnt));
            if (File.Exists(Path.Combine(sourceDirectory, "ReMap-ENT-report.txt")))
                throw new InvalidDataException(L.T("#ENT_SOURCE_MUST_BE_ORIGINAL"));
            string sourceMap = ValidateMapName(document?.editingMap);
            foreach (string kind in LumpKinds)
            {
                string source = Path.Combine(sourceDirectory, sourceMap + "_" + kind + ".ent");
                if (!File.Exists(source)) throw new FileNotFoundException(
                    L.F("#ENT_SOURCE_MISSING_ARG0", Path.GetFileName(source)), source);
                ValidateBase(File.ReadAllText(source), source);
            }

            fragments = Generate(document, worldObjects);
            string outputMap = publish ? PublishedMapName(document?.name) : sourceMap;
            string project = SafeDirectoryName(document.name);
            string outputDirectory = Path.Combine(sourceDirectory, outputMap + "_remap_ent_" + project);
            Directory.CreateDirectory(outputDirectory);
            foreach (string kind in LumpKinds)
            {
                string source = Path.Combine(sourceDirectory, sourceMap + "_" + kind + ".ent");
                string destination = Path.Combine(outputDirectory, outputMap + "_" + kind + ".ent");
                string fragment = kind == "script" ? fragments.Script : kind == "snd" ? fragments.Sound :
                    kind == "spawn" ? fragments.Spawn : "";
                string original = File.ReadAllText(source);
                if (kind == "script" && fragments.PlayerStart.Length > 0)
                {
                    if (preserveBaseEntities) original = ReplaceSinglePlayerStart(original, fragments.PlayerStart, source);
                    else fragment = JoinFragments(fragments.PlayerStart, fragment);
                }
                string content = preserveBaseEntities ? Merge(original, fragment, source) : ReplaceEntities(original, fragment, source);
                File.WriteAllText(destination, content, new UTF8Encoding(false));
            }
            if (publish)
                File.WriteAllText(Path.Combine(outputDirectory, outputMap + ".kv"),
                    BuildLevelSettings(document, levelRpaks), new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(outputDirectory, "ReMap-ENT-report.txt"),
                BuildReport(sourceMap, outputMap, publish, preserveBaseEntities, fragments), new UTF8Encoding(false));
            return outputDirectory;
        }

        public static string PublishedMapName(string sceneName)
        {
            string normalized = (sceneName ?? "").Trim().Normalize(NormalizationForm.FormD);
            var result = new StringBuilder();
            bool separator = false;
            foreach (char character in normalized)
            {
                UnicodeCategory category = CharUnicodeInfo.GetUnicodeCategory(character);
                if (category == UnicodeCategory.NonSpacingMark) continue;
                bool asciiLetter = character >= 'A' && character <= 'Z' || character >= 'a' && character <= 'z';
                bool digit = character >= '0' && character <= '9';
                if (asciiLetter || digit)
                {
                    if (separator && result.Length > 0 && result[result.Length - 1] != '_') result.Append('_');
                    result.Append(char.ToLowerInvariant(character));
                    separator = false;
                }
                else separator = result.Length > 0;
            }
            string stem = result.ToString().Trim('_');
            if (stem.StartsWith("mp_remap_", StringComparison.Ordinal)) stem = stem.Substring(9);
            else if (stem.StartsWith("mp_", StringComparison.Ordinal)) stem = stem.Substring(3);
            stem = stem.Trim('_');
            if (stem.Length == 0) throw new ArgumentException(L.T("#INVALID_MAP_NAME_1_128"));
            string code = "mp_remap_" + stem;
            if (code.Length > 128) code = code.Substring(0, 128).TrimEnd('_');
            return ValidateMapName(code);
        }

        public static string BuildLevelSettings(MapDocument document, IEnumerable<string> levelRpaks)
        {
            if (document == null) throw new ArgumentNullException(nameof(document));
            string sourceMap = ValidateMapName(document.editingMap);
            var archives = new List<string> { sourceMap + ".rpak" };
            foreach (string value in levelRpaks ?? Array.Empty<string>())
            {
                string archive = (value ?? "").Trim();
                if (!Regex.IsMatch(archive, @"^[A-Za-z0-9_()\-]+\.rpak$", RegexOptions.CultureInvariant))
                    throw new ArgumentException(L.F("#INVALID_RPAK_FILE_NAME_ARG0", archive));
                if (!archives.Contains(archive, StringComparer.OrdinalIgnoreCase)) archives.Add(archive);
            }
            var result = new StringBuilder();
            result.AppendLine("\"LevelSet\"");
            result.AppendLine("{");
            result.Append("    \"StreamDB\" \"").Append(sourceMap).AppendLine("\"");
            result.AppendLine("    \"PakList\"");
            result.AppendLine("    {");
            foreach (string archive in archives)
            {
                string mode = archive.EndsWith("_client_perm.rpak", StringComparison.OrdinalIgnoreCase) || archive.EndsWith("_client_temp.rpak", StringComparison.OrdinalIgnoreCase) ? "1" : "2";
                result.Append("        \"").Append(archive).Append("\" \"").Append(mode).AppendLine("\"");
            }
            result.AppendLine("    }");
            result.AppendLine("}");
            return result.ToString();
        }

        public static ReMapLooseMapInstall InstallLooseMap(string bundleDirectory, MapDocument document, string gameDirectory, string platformDirectory)
        {
            return InstallLooseMap(bundleDirectory, document, gameDirectory, platformDirectory, false);
        }

        public static ReMapLooseMapInstall InstallLooseMap(string bundleDirectory, MapDocument document, string gameDirectory, string platformDirectory, bool publish)
        {
            if (document == null) throw new ArgumentNullException(nameof(document));
            string map = publish ? PublishedMapName(document.name) : ValidateMapName(document.editingMap);
            IReadOnlyList<string> configs = publish ? Array.Empty<string>() : EnsureDiskPriority(gameDirectory, platformDirectory);
            var lumps = new List<string>();
            foreach (string kind in LumpKinds)
                lumps.Add(InstallLump(bundleDirectory, map, kind, document.gameTarget, gameDirectory, platformDirectory));
            string destination = "";
            if (publish)
            {
                string source = Path.Combine(Path.GetFullPath(bundleDirectory ?? throw new ArgumentNullException(nameof(bundleDirectory))), map + ".kv");
                if (!File.Exists(source)) throw new FileNotFoundException(L.F("#ENT_SOURCE_MISSING_ARG0", Path.GetFileName(source)), source);
                string root = Path.GetFullPath(platformDirectory ?? throw new ArgumentNullException(nameof(platformDirectory)));
                if (!Directory.Exists(root)) throw new DirectoryNotFoundException(root);
                string settings = Path.Combine(root, "scripts", "levels", "settings");
                Directory.CreateDirectory(settings);
                destination = Path.Combine(settings, map + ".kv");
                BackupAndCopy(source, destination);
            }
            return new ReMapLooseMapInstall
            {
                MapName = map,
                LevelSettingsPath = destination,
                EntityLumpPaths = lumps,
                DiskPriorityConfigPaths = configs
            };
        }

        public static IReadOnlyList<string> EnsureDiskPriority(string gameDirectory, string platformDirectory)
        {
            var roots = new[]
            {
                platformDirectory,
                string.IsNullOrWhiteSpace(gameDirectory) ? "" : Path.Combine(gameDirectory, "platform"),
                string.IsNullOrWhiteSpace(gameDirectory) ? "" : Path.Combine(gameDirectory, "platform_")
            }
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(Path.GetFullPath)
            .Where(Directory.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
            if (roots.Length == 0) throw new DirectoryNotFoundException(platformDirectory ?? gameDirectory ?? "");

            var configured = new List<string>();
            foreach (string root in roots)
            {
                string system = Path.Combine(root, "cfg", "system");
                if (!Directory.Exists(system)) continue;
                string autoexec = Path.Combine(system, "autoexec.cfg");
                SetManagedConfig(autoexec, "fs_vpk_prioritizeDisk \"1\"");
                configured.Add(autoexec);

                var startupFiles = Directory.EnumerateFiles(system, "startup*.cfg", SearchOption.TopDirectoryOnly).ToList();
                foreach (string required in new[] { "startup_launcher.cfg", "startup_default.cfg", "startup_dedi_default.cfg" })
                {
                    string path = Path.Combine(system, required);
                    if (!startupFiles.Contains(path, StringComparer.OrdinalIgnoreCase)) startupFiles.Add(path);
                }
                foreach (string startup in startupFiles)
                {
                    SetManagedConfig(startup, "+fs_vpk_prioritizeDisk 1");
                    configured.Add(startup);
                }
            }
            if (configured.Count == 0) throw new DirectoryNotFoundException(L.T("#CONFIGURE_PLATFORM_FOLDER_SETTINGS_FIRST"));
            return configured.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        }

        private static void SetManagedConfig(string path, string command)
        {
            string source = File.Exists(path) ? File.ReadAllText(path) : "";
            string newline = source.Contains("\r\n") ? "\r\n" : "\n";
            string block = DiskPriorityBegin + newline + command + newline + DiskPriorityEnd;
            var expression = new Regex(Regex.Escape(DiskPriorityBegin) + @"[\s\S]*?" + Regex.Escape(DiskPriorityEnd), RegexOptions.CultureInvariant);
            string content = expression.IsMatch(source) ? expression.Replace(source, block, 1) : source.TrimEnd('\r', '\n') + (source.Length > 0 ? newline + newline : "") + block + newline;
            if (content != source) File.WriteAllText(path, content, new UTF8Encoding(false));
        }

        public static string InstallScriptLump(string bundleDirectory, string map, string gameTarget, string gameDirectory, string platformDirectory)
        {
            return InstallLump(bundleDirectory, map, "script", gameTarget, gameDirectory, platformDirectory);
        }

        public static string InstallSoundLump(string bundleDirectory, string map, string gameTarget, string gameDirectory, string platformDirectory)
        {
            return InstallLump(bundleDirectory, map, "snd", gameTarget, gameDirectory, platformDirectory);
        }

        private static string InstallLump(string bundleDirectory, string map, string kind, string gameTarget, string gameDirectory, string platformDirectory)
        {
            map = ValidateMapName(map);
            string source = Path.Combine(Path.GetFullPath(bundleDirectory ?? throw new ArgumentNullException(nameof(bundleDirectory))), map + "_" + kind + ".ent");
            if (!File.Exists(source)) throw new FileNotFoundException(L.F("#ENT_SOURCE_MISSING_ARG0", Path.GetFileName(source)), source);
            ValidateBase(File.ReadAllText(source), source);
            string root = GameTargets.Normalize(gameTarget) == GameTargets.R5Flowstate
                ? Path.Combine(gameDirectory ?? "", "platform")
                : platformDirectory;
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) throw new DirectoryNotFoundException(root ?? "");
            string maps = Path.Combine(Path.GetFullPath(root), "maps");
            Directory.CreateDirectory(maps);
            string destination = Path.Combine(maps, map + "_" + kind + ".ent");
            BackupAndCopy(source, destination);
            return destination;
        }

        private static void BackupAndCopy(string source, string destination)
        {
            if (File.Exists(destination) && !File.Exists(destination + ".remap.bak")) File.Copy(destination, destination + ".remap.bak");
            File.Copy(source, destination, true);
        }

        public static string Merge(string baseEnt, string fragment, string sourceName = "base .ent")
        {
            ValidateBase(baseEnt, sourceName);
            string newline = baseEnt.Contains("\r\n") ? "\r\n" : "\n";
            string body = baseEnt.TrimEnd('\0', '\r', '\n');
            string addition = (fragment ?? "").Replace("\r\n", "\n").Trim();
            if (addition.Length == 0) return body + newline + "\0";
            addition = addition.Replace("\n", newline);
            return body + newline + addition + newline + "\0";
        }

        public static bool TryReadSinglePlayerStart(string source, out Vector3 origin, out Vector3 angles, out int count)
        {
            ValidateBase(source, "base .ent");
            Match[] matches = PlayerStartMatches(source);
            count = matches.Length;
            origin = angles = Vector3.zero;
            if (count != 1) return false;
            return TryEntityVector(matches[0].Value, "origin", out origin) && TryEntityVector(matches[0].Value, "angles", out angles);
        }

        public static string ReplaceSinglePlayerStart(string source, string replacement, string sourceName = "base .ent")
        {
            ValidateBase(source, sourceName);
            Match[] sourceMatches = PlayerStartMatches(source);
            Match[] replacementMatches = PlayerStartMatches(replacement);
            if (sourceMatches.Length != 1) throw new InvalidDataException(L.F("#BASE_PLAYER_START_COUNT_ARG0", sourceMatches.Length));
            if (replacementMatches.Length != 1 || !TryEntityVector(replacementMatches[0].Value, "origin", out Vector3 origin) || !TryEntityVector(replacementMatches[0].Value, "angles", out Vector3 angles))
                throw new InvalidDataException(L.T("#GENERATED_PLAYER_START_INVALID"));
            string updated = ReplaceEntityVector(sourceMatches[0].Value, "origin", VectorValue(origin));
            updated = ReplaceEntityVector(updated, "angles", VectorValue(angles));
            return source.Remove(sourceMatches[0].Index, sourceMatches[0].Length).Insert(sourceMatches[0].Index, updated);
        }

        public static string ReplaceEntities(string baseEnt, string fragment, string sourceName = "base .ent")
        {
            ValidateBase(baseEnt, sourceName);
            string newline = baseEnt.Contains("\r\n") ? "\r\n" : "\n";
            string header = Header.Match(baseEnt).Value.TrimEnd('\r', '\n');
            string addition = (fragment ?? "").Replace("\r\n", "\n").Trim();
            if (addition.Length == 0) return header + newline + "\0";
            return header + newline + addition.Replace("\n", newline) + newline + "\0";
        }

        private static string JoinFragments(string first, string second)
        {
            first = (first ?? "").Trim();
            second = (second ?? "").Trim();
            if (first.Length == 0) return second;
            if (second.Length == 0) return first;
            return first + "\n" + second;
        }

        private static Match[] PlayerStartMatches(string source)
        {
            return EntityBlock.Matches(source ?? "").Cast<Match>().Where(match => string.Equals(EntityValue(match.Value, "classname"), "info_player_start", StringComparison.Ordinal)).ToArray();
        }

        private static string EntityValue(string block, string key)
        {
            Match match = Regex.Match(block ?? "", "(?m)^\"" + Regex.Escape(key) + "\"\\s+\"(?<value>[^\"]*)\"\\s*$", RegexOptions.CultureInvariant);
            return match.Success ? match.Groups["value"].Value : "";
        }

        private static bool TryEntityVector(string block, string key, out Vector3 value)
        {
            value = Vector3.zero;
            string[] parts = EntityValue(block, key).Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 3 || !float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float x) || !float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float y) || !float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float z)) return false;
            value = new Vector3(x, y, z);
            return float.IsFinite(x) && float.IsFinite(y) && float.IsFinite(z);
        }

        private static string ReplaceEntityVector(string block, string key, string value)
        {
            var expression = new Regex("(?m)^\"" + Regex.Escape(key) + "\"\\s+\"[^\"]*\"\\s*$", RegexOptions.CultureInvariant);
            if (expression.Matches(block).Count != 1) throw new InvalidDataException(L.T("#GENERATED_PLAYER_START_INVALID"));
            return expression.Replace(block, "\"" + key + "\" \"" + value + "\"", 1);
        }

        private static string BuildReport(string sourceMap, string outputMap, bool publish,
            bool preserveBaseEntities, ReMapEntFragments fragments)
        {
            var result = new StringBuilder();
            result.AppendLine("ReMap loose map export: " + outputMap);
            result.AppendLine("Mode: " + (publish ? "publication" : "base-map development"));
            result.AppendLine("Base Apex map: " + sourceMap);
            result.AppendLine("Base entities: " + (preserveBaseEntities ? "preserved" : "excluded"));
            result.AppendLine("Objects without a faithful native entity representation remain available through the separate .nut export.");
            result.AppendLine("Generated entities: " + fragments.EntityCount.ToString(CultureInfo.InvariantCulture) +
                " (script=" + fragments.ScriptEntityCount + ", snd=" + fragments.SoundEntityCount +
                ", spawn=" + fragments.SpawnEntityCount + ").");
            result.AppendLine("The five entity lumps and level settings KV can be loaded directly from the game's loose platform folders; no VPK repack is required.");
            result.AppendLine("Extraction tool: ReVPK from R5Reloaded/r5sdk, primarily authored by Kawe Mazidjatari (Mauler125).");
            result.AppendLine("ReVPK source and license: https://github.com/R5Reloaded/r5sdk");
            if (fragments.NutOnlyObjects.Count > 0)
            {
                result.AppendLine();
                result.AppendLine("Kept in .nut only:");
                foreach (string item in fragments.NutOnlyObjects.Distinct()) result.AppendLine("- " + item);
            }
            return result.ToString();
        }

        private static int AppendDoor(StringBuilder output, MapObject door, Vector3 offset)
        {
            var profile = ReMapDoorProfiles.Find(door.doorType);
            if (profile.Id == "double")
            {
                string first = LinkGuid(door.id, 0), second = LinkGuid(door.id, 1);
                AppendPropDoor(output, Transform(door, new Vector3(0, -60, 0), Vector3.zero), offset, first, second);
                AppendPropDoor(output, Transform(door, new Vector3(0, 60, 0), new Vector3(0, 180, 0)), offset, second, null);
                return 2;
            }
            if (profile.Id == "single")
            {
                AppendPropDoor(output, Transform(door, Vector3.zero, Vector3.zero), offset, null, null);
                return 1;
            }
            AppendAnimatedProp(output, door, offset, profile.ModelPath, "survival_door_plain", 0);
            return 1;
        }

        private static int AppendZipline(StringBuilder output, MapObject zipline,
            IReadOnlyDictionary<string, MapObject> byId, Vector3 offset)
        {
            if (!byId.TryGetValue(zipline.ziplineStartId, out var start) ||
                !byId.TryGetValue(zipline.ziplineEndId, out var end))
                throw new ArgumentException(L.F("#ARG0_ZIPLINE_ENDPOINTS_MISSING", zipline.displayName));
            bool vertical = zipline.ziplineMode == "vertical";
            int count = AppendZiplinePointModels(output, start,
                ReMapZiplineProfiles.Find(start.customProfile), start.ziplineArmHeight, offset, true,
                out Vector3 startCable);
            Vector3 endCable;
            if (vertical) endCable = WorldView.ToVector(end.position);
            else count += AppendZiplinePointModels(output, end,
                ReMapZiplineProfiles.Find(end.customProfile), end.ziplineArmHeight, offset, true,
                out endCable);
            if (vertical) endCable = new Vector3(startCable.x, endCable.y, startCable.z);

            Vector3 startApex = ApexDisplay.Position(startCable + offset);
            Vector3 endApex = ApexDisplay.Position(endCable + offset);
            Vector3 startAngles = vertical ? new Vector3(0, zipline.ziplinePushOffAngle, 0) :
                DirectionAngles(endApex - startApex);
            Vector3 endAngles = vertical ? startAngles : DirectionAngles(startApex - endApex);
            string startGuid = LinkGuid(zipline.id, 0), endGuid = LinkGuid(zipline.id, 1);

            AppendEntity(output,
                Pair("ZiplinePushOffInDirectionX", Bool(zipline.ziplinePushOffInDirectionX)),
                Pair("origin", VectorValue(endApex)), Pair("angles", VectorValue(endAngles)),
                Pair("link_guid", endGuid), Pair("ZiplineVertical", Bool(vertical)),
                Pair("ZiplineLengthScale", Number(zipline.ziplineLengthScale)),
                Pair("ZiplineAutoDetachDistance", Number(zipline.ziplineAutoDetachEnd)),
                Pair("classname", "zipline_end"));

            var fields = new List<KeyValuePair<string, string>>();
            if (zipline.ziplineRestPoint)
            {
                fields.Add(Pair("_zipline_rest_point_1", VectorValue(endApex)));
                fields.Add(Pair("_zipline_rest_point_0", VectorValue(startApex)));
            }
            fields.Add(Pair("ZiplinePreserveVelocity", Bool(zipline.ziplinePreserveVelocity)));
            fields.Add(Pair("ZiplineFadeDistance", Number(zipline.ziplineFadeDistance)));
            fields.Add(Pair("ZiplineDropToBottom", Bool(zipline.ziplineDropToBottom)));
            fields.Add(Pair("Width", Number(zipline.ziplineWidth)));
            fields.Add(Pair("Material", "cable/zipline.vmt"));
            fields.Add(Pair("gamemode_survival", "1"));
            fields.Add(Pair("gamemode_freedm", "1"));
            fields.Add(Pair("gamemode_control", "1"));
            fields.Add(Pair("gamemode_arenas", "1"));
            fields.Add(Pair("DetachEndOnUse", Bool(zipline.ziplineDetachEndOnUse)));
            fields.Add(Pair("DetachEndOnSpawn", Bool(zipline.ziplineDetachEndOnSpawn)));
            fields.Add(Pair("scale", Number(zipline.ziplineScale)));
            fields.Add(Pair("angles", VectorValue(startAngles)));
            fields.Add(Pair("origin", VectorValue(startApex)));
            fields.Add(Pair("link_to_guid_0", endGuid));
            fields.Add(Pair("link_guid", startGuid));
            fields.Add(Pair("ZiplineVertical", Bool(vertical)));
            fields.Add(Pair("ZiplineVersion", "3"));
            fields.Add(Pair("ZiplineSpeedScale", Number(zipline.ziplineSpeed)));
            fields.Add(Pair("ZiplinePushOffInDirectionX", Bool(zipline.ziplinePushOffInDirectionX)));
            fields.Add(Pair("ZiplineLengthScale", Number(zipline.ziplineLengthScale)));
            fields.Add(Pair("ZiplineAutoDetachDistance", Number(zipline.ziplineAutoDetachStart)));
            fields.Add(Pair("classname", "zipline"));
            AppendEntity(output, fields.ToArray());
            return count + 2;
        }

        private static int AppendCurvedZipline(StringBuilder output, MapObject zipline,
            List<MapObject> world, Vector3 offset)
        {
            var points = world.Where(item => item.customType == "curved-zipline-point" &&
                item.parentId == zipline.id).OrderBy(PointIndex).ToList();
            if (points.Count < 2)
                throw new ArgumentException(L.F("#ARG0_CURVED_ZIPLINE_POINTS_MISSING", zipline.displayName));
            var adjusted = new List<Vector3>();
            int count = 0;
            foreach (var point in points)
            {
                count += AppendZiplinePointModels(output, point,
                    ReMapZiplineProfiles.Find(point.customProfile), point.ziplineArmHeight,
                    offset, false, out Vector3 cable);
                adjusted.Add(cable);
            }
            List<Vector3> curve = BezierPath(adjusted, zipline.curvedZiplineSegments);
            for (int index = 0; index < curve.Count; index++)
            {
                var fields = new List<KeyValuePair<string, string>>
                {
                    Pair("MoveSpeed", Number(64f * zipline.ziplineSpeed)), Pair("Slack", "25"),
                    Pair("Subdiv", "2"), Pair("Width", Number(zipline.ziplineWidth)), Pair("Type", "0"),
                    Pair("TextureScale", "1"), Pair("PositionInterpolator", "2"),
                    Pair("RopeMaterial", "cable/zipline.vmt"), Pair("Zipline", "1"),
                    Pair("ZiplineAutoDetachDistance", "150"), Pair("ZiplineSagEnable", "0"),
                    Pair("ZiplineSagHeight", "50"), Pair("fadedist", "50000"),
                    Pair("origin", VectorValue(ApexDisplay.Position(curve[index] + offset))),
                    Pair("link_guid", LinkGuid(zipline.id, index))
                };
                if (index + 1 < curve.Count)
                    fields.Add(Pair("link_to_guid_0", LinkGuid(zipline.id, index + 1)));
                fields.Add(Pair("classname", index == 0 ? "move_rope" : "keyframe_rope"));
                AppendEntity(output, fields.ToArray());
            }
            return count + curve.Count;
        }

        private static int AppendZiprail(StringBuilder output, MapObject ziprail,
            List<MapObject> world, Vector3 offset)
        {
            var points = world.Where(item => item.customType == "ziprail-point" &&
                item.parentId == ziprail.id).OrderBy(PointIndex).ToList();
            if (points.Count < 2)
                throw new ArgumentException(L.F("#ARG0_ZIPRAIL_POINTS_MISSING", ziprail.displayName));
            int count = 0;
            var cablePositions = points.Select(ZiprailCablePosition).ToList();
            foreach (var point in points)
            {
                var profile = ReMapZiprailProfiles.Find(point.customProfile);
                foreach (var component in ReMapZiprailProfiles.Components(profile, point.ziplineArmHeight))
                {
                    MapObject transformed = TransformUnity(point, component.Position, component.Rotation);
                    AppendNativeProp(output, transformed, offset, component.ModelPath, true);
                    count++;
                }
            }
            count += AppendZiprailEndModel(output, points[0], cablePositions[0], cablePositions[1], offset);
            count += AppendZiprailEndModel(output, points[points.Count - 1], cablePositions[points.Count - 1], cablePositions[points.Count - 2], offset);

            int pathEntityCount = points.Count + 2;
            for (int entityIndex = 0; entityIndex < pathEntityCount; entityIndex++)
            {
                bool endpoint = entityIndex == 0 || entityIndex == pathEntityCount - 1;
                int pointIndex = entityIndex == 0 ? 0 :
                    entityIndex == pathEntityCount - 1 ? points.Count - 1 : entityIndex - 1;
                MapObject point = points[pointIndex];
                var fields = new List<KeyValuePair<string, string>>();
                if (endpoint)
                {
                    Vector3 current = ApexDisplay.Position(cablePositions[pointIndex] + offset);
                    int adjacent = pointIndex == 0 ? 1 : points.Count - 2;
                    Vector3 other = ApexDisplay.Position(cablePositions[adjacent] + offset);
                    fields.Add(Pair("ziprailMountReverseDistance", "200"));
                    fields.Add(Pair("ZiplineVertical", "0"));
                    fields.Add(Pair("ZiplinePushOffInDirectionX", "0"));
                    fields.Add(Pair("ZiplinePreserveVelocity", "0"));
                    fields.Add(Pair("ziplineMountReverseDistance", "0"));
                    fields.Add(Pair("ZiplineLengthScale", "1"));
                    fields.Add(Pair("ZiplineFadeDistance", "-1"));
                    fields.Add(Pair("ZiplineDropToBottom", "1"));
                    fields.Add(Pair("useAutoDetachSpeed", "0"));
                    float autoDetachDistance = pointIndex == 0 ? ziprail.ziplineAutoDetachStart : ziprail.ziplineAutoDetachEnd;
                    if (autoDetachDistance > 0) fields.Add(Pair("useZiprailAutoDetachSpeed", "1"));
                    fields.Add(Pair("Material", "cable/zipline.vmt"));
                    fields.Add(Pair("gamemode_survival", "1"));
                    fields.Add(Pair("gamemode_freedm", "1"));
                    fields.Add(Pair("gamemode_control", "1"));
                    fields.Add(Pair("gamemode_arenas", "1"));
                    fields.Add(Pair("DetachEndOnUse", "0"));
                    fields.Add(Pair("DetachEndOnSpawn", "0"));
                    fields.Add(Pair("scale", "1"));
                    fields.Add(Pair("angles", VectorValue(DirectionAngles(current - other))));
                    fields.Add(Pair("ZiplineSpeedScale", Number(ziprail.ziplineSpeed)));
                    fields.Add(Pair("ZiplineAutoDetachDistance", Number(autoDetachDistance)));
                    fields.Add(Pair("Width", Number(ziprail.ziplineWidth)));
                    fields.Add(Pair("isZiprailStart", "1"));
                }
                else
                {
                    fields.Add(Pair("tangent_type", "0"));
                    fields.Add(Pair("perfect_circular_rotation", "0"));
                    fields.Add(Pair("num_smooth_points", "-1"));
                }
                fields.Add(Pair("origin", VectorValue(ApexDisplay.Position(cablePositions[pointIndex] + offset))));
                if (entityIndex + 1 < pathEntityCount)
                    fields.Add(Pair("link_to_guid_0", LinkGuid(ziprail.id, entityIndex + 1)));
                fields.Add(Pair("link_guid", LinkGuid(ziprail.id, entityIndex)));
                fields.Add(Pair("classname", endpoint ? "zipline" : "script_mover_train_node"));
                AppendEntity(output, fields.ToArray());
            }
            return count + pathEntityCount;
        }

        private static int AppendZiprailEndModel(StringBuilder output, MapObject point, Vector3 cablePosition, Vector3 adjacentCablePosition, Vector3 offset)
        {
            if (!ReMapZiprailProfiles.Find(point.customProfile).HasArm) return 0;
            Vector3 outward = cablePosition - adjacentCablePosition;
            outward.y = 0f;
            Quaternion rotation = outward.sqrMagnitude < .000001f ? Quaternion.identity : Quaternion.FromToRotation(Vector3.right, outward.normalized);
            Vector3 modelPosition = cablePosition + rotation * ApexDisplay.UnityPosition(new Vector3(ReMapZiprailProfiles.CordEndOriginOffsetApex, 0f, 0f));
            var end = new MapObject { position = WorldView.ToData(modelPosition), rotation = WorldView.ToData(rotation.eulerAngles) };
            AppendNativeProp(output, end, offset, ReMapZiprailProfiles.CordEndModelPath, false);
            return 1;
        }

        private static int AppendZiprailSounds(StringBuilder output, MapObject ziprail,
            List<MapObject> world, Vector3 offset)
        {
            int count = 0;
            foreach (var point in world.Where(item => item.customType == "ziprail-point" &&
                item.parentId == ziprail.id).OrderBy(PointIndex))
            {
                var profile = ReMapZiprailProfiles.Find(point.customProfile);
                if (!profile.HasArm) continue;
                Vector3 origin = ApexDisplay.Position(ZiprailCablePosition(point) + offset);
                AppendEntity(output,
                    Pair("radius", "0"), Pair("model", "mdl/dev/editor_ambient_generic_node.rmdl"),
                    Pair("isWaveAmbient", "0"), Pair("enabled", "1"), Pair("scale", "1"),
                    Pair("angles", Angles(point.rotation)), Pair("origin", VectorValue(origin)),
                    Pair("soundName", "3p_Ziprail_Emit_TowerBy"), Pair("classname", "ambient_generic"));
                count++;
            }
            return count;
        }

        private static Vector3 ZiprailCablePosition(MapObject point)
        {
            var profile = ReMapZiprailProfiles.Find(point.customProfile);
            Vector3 position = WorldView.ToVector(point.position);
            Quaternion rotation = Quaternion.Euler(WorldView.ToVector(point.rotation));
            return position + rotation * ApexDisplay.UnityPosition(ReMapZiprailProfiles.CableOffsetApex(profile));
        }

        private static int AppendZiplinePointModels(StringBuilder output, MapObject point,
            ReMapZiplineProfile profile, float height, Vector3 offset, bool collision,
            out Vector3 cablePosition)
        {
            int count = 0;
            Vector3 localCable = Vector3.zero;
            if (profile.HasSupport)
            {
                AppendNativeProp(output, point, offset, ReMapZiplineProfiles.SupportModelPath, collision);
                MapObject arm = Transform(point, new Vector3(4, -2.5f, height),
                    ReMapZiplineProfiles.ArmRotationApex);
                AppendNativeProp(output, arm, offset, ReMapZiplineProfiles.ArmModelPath, collision);
                localCable = new Vector3(4, -2.5f, height) + ReMapZiplineProfiles.ArmToCableApex;
                count += 2;
            }
            else if (profile.HasArm)
            {
                MapObject arm = Transform(point, Vector3.zero, ReMapZiplineProfiles.ArmRotationApex);
                AppendNativeProp(output, arm, offset, ReMapZiplineProfiles.ArmModelPath, collision);
                localCable = ReMapZiplineProfiles.ArmToCableApex;
                count++;
            }
            Vector3 position = WorldView.ToVector(point.position);
            Quaternion rotation = Quaternion.Euler(WorldView.ToVector(point.rotation));
            cablePosition = position + rotation * ApexDisplay.UnityPosition(localCable);
            return count;
        }

        private static int AppendJumpTowerModels(StringBuilder output, MapObject tower, Vector3 offset)
        {
            MapObject towerBase = Transform(tower, Vector3.zero, Vector3.zero);
            MapObject balloon = Transform(tower, new Vector3(0f, 0f, tower.jumpTowerHeight), Vector3.zero);
            AppendNativeProp(output, towerBase, offset, ReMapApp.JumpTowerBaseModelPath, 6, true);
            AppendNativeProp(output, balloon, offset, ReMapApp.JumpTowerBalloonModelPath, 3, false);
            return 2;
        }

        private static void AppendNativeProp(StringBuilder output, MapObject item, Vector3 offset,
            string model, bool collision)
        {
            AppendNativeProp(output, item, offset, model, collision ? 6 : 0, collision);
        }

        private static void AppendNativeProp(StringBuilder output, MapObject item, Vector3 offset,
            string model, int solid, bool canMantle)
        {
            bool collision = solid != 0;
            var fields = new List<KeyValuePair<string, string>>
            {
                Pair("StartDisabled", "0"), Pair("spawnflags", "0"),
                Pair("solid", solid.ToString(CultureInfo.InvariantCulture)), Pair("fadedist", "-1"),
                Pair("collide_titan", collision ? "1" : "0"), Pair("collide_ai", collision ? "1" : "0"),
                Pair("scale", "1"), Pair("angles", Angles(item.rotation)),
                Pair("origin", Position(item.position, offset)), Pair("model", model),
                Pair("ClientSide", "0"), Pair("script_name", "remap_prop"),
                Pair("can_mantle", canMantle ? "1" : "0")
            };
            if (!collision) fields.Add(Pair("contents", "0"));
            fields.Add(Pair("classname", "prop_dynamic"));
            AppendEntity(output, fields.ToArray());
        }

        private static MapObject TransformUnity(MapObject source, Vector3 localPosition, Vector3 localRotation)
        {
            Vector3 position = WorldView.ToVector(source.position);
            Quaternion rotation = Quaternion.Euler(WorldView.ToVector(source.rotation));
            return new MapObject
            {
                id = source.id, position = WorldView.ToData(position + rotation * localPosition),
                rotation = WorldView.ToData((rotation * Quaternion.Euler(localRotation)).eulerAngles)
            };
        }

        private static List<Vector3> BezierPath(IReadOnlyList<Vector3> points, int segmentsPerSpan)
        {
            segmentsPerSpan = Math.Max(2, Math.Min(32, segmentsPerSpan));
            var tangents = new List<Vector3> { (points[1] - points[0]) * .5f };
            for (int index = 1; index < points.Count - 1; index++)
            {
                Vector3 direction = points[index + 1] - points[index - 1];
                float length = Math.Min(Vector3.Distance(points[index - 1], points[index]),
                    Vector3.Distance(points[index], points[index + 1])) * .5f;
                tangents.Add(direction.magnitude < .001f ? Vector3.zero : direction.normalized * length);
            }
            tangents.Add((points[points.Count - 1] - points[points.Count - 2]) * .5f);
            var result = new List<Vector3> { points[0] };
            for (int span = 0; span < points.Count - 1; span++)
            {
                Vector3 p0 = points[span], p1 = p0 + tangents[span];
                Vector3 p3 = points[span + 1], p2 = p3 - tangents[span + 1];
                for (int segment = 1; segment <= segmentsPerSpan; segment++)
                {
                    float t = segment / (float)segmentsPerSpan;
                    Vector3 a = Vector3.LerpUnclamped(p0, p1, t);
                    Vector3 b = Vector3.LerpUnclamped(p1, p2, t);
                    Vector3 c = Vector3.LerpUnclamped(p2, p3, t);
                    result.Add(Vector3.LerpUnclamped(Vector3.LerpUnclamped(a, b, t),
                        Vector3.LerpUnclamped(b, c, t), t));
                }
            }
            return result;
        }

        private static Vector3 DirectionAngles(Vector3 direction)
        {
            if (direction.sqrMagnitude < .000001f) return Vector3.zero;
            direction.Normalize();
            float planar = Mathf.Sqrt(direction.x * direction.x + direction.y * direction.y);
            return new Vector3(-Mathf.Atan2(direction.z, planar) * Mathf.Rad2Deg,
                Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg, 0);
        }

        private static MapObject Transform(MapObject source, Vector3 apexPosition, Vector3 apexRotation)
        {
            Vector3 position = WorldView.ToVector(source.position);
            Quaternion rotation = Quaternion.Euler(WorldView.ToVector(source.rotation));
            return new MapObject
            {
                id = source.id, position = WorldView.ToData(position + rotation * ApexDisplay.UnityPosition(apexPosition)),
                rotation = WorldView.ToData((rotation * Quaternion.Euler(ApexDisplay.UnityAngles(apexRotation))).eulerAngles)
            };
        }

        private static void AppendPropDoor(StringBuilder output, MapObject item, Vector3 offset,
            string linkGuid, string linkTo)
        {
            var fields = new List<KeyValuePair<string, string>>
            {
                Pair("only_spawn_in_freelance", "0"), Pair("disableshadows", "0"), Pair("scale", "1"),
                Pair("angles", Angles(item.rotation)), Pair("origin", Position(item.position, offset))
            };
            if (linkTo != null) fields.Add(Pair("link_to_guid_0", linkTo));
            if (linkGuid != null) fields.Add(Pair("link_guid", linkGuid));
            fields.Add(Pair("model", ReMapDoorProfiles.SingleModelPath));
            fields.Add(Pair("classname", "prop_door"));
            AppendEntity(output, fields.ToArray());
        }

        private static void AppendAnimatedProp(StringBuilder output, MapObject item, Vector3 offset,
            string model, string scriptName, int skin)
        {
            AppendEntity(output,
                Pair("StartDisabled", "0"), Pair("spawnflags", "0"), Pair("solid", "6"),
                Pair("skin", skin.ToString(CultureInfo.InvariantCulture)), Pair("rendermode", "0"),
                Pair("rendercolor", "255 255 255"), Pair("renderamt", "255"), Pair("fadedist", "-1"),
                Pair("disableshadows", "0"), Pair("collide_titan", "1"), Pair("collide_ai", "1"),
                Pair("ClientSide", "0"), Pair("scale", Number(item.scale.x == 0 ? 1 : item.scale.x)),
                Pair("angles", Angles(item.rotation)), Pair("origin", Position(item.position, offset)),
                Pair("script_name", scriptName), Pair("model", model), Pair("classname", "prop_dynamic"));
        }

        private static void AppendProp(StringBuilder output, MapObject item, Vector3 offset, bool solid)
        {
            string model = SafeModel(item.gameModelPath, item.displayName);
            var fields = new List<KeyValuePair<string, string>>
            {
                Pair("StartDisabled", "0"), Pair("spawnflags", "0"), Pair("fadedist", Number(item.fadeDistance)),
                Pair("collide_titan", "1"), Pair("collide_ai", "1"), Pair("scale", Number(item.scale.x)),
                Pair("angles", Angles(item.rotation)), Pair("origin", Position(item.position, offset)),
                Pair("targetname", "ReMapEntProp"), Pair("solid", solid ? "6" : "0"),
                Pair("model", model), Pair("ClientSide", "0"), Pair("script_name", "remap_prop"),
                Pair("can_mantle", item.allowMantle ? "1" : "0")
            };
            var used = new HashSet<string>(fields.Select(field => field.Key), StringComparer.OrdinalIgnoreCase);
            foreach (var property in item.scriptProperties ?? new List<ScriptProperty>())
            {
                if (property == null || property.scope != "kv" || string.IsNullOrWhiteSpace(property.name) ||
                    used.Contains(property.name) || !Regex.IsMatch(property.name, @"^[A-Za-z_][A-Za-z0-9_]*$")) continue;
                fields.Add(Pair(property.name, EntPropertyValue(property.value)));
                used.Add(property.name);
            }
            fields.Add(Pair("classname", "prop_dynamic"));
            AppendEntity(output, fields.ToArray());
        }

        private static string EntPropertyValue(string value)
        {
            string result = (value ?? "").Trim();
            if (result.Length >= 2 && result[0] == '"' && result[result.Length - 1] == '"')
                result = result.Substring(1, result.Length - 2);
            return result;
        }

        private static void AppendEntity(StringBuilder output, params KeyValuePair<string, string>[] fields)
        {
            output.AppendLine("{");
            foreach (var field in fields)
                output.Append('"').Append(Safe(field.Key)).Append("\" \"").Append(Safe(field.Value)).AppendLine("\"");
            output.AppendLine("}");
        }

        private static KeyValuePair<string, string> Pair(string key, string value) => new KeyValuePair<string, string>(key, value);
        private static string Position(Float3 position, Vector3 offset) => VectorValue(ApexDisplay.Position(WorldView.ToVector(position) + offset));
        private static string Angles(Float3 angles) => VectorValue(ApexDisplay.Angles(WorldView.ToVector(angles)));
        private static string VectorValue(Vector3 value) => Number(value.x) + " " + Number(value.y) + " " + Number(value.z);

        private static string Number(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) throw new ArgumentException("ENT values must be finite.");
            double rounded = Math.Round(value, 4, MidpointRounding.AwayFromZero);
            if (Math.Abs(rounded) < .00005) rounded = 0;
            return rounded.ToString("0.####", CultureInfo.InvariantCulture);
        }

        private static string Bool(bool value) => value ? "1" : "0";
        private static int PointIndex(MapObject item) => int.TryParse(item.customRole, NumberStyles.Integer,
            CultureInfo.InvariantCulture, out int value) ? value : int.MaxValue;
        private static string Display(MapObject item) => string.IsNullOrWhiteSpace(item.displayName) ? item.customType : item.displayName;
        private static string LinkGuid(string id, int index)
        {
            string value = (id ?? "").Replace("-", "");
            if (value.Length < 8 || !value.Take(8).All(Uri.IsHexDigit)) value = Guid.NewGuid().ToString("N");
            return value.Substring(0, 8).ToLowerInvariant() + ((uint)index).ToString("x8", CultureInfo.InvariantCulture);
        }

        private static string Safe(string value)
        {
            value = value ?? "";
            if (value.IndexOfAny(new[] { '"', '\r', '\n', '\0' }) >= 0)
                throw new ArgumentException(L.T("#ENT_VALUE_CONTAINS_INVALID_CHARACTERS"));
            return value;
        }

        private static string SafeModel(string value, string displayName)
        {
            string model = (value ?? "").Trim().Replace('\\', '/');
            if (!model.StartsWith("mdl/", StringComparison.OrdinalIgnoreCase) ||
                !model.EndsWith(".rmdl", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException(L.F("#ARG0_NO_VALID_APEX_MODEL", displayName));
            return Safe(model);
        }

        private static string ValidateMapName(string value)
        {
            string map = (value ?? "").Trim();
            if (!Regex.IsMatch(map, @"^mp_[A-Za-z0-9_]{1,124}$", RegexOptions.CultureInvariant))
                throw new ArgumentException(L.T("#SELECT_EDITED_APEX_MAP_BUILDING"));
            return map;
        }

        private static void ValidateBase(string content, string source)
        {
            if (string.IsNullOrEmpty(content) || !Header.IsMatch(content))
                throw new InvalidDataException(L.F("#ARG0_NOT_VALID_ENT_LUMP", source));
        }

        private static string SafeDirectoryName(string value)
        {
            var result = new StringBuilder();
            foreach (char character in value ?? "")
                if (char.IsLetterOrDigit(character) || character == '-' || character == '_') result.Append(character);
            return result.Length == 0 ? "project" : result.ToString().Substring(0, Math.Min(48, result.Length));
        }
    }
}
