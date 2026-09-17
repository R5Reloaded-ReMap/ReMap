# Albedo textures and missing material repair

The static Unity renderer uses albedo textures. New cache processing keeps explicit CAST albedo/diffuse references, exact legacy color candidates and recognized color-layer texture names. Normal, specular, gloss and AO exports are not normalized or added to the shared cache. The original game archives and existing shared texture cache are preserved; this is not a bulk purge of earlier cached textures.

An earlier experimental native patch filtered texture export before PNG encoding. It is not selected by the current application: ReMap always uses the official `rsx.exe` bundled at the build root. The old patch is retained only as development history.

## Confirmed example

canyonland_thunderdome_ground_cap_01 (GUID 04c630f398c9d040, Desertlands) contains four materials. The first three have explicit CAST albedo links. metal_floor_holes_02 has no texture links or file child nodes in CAST, although metal_floor_holes_02_col.png already exists in the model texture manifest. The earlier renderer therefore used its neutral fallback color for the cap. This is a missing material binding, not a failed download or missing source texture.

Resolution now prefers explicit CAST links, then exact RSX-generated names for the same material inside the same model: _albedoTexture.png, _diffuseTexture.png and _col.png. Render-pass suffix handling matches RSX. It does not select arbitrary PNG files or substitute normal/specular maps. The fallback works with the existing shared texture manifest, without re-extracting this model. Missing files and missing bindings are distinguished in the preview tooltip. A corrupt albedo can leave that material unresolved while the remaining materials load.

Only thumbnails previously marked with missing albedos are queued for a one-time refresh under rendererVersion 2. They can be rebuilt from existing exports. Successfully resolved older thumbnails do not need regeneration.

Albedo alpha is preserved in PNG processing. The current simple URP material does not reproduce every Apex shader, tint, transparency, layer blend or UV behavior. An actually white albedo or an unsupported game material can still look different from the game; those cases require checking the specific material.

## Validation

AlbedoTests checks explicit binding precedence, exact fallback filenames, render-pass suffixes, color classification and resolution of the reported real cached fixture. Player flag -remapAlbedoSmoke creates an isolated copy of the reported model's 18 textures, normalizes it, requires exactly four retained albedos and four textured Unity materials, and checks that the original manifest remains unchanged. It writes albedo-repair-preview.png beside the tested executable. No game archives or old shared textures are deleted by this check.
