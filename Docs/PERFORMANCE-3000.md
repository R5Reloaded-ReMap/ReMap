# Performance target: 3,000 instances

The reference scene contains **3,000 placed models** grouped into 30 sectors and one global folder, for 3,031 document elements. These are instances; the number of unique models, materials, textures, and triangles also determines the real cost.

Reference goals on the test system are selection below 16 ms, common edits below 50 ms, rendering around 60 FPS, and a 95th percentile below 20 ms for the scenes described here. These are development budgets, not guarantees for every computer or map composition.

## Optimizations

- Hierarchy virtualization starts above 200 elements. Only visible rows plus an overscan margin exist in the UI—25 rows in the benchmark—while placeholders preserve scrolling.
- IDs and structure remain available for keyboard navigation, reparenting, and renaming.
- Names and paths use a shared ID lookup table. Position edits no longer rebuild the hierarchy.
- Incremental object synchronization retains unchanged parents and transforms. Only affected branches are detached before structural changes, and collision synchronization runs only after an effective edit.
- Selection bounds and gizmo drawing remain cached until the camera or objects change.
- Collision hits resolve objects through a direct lookup table.
- The saved/modified state uses the document revision and restores correctly through undo/redo instead of serializing the complete map after every edit.
- Simple transforms no longer rebuild the model catalog.
- Meshes, materials, textures, and collision data remain shared between copies.

## Measurements from September 12, 2026

Windows, Unity 6000.3.24f1, i9-14900K, RTX 4080, 1600×900 window, application limited to 60 FPS. Baseline and optimized cube measurements use the same scene. The Apex scene uses four cached references: 1,200 `death_box_01`, 1,200 `office_chair_leather`, 540 `hovership_platform_mp_body_static`, and 60 `hovership_platform_mp`, with precise collisions enabled. This represents about 22.44 million instanced triangles before off-screen surface culling.

| Operation | Cubes before | Cubes after | Apex models after |
| --- | ---: | ---: | ---: |
| Scene creation | 648.36 ms | 37.53 ms | 198.60 ms |
| Refresh | 655.97 ms | 3.27 ms | 3.59 ms |
| Selection | 615.24 ms | 1.13 ms | 1.01 ms |
| Edit | 650.25 ms | 7.02 ms | 8.25 ms |
| Median frame | 16.68 ms | 16.67 ms | 16.69 ms |
| 95th-percentile frame | 17.11 ms | 17.18 ms | 17.04 ms |
| 95th percentile with the global folder selected | 18.95 ms | 17.15 ms | 17.11 ms |

Timed scene creation includes scene objects and cached resources, but not initial indexing or RSX extraction. Operation values are medians from several repetitions. Frame values are measured after stabilization with a fixed camera over 90 frames, then 45 frames with the global folder selected.

These measurements do not establish maximum GPU throughput and are not a test of 3,000 unique models.

Measured Unity allocated memory falls from roughly 455 MiB to 142 MiB for the cube scene; the Apex scene uses roughly 154 MiB. These counters do not represent total process RAM or complete GPU memory. Temporary allocations and garbage collection affect the measurements.

## Reproduce and inspect

- `-remapPerfSmoke`: 3,000 cubes.
- `-remapPerfSmoke -remapPerfReal`: 3,000 instances of the four cached Apex models; the test performs no extraction.
- `-screen-width 1600 -screen-height 900 -screen-fullscreen 0`: reference window.

JSON reports and captures are written to `Builds/Windows/performance-optimized.*` and `performance-real.*`. `performance-baseline.json` retains the pre-optimization measurement; passing the baseline flag to the current build does not restore old code.

The test also checks access to the final hierarchy row, movement and undo of the 3,000-object folder, renaming a distant row, reparenting, hierarchy close/reopen, and focus retention. An unchanged scene must perform zero transform writes; changing one document object must update exactly one object.

Final validation included the EditMode suite, successful cube and Apex benchmarks, nested-folder regression (`REMAP_NESTED_HIERARCHY_OK`), gizmo/camera regression (`REMAP_INTERACTION_SMOKE_OK`), and asset reload (`REMAP_ASSET_SMOKE_OK`).

Logs: `Logs/remap-perf-tests.xml`, `Logs/remap-perf-baseline.log`, `Logs/remap-perf-optimized.log`, `Logs/remap-perf-real.log`, `Logs/remap-perf-hierarchy.log`, `Logs/remap-perf-interactions.log`, and `Logs/remap-perf-assets.log`.

## After multi-selection support

Windows player measurement from September 12, 2026 on Unity 6000.3.24f1, i9-14900K / RTX 4080, using 3,000 instances of four cached Apex models:

- Simultaneously selecting 3,000 models: 5.3 ms.
- First live position update of the 3,000 roots: 7.9 ms.
- Committing that edit: 10.0 ms, with undo verified.
- Median frame: 16.66 ms.
- 95th percentile with multi-selection: 17.03 ms.
- Hierarchy: 25 instantiated UI rows and four shared model resources.

These are point measurements for this scene and system, not guarantees for 3,000 different models. The movement measurement includes initial pose capture and one refresh; it does not measure a long drag sequence.

Report: `Builds/Windows/performance-real.json`. Log: `Logs/remap-editor-final-perf.log`.
