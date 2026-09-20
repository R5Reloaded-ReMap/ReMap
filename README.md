# ReMap — standalone map editor for Apex R5

ReMap is a Windows 3D map editor for **R5Reloaded** and **R5Flowstate**, built with Unity **6000.3.24f1**.

## Highlights

- Search, preview, place, group, duplicate, and transform Apex models.
- Hierarchy, multi-selection, undo/redo, reusable assemblies, and construction tools.
- Per-project game target, edited map, origin offset, and RPAK selection.
- Optional BSP collision terrain and MPRT static-model reference.
- Custom gameplay objects: ziplines, doors, jump pads, triggers, jump towers, buttons, sounds, panels, cameras, and more.
- `.nut` script export plus optional native loose `.ent` generation.
- Live rebuild through the Flowstate launcher or the active R5Reloaded `r5apex.exe`; RCON is optional.
- Developer compatibility scene that reports generated and ignored object configurations.

## Target support

| Target | Specific support |
| --- | --- |
| R5Reloaded | Native two-point ziplines and curved ziplines; ziprails disabled. |
| R5Flowstate | Native ziprails; curved ziplines disabled. |

Projects are locked to their selected target. Porting creates a validated copy and reports unavailable models.

## Run and build

Add the repository to Unity Hub, open `Assets/ReMap/Workspace.unity`, and enter Play mode. If needed, use **ReMap > Prepare and open workspace**.

Build from the interactive Windows menu with `Build-ReMap.cmd`, or run:

```powershell
.\Tools\Build-ReMap.ps1
```

For the optimized build with developer-only tools:

```powershell
.\Tools\Build-ReMap.ps1 -BuildRsx Never -DeveloperTools
```

Distribute the complete `Builds/Windows` directory, including `ReMap_Data`, `ReMapBridge.exe`, RSX, ReVPK, and their legal notices.

## Game workflow

1. Configure the R5R and/or R5F game roots in **Settings**.
2. Create a project and select its edited map and additional RPAKs.
3. Build a `.nut` script or install a native `.ent` development map.
4. Restart or live-rebuild the active map from ReMap.
5. Use **Reset installed game script** to clear ReMap scripts and restore or remove its loose `.ent` overrides.

The Asset Cache location is configurable in **Settings**. The cache can grow to approximately **10 GB**, so choose a folder on a drive with enough free space.

Saved projects are stored under `%USERPROFILE%\AppData\LocalLow\ReMap\ReMap\Maps`. Portable projects use `.remap-project.json`.
Global settings are stored in `%USERPROFILE%\AppData\LocalLow\ReMap\ReMap\asset-source.local.json`. ReMap migrates the previous file stored beside the executable on first launch after updating.

When beta.2 is extracted beside rather than over beta.1, use **Import beta.1 settings** on the welcome screen and select the old application folder. ReMap keeps using its existing `AssetCache` in place; it does not copy or delete the cache.

## Documentation

[Getting started](Docs/GETTING-STARTED.md) · [Editing tools](Docs/EDITOR-TOOLS.md) · [Construction tools](Docs/CONSTRUCTION-TOOLS.md) · [Game export](Docs/GAME-EXPORT.md) · [Asset pipeline](Docs/AssetPipeline.md) · [Releases](Docs/RELEASING.md) · [Contributing](CONTRIBUTING.md)

## License and credits

ReMap continues the original work by Zee and Julefox and is maintained by Julefox. It builds on the Apex modding work of Mauler125 and the [r5sdk](https://github.com/R5Reloaded/r5sdk) community.
ReMap is licensed under [MPL 2.0](LICENSE). Bundled RSX is distributed separately under AGPL-3.0; see [licensing](LICENSING.md) and [third-party notices](THIRD_PARTY_NOTICES.md). This project is not affiliated with Electronic Arts, Respawn Entertainment, Valve, or Unity Technologies.
