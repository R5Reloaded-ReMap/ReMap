# Release workflow

Start the interactive workflow by double-clicking `Build-ReMap.cmd`. Packaging entries appear when the maintainer-only local packager is installed. Local builds and packages are written only to ignored `Builds/` and `Release/` directories.

## Build and package modes

| Mode | Binary | Version in package | Git tag |
| --- | --- | --- | --- |
| Local build | Optimized | Current project version | None |
| Personal/test package | Optimized | `0.1.0-dev.<UTC timestamp>` | Never |
| Beta package | Optimized | Next `0.1.0-beta.N` | Optional, default No |
| Release candidate package | Optimized | Next `0.1.0-rc.N` | Optional, default No |
| Public release package | Optimized | Selected semantic version | `v0.1.0`, default Yes |

Beta and release-candidate numbers are derived independently from existing local tags. For example, `v0.1.0-beta.2` makes the next beta `0.1.0-beta.3`, while `v0.1.0-rc.1` makes the next release candidate `0.1.0-rc.2`.

Final-release version choices follow semantic versioning. Patch changes `0.1.0` to `0.1.1`; minor changes it to `0.2.0`; major changes it to `1.0.0`. A version change is committed locally before the build. Keeping the current version creates no version commit.

## Tags and publication

Tags are annotated and created only after the ZIP has been built successfully. The workflow never moves an existing tag and never pushes a commit, tag, package, or GitHub Release. Review the resulting ZIP and local tag before publishing them yourself.

Beta and release-candidate tags identify testable prereleases, not every private build. Personal/test packages therefore stay untagged. The final stable release drops the prerelease suffix: `v0.1.0-rc.2` can be followed by `v0.1.0`.

## Package contents

Every distributable ZIP contains:

- the complete Windows application, including the matching `rsx.exe`;
- the exact tracked ReMap source snapshot used for the build;
- the exact tracked RSX source snapshot used for the build;
- `RELEASE-MANIFEST.json` with versions, repositories, commits, optional tags, build channel, and binary SHA-256 hashes;
- `SOURCE-CODE.md` with the corresponding build commands and license information.

Packaging requires both the ReMap and sibling RSX repositories to be clean, so the recorded commits and bundled source snapshots remain reproducible.
