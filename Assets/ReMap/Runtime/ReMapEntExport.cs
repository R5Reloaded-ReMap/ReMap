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
        public IReadOnlyList<string> NutOnlyObjects { get; internal set; } = Array.Empty<string>();
        public int EntityCount => ScriptEntityCount + SoundEntityCount + SpawnEntityCount;
    }

    // .nut remains ReMap's primary output. This exporter only serializes objects with
    // a faithful native entity-lump representation and merges them into copies of the
    // original map lumps so num_models and Respawn's existing entities stay intact.
    public static class ReMapEntExporter
    {
        private static readonly string[] LumpKinds = { "env", "fx", "script", "snd", "spawn" };
        private static readonly Regex Header = new Regex(@"\AENTITIES02 num_models=\d+(?:\r?\n|\z)",
            RegexOptions.CultureInvariant);

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
            var nutOnly = new List<string>();
            Vector3 offset = WorldView.ToVector(document.originOffset);
            int scriptCount = 0, soundCount = 0, spawnCount = 0;

            foreach (var item in world.Where(item => !item.isGroup && item.customType == "" && !item.clientSide))
            {
                AppendProp(script, item, offset, true);
                scriptCount++;
            }
            foreach (var item in world.Where(item => !item.isGroup && item.customType == "" && item.clientSide))
                nutOnly.Add(Display(item) + " (client-side prop)");

            foreach (var item in world.Where(item => item.customType == "door"))
            {
                if (item.doorGold || item.doorSpawnOpen)
                {
                    nutOnly.Add(Display(item) + " (door options require .nut)");
                    continue;
                }
                scriptCount += AppendDoor(script, item, offset);
            }

            foreach (var item in world.Where(item => item.customType == "loot-bin"))
            {
                AppendAnimatedProp(script, item, offset, ReMapApp.LootBinModelPath,
                    "survival_lootbin", item.lootBinSkin);
                scriptCount++;
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

            foreach (var item in world.Where(item => item.customType == "spawn-point"))
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
                "", "door", "door-component", "loot-bin", "window-hint", "sound", "sound-point", "spawn-point"
            };
            foreach (var item in world.Where(item => item.customType.Length > 0 && !represented.Contains(item.customType) &&
                !item.customType.EndsWith("-component", StringComparison.Ordinal) &&
                !item.customType.EndsWith("-point", StringComparison.Ordinal) &&
                !item.customType.EndsWith("-target", StringComparison.Ordinal)))
                nutOnly.Add(Display(item));

            return new ReMapEntFragments
            {
                Script = script.ToString(), Sound = sound.ToString(), Spawn = spawn.ToString(),
                ScriptEntityCount = scriptCount, SoundEntityCount = soundCount,
                SpawnEntityCount = spawnCount, NutOnlyObjects = nutOnly
            };
        }

        public static string WriteMergedBundle(string selectedSourceEnt, MapDocument document,
            IEnumerable<MapObject> worldObjects)
        {
            if (string.IsNullOrWhiteSpace(selectedSourceEnt)) throw new ArgumentNullException(nameof(selectedSourceEnt));
            string sourceDirectory = Path.GetDirectoryName(Path.GetFullPath(selectedSourceEnt));
            string map = ValidateMapName(document?.editingMap);
            foreach (string kind in LumpKinds)
            {
                string source = Path.Combine(sourceDirectory, map + "_" + kind + ".ent");
                if (!File.Exists(source)) throw new FileNotFoundException(
                    L.F("#ENT_SOURCE_MISSING_ARG0", Path.GetFileName(source)), source);
                ValidateBase(File.ReadAllText(source), source);
            }

            ReMapEntFragments fragments = Generate(document, worldObjects);
            string project = SafeDirectoryName(document.name);
            string outputDirectory = Path.Combine(sourceDirectory, map + "_remap_ent_" + project);
            Directory.CreateDirectory(outputDirectory);
            foreach (string kind in LumpKinds)
            {
                string source = Path.Combine(sourceDirectory, map + "_" + kind + ".ent");
                string destination = Path.Combine(outputDirectory, Path.GetFileName(source));
                string fragment = kind == "script" ? fragments.Script : kind == "snd" ? fragments.Sound :
                    kind == "spawn" ? fragments.Spawn : "";
                if (fragment.Length == 0) File.Copy(source, destination, true);
                else File.WriteAllText(destination, Merge(File.ReadAllText(source), fragment, source),
                    new UTF8Encoding(false));
            }
            File.WriteAllText(Path.Combine(outputDirectory, "ReMap-ENT-report.txt"),
                BuildReport(map, fragments), new UTF8Encoding(false));
            return outputDirectory;
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

        private static string BuildReport(string map, ReMapEntFragments fragments)
        {
            var result = new StringBuilder();
            result.AppendLine("ReMap secondary .ent export for " + map);
            result.AppendLine("The .nut export remains the primary and complete ReMap output.");
            result.AppendLine("Generated entities: " + fragments.EntityCount.ToString(CultureInfo.InvariantCulture) +
                " (script=" + fragments.ScriptEntityCount + ", snd=" + fragments.SoundEntityCount +
                ", spawn=" + fragments.SpawnEntityCount + ").");
            result.AppendLine("These files are merged copies of the five selected base-map lumps; use them only in an ENT/BSP repack workflow.");
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
                Pair("model", model), Pair("ClientSide", "0")
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
            if (value.Length < 12 || !value.Take(12).All(Uri.IsHexDigit)) value = Guid.NewGuid().ToString("N");
            return value.Substring(0, 12).ToLowerInvariant() + index.ToString("x4", CultureInfo.InvariantCulture);
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
