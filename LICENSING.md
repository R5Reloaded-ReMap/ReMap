# Licensing

Unless a file or directory states otherwise, the original source code, documentation, configuration, and assets in this repository are licensed under the [Mozilla Public License 2.0](LICENSE).

The MPL applies at the file level. Files derived from third-party projects remain under their original licenses and are not relicensed as part of ReMap.

## RSX

ReMap communicates with [RSX](https://github.com/R5Reloaded-ReMap/rsx) as a separate external process. The RSX source tree is maintained in its own repository under the GNU Affero General Public License v3.0.

This repository contains a verbatim copy of the RSX license and source-compliance information under [`ThirdParty/RSX`](ThirdParty/RSX/README.md). ReMap binary releases that include `rsx.exe` must also provide access to the complete corresponding RSX source for that executable.

## ReVPK

ReMap invokes ReVPK as a separate external process from an existing R5Reloaded or R5Flowstate installation. ReVPK comes from [R5Reloaded/r5sdk](https://github.com/R5Reloaded/r5sdk), based on [Mauler125/r5sdk](https://github.com/Mauler125/r5sdk), and is primarily authored by Kawe Mazidjatari (Mauler125).

ReMap does not bundle or relicense ReVPK. ReVPK remains governed by the Source 1 SDK license and the r5sdk third-party legal notices. If a future ReMap distribution includes ReVPK, it must also include the applicable `license/LICENSE` and `license/thirdpartylegalnotices.txt` files from r5sdk. See [`ThirdParty/ReVPK`](ThirdParty/ReVPK/README.md).

## Third-party and game material

Third-party components remain governed by their respective licenses and notices. ReMap does not grant rights to Apex Legends, Titanfall, Valve, Respawn Entertainment, Electronic Arts, or assets extracted from their products. Product names and trademarks belong to their respective owners.
