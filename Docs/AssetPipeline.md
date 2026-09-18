# Local asset loading — prototype status

> Current implementation: the official RSX bundled beside `ReMap.exe` is launched directly in command-line mode. Previews are generated progressively and cached. RSX selects a loaded mip no larger than 512 px for preview PNGs, and Unity enforces the same fixed limit as a fallback. Textures share the content-addressed `AssetCache/Textures` directory. Active models use the stable `AssetCache/Models` path. See [README](../README.md) and [RSX integration](../Tools/RSX-BACKEND.md).

> Both official four-column CSV and extended six-column CSV indexes are accepted. Failed textured exports are retried as geometry-only exports in a separate work directory and log.

## Current flow

RSX remains an external executable launched without a shell. C# code manages metadata, selected archives, GUID deduplication, cache storage, CAST v1 geometry, and Unity rendering. RSX source code is not embedded into the application.

1. R5Flowstate map names come from `platform/r5f_map_names.txt` when present. R5Reloaded maps are detected from `platform/scripts/levels/mp_*.rson` and matched to exact RPAK filenames.
2. Common archives are selected per target. R5Reloaded includes `common_sdk`; R5Flowstate includes `common_flowstate`.
3. Every selected map contributes its main RPAK plus `_client_perm` and `_client_temp` when present. RSX resolves patch packs. Archives are never guessed by a broad “desertlands” or “common” filename prefix.
4. Every archive receives its own index, followed by a merge on normalized 64-bit GUID. One record retains all known origins. Matching names do not merge different GUIDs.
5. The library displays Common plus the union of models from selected map RPAKs. One selected origin is sufficient; incompatible indexed references are always hidden.
6. Changing selected archives retains placed objects and reports incompatible references.
7. Clicking a model exports its LOD0 CAST and images when not already cached. Vertex conversion retains the original pivot and uses the centralized Source Z-up/Apex-unit to Unity Y-up/meter conversion.
8. Instances share meshes, materials, textures, and collision data. Temporary preview geometry is released after capture.
9. The library exposes the complete filtered catalog in its scrollbar immediately. It virtualizes cards and thumbnails to the visible rows, so browsing thousands of models does not retain thousands of UI objects or textures.
10. The optional map reference uses ReVPK to unpack the selected server BSP. ReMap's own managed reader decodes its BVH collision terrain and static-prop game lump. MPRT models then reuse the normal GUID-indexed RSX model and texture cache.

Saved JSON files retain selected target maps and original `mdl/...rmdl` references independently from object display names. Stored provenance represents the index state at placement time; a future exporter must revalidate the active game installation and complete dependencies.

## Cache and memory

`asset-source.local.json`, `Tools/.local`, and `AssetCache` are excluded from Git.

The active cache layout is:

- `AssetCache/Models`: per-GUID CAST exports, manifests, and thumbnails;
- `AssetCache/Textures`: shared content-addressed PNGs;
- `AssetCache/index`: per-archive CSV indexes;
- `AssetCache/active-generation.txt`: fingerprint of the current RPAK index set and RSX binary;
- `AssetCache/Worker`: temporary extraction data.
- `AssetCache/MapReferences`: per-game BSP sidecar lumps and generated `.mprt` files.

RPAK/STARPAK and RSX sizes and timestamps contribute to the index fingerprint. Models and textures remain in shared GUID-based folders when the active game or index generation changes. `AssetCache/Versions` contains archived index generations, not alternate copies of compatible models. The full cache path is visible in Settings and can be opened from the application.

RSX operations are serialized and launched without a command shell. They use local logs, output validation, a three-minute timeout, and owned-process shutdown when the editor closes. The Unity catalog stores metadata rather than every mesh.

RSX still decompresses an archive in its own process. Approximately 2.6 GiB was observed while reading `common` with official RSX; the Flowstate material pipeline can peak around 5.5 GiB. This memory is temporary.

CAST files are limited to 128 MiB, models to one million vertices, and albedos to 16 cumulative megapixels. There is not yet a global RAM/VRAM budget for scenes containing many different models.

## Known limitations

- Indexing Common + Desertlands + Olympus produces **6,817 unique model GUIDs**, including **3,590 Common**. This is not the complete game.
- Geometry, thumbnails, save-file reopening, and cache restoration are validated with real models including `death_box_01`, `office_chair_leather`, and both hover-ship platform variants.
- Some PNGs have no semantic albedo link in CAST; other materials depend on unresolved archives. The editor displays a neutral material and reports the missing albedo instead of guessing from `texture_0` or `texture_1`.
- The official RSX 2.2.1 build cannot export every indexed reference. An indexed name does not guarantee a successful preview.
- Import is static only: no skinning, skeleton pose, animation, or complete Apex material.
- Unprepared models appear as placeholders while retaining their references. Loading a map reindexes its selected RPAKs and restores already cached models.
- The BSP overlay is a locked collision-terrain reference, not editable render geometry. Server BSPs do not contain the client terrain-material bindings, so the terrain uses a neutral material; MPRT model albedos still use the normal model pipeline.
- Script export and live commands are target-aware; complete in-game validation is still required for newly added custom-object types.

## Sources

- Official RSX 2.2.1: https://github.com/r-ex/rsx/releases/tag/2.2.1
- Official RSX CLI: https://github.com/r-ex/rsx/blob/main/docs/CLI.md
- CAST specification: https://github.com/dtzxporter/cast
- Flowstate launcher revision studied: `d782008063ccf09cb73453e9746b1b6f2721342b`, https://github.com/R5Flowstate/launcher
- Flowstate RSX revision studied: `ab3b7d0bad68c749f08329bccd575844b702915b`, https://github.com/R5Flowstate/rsx

The launcher contains a minimal C# RPAK reader for UI images using Oodle. It is not a mesh reader, and none of its source has been integrated here.

## Archives loaded together

Map checkboxes represent RPAKs loaded together by the played map. With Desertlands and Olympus selected, a model from either archive is available, while Common is always included. A model present in both appears once by GUID.

The same rule applies to thumbnails, extraction-archive selection, placement, and assemblies. Removing one archive disables only models that come from neither Common nor another selected source. JSON `targetMaps` and `availableMaps` fields preserve saved-map compatibility information.

Validation includes the EditMode suite and `-remapArchiveUnionSmoke` against real Desertlands and Olympus indexes. This editor configuration step does not change game configuration.
