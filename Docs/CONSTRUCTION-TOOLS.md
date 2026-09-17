# Construction tools port

The behavior of the original ReMap tools has been reimplemented for the standalone player without any UnityEditor dependency.

| Original tool | Standalone command |
| --- | --- |
| DropToGroundTool | Drop onto the Y=0 plane using the bounding-box base and a margin |
| DropToFirstGroundTool | Find supports below the selection, exclude the moved objects, and leave them unchanged when no support exists |
| RandomYRotation | Random vertical rotation within a range |
| RandomScale | Random uniform scale factor within a range |
| GridTool | Grid or wall following the source object's local axes, with automatic dimensions, folders, and complete sub-branches |
| SerializeMode | Six-direction duplication using local/world axes, edge-to-edge spacing, or a fixed step |
| ModelDistance | Live distance between two pivots |
| PropInfo | Model, world pose, dimensions, and text copying without code generation |
| QuickMenu | Draggable floating tool window opened from the top menu |

The old name “Serialize Mode” referred to directional duplication; that behavior is retained. The floating window shows one tool selected from its dropdown. Modes that operated on several independently selected objects are now supported directly by multi-selection. Selecting a folder remains useful when an entire construction should be processed as one branch.

Importers, code generators, data tables, `.ent` scripts, and game exports are outside this port. Realm IDs and gameplay components such as doors, buttons, ziplines, sounds, triggers, and loot require a future game-integration data model and are not presented as functional in this offline editor. Unity Editor-only startup, serialization, prefab sorting, theme, and plugin tools are not copied into the player.

## Guarantees and limitations

- Commands prepare and validate a document copy before creating one undo transaction. Errors and ground operations without a valid result do not create empty history entries.
- World offsets are converted into the parent coordinate system, including rotated and scaled parents.
- Copies retain model and map references, disabled states, and complete subfolder structures while receiving new IDs. Rendering resources remain shared.
- Dimensions are bounding volumes projected onto the duplication axis.
- Hovering directional-copy buttons or the grid creation button renders non-interactive green holograms at every future placement. Leaving the button clears the preview.
- Ground placement uses nine vertical probes below the volume and concave MeshColliders that follow loaded Apex visual geometry. Geometry holes remain open; holes painted only in textures do not.
- Ground placement is not a physics simulation and does not import official Apex terrain collision.
- Construction tools do not modify game files. Game-code export remains a separate explicit File-menu action.

## Validation

`ConstructionTests` covers copied references and descendants, recursive targets, pre-mutation limits, saving, and undo/redo.

`-remapToolsSmoke` exercises the player commands with a rotated and scaled parent: placement on an active support, rejection of a hidden support, missing ground, folder placement, recursive processing without self-collision, variations, directional copies, subfolder grids, undo/redo, and live measurement.

Capture: `Builds/Windows/construction-tools-preview.png`.

Validated with Unity 6000.3.24f1 through the EditMode suite, successful Windows compilation, `REMAP_CONSTRUCTION_TOOLS_OK` at 1600×900 and 900×900, and the `REMAP_DOCK_LAYOUT_OK` panel regression check.

Logs: `Logs/remap-tools-tests.xml`, `Logs/remap-tools-build.log`, `Logs/remap-tools-player.log`, `Logs/remap-tools-compact.log`, and `Logs/remap-tools-layout-regression.log`.
