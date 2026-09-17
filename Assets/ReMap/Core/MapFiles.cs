using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace ReMap.Standalone.Core
{
    public interface IMapCodec
    {
        string Encode(MapDocument document);
        MapDocument Decode(string json);
    }

    /// <summary>Game-independent local saves; never writes to a game installation.</summary>
    public sealed class MapFiles
    {
        public const string PortableExtension = ".remap-project.json";
        private const int MaxBytes = 16 * 1024 * 1024;
        private readonly string folder;
        private readonly IMapCodec codec;
        public static string EnsurePortableExtension(string path) =>
            path.EndsWith(PortableExtension, StringComparison.OrdinalIgnoreCase)
                ? path : path + PortableExtension;
        public MapFiles(string folder, IMapCodec codec)
        {
            this.folder = Path.GetFullPath(folder);
            this.codec = codec ?? throw new ArgumentNullException(nameof(codec));
        }

        public string PathFor(string slot)
        {
            if (string.IsNullOrWhiteSpace(slot) || slot.Length > 64)
                throw new ArgumentException(L.T("#INVALID_FILE_NAME"));
            foreach (char c in slot)
                if (!((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') ||
                      (c >= '0' && c <= '9') || c == '-' || c == '_'))
                    throw new ArgumentException(L.T("#FILE_NAME_LETTERS_NUMBERS_HYPHENS"));
            // A prefix also avoids Windows device names such as CON and NUL.
            return Path.Combine(folder, "map-" + slot + ".remap.json");
        }

        public void Save(string slot, MapDocument document)
        {
            document.Validate();
            var path = PathFor(slot);
            var json = codec.Encode(document);
            if (Encoding.UTF8.GetByteCount(json) > MaxBytes)
                throw new IOException(L.T("#SAVE_FILE_EXCEEDS_16_MIB"));
            Directory.CreateDirectory(folder);
            var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temp, json, new UTF8Encoding(false));
                if (File.Exists(path)) File.Replace(temp, path, path + ".bak");
                else File.Move(temp, path);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }

        public MapDocument Load(string slot)
        {
            return ReadDocument(PathFor(slot));
        }

        public void ExportPortable(string path, MapDocument document)
        {
            if (document == null) throw new ArgumentNullException(nameof(document));
            document.Validate();
            path = Path.GetFullPath(path ?? throw new ArgumentNullException(nameof(path)));
            string json = codec.Encode(document);
            if (Encoding.UTF8.GetByteCount(json) > MaxBytes)
                throw new IOException(L.T("#SAVE_FILE_EXCEEDS_16_MIB"));
            string directory = Path.GetDirectoryName(path);
            if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
                throw new DirectoryNotFoundException(L.T("#EXPORT_FOLDER_NOT_FOUND"));
            string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temp, json, new UTF8Encoding(false));
                if (File.Exists(path)) File.Replace(temp, path, null);
                else File.Move(temp, path);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }

        public MapDocument ImportPortable(string path, string slot)
        {
            MapDocument document = ReadDocument(path);
            Save(slot, document);
            return document;
        }

        public MapDocument ReadPortable(string path) => ReadDocument(path);

        private MapDocument ReadDocument(string path)
        {
            path = Path.GetFullPath(path ?? throw new ArgumentNullException(nameof(path)));
            using (var stream = File.OpenRead(path))
            {
                if (stream.Length > MaxBytes) throw new IOException(L.T("#SAVE_FILE_EXCEEDS_16_MIB"));
                using (var reader = new StreamReader(stream, Encoding.UTF8))
                {
                    var document = codec.Decode(reader.ReadToEnd());
                    if (document == null) throw new ArgumentException(L.T("#EMPTY_SAVE_FILE"));
                    document.Validate();
                    return document;
                }
            }
        }
        public bool Exists(string slot) => File.Exists(PathFor(slot));
        public void Rename(string currentSlot, string newSlot)
        {
            string source = PathFor(currentSlot), destination = PathFor(newSlot);
            if (!File.Exists(source)) throw new FileNotFoundException(L.T("#CURRENT_MAP_SAVE_FOUND"), source);
            if (File.Exists(destination)) throw new IOException(L.T("#MAP_ALREADY_USES_NAME"));
            File.Move(source, destination);
        }

        public string[] ListSlots()
        {
            if (!Directory.Exists(folder)) return Array.Empty<string>();
            var result = new List<string>();
            foreach (var path in Directory.GetFiles(folder, "map-*.remap.json", SearchOption.TopDirectoryOnly))
            {
                var name = Path.GetFileName(path);
                const string prefix = "map-", suffix = ".remap.json";
                if (name.Length <= prefix.Length + suffix.Length || !name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || !name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) continue;
                result.Add(name.Substring(prefix.Length, name.Length - prefix.Length - suffix.Length));
            }
            result.Sort(StringComparer.OrdinalIgnoreCase);
            return result.ToArray();
        }

        public string[] ListSlots(string gameTarget)
        {
            string target = GameTargets.Normalize(gameTarget);
            var result = new List<string>();
            foreach (string slot in ListSlots())
            {
                try
                {
                    if (GameTargets.Normalize(Load(slot).gameTarget) == target) result.Add(slot);
                }
                catch (Exception exception) when (exception is IOException || exception is ArgumentException || exception is UnauthorizedAccessException)
                {
                    // A broken save stays out of the project menu without hiding valid projects.
                }
            }
            return result.ToArray();
        }
    }
}
