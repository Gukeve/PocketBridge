# Third-party notices

This inventory applies to PocketBridge source and the Windows x64 release
artifact. PocketBridge is an independent project and is not affiliated with,
sponsored by, or endorsed by Genymobile, Google, the FFmpeg project, or any
other third-party supplier.

The PocketBridge GitHub artifact intentionally does **not** contain the native
scrcpy or Android Platform Tools runtime. PocketBridge downloads the pinned
official archives directly when the user requests runtime installation. Anyone
who creates a different distribution that embeds those binaries must perform
their own compliance review and satisfy the obligations listed below.

## Component inventory

| Component | Version used | Source | License and copyright notice | Binary in PocketBridge ZIP? | Distribution obligations |
|---|---:|---|---|---:|---|
| PocketBridge original code | Initial public release | This repository | Apache License 2.0; Copyright 2026 PocketBridge contributors | Yes | Provide `LICENSE` and `NOTICE`; third-party code remains under its own license. |
| scrcpy-derived protocol code | Protocol 4.1 | [Genymobile/scrcpy v4.1](https://github.com/Genymobile/scrcpy/tree/v4.1) | Apache-2.0; Copyright (C) 2018 Genymobile; Copyright (C) 2018-2026 Romain Vimont | Source only | Retain the copyright, Apache-2.0 license, attribution notices, and prominent modification notices. See the three files listed below. |
| scrcpy client (`scrcpy.exe`) | 4.1 | [Official v4.1 release](https://github.com/Genymobile/scrcpy/releases/tag/v4.1), asset `scrcpy-win64-v4.1.zip`, SHA-256 `5b12172b3264b2889f4583ee64752ce832e29bc8b1089dca81093459697165db` | Apache-2.0; same Genymobile/Romain Vimont notices | No; downloaded directly | If redistributed: include Apache-2.0, retain notices, identify modifications, and separately comply with every native dependency in the archive. |
| scrcpy server (`scrcpy-server`) | 4.1 | Same official v4.1 release | Apache-2.0; same Genymobile/Romain Vimont notices | No; downloaded directly | If redistributed: include Apache-2.0 and retain notices. PocketBridge deploys the unmodified server to the selected Android device. |
| FFmpeg libraries (`avcodec-62.dll`, `avformat-62.dll`, `avutil-60.dll`, `swresample-6.dll`) | FFmpeg 8.1.2; libavcodec 62.28.102, libavformat 62.12.102, libavutil 60.26.102, libswresample 6.3.102 | [FFmpeg 8.1.2 source](https://ffmpeg.org/releases/ffmpeg-8.1.2.tar.xz); built by scrcpy v4.1 release scripts | LGPL-2.1-or-later for this upstream configuration; Copyright (C) FFmpeg project and individual contributors | No; contained in downloaded scrcpy archive | If redistributed: provide LGPL 2.1, prominent notice, allow reverse engineering/relinking for debugging modifications, keep the DLLs replaceable, and provide the corresponding complete source or a compliant written/network source offer. Do not enable GPL/nonfree options without reevaluating the resulting license. |
| dav1d, statically linked into the FFmpeg build | 1.5.3 | [VideoLAN dav1d 1.5.3](https://code.videolan.org/videolan/dav1d/-/tree/1.5.3) | BSD-2-Clause; Copyright (C) 2018-2025 VideoLAN and dav1d authors. The x86 build also contains ISC-licensed `x86inc.asm`, Copyright (C) 2005-2024 x264 project | No; embedded in downloaded FFmpeg DLLs | If the FFmpeg DLLs are redistributed, reproduce both copyright/license notices in documentation or accompanying materials. |
| zlib, statically linked into the FFmpeg build | 1.3.1 | [zlib 1.3.1](https://github.com/madler/zlib/tree/v1.3.1) | zlib License; Copyright (C) 1995-2022 Jean-loup Gailly and Mark Adler | No; embedded in downloaded FFmpeg DLLs | If redistributed, retain the zlib notice; do not misrepresent origin and mark altered source versions. |
| SDL (`SDL3.dll`) | 3.4.12 | [SDL release-3.4.12](https://github.com/libsdl-org/SDL/tree/release-3.4.12) | zlib License; Copyright (C) 1997-2026 Sam Lantinga | No; contained in downloaded scrcpy archive | If redistributed, retain the complete SDL copyright and license notice; identify altered source versions. |
| libusb (`libusb-1.0.dll`) | 1.0.30 | [libusb v1.0.30](https://github.com/libusb/libusb/tree/v1.0.30) | LGPL-2.1-or-later; copyright belongs to libusb contributors as identified in its source files | No; contained in downloaded scrcpy archive | If redistributed, include LGPL 2.1, keep the shared library replaceable, permit reverse engineering for debugging modifications, and provide corresponding source or a compliant source offer. |
| Android SDK Platform-Tools (`adb.exe`, `AdbWinApi.dll`, `AdbWinUsbApi.dll`) | 37.0.0 / ADB 1.0.41 build 37.0.0-14910828 | [Google archive](https://dl.google.com/android/repository/platform-tools_r37.0.0-win.zip), SHA-256 `4fe305812db074cea32903a489d061eb4454cbc90a49e8fea677f4b7af764918`; [ADB source](https://android.googlesource.com/platform/packages/modules/adb/) | ADB is Apache-2.0 and the binary package contains additional notices from Google, Android Open Source Project authors, and third parties | No; downloaded directly | If redistributed, ship the package's unmodified `NOTICE.txt`, preserve all copyright/proprietary notices, and comply with each license reproduced there. Android/Google trademarks are not licensed. |
| FFmpeg.AutoGen (`FFmpeg.AutoGen.dll`) | NuGet 8.0.0.1, repository commit `8003b88e6a3189bcceba6825e134ad7ed0e06928` | [NuGet package](https://www.nuget.org/packages/FFmpeg.AutoGen/8.0.0.1), [source](https://github.com/Ruslan-B/FFmpeg.AutoGen) | MIT; Copyright (c) 2025 Ruslan Balanukhin (Rationale One) | Yes | Include the copyright and MIT permission notice with copies or substantial portions. |
| .NET apphost used by `PocketBridge.exe` | .NET SDK 8.0.423 / .NET 8.0.29 runtime pack in the audited build | [.NET runtime v8.0.29](https://github.com/dotnet/runtime/tree/v8.0.29) | MIT; Copyright (c) .NET Foundation and Contributors; additional upstream notices | Yes, apphost only | Include the runtime MIT license and applicable third-party notices. The .NET 8 Desktop Runtime itself is required separately and is not included in the framework-dependent ZIP. |

## scrcpy-derived source files

The following PocketBridge files are original C# implementations whose wire
formats, protocol constants, server launch sequence, socket handshake, and
codec-configuration packet handling were adapted from scrcpy 4.1:

- `PocketBridge.Infrastructure/Embedded/ScrcpyProtocolV41.cs`
- `PocketBridge.Infrastructure/Embedded/ScrcpyTransport.cs`
- `PocketBridge.Infrastructure/Embedded/EmbeddedScrcpySession.cs`

They carry explicit modification and copyright headers. No C, Java, Android
resource, or UI source file from scrcpy is compiled into PocketBridge.

## Decoder configuration

PocketBridge calls FFmpeg/libavcodec through FFmpeg.AutoGen. The official
scrcpy 4.1 Windows build script configures shared FFmpeg libraries without
`--enable-gpl` or `--enable-nonfree`, so the audited binaries are treated as
LGPL-2.1-or-later. The build also enables dav1d and zlib; their notices must be
kept when those FFmpeg DLLs are redistributed.

PocketBridge does not modify these native binaries. The native DLLs are loaded
from `runtime/scrcpy` after the user-approved automatic download and remain
separate replaceable files.

## License files

- `LICENSE` — Apache-2.0 for original PocketBridge code.
- `NOTICE` — PocketBridge and scrcpy-derived source attribution.
- `licenses/scrcpy-Apache-2.0.txt`
- `licenses/FFmpeg-LGPL-2.1.txt`
- `licenses/FFmpeg.AutoGen-MIT.txt`
- `licenses/SDL-zlib.txt`
- `licenses/libusb-LGPL-2.1.txt`
- `licenses/dav1d-BSD-2-Clause.txt`
- `licenses/dav1d-x86inc-ISC.txt`
- `licenses/zlib.txt`
- `licenses/Android-Platform-Tools-37.0.0-NOTICE.txt`
- `licenses/dotnet-runtime-MIT.txt`
- `licenses/dotnet-runtime-ThirdPartyNotices.txt`

This file is an engineering compliance inventory, not legal advice. A release
publisher remains responsible for confirming that the actual ZIP exactly
matches this inventory.
