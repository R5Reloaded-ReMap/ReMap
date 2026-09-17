# Apex coordinates and scale

The interface uses Source/Apex units: X and Y are horizontal and Z is vertical. The import factor is centralized: 1 unit equals 0.0254 Unity rendering meters. Models retain their original size and pivot; no arbitrary filename-based normalization is applied.

## Calibration against real vertices

The following three models were extracted from `mp_rr_desertlands_hu.rpak`, measured from the LOD0 CAST data, and loaded into the editor engine.

| Model | Measured X / Y / Z dimensions in Apex units |
| --- | --- |
| thunderdome_cage_ceiling_256x256_02 | 255.999 / 255.999 / 16 |
| thunderdome_cage_ceiling_256x128_02 | 127.999 / 255.999 / 16 |
| thunderdome_cage_ceiling_128x128_03 | 255.999 / 255.999 / 16.751 |

A nominal size of 256 units corresponds to 6.5024 meters in Unity. The final thousandths come from the exported vertices. Despite its name, `128x128_03` is close to 256 × 256; filenames alone cannot determine scale.

## Interface and saved files

- Local positions, read-only world positions, relative movement, dimensions, measurements, margins, and spacing use Apex units.
- Local angles use P for pitch, Y for yaw, and R for roll. Composition is Source `Rz(yaw) × Ry(pitch) × Rx(roll)`, followed by the basis change to Unity. Vertical grid and variation rotations use the Apex Z axis.
- Scale values are unitless factors on the Apex X/Y/Z axes. Gizmo colors follow the same axes: vertical Z is blue and horizontal Y is green. The XYZ link preserves model proportions and is enabled by default.
- Apex `prop_dynamic` scaling changes the rendered model but not its in-game hitbox: collision remains at scale 1:1. Scale mode displays this warning explicitly.
- Grid and snapping default to 64 units, so a 256-unit tile spans four cells. The step setting updates the rendered grid and is shared by gizmos, placement, and drag-and-drop.
- Existing save files keep the `unity-y-up-meters` storage schema. Conversion happens at the UI boundary, preserving positions, dimensions, and scale factors. A future `.nut/.gnut` exporter can reuse the centralized conversion.

## World limits

`MaxWorldCoord = (1 << 16) - 1 = 65,535` and `CoordRange = 131,071`. Every object or folder pivot must remain within `[-65,535, +65,535]` on each world axis, equivalent to ±1,664.589 rendering meters. This does not constrain every mesh vertex or the camera.

Transactional validation composes every parent transform, including rotation and non-uniform scale. It covers placement, copies, grids, assemblies, reparenting, scene loading, and editing. Move, rotate, and scale previews reject the complete proposed operation when any descendant would leave the world. Disabled objects are validated as well. Rejected edits do not enter history.

## Validation

The EditMode suite covers position and angle conversions, all six inclusive limits, one-unit overflow, transactional rollback, rotated and scaled hierarchies, comparison with Unity matrices, and existing save files.

`-remapApexCoordinatesSmoke` measures the three real CAST files, edits the actual inspector controls, checks 65,535 versus 65,536, exercises group previews, and validates the 64-unit grid. Validation of 3,000 objects takes approximately 5 ms on the reference system.

Logs: `Logs/remap-apex-tests.xml` and `Logs/remap-apex-player.log`. Detailed measurements: `Logs/ScaleQA/measurements.json`.
