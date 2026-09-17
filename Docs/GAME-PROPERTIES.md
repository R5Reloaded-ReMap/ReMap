# Game property reference

ReMap exports stock models as `prop_dynamic` entities and exposes the classic ReMap creation settings separately from the revised Options list:

- **Allow mantle** (`true` by default) calls `AllowMantle()` after creation;
- **Fade distance** (`50000` Apex units by default) is passed to `CreatePropDynamic` and stored in `kv.fadedist`; `-1` disables distance fading;
- **Realm ID** (`-1` by default) moves the prop into the selected realm when the value is non-negative;
- **Client-side prop** (`false` by default) exports the model through `CreateClientSidePropDynamic` inside `Cl_ReMap_LoadMap()` in `cl_remap_map.nut` on both R5Reloaded and R5Flowstate;
- model, origin, angles, and uniform model scale are always exported.

Server-side props also receive these bridge defaults:

- `SOLID_VPHYSICS` (`kv.solid = 6`);
- normal rendering (`kv.rendermode = 0`, `kv.renderamt = 1`);
- the `remap_prop` script name;

Client-side props preserve model scale and apply their `.kv`, `.e`, `.s`, or unscoped Options after creation. Mantle, fade distance, and realm assignment are server-only and are not applied to them. Client `.e` assignments target `ClientEntityStruct`, so the selected field must exist there; the server and client `.e` schemas are not interchangeable.

For that reason, client-side props do not show the curated server `.e` presets. The `.e` family remains available through **Manual field...** for users who know the matching `ClientEntityStruct` field.

No additional property is required for an ordinary exported model. Properties in the inspector are optional behavior or rendering overrides.

## Supported scopes

| Scope | Meaning | Assignment | Use in ReMap |
| --- | --- | --- | --- |
| `.kv` | Engine entity keyvalues | `entity.kv.name = value` | Rendering and engine behavior supported by `prop_dynamic` |
| `.e` | Typed `ServerEntityStruct` fields | `entity.e.name = value` | Server gameplay flags whose field and value type already exist |
| `.s` | Dynamic Squirrel table | `entity.s.name <- value` | Custom metadata consumed by another script |

`.p`, `.w`, `.ai`, `.proj`, and `.wp` belong to players, weapons, NPCs, projectiles, and other specialized entity types. They are deliberately unavailable for `prop_dynamic` objects.

## Property selectors

The inspector first selects a property family (`.kv`, `.e`, or `.s`), then offers only fields compatible with that family. Existing properties use the same two selectors.

- `.kv.solid`, `.kv.fadedist`, `.kv.disableshadows`, `.kv.rendercolor`, `.kv.renderamt`, and `.kv.rendermode`;
- `.e.preventStickyEnts`, `.e.destroyOutOfBounds`, `.e.canBurn`, `.e.blocksThermite`, `.e.ignoreJumpPad`, and `.e.ignorePingTrace`;
- a neutral `.s.remapValue` entry for custom script data.

Choose **Manual field...** to enter a field name that is not in the curated list. Values remain editable raw Squirrel expressions: `6`, `true`, `<255, 120, 40>`, `eSomeEntity`, or `"text"`.

## Important limitations

`.e` is strongly typed. A field must exist in the current `ServerEntityStruct`, and its value must match the declared type. `.s` is the correct scope for new custom names, but custom data has no effect until another game script reads it.

Some `.kv` fields are read only when the entity spawns. `solid` is available because it is a valid `.kv` field and is useful for explicit overrides, although the normal ReMap bridge already creates props with `SOLID_VPHYSICS`. `spawnflags`, `SpawnAsPhysicsMover`, `CollisionGroup`, `contents`, and entity-class-specific rope or trigger keyvalues should become explicit ReMap creation options if support is added later.

Methods such as `SetSkin`, `SetTeam`, `Hide`, `NotSolid`, `DisableGrappleAttachment`, and `DisallowObjectPlacement` are calls rather than `.x` properties. They need dedicated export options instead of being entered as property names. `AllowMantle` and realm assignment already have dedicated classic prop settings.
