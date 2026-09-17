using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using ReMap.Standalone.Core;
using UnityEngine;

namespace ReMap.Standalone
{
    public static class AppLocalization
    {
        [Serializable] public sealed class Entry { public string key, text; }
        [Serializable] public sealed class Catalog { public string language, name; public Entry[] entries; }
        public sealed class LanguageOption
        {
            public readonly string Code, Name;
            internal LanguageOption(string code, string name) { Code = code; Name = name; }
        }
        private const string Preference = "ReMap.Language.v1";
        internal static string PreferenceKey => Preference + (Environment.GetCommandLineArgs().AnySmokeFlag() ? ".qa" : "");
        public static string Folder => Path.Combine(Application.streamingAssetsPath, "Localization");
        public static IReadOnlyList<LanguageOption> Languages { get; private set; } = new[] { new LanguageOption("en", "English") };
        public static string SelectedLanguage { get; private set; } = "en";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Initialize()
        {
            var catalogs = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
            var options = new List<LanguageOption> { new LanguageOption("en", "English") };
            if (Directory.Exists(Folder)) foreach (string file in Directory.GetFiles(Folder, "*.json").OrderBy(p => p, StringComparer.Ordinal))
            {
                try
                {
                    if (new FileInfo(file).Length > 2 * 1024 * 1024) throw new FormatException("Language catalog exceeds 2 MiB.");
                    var catalog = JsonUtility.FromJson<Catalog>(ReadUtf8(file));
                    var messages = ReadCatalog(catalog);
                    if (catalogs.ContainsKey(catalog.language)) throw new FormatException("Duplicate language code.");
                    catalogs.Add(catalog.language, messages);
                    if (catalog.language != "en") options.Add(new LanguageOption(catalog.language, catalog.name));
                }
                catch (Exception ex) { Debug.LogWarning("Language catalog ignored: " + Path.GetFileName(file) + ": " + ex.Message); }
            }
            Languages = options.AsReadOnly();
            bool qa = Environment.GetCommandLineArgs().AnySmokeFlag();
            string requested = qa ? "en" : PlayerPrefs.GetString(Preference, "en");
            string argument = Environment.GetCommandLineArgs().FirstOrDefault(a => a.StartsWith("-remapLanguage=", StringComparison.Ordinal));
            if (argument != null) requested = argument.Substring("-remapLanguage=".Length);
            SelectedLanguage = options.Any(o => o.Code == requested) ? requested : "en";
            catalogs.TryGetValue("en", out var english); catalogs.TryGetValue(SelectedLanguage, out var selected);
            L.Configure(SelectedLanguage, english, selected);
        }
        internal static string ReadUtf8(string path)
        {
            string text = new UTF8Encoding(false, true).GetString(File.ReadAllBytes(path));
            if (text.IndexOf('\uFFFD') >= 0) throw new FormatException("Language catalog contains replacement characters.");
            return text;
        }
        public static Dictionary<string, string> ReadCatalog(Catalog catalog)
        {
            if (catalog == null || string.IsNullOrWhiteSpace(catalog.language) || string.IsNullOrWhiteSpace(catalog.name) || catalog.entries == null)
                throw new FormatException("Language catalog requires language, name and entries.");
            if (CultureInfo.GetCultureInfo(catalog.language).Name != catalog.language || catalog.language.Length == 0)
                throw new FormatException("Use a canonical culture code, such as en or fr.");
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var entry in catalog.entries)
            {
                if (entry == null || !L.IsTag(entry.key) || result.ContainsKey(entry.key))
                    throw new FormatException("Translation keys must be unique #UPPER_SNAKE_CASE tags.");
                result.Add(entry.key, entry.text); // Empty translations fall back to English in L.
            }
            return result;
        }
        public static void SelectForNextLaunch(string code)
        {
            if (!Languages.Any(o => o.Code == code)) throw new ArgumentException("Unknown language.", nameof(code));
            SelectedLanguage = code;
            PlayerPrefs.SetString(PreferenceKey, code); PlayerPrefs.Save();
        }
    }
}
