# Thumbnail loading queue

Update from September 12, 2026:

- Unity continuations, texture resizing, and thumbnail generation continue while another application has focus.
- Models currently visible in the responsive grid have priority over the rest of the compatible catalog.
- The catalog adds records progressively in batches while the user scrolls, instead of creating thousands of UI cards at once.
- Flowstate RSX accepts up to eight GUIDs from the same archive in one extraction. Models with identical filenames remain in separate batches to prevent GUID collisions.
- Each application runs only one extraction process at a time. Exported models enter Unity one by one to render their image, then release their temporary geometry while textures remain shared.
- Cards distinguish waiting, extraction, and failure. Clicking a failed card retries it. Clicking another card interrupts an unrelated background batch or waits for the requested model when that batch already contains it.
- Foreground preview requests no longer open a blocking modal. The scene remains editable during extraction; only initial archive indexing keeps a progress overlay.
- Cache state is loaded once and tracked in memory instead of probing thousands of files every 200 ms.
- Batch exports retain the GUID-based cache format and existing `complete.txt` files. When textured export fails for some members, remaining failures are retried together as geometry-only exports.
- Errors remain visible and are logged. Successful geometry/albedo states are not displayed as warnings.

Preparing thousands of assets can still take time. Pause stops the next background RSX extraction; a texture conversion already in progress is allowed to finish. A thumbnail only confirms the current static-geometry and albedo pipeline, not complete Apex material reproduction.

Validation uses `ThumbnailQueueTests` and `-remapAssetQueueSmoke` with six `holo_spray` models from a “plat” search in an isolated QA cache. The game installation is never modified.

Current build: `Builds/Windows/ReMap.exe`.

## Visible-model priority and texture paths

- A stabilized search/grid change may cancel a background RSX batch after 350 ms when none of its pending models remain relevant to the visible results.
- Canceled batches do not mark models as failed and do not trigger the geometry-only fallback.
- Visible previews whose CAST file is already cached run before new decompression.
- Exports remain serialized, and only subprocesses owned by the relevant worker may be interrupted.
- The progress counter includes elapsed batch time. Compact card labels wrap when needed.
- An indexing request made during extraction is retained and waits for the worker after the unrelated background batch is interrupted.
- Older batch folders repeated the model name and a 32-character UUID in every path. One real case produced PNG paths from 261 to 267 characters.
- Those caches are compacted before reading, with `complete.txt` and manifest updates, without re-extracting existing data.
- New exports use shorter temporary directories so PNGs are not lost before import.
- Two normalization requests for the same model are serialized to prevent one operation from deleting PNGs the other is about to read.
- Failures from a previous application session receive one fresh retry at the next launch.

Reproducible validation: `-remapThumbnailPrioritySmoke`, using the isolated cache recorded in `Logs/thumbnail-qa-root.txt`. It copies indexes and one failing model, requests indexing while the worker is busy, repairs long paths, runs two simultaneous normalizations, interrupts an unrelated Common export, and then extracts the visible Olympus platform set.

Apex materials without an exported albedo remain reported. A thumbnail is not a complete reproduction of the game shader.

Historical cold-cache results from September 12: visible priority in 0.54 s; eight thumbnails in 51.58 s; then 0.42 s to regenerate those images from cached CAST and texture files. Every model loaded at least one albedo. Remaining unresolved submaterials came from links absent from the CAST rather than from queue ordering.

Logs: `Logs/remap-thumbnail-priority-player.log`, `Logs/remap-thumbnail-cache-player.log`, and `Logs/remap-thumbnail-drop-player.log`.

## Thunderdome search and archive indexing

`thunderdome_cage_ceiling_128x128_03` has GUID `119fd55c4d9f80e7` and comes from `mp_rr_desertlands_hu.rpak` (World’s Edge).

Map toggles once changed compatibility without refreshing indexes. Closing Settings after changing archives now starts indexing automatically. The explicit Index action remains available without launching a duplicate request, and the current search is preserved.

`-remapArchiveUnionSmoke` uses an isolated QA cache, starts with Olympus only, enters the exact model name, enables World’s Edge through the real settings controls, and closes the dialog. The model then appears among 6,817 unique GUIDs. The test also verifies the thumbnail queue, extraction archive, removal of World’s Edge, and undo.

Log: `Logs/remap-thunderdome-player.log`.
