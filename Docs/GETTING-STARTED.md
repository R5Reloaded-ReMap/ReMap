# Getting started with ReMap

This guide follows the same five pages available from **Help > Getting started** inside the application.

## 1. Configure ReMap

Open **Settings** and select the root folder of R5Reloaded and/or R5Flowstate. ReMap detects the matching `platform` and RPAK folders automatically.

Choose an Asset Cache folder on a drive with roughly 10 GB of free space and save it. The cache contains indexes, extracted models, textures, and thumbnails. Changing the location does not move or delete the previous cache.

Save the game paths. Use the first-time setup action to prepare the selected installation when required.

## 2. Create and index a project

Use **File > New map**, choose the target game, then select the Apex map to edit. Open **Indexing**, enable any additional map archives needed by the project, and apply the changes. Common archives are included automatically.

The first indexing pass can take some time. Editing remains available while thumbnails continue in the background.

## 3. Place and edit objects

Search the model library, then double-click a card or drag it into the scene. Use **W**, **E**, and **R** for move, rotate, and scale. Press **F** to frame the selection.

The hierarchy supports renaming, grouping, ordering, disabling, and reparenting. **Ctrl+D** places a duplicate, **Escape** cancels placement, and **Ctrl+S** saves the project.

## 4. Export and test

Open the generated game code from the toolbar or File menu. Review the Scripts and ENT tabs, then use **Build and install map**. Native ENT optimization is enabled by default; unsupported objects retain their script fallback.

Automatic map restart is optional. When game communication is unavailable, launch or reload the installed map from the game.

## 5. Essential shortcuts

| Shortcut | Action |
| --- | --- |
| W / E / R | Move / rotate / scale |
| F | Frame selection |
| F2 | Rename hierarchy item |
| Ctrl+A | Select all |
| Ctrl+C / X / V | Copy / cut / paste |
| Ctrl+D | Place a duplicate |
| Ctrl+G | Group selection |
| Ctrl+Z / Ctrl+Y | Undo / redo |
| Ctrl+S | Save |
| Delete | Delete selection |
| Escape | Cancel placement or gesture |
| F11 or Alt+Enter | Toggle fullscreen |

Hold the right mouse button with WASD/ZQSD for 3D flight. Use the wheel to zoom, middle-click to focus, middle-drag to pan, and Alt + left mouse to orbit around the focus point.

## Troubleshooting

Use **Help > Open log folder** when an operation fails. **Help > Copy diagnostic information** copies the application version, target, configured paths, and log location without including the RCON key or password.
