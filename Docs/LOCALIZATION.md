# Localization

The standalone editor defaults to English, independently of the operating system language. French is included. Choose **Settings → Interface → Language**, save the current map and restart the app. The choice is saved in the `ReMap.Language.v1` PlayerPrefs preference. Changing it does not reload the scene, restart asset extraction or modify map data.

## Editable language files

Source: `Assets/StreamingAssets/Localization/en.json` and `fr.json`.

Windows build: `ReMap_Data/StreamingAssets/Localization/` next to the executable. These files are included automatically by Unity and read at startup. No model or texture loading is required. To keep edits across builds, change the source copies too.

To add a language, copy `en.json`, set `language` to a canonical .NET culture code such as `de`, set `name` to its native name, and translate each `text`. Keep every stable `#TAG` key unchanged. Save as UTF-8 JSON, for example `de.json`, in the same folder, then relaunch. Valid files are automatically listed in Settings. No source changes or rebuild are needed for a local Windows installation.

```json
{
  "language": "de",
  "name": "Deutsch",
  "entries": [
    { "key": "#SAVE", "text": "Speichern" },
    { "key": "#THUMBNAILS_ARG0_ARG1", "text": "Vorschaubilder: {0}/{1}" }
  ]
}
```

Preserve numbered placeholders such as `{0}`, `{1}`, their format specifiers and JSON newline escapes. Placeholders may move within a translation, but must not be removed or renumbered. Empty or missing translations fall back to the English catalog, then to the English source message. Invalid translated format strings fall back to English when displayed. Malformed catalogs and duplicate keys are ignored with a warning in the player log. Files are limited to 2 MiB.

## Developer conventions

Use a short semantic tag such as `L.T("#SAVE")` for static UI text and `L.F("#SAVED_FILE", value)` for complete dynamic messages. Tags use uppercase ASCII letters, digits and underscores, start with `#`, stay below 64 characters, and must never change when the English wording changes. Add the same tag to both bundled catalogs; the English `text` is the readable fallback. Do not translate model paths, asset GUIDs, archive identifiers, CSS classes, serialized property names or user-entered names. User content is saved verbatim. Generated default object/group names use the language active when created.

`Core/Localization.cs` has no Unity dependency. Runtime startup loads small text catalogs before creating the UI; worker threads read an immutable snapshot. Switching the preference applies only on next launch, so existing asynchronous status updates and controls never mix languages. This does not change serialization culture or the map schema.

Map titles provided by the game, RSX log contents, and operating system errors retain the source language. Unity's own Editor UI language is controlled by Unity; our two Unity menu commands use English.

## Validation

`LocalizationTests` covers tag syntax and length, fallback, malformed placeholders, matching English/French keys and placeholders, duplicate keys, unchanged saved map data, and defensive copying for background readers. Run the complete `ReMap.Tests` EditMode suite.

Player QA: `-remapLocalizationSmoke` for default English; add `-remapLanguage=fr` for French. These checks verify the actual main UI, demo catalog, Settings language selector, preference persistence and unchanged active map/revision. QA uses a separate preference key and restores it afterwards. Screenshots are written beside the tested executable.
