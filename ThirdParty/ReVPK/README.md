# ReVPK

ReMap invokes ReVPK as a separate external process to extract the five original map ENT lumps from the server VPK selected through an R5Reloaded or R5Flowstate installation. ReMap does not modify the source VPK.

ReVPK is part of [R5Reloaded/r5sdk](https://github.com/R5Reloaded/r5sdk), which is based on [Mauler125/r5sdk](https://github.com/Mauler125/r5sdk). The ReVPK implementation was primarily authored by Kawe Mazidjatari (Mauler125); later contributions include fixes by O-Robotic.

Windows builds bundle `revpk.exe` at the application root, beside `ReMap.exe`. ReVPK remains governed by the Source 1 SDK license in [`license/LICENSE`](https://github.com/R5Reloaded/r5sdk/blob/p4sync/license/LICENSE) and the r5sdk [`license/thirdpartylegalnotices.txt`](https://github.com/R5Reloaded/r5sdk/blob/p4sync/license/thirdpartylegalnotices.txt).

The matching files are distributed as `REVPK-LICENSE.txt` and `REVPK-THIRD-PARTY-NOTICES.txt`. ReMap invokes the executable as a separate process and does not relicense it.
