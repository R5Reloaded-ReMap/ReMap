# Changelog

All notable changes to ReMap will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and releases will use [Semantic Versioning](https://semver.org/spec/v2.0.0.html) once the public versioning scheme begins.

## [Unreleased]

### Added

- Discord Rich Presence with the active game target, object count, and application version.
- First-launch welcome screen with automatic detection of R5Reloaded and R5Flowstate installations.
- Configurable Asset Cache location with an explicit 10 GB storage recommendation.
- First-launch Asset Cache location confirmation before game indexing begins.
- First-launch import of beta.1 settings and its existing Asset Cache from a separate application folder.
- In-app Getting Started and keyboard/camera shortcut pages, available from the Help menu.
- Help actions to open the application log folder and copy non-sensitive diagnostic information.
- Bundled ReVPK support for native entity extraction and Windows packages.

### Changed

- Global asset-source settings now live in Unity's persistent application-data directory and migrate automatically from the previous file beside the executable.
- The general-purpose `ReMapLiveBridge` helper is now named `ReMapBridge`; future builds remove stale copies of the previous executable.
- User-facing BSP, MPRT, ReVPK, ENT, bridge, and zipline messages are localized in English and French.
- Duplicated and pasted objects now use incrementing `_01`, `_02`, ... suffixes instead of a localized "Copy" suffix.

### Fixed

- Custom Asset Cache locations now survive clean rebuilds and moving to a new application package.
- Settings writes now use an atomic temporary-file replacement to reduce the risk of a truncated configuration file.
- MPRT model visibility now starts disabled on every launch while Main BSP visibility remains persistent.
- Numeric fields in the Settings window no longer collapse vertically at compact window sizes.
