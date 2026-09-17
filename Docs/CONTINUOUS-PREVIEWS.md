# Persistent RSX previews

The bundled ReMap fork of official RSX keeps one hidden process alive for an entire thumbnail preparation run. The editor sends model requests over redirected standard input; RSX replies only after each export has finished. Unity validates and commits each model to its shared cache, normalizes shared textures, renders its thumbnail, then releases the temporary Unity model.

## Loading and memory

The session keeps Common dependencies plus one owning archive loaded. Requests from that archive reuse the loaded data. When switching archives, RSX uses its existing ClearAssetData lifecycle before loading the next set in the same process. This avoids retaining all selected maps at once; it is not a fixed RAM cap. Native RSX memory still depends on the size of Common and the current archive. Unity does not retain thousands of thumbnail models.

Visible models remain the priority. Once they are prepared, the background queue finishes the currently open archive before switching. Moving to a different library page cancels the pending batch after the active model completes, without destroying the loaded session. A completed export is cached even if the user changes pages. Pausing thumbnails releases the session once the current request finishes; resuming can start a new one. Completing the run, closing the app, changing sources or reindexing also releases it. A backend crash or request timeout can require a new process; successful cached work survives.

The session is released before index subprocesses to avoid waiting on its own named single-instance mutex. A second editor/RSX instance may still wait for the one that is already processing archives; use one editor window for generation.

## Backend

The modified native source is kept in the sibling `rsx` repository. Its changes are limited to the persistent ReMap protocol, hidden command-line execution, and generation of the compatibility marker. The upstream license and corresponding source obligations are unchanged; RSX remains an external process.

Build the Release x64 solution normally. It writes `bin/Release/rsx.exe` and `rsx.exe.remap-session-v1`; ReMap copies both beside its executable. CAST/PNG formats are unchanged, so existing exports and thumbnails remain reusable.

Protocol v1 starts with REMAP_SESSION READY 1 (tab separated). LOAD carries absolute RPAK paths, EXPORT carries a 16-digit asset GUID and an 8-digit job ID, QUIT ends the process. The native exporter creates a distinct job directory so identical model basenames cannot overwrite each other. Unity publishes complete.txt only after export acknowledgment, exact LOD0 matching and CAST validation. Errors are explicit; geometry-only fallback is attempted within the same native session when a normal export reports failure.

## Verification

The EditMode queue tests cover preferring the loaded archive and allowing visible models to override that preference. The -remapContinuousSmoke player test selects models from the active R5Reloaded or R5Flowstate index, exports several models from one archive in succession, verifies one process and one archive load, switches to a second archive in the same process, checks Unity model release, then reloads all cached exports without starting RSX. It writes per-model timings and a summary to the player log. QA exports use an isolated Logs/SQA folder.

The original timing experiment under Logs/RsxTiming-20260913-005802 was interrupted because another editor instance was running. Its load-only timing includes lock contention and must not be treated as an extraction benchmark.

## Verified on 2026-09-13

- 70 EditMode tests passed (`Logs/remap-continuous-final-tests.xml`).
- Actual player run passed (`Logs/remap-continuous-player.log`). All eight Olympus exports used PID 24412 and one archive load; a Desertlands ceiling was then exported in that same process after replacing the loaded archive set.
- After the first model, extraction, texture normalization and thumbnail rendering took 1.387, 1.247, 1.814, 2.105, 0.700, 0.474 and 0.494 seconds for the seven remaining models. These are measurements of those fixtures, not a guarantee for all models. The first model took 115.937 seconds including a confirmed wait for the old editor's RSX instance; the 124.160-second total is not a clean startup benchmark.
- All eight model texture manifests contained exported textures; temporary Unity models were released. After ending the session, cached model lookup for all eight took 0.002 seconds and started no process. That timing measures cached export lookup, not PNG thumbnail decoding.
- The validated player is published as the single `Builds/Windows/ReMap.exe` build. Existing game, model, and texture caches were not cleared during this validation.

## Immediate thumbnail display

Preview generation requests, renders, and publishes one model at a time; the next export starts after the previous thumbnail is available. The progress label names the current model. The library uses a responsive scrolling grid with progressively added records.
