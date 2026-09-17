# ReMap — standalone map editor for R5Reloaded and R5Flowstate

Unity **6000.3.24f1** project using **URP 17.3.0**, UI Toolkit, and the Input System.

ReMap continues the work of the original editor created by Zee ([@AyeZeeBB](https://x.com/AyeZeeBB), `zee_x64` on Discord) and Julefox ([@Julefox_](https://x.com/Julefox_), `julefox` on Discord). This standalone rewrite is created and maintained by Julefox — made with love for the Apex modding community. ❤️

The project also owes its technical foundation to [Mauler125](https://github.com/Mauler125) and the Apex modding work surrounding [r5sdk](https://github.com/Mauler125/r5sdk). See [licensing](LICENSING.md), [credits and third-party notices](THIRD_PARTY_NOTICES.md), including the bundled [RSX license](ThirdParty/RSX/LICENSE), before redistribution.

## Open and run

Add this repository folder as an existing project in Unity Hub.
Open `Assets/ReMap/Workspace.unity` and enter Play mode. If the scene is missing, use **ReMap > Prepare and open workspace**.
Unity generates the solution files used by code editors.

Double-click `Build-ReMap.cmd` for the interactive Windows build menu, or run `./Tools/Build-ReMap.ps1` from PowerShell for automation. The menu provides optimized local builds; when the maintainer-only local packager is installed, it also provides personal test packages, Unity development/RC packages, and public release packages. Release candidates use compact tags such as `v0.1.0-rc.1`; final releases use `v0.1.0`. Tags are created locally only after a package succeeds and are never pushed automatically. See [Release workflow](Docs/RELEASING.md).
Distribute the complete Windows directory, not the executable alone. It contains `ReMap.exe`, `ReMap_Data`, `ReMapLiveBridge.exe`, and the ReMap session build of official RSX with its legal notices. The application version and project credits are available from **Help > About ReMap**.

## Main features

- Searchable Apex model library with compact thumbnails, categories derived from `mdl/<category>/...`, and RPAK provenance.
- Responsive multi-row library with continuous vertical scrolling and progressive batches instead of numbered pages.
- Click a model to inspect it, then place it in the scene; models can also be dragged into the scene or a folder.
- Optional read-only map reference with two independent controls: the main BSP collision terrain and the static models listed by its MPRT data.
- Ctrl/Shift multi-selection, scene selection rectangle, hierarchy ranges, shared transforms, and rotation-aware selection outlines.
- Live property and gizmo synchronization. Dragging X/Y/Z moves on one axis; the XY, XZ, and YZ squares move on two-axis planes. Each gesture creates one undo step.
- Duplication, deletion, grouping, and a 64-operation undo history.
- Versioned JSON map files, explicit edited-map selection, and the exact set of RPAK sources saved with each map.
- On reopening a map, its selected RPAKs are indexed again and already extracted models are restored from cache.
- On-demand resource creation with shared meshes, materials, textures, and collision data between instances.
- Reusable personal assemblies that retain hierarchy and model references without copying textures.
- Precise CAST mesh collisions for selection, placement, and construction tools.
- Apex server-script export for stock models, including per-object `.kv` and `.e` assignments.

## Camera and shortcuts

Right mouse + mouse movement looks around. The wheel zooms proportionally; Shift + wheel accelerates it. Middle-button drag pans in the camera plane, while a middle click focuses the pointed surface.

Right mouse + WASD/ZQSD flies in the viewing direction. R/F moves vertically and Shift accelerates. Without the right mouse button, F frames the selection.

- Arrow keys: move the selection on the ground plane.
- Page Up/Page Down: change height.
- W/E/R: move, rotate, or scale.
- Ctrl+C / Ctrl+V / Ctrl+X: copy, paste, or cut selected branches.
- Ctrl+D: preview a duplicate of the selected branches, then click a surface to place it; Escape cancels.
- Delete: remove.
- Ctrl+G: group.
- Ctrl+Z / Ctrl+Y: undo/redo.
- Ctrl+S: save.
- F11 or Alt+Enter: enter borderless fullscreen or restore the previous window size. Fullscreen covers the complete display, including the Windows taskbar area.
- Escape: cancel the model or assembly currently being placed, regardless of UI focus.

Document shortcuts work in both the 3D view and hierarchy. Text fields retain their normal editing shortcuts.

## Maps and saved projects

The project selector in the upper-right corner lists saved maps. Creating a map opens a dialog where you choose:

1. the local project name;
2. the target game, with R5Reloaded selected by default;
3. the Apex map being edited;
4. any additional RPAKs whose models are needed.

The edited map is always included in the selected RPAKs. Saving and loading preserve both values.

Maps are stored in `%USERPROFILE%\AppData\LocalLow\ReMap\ReMap\Maps`. Existing saves from the former application directory are copied automatically on first use.
When a file is replaced, the previous version is retained as `.bak`.
The latest workspace is also recovered locally after an unexpected close.

The game badge beside the project selector opens the dedicated **Workspace game** dialog. Switching there saves the current project and loads the last project created for the selected game; it never changes the target stored in the current project.

**File > Export current project…** creates a portable `.remap-project.json` file containing the complete hierarchy, transforms, target game, edited map, RPAK selection, and model references. **File > Import shared project…** validates a received file, gives it a collision-free local project name, and opens it when it targets the current workspace game. Model cache files are intentionally not embedded; the receiving installation rebuilds them from its own RPAKs.

## Multiple selection and assemblies

Ctrl+C, Ctrl+V, and Ctrl+X preserve complete selected branches and their internal hierarchy. Ctrl+G groups the selection. Ctrl+D previews every selected root branch at the cursor and inserts the copies only after a surface click. Delete operates on every selected root branch.

To create an assembly, select objects or folders, click **Save selection**, and then enter its name in the dialog. The result appears under **Assemblies** and can be placed by clicking or dragging its card.

Assembly files are stored in `%USERPROFILE%\AppData\LocalLow\ReMap\ReMap\Assemblies`.
They preserve subfolders, transforms, disabled states, and model provenance. Each insertion receives independent object IDs.

See [Editing tools](Docs/EDITOR-TOOLS.md). Player check: `-remapEditorSmoke`.

## Apex assets and loaded archives

Open **Settings** for global preferences, the two independent R5Reloaded/R5Flowstate installation paths, live connection, preview quality, cache location, and map-reference display. Each game's paths are grouped in a collapsible section. ReMap detects `paks/Win64`, `paks/Win64_server`, available map RPAKs, and model entries automatically for the selected game.

Open **Loaded archives** or **Tools > Indexing…** for the current project's dedicated Indexing page. It displays the project's locked target game, the edited map, and additional RPAK sources. Closing the page does not start background work; **Apply, index and close** explicitly rebuilds the model catalog.

ReMap always calls the bundled RSX directly. It is a minimal fork of the official source which keeps one process and the currently needed RPAKs loaded while thumbnails are prepared. There is no path or version selector: every Windows build copies `rsx.exe` and its session marker beside `ReMap.exe`. During development, the source build at `rsx/bin/Release/rsx.exe` is detected automatically.

At startup or after loading a saved map, the selected archives that exist in the active installation are indexed from cache. A source saved for another or older installation is ignored until it becomes available again. Missing thumbnails are prepared progressively, one model at a time. Existing thumbnails are reused. Clicking a card gives that model priority.

The catalog is deduplicated by normalized 64-bit GUID. A model is available when it belongs to Common or at least one selected map archive. A single card can list several source RPAKs. Changing the selected archives never deletes placed objects; incompatible references remain editable and are reported.

The editor describes which RPAKs the played map is expected to load. Building a game script writes only the configured platform source tree; it does not edit RPAKs or extracted assets.

A real Common + Desertlands + Olympus pass produced **6,817 unique models**, including **3,590 Common** entries. Static geometry, albedo textures, stock-model `.gnut` export, BSP collision-terrain reference, and MPRT static-model placement are supported. Animation, skinning, complete Apex terrain materials, gameplay entities, custom-model compilation, and editable level geometry remain outside the current scope.

Pipeline details: [Asset pipeline](Docs/AssetPipeline.md). Tool details: [RSX integration](Tools/RSX-BACKEND.md).

Game integration: [Apex game script export](Docs/GAME-EXPORT.md) and [game property reference](Docs/GAME-PROPERTIES.md).

## Asset cache

The active cache has a stable, readable layout:

- `AssetCache/Models`: extracted CAST models and per-model manifests.
- `AssetCache/Textures`: content-addressed PNG files shared across models.
- `AssetCache/index`: cached archive indexes.
- `AssetCache/active-generation.txt`: the current RPAK index fingerprint.
- `AssetCache/Worker`: temporary RSX work directory, created only when needed.
- `AssetCache/MapReferences`: extracted BSP sidecar lumps and generated MPRT placement data, separated by target game and map.

The full cache path appears in Settings with an **Open cache folder** button.
`AssetCache/Models` and `AssetCache/Textures` are shared between both games by model GUID. Switching installations does not replace a compatible model that is already cached. `AssetCache/Versions` retains older per-installation RPAK indexes so they can be restored without mixing archive metadata. Generated caches and local tools are excluded from Git.

Saved projects are listed only for the active target game, with separate recovery state for R5Reloaded and R5Flowstate. A project's game target is locked after creation, so it cannot be launched against the other installation. **File > Port current map to the other game…** selects the opposite game, verifies the target RPAKs, and creates a converted copy only when every Apex model exists under the same GUID and internal model path. If conversion is blocked, a dedicated report lists every unavailable or incompatible model and no copy is created. Live rebuilding is disabled while a scene references map RPAKs absent from the active installation.

New textures are limited to **1024 px** by default, configurable from 256 to 2048. Existing files are preserved. Content hashes avoid duplicate PNG copies, and runtime reference counting avoids duplicate GPU textures.

The first full pass can take time because RSX decompresses archives in its own process, although Unity only retains the current preparation model.

## Hierarchy and folders

The hierarchy displays nested folders with indentation, icons, child counts, and the selected path. It scrolls horizontally for deep nesting.

- **+ Folder** creates a root folder; **+ Subfolder** creates one under the selected folder.
- F2 or **Rename** edits a row. Enter confirms and Escape cancels.
- Dragging a folder moves its complete branch while preserving world-space poses.
- With automatic folder closing enabled, folders already opened by the user remain visible while a hierarchy drag is in progress, so they can still receive the moved item.
- Dropping on **Scene**, empty hierarchy space, or **Move to root** removes the parent.
- Cycles are rejected before the document changes.
- Arrow keys navigate visible rows; Right opens a folder and Left closes it or selects its parent.
- Disabled folders hide and disable their descendants while remaining present in saved files.
- Reparenting, duplication, deletion, and activation state are undoable.

Player check: `-remapHierarchySmoke`.

## Editor layout

The editor uses a compact dark theme. Panels remain docked around the scene.

- Drag the vertical splitter to resize the side panel.
- Drag the splitter above the library to change its height.
- Drag the hierarchy/properties splitter to adjust their vertical share.
- The × button closes one panel; **View** reopens it or resets the layout.
- **Maximize** temporarily hides other panels and **Restore** returns to the saved layout.
- The Settings window can be moved by its title and resized from its lower-right corner.
- Panel sizes and visibility are stored separately from map files.
- Scrollbars use compact tracks with usable minimum thumb sizes.

Player check: `-remapLayoutSmoke`.

## Construction tools

Open **Construction tools** from the top menu. The draggable floating window displays one selected tool at a time, leaving the Properties panel and viewport available.
Use the status button in its title bar to pause the selected tool without closing the window or losing its settings. Selecting another tool or reopening the window activates it again.

- **Drop to ground**: move the selection base onto a support below it, with an optional margin.
- **Select by type**: select matching models, regular props, folders, or ziplines across the scene or inside selected branches.
- **Batch model replacement**: build a list from Library models, then replace selected props while preserving transforms and parameters; optionally choose a random replacement per object.
- **Parameter transfer**: copy ReMap/object parameters and Game Script properties between compatible objects without changing their model, transform, hierarchy, or structural links.
- **Measure / information**: measure a live pivot-to-pivot distance on any combination of Apex X, Y and Z axes, inspect object information, and copy the result.
- **Each folder object**: apply ground placement and variations independently to active descendants.
- **Directional duplication**: select one of the six colored arrows around the model to preview and create a copy, using local or world directions, edge-to-edge or a fixed step.
- **Grid / wall**: create XZ or XY grids while retaining complete branches.
- **Variations**: random vertical rotation and uniform scale.
- **Adjust**: snap to the grid, reset local rotation, or reset local scale.
- **Folder pivot**: move a folder pivot to the bottom center, full center, or top center without moving any child. Select reference models, then right-click their folder to calculate the anchor from that selection; moving the folder afterwards still moves every child.
- **Measure / information**: live pivot distance, XYZ offsets, world pose, dimensions, and copyable model information.

Each command creates one undo operation. The game terrain is not imported implicitly; only scene objects and the optional Y=0 plane can act as supports.
Hovering a directional-duplication gizmo arrow or the grid creation button displays the future copies as green holograms without changing the scene.

See [Construction tools](Docs/CONSTRUCTION-TOOLS.md). Player check: `-remapToolsSmoke`.

## Precise collisions

Apex objects use concave MeshColliders derived from displayed CAST triangles. Copies reuse the same mesh and collision data. Thumbnails and placement ghosts do not prepare collision resources.

These are editor interaction collisions, not imported official Apex collision volumes. Geometry holes remain open, while holes painted only in an alpha texture remain solid triangles.

Player check: `-remapCollisionSmoke`.

## Performance target

The editor is optimized and measured against a reference scene containing **3,000 instances**. The hierarchy is virtualized, transforms update incrementally, and selection/gizmo bounds are cached.

On the reference i9-14900K / RTX 4080 system at 1600×900, the benchmark runs around 60 FPS, selection is approximately 1 ms, and a common edit is approximately 8 ms. Unique model count and model complexity remain important.

See [3,000-instance performance](Docs/PERFORMANCE-3000.md).

## Architecture

- `Assets/ReMap/Core`: data, validation, transactional history, coordinates, and file handling without Unity dependencies.
- `Assets/ReMap/Runtime`: UI, 3D workspace, model pipeline, tools, and player checks.
- `Assets/ReMap/Editor`: workspace preparation and the single Windows build command.
- `Assets/ReMap/Tests/Editor`: save, hierarchy, asset, transform, localization, and cache tests.

## Validation

Run the `ReMap.Tests` EditMode assembly from Unity Test Runner.
Run the complete EditMode suite after changing game profiles, export generation, or asset detection.

Useful standalone checks include:

- `-remapSmoke`: core editing, history, save, selection, and rendering.
- `-remapAssetSmoke`: real archives, GUID deduplication, target maps, previews, and cache restoration.
- `-remapEditorSmoke`: multi-selection and assemblies.
- `-remapWorkspaceSmoke`: folders and shared textures.
- `-remapLayoutSmoke`: docked layout and dialogs.
- `-remapCollisionSmoke`: precise collision behavior.
- `-remapPerfSmoke -remapPerfReal`: 3,000 cached Apex instances.

Generated executables, screenshots, reports, local tools, and game caches are not versioned.

## Contributing

Contributions are welcome. Read [`CONTRIBUTING.md`](CONTRIBUTING.md) for setup, validation, commit, and licensing expectations. Use [`SECURITY.md`](SECURITY.md) to report vulnerabilities privately.

## License

ReMap's original code and documentation are licensed under the [Mozilla Public License 2.0](LICENSE). See [`LICENSING.md`](LICENSING.md) for scope and exceptions.

RSX is a separate external program maintained in [`R5Reloaded-ReMap/rsx`](https://github.com/R5Reloaded-ReMap/rsx) under the GNU Affero General Public License v3.0. Its license and source-compliance details are available under [`ThirdParty/RSX`](ThirdParty/RSX/README.md).

This project is not affiliated with or endorsed by Electronic Arts, Respawn Entertainment, Valve, or Unity Technologies. Product names and trademarks belong to their respective owners.
