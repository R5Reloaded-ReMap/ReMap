# RSX integration

ReMap communicates directly with RSX as an external command-line process. RSX is not linked into the Unity player and does not pass through the launcher-console helper used for live Apex commands.

ReMap ships one minimal fork of official [reSource Xtractor](https://github.com/r-ex/rsx), maintained separately at [`R5Reloaded-ReMap/rsx`](https://github.com/R5Reloaded-ReMap/rsx). It retains the official CLI and adds the `--remap-session` protocol needed to keep one process and one archive set alive across model exports. A model is selected by its hexadecimal GUID and every output is validated as CAST before it enters the cache.

The fork source is expected beside this repository in `../rsx`. Build `rsx.sln` in Release/x64; its project writes `bin/Release/rsx.exe` and the `.remap-session-v1` compatibility marker. ReMap detects that path automatically while running in the Unity editor. The Windows build copies both files, the RSX license, and its third-party notices beside `ReMap.exe`. The distributed app always resolves that root-level binary, so no RSX path or version setting is exposed.

The currently integrated RSX revision is `c47ef0a2c2391f28ad09f33bf7a500ccfaa3dfda`. Binary releases must identify the exact RSX revision they contain and provide access to its complete corresponding source. ReMap does not duplicate the RSX source or maintain patch snapshots in this repository.

The app first attempts textured export, then retries geometry-only if extraction fails. Attempts have separate output folders and logs. A cache entry is committed only after a successful process exit, exactly one LOD0 CAST result, and structural validation.
