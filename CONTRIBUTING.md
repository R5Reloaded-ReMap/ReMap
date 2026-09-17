# Contributing to ReMap

Thank you for helping improve ReMap.

## Before opening a change

- Search existing issues and pull requests to avoid duplicate work.
- Keep changes focused. Separate refactors from behavior changes when practical.
- Do not commit game files, extracted assets, caches, generated builds, credentials, or third-party code without compatible licensing information.
- Discuss large features or format changes in an issue before implementation.

## Development setup

1. Install Unity `6000.3.24f1` through Unity Hub.
2. Clone this repository and open it as an existing Unity project.
3. Open `Assets/ReMap/Workspace.unity`, or use **ReMap > Prepare and open workspace**.
4. For RSX integration work, clone [`R5Reloaded-ReMap/rsx`](https://github.com/R5Reloaded-ReMap/rsx) beside this repository as `../rsx`.

Run `./Tools/Build-ReMap.ps1` from PowerShell for a Windows build. It builds RSX in Release/x64 when the expected binary or ReMap session marker is absent, then invokes the pinned Unity editor. Use `-BuildRsx Always` for a release candidate or `-ValidateOnly` to check local prerequisites without compiling.

Unity generates IDE solution and project files locally; do not commit them.

## Validation

- Run the `ReMap.Tests` EditMode assembly in Unity Test Runner.
- Run the smoke checks relevant to the changed subsystem, as documented in the main README.
- Confirm `git diff --check` succeeds.
- Update documentation and `CHANGELOG.md` when user-visible behavior changes.

## Commits and pull requests

Use concise, imperative commit subjects. Conventional prefixes such as `feat:`, `fix:`, `docs:`, `test:`, and `chore:` are encouraged.

Pull requests should explain the problem, the chosen solution, how it was validated, and any compatibility or licensing impact.

## Contribution license

By submitting a contribution, you agree to license it under the Mozilla Public License 2.0 unless the contribution is explicitly identified as third-party material governed by another compatible license.
