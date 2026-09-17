# RSX integration

ReMap communicates directly with RSX as an external command-line process. RSX is not linked into the Unity player and does not pass through the launcher-console helper used for live Apex commands.

ReMap ships one minimal fork of official [reSource Xtractor](https://github.com/r-ex/rsx), maintained separately at [`R5Reloaded-ReMap/rsx`](https://github.com/R5Reloaded-ReMap/rsx). It retains the official CLI and adds the `--remap-session` protocol needed to keep one process and one archive set alive across model exports. A model is selected by its hexadecimal GUID and every output is validated as CAST before it enters the cache.

The fork source is expected beside this repository in `../rsx`. `Tools/Build-ReMap.ps1` builds `rsx.sln` in Release/x64 when needed; its project writes `bin/Release/rsx.exe`, the `.remap-session-v1` compatibility marker, and the `.remap-session-v2` batch marker. Batch-capable builds export up to eight models from one loaded archive with a bounded worker pool. An alternate checkout can be supplied with `-RsxRoot`. ReMap detects the sibling path automatically while running in the Unity editor. The Windows build copies the binary and both markers, the RSX license, and its required third-party notices beside `ReMap.exe`. The distributed app always resolves that root-level binary, so no RSX path or version setting is exposed.

The currently integrated RSX revision is `d57296448a3840120371610a63e8b7dbac436143`. Binary releases must identify the exact RSX revision they contain and provide access to its complete corresponding source. ReMap does not duplicate the RSX source or maintain patch snapshots in this repository.

The app first attempts textured export, then retries geometry-only if extraction fails. Attempts have separate output folders and logs. A cache entry is committed only after a successful process exit, exactly one LOD0 CAST result, and structural validation.
