# Model orientation corrections

ReMap reads visual orientation exceptions from
`Assets/StreamingAssets/ModelOrientationCorrections.json`. The same file is shipped next to the
Windows application in `ReMap_Data/StreamingAssets/ModelOrientationCorrections.json`, where it can
be edited without rebuilding the application.

Each entry identifies a model and supplies an Apex `pitch`, `yaw`, and `roll` correction:

```json
{
  "corrections": [
    {
      "model": "charge_pylon_01_cells",
      "pitch": 0.0,
      "yaw": 0.0,
      "roll": -90.0
    }
  ]
}
```

`model` can be a bare model name or a full `.rmdl` path. Matching ignores case, directories, the
file extension, and the `_LOD0` suffix used by exported `.cast` files. ReMap detects file changes
when it next loads a model. Models that are already present in the scene must be reloaded before a
new correction becomes visible. Invalid entries are ignored; an invalid file disables the visual
corrections until it is fixed.

These corrections affect only the mesh displayed in ReMap. Object angles saved in a project and
exported to Apex are not modified.
