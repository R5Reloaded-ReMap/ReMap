using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace ReMap.Standalone.Core
{
    [Serializable] public sealed class AssetOrigin
    {
        // Empty mapId denotes an explicitly declared common archive, never a filename guess.
        public string mapId = "", archive = "";
    }
    [Serializable] public sealed class GameAssetRecord
    {
        public string guid, modelPath;
        public List<AssetOrigin> origins = new List<AssetOrigin>();
        [NonSerialized] private string cachedModelPath, cachedName, cachedCategory;
        public string Id => "apex:" + guid;
        public string Name { get { RefreshPathCache(); return cachedName; } }
        public string Category { get { RefreshPathCache(); return cachedCategory; } }
        private void RefreshPathCache()
        {
            string source = modelPath ?? "";
            if (string.Equals(source, cachedModelPath, StringComparison.Ordinal) && cachedName != null) return;
            cachedModelPath = source;
            string path = source.Replace('\\', '/');
            cachedName = Path.GetFileNameWithoutExtension(path);
            path = path.TrimStart('/');
            if (path.StartsWith("mdl/", StringComparison.OrdinalIgnoreCase)) path = path.Substring(4);
            int separator = path.IndexOf('/');
            cachedCategory = separator > 0 ? path.Substring(0, separator) : L.T("#UNCATEGORIZED");
        }
        public bool IsCommon => origins.Any(o => o.mapId == "");
        public bool Supports(IEnumerable<string> maps) => AssetCompatibility.Supports(IsCommon, origins.Select(o => o.mapId), maps);
        public bool Supports(ISet<string> maps) => AssetCompatibility.Supports(IsCommon, origins.Select(o => o.mapId), maps);
    }
    public static class AssetCompatibility
    {
        private static readonly string[] MapVariantSuffixes = {
            "_mu1", "_mu2", "_mu3", "_mu4", "_hu", "_night", "_tt", "_64k_x_64k"
        };

        public static string BaseMapId(string mapId)
        {
            string value = mapId ?? "";
            foreach (string suffix in MapVariantSuffixes)
                if (value.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                    return value.Substring(0, value.Length - suffix.Length);
            return value;
        }

        public static string[] ExpandTargets(IEnumerable<string> targets, IEnumerable<string> available)
        {
            var known = new HashSet<string>((available ?? Array.Empty<string>()).Where(map =>
                !string.IsNullOrWhiteSpace(map)), StringComparer.OrdinalIgnoreCase);
            var result = new List<string>();
            var added = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string target in (targets ?? Array.Empty<string>()).Where(map =>
                !string.IsNullOrWhiteSpace(map)).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                string baseMap = BaseMapId(target);
                if (!string.Equals(baseMap, target, StringComparison.OrdinalIgnoreCase) &&
                    known.Contains(baseMap) && added.Add(baseMap)) result.Add(baseMap);
                if (known.Contains(target) && added.Add(target)) result.Add(target);
            }
            return result.ToArray();
        }

        public static bool Supports(bool common, IEnumerable<string> available, IEnumerable<string> targets)
        {
            if (common) return true;
            var known = new HashSet<string>(available ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
            // Selected maps describe RPaks loaded together by the played level.
            return (targets ?? Array.Empty<string>()).Any(target => known.Contains(target) ||
                known.Contains(BaseMapId(target)));
        }
    }
    public static class MapPortCompatibility
    {
        private static List<MapObject> MissingModelObjects(MapDocument document, IEnumerable<GameAssetRecord> available)
        {
            if (document == null) throw new ArgumentNullException(nameof(document));
            var records = (available ?? Enumerable.Empty<GameAssetRecord>()).GroupBy(record => GameAssetIndex.NormalizeGuid(record.guid), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.OrdinalIgnoreCase);
            var missing = new List<MapObject>();
            foreach (var item in document.objects.Where(item => item.assetId != null && item.assetId.StartsWith("apex:", StringComparison.OrdinalIgnoreCase)))
            {
                string guid;
                try { guid = GameAssetIndex.NormalizeGuid(item.assetId.Substring(5)); }
                catch (InvalidDataException) { missing.Add(item); continue; }
                string expectedPath = (item.gameModelPath ?? "").Replace('\\', '/').Trim();
                if (!records.TryGetValue(guid, out var matches) || (expectedPath.Length > 0 && !matches.Any(record => string.Equals((record.modelPath ?? "").Replace('\\', '/').Trim(), expectedPath, StringComparison.OrdinalIgnoreCase))))
                    missing.Add(item);
            }
            return missing;
        }

        private static string MissingModelLabel(MapObject item)
        {
            string expectedPath = (item.gameModelPath ?? "").Replace('\\', '/').Trim();
            if (expectedPath.Length > 0) return expectedPath;
            try { return item.displayName + " (" + GameAssetIndex.NormalizeGuid(item.assetId.Substring(5)) + ")"; }
            catch (InvalidDataException) { return item.displayName; }
        }

        public static string[] MissingModels(MapDocument document, IEnumerable<GameAssetRecord> available) =>
            MissingModelObjects(document, available).Select(MissingModelLabel).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

        public static string[] DisableObjectsUsingMissingModels(MapDocument document, IEnumerable<GameAssetRecord> available)
        {
            var missing = MissingModelObjects(document, available);
            var byId = document.objects.ToDictionary(item => item.id);
            foreach (var item in missing)
            {
                item.disabled = true;
                string parentId = item.parentId;
                while (!string.IsNullOrEmpty(parentId) && byId.TryGetValue(parentId, out var parent) && !string.IsNullOrEmpty(parent.customType))
                {
                    parent.disabled = true;
                    parentId = parent.parentId;
                }
            }
            return missing.Select(MissingModelLabel).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        }

        public static int RemoveUnsupportedObjects(MapDocument document, string gameTarget)
        {
            if (document == null) throw new ArgumentNullException(nameof(document));
            MapObject[] roots = document.objects.Where(item =>
                (item.customType == "ziprail" || item.customType == "curved-zipline") &&
                !GameTargets.SupportsCustomType(gameTarget, item.customType)).ToArray();
            var removed = new HashSet<string>();
            foreach (var root in roots) removed.UnionWith(MapHierarchy.Subtree(document, root.id));
            document.objects.RemoveAll(item => removed.Contains(item.id));
            return roots.Length;
        }
    }
    public static class GameAssetIndex
    {
        public static string NormalizeModelPath(string value) =>
            (value ?? "").Replace('\\', '/').Trim().TrimStart('/');

        public static bool SameModelPath(string left, string right) =>
            string.Equals(NormalizeModelPath(left), NormalizeModelPath(right),
                StringComparison.OrdinalIgnoreCase);

        public static bool MatchesSearch(GameAssetRecord record, string search)
        {
            if (record == null) return false;
            string term = NormalizeModelPath(search);
            if (term.Length == 0) return true;
            return NormalizeModelPath(record.modelPath).IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0 ||
                record.Name.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0 ||
                record.Category.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public static bool ShowInCatalog(GameAssetRecord record, string search,
            bool automaticallyPrepared, bool alreadyLoaded)
        {
            if (!MatchesSearch(record, search)) return false;
            return NormalizeModelPath(search).Length > 0 || automaticallyPrepared || alreadyLoaded;
        }

        public static string NormalizeGuid(string value)
        {
            if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) value = value.Substring(2);
            if (!ulong.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var number) || number == 0)
                throw new InvalidDataException(L.T("#INVALID_ASSET_GUID"));
            return number.ToString("x16", CultureInfo.InvariantCulture);
        }
        public static bool IsSupportedCsvHeader(string header)
        {
            if (header == null) return false;
            var columns = header.Split(',');
            return columns.Length >= 4 && columns[0] == "type" && columns.Last() == "asset_name" && columns.Contains("guid") && columns.Contains("file_name");
        }
        public static List<GameAssetRecord> ReadCsv(string path, string mapId, string archive)
        {
            var records = new List<GameAssetRecord>();
            using (var lines = File.ReadLines(path).GetEnumerator())
            {
                if (!lines.MoveNext() || !IsSupportedCsvHeader(lines.Current)) throw new InvalidDataException(L.T("#UNRECOGNIZED_RSX_INDEX_COLUMNS"));
                var columns = lines.Current.Split(','); int guidIndex = Array.IndexOf(columns, "guid");
                while (lines.MoveNext())
                {
                    if (!lines.Current.StartsWith("mdl_,", StringComparison.Ordinal)) continue;
                    // Official RSX has four columns; Flowstate adds pakver and rsxver.
                    var parts = lines.Current.Split(new[] { ',' }, columns.Length);
                    if (parts.Length != columns.Length) continue;
                    string name = parts[parts.Length - 1].Trim();
                    if (!name.EndsWith(".rmdl", StringComparison.OrdinalIgnoreCase)) continue;
                    records.Add(new GameAssetRecord { guid = NormalizeGuid(parts[guidIndex]), modelPath = name,
                        origins = new List<AssetOrigin> { new AssetOrigin { mapId = mapId, archive = archive } } });
                }
            }
            return records;
        }
        public static List<GameAssetRecord> Merge(IEnumerable<GameAssetRecord> records)
        {
            var result = new Dictionary<string, GameAssetRecord>(StringComparer.Ordinal);
            foreach (var record in records)
            {
                var key = NormalizeGuid(record.guid);
                if (!result.TryGetValue(key, out var found)) result.Add(key, found = new GameAssetRecord { guid = key, modelPath = record.modelPath });
                foreach (var origin in record.origins)
                    if (!found.origins.Any(o => o.mapId == origin.mapId && o.archive == origin.archive))
                        found.origins.Add(new AssetOrigin { mapId = origin.mapId, archive = origin.archive });
            }
            return result.Values.OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }
    }
}
