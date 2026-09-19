# Editing tools

The following tools run in the standalone application without launching the game.

## Selection

- Click in the scene to select. Ctrl/Shift + click adds or removes an object.
- Drag in the scene to select the centers of active models inside a rectangle, including objects behind other geometry. Ctrl/Shift retains the previous selection.
- In the hierarchy, Ctrl + click toggles a row and Shift + click selects a range of visible rows, including rows in expanded subfolders. Ctrl + Shift also retains previously selected elements.
- Ctrl+A selects every document element. F frames the complete selection.
- A single-object outline follows the object's rotation. Multiple objects use one combined selection volume, and selected hierarchy rows are highlighted.
- When both a folder and one of its children are selected, transforms, duplication, and deletion process the branch only once.

## Transforms

- W selects translation, E rotation, and R scale.
- Drag a colored axis for one-dimensional movement. In move mode, the small XY, XZ, and YZ squares constrain movement to their two-axis plane. In scale mode, the center square changes all three axes uniformly.
- Global/Local changes the translation and rotation axes. Scale uses each selected root's local axes; offsets between objects are scaled in the active object's frame.
- Transforms remain position/rotation/scale operations: mesh shearing and negative scale are not supported.
- Center uses the combined selection bounds. Pivot uses the active object's origin.
- Snapping enables configurable steps. **Steps…** defaults to 64 Apex units, 15°, and a 0.1 scale factor. Positive custom values are session-local.
- Single-object position, rotation, and scale properties are local to the parent. Position 0 / 0 / 0 is the parent folder's origin, and moving a folder moves every descendant without changing their local coordinates. The world position shown below is read-only. With multiple objects, the inspector shows relative world movement, rotation in the selected frame, and scale factors. Those controls return to 0 / 0 / 1 after commit.
- Rendering and values update during a gesture. Releasing commits one operation; Escape or focus loss cancels a gizmo gesture.
- Arrow keys and Page Up/Page Down move the complete selection on world axes.

Ctrl+D starts placement for a duplicate of the selected branches. A full ghost follows the cursor and the document changes only after clicking a surface; Escape cancels. Delete removes selected branches, and Ctrl+G places them in a new folder. Grouping and reparenting preserve world poses within Unity TRS limitations. A rotated parent with non-uniform scale can imply shear that cannot be represented exactly as position/rotation/scale.

Dragging a hierarchy selection onto a folder moves every selected root together. Dropping it in the scene places the selection bounds on the pointed surface while retaining the parent structure. Folder cycles are rejected before mutation. The context menu and multi-object inspector can also enable or disable the selection.

Ctrl+C, Ctrl+V, and Ctrl+X copy, paste, or cut complete selected branches while preserving their internal hierarchy. Ctrl+Z, Ctrl+Y / Ctrl+Shift+Z, Ctrl+S, Ctrl+D, Ctrl+G, and Delete work throughout the application. Text controls keep their editing shortcuts. F11 or Alt+Enter toggles fullscreen. Escape cancels the model or assembly currently being placed without requiring scene focus.

Ground placement, variation, and reset tools also support multi-selection. Group a construction first when it should be repeated as one grid unit.

## Personal assemblies

1. Select models and/or folders.
2. Click **Save selection** in the library.
3. Enter a name in the dialog using ASCII letters, numbers, hyphens, or underscores.
4. Find the result under **Assemblies**.
5. Click its card and then a surface, or drag the card into the scene or a folder.

The small placement cube marks the saved bounds' base anchor; it does not preview the complete assembly geometry. Escape cancels placement.

Files are stored in `%USERPROFILE%\AppData\LocalLow\ReMap\ReMap\Assemblies`. Every insertion creates independent IDs. A duplicate name receives a suffix so the previous assembly is preserved. Instances are editable copies and do not update when another assembly is saved later.

Subfolders, poses, disabled states, and model provenance are retained. Model and texture files are not copied into assemblies. Prepared resources stay shared, and already extracted models are restored from cache. Missing models must be prepared from the game-model library. An assembly may combine several selected map archives as long as every model belongs to Common or at least one active source.

## Validation

`-remapEditorSmoke` checks Ctrl/Shift/rectangle selection, multiple transforms under a rotated and scaled parent, local and uniform gizmos, live values, undo, folders, and assemblies. Its test assemblies use a temporary QA directory separate from the personal library.

`-remapPerfSmoke -remapPerfReal` measures a scene containing 3,000 instances of four cached Apex models. It also checks individual selection and movement of all 3,000 models, undo, and hierarchy virtualization.

Custom scripted objects and game-code generation remain outside this stage.

## World player spawn

Select **World Spawn** in the hierarchy and enable **Set a custom player spawn** to create one managed `mdl/dev/mp_spawn.rmdl` marker at the scene origin. ReMap selects it immediately; move and rotate it like any other object to define the player's spawn position and facing direction. The marker is exported as `info_spawnpoint_human` in both generated `.nut` scripts and native `_spawn.ent` output. Disabling the option removes only this managed marker; advanced player spawn points added from the Custom catalog remain untouched.

## Generated code

The main Move/Rotate/Scale toolbar also exposes **Code**, which opens the generated-output window directly. Its Scripts and ENT tabs show both export representations without opening the File menu.

## Locked map reference

Select the edited Apex map in Settings, then enable **Show main BSP terrain** and/or **Show MPRT models**. The same two independent switches and a reload action are available in the Scene inspector and from the hierarchy root context menu.

The first load extracts the selected server BSP into `AssetCache/MapReferences`; later loads reuse it. A cancellable progress overlay covers BSP decoding, model extraction, and placement. The collision terrain receives colliders so normal placement can target the map surface. Terrain and MPRT instances are not added to the map document or hierarchy, cannot be selected, moved, renamed, disabled, exported, or saved, and follow the scene origin offset automatically.

The BSP server archive exposes collision geometry rather than client render-material bindings. Terrain therefore uses a stable neutral material. Static MPRT models reuse the standard RSX CAST/albedo cache and retain their model textures when available.

## Drag-and-drop and wheel speed

A model already prepared by a preview or thumbnail is dropped immediately even while RSX processes unrelated items. A model already on disk is prepared without recreating its thumbnail. Only models absent from cache wait for extraction. The release-time surface point is retained before any wait begins.

The wheel uses normalized Input System ticks: roughly 20% distance change per tick, proportional to navigation distance. Shift + wheel accelerates it. This also works while the Place tool is active. Distance remains clamped from 0.3 to 1,500 meters.

`-remapDropWheelSmoke` checks a prepared real model while another extraction is marked busy, consistent heights with Place, no unnecessary thumbnail regeneration, and virtual-wheel camera movement.

## Focus and orbit

Middle-click a surface to focus that raycast point, including the empty ground plane. The camera retains its orientation and moves to 65% of the original distance from the point. The 0.28-second eased transition continues when the cursor leaves the scene.

Hold Alt + left mouse to orbit around the focus point at constant radius. Selection and placement remain unchanged. The gesture must start in the scene but continues outside it until release. F also defines a pivot from the framed selection.

Right mouse rotates in place and enables 3D flight. Middle-button drag pans. Those controls and zoom immediately interrupt an active focus transition. Focus loss or a modal dialog cancels camera gestures.

`-remapCameraOrbitSmoke` validates an exact off-center model point, smooth animation, empty ground, orbit radius and aim, absence of accidental placement or gizmo transforms, panning, in-place rotation, and manual interruption.
