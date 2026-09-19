# Credits and third-party notices

## ReMap

ReMap continues the lineage of the original editor created by:

- Zee — [@AyeZeeBB on X](https://x.com/AyeZeeBB), `zee_x64` on Discord;
- Julefox — [@Julefox_ on X](https://x.com/Julefox_), `julefox` on Discord.

This standalone ReMap rewrite is created and maintained by Julefox.

Made with love for the Apex modding community. ❤️

## Apex modding foundation

ReMap would not exist without the foundational Apex modding work created by [Mauler125](https://github.com/Mauler125), notably [r5sdk](https://github.com/Mauler125/r5sdk), and the wider community built around it.

This acknowledgement is not a grant of rights to Valve, Respawn Entertainment, Electronic Arts, or third-party material. If a distribution copies or includes material from another project, that project's own license and notice files must be retained and followed.

The Mauler125 r5sdk repository includes Valve's Source 1 SDK license under `license/LICENSE`, along with third-party legal notices. Those terms are distinct from a permissive license for ReMap's original code.

## ReVPK

ReMap invokes ReVPK from an existing R5Reloaded or R5Flowstate installation to extract the original map ENT lumps. ReVPK is part of [R5Reloaded/r5sdk](https://github.com/R5Reloaded/r5sdk), based on [Mauler125/r5sdk](https://github.com/Mauler125/r5sdk), and was primarily authored by Kawe Mazidjatari (Mauler125). Later contributions include fixes by O-Robotic.

ReVPK runs as a separate external process and is not bundled or relicensed by ReMap. Its source is governed by the Source 1 SDK license and the r5sdk third-party legal notices. See [`ThirdParty/ReVPK`](ThirdParty/ReVPK/README.md).

## RSX

Windows builds of ReMap bundle a modified RSX executable as an external process. RSX is licensed under the GNU Affero General Public License, version 3. Its license, notices, and the corresponding source for the distributed build must remain available with a release.

- ReMap RSX source: https://github.com/R5Reloaded-ReMap/rsx
- Current integrated revision: `c47ef0a2c2391f28ad09f33bf7a500ccfaa3dfda`
- Original upstream project: https://github.com/r-ex/rsx
- Local license and source-compliance details: [`ThirdParty/RSX`](ThirdParty/RSX/README.md)

## Project license

ReMap's original code and documentation are licensed under the Mozilla Public License 2.0. See [`LICENSE`](LICENSE) and [`LICENSING.md`](LICENSING.md).

The MPL does not replace the separate licenses and notices that apply to bundled third-party components.
