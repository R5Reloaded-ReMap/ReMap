using System;
using System.Collections.Generic;
using System.Globalization;

namespace ReMap.Standalone.Core
{
    // Stable #TAGS are lookup keys. English catalog text is the readable fallback. No Unity dependency.
    public static class L
    {
        private sealed class State
        {
            internal readonly string Language;
            internal readonly CultureInfo Culture;
            internal readonly Dictionary<string, string> English, Translated;
            internal State(string language, IDictionary<string, string> english, IDictionary<string, string> translated)
            {
                Culture = CultureInfo.GetCultureInfo(language);
                Language = language;
                English = Copy(english); Translated = Copy(translated);
            }
            private static Dictionary<string, string> Copy(IDictionary<string, string> source)
            {
                var result = new Dictionary<string, string>(StringComparer.Ordinal);
                if (source != null) foreach (var pair in source)
                    if (!string.IsNullOrEmpty(pair.Key) && !string.IsNullOrWhiteSpace(pair.Value)) result[pair.Key] = pair.Value;
                return result;
            }
            internal string Fallback(string key) => English.TryGetValue(key, out var value) ? value : ReadableTag(key);
            internal string Text(string key) => Translated.TryGetValue(key, out var value) ? value : Fallback(key);
            private static string ReadableTag(string key)
            {
                if (!IsTag(key)) return key;
                string value = key.Substring(1).Replace('_', ' ').ToLowerInvariant();
                return value.Length == 0 ? key : char.ToUpperInvariant(value[0]) + value.Substring(1);
            }
        }
        // Published atomically; background asset workers never observe a partially loaded catalog.
        private static volatile State current = new State("en", null, null);
        public static string Language => current.Language;
        public static bool IsTag(string key)
        {
            if (string.IsNullOrEmpty(key) || key.Length < 2 || key[0] != '#') return false;
            for (int i = 1; i < key.Length; i++)
            {
                char character = key[i];
                if ((character < 'A' || character > 'Z') && (character < '0' || character > '9') && character != '_') return false;
            }
            return true;
        }
        public static void Configure(string language, IDictionary<string, string> english = null, IDictionary<string, string> translated = null)
            => current = new State(language, english, translated);
        public static string T(string key) => current.Text(key);
        public static string F(string key, params object[] arguments)
        {
            var state = current;
            try { return string.Format(state.Culture, state.Text(key), arguments); }
            catch (FormatException)
            {
                // An invalid community translation must not break editing or an asset worker.
                try { return string.Format(CultureInfo.InvariantCulture, state.Fallback(key), arguments); }
                catch (FormatException) { return state.Fallback(key); }
            }
        }
    }
}
