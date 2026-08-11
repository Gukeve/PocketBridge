# PocketBridge

[Русский](README.ru.md) · [简体中文](README.zh-CN.md)

PocketBridge is an independent open-source Windows application for discovering,
controlling, and managing Android devices over ADB. It interoperates with the
official scrcpy runtime but is **not affiliated with, sponsored by, or endorsed
by Genymobile**. scrcpy and its trademarks belong to their respective owners.

## Features

- USB and classic ADB-over-TCP/IP discovery in a serial-scoped device list.
- Embedded H.264 display using the scrcpy-server 4.1 protocol, FFmpeg/libavcodec,
  a low-latency WPF renderer, and mouse/keyboard control.
- Multiple independent embedded sessions for different device serials.
- Explicit fallback command that opens the official scrcpy client window.
- Back, Home, Recents, volume, power, screenshot, and confirmed restart actions.
- APK installation and a shared-storage file manager rooted at
  `/storage/emulated/0`.
- English, Russian, and Simplified Chinese UI resources.
- Automatic runtime preparation; users never select `adb.exe` or `scrcpy.exe`
  manually.

PocketBridge never selects the first detected phone automatically. Device-bound
ADB and scrcpy commands always include the selected serial number.

## Requirements

- Windows 10 or Windows 11 x64.
- [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0).
- An Android device with authorized USB debugging, or an already authorized ADB
  TCP/IP endpoint.

## Runtime installation

The public PocketBridge ZIP does not redistribute native third-party runtime
binaries. On first setup, PocketBridge downloads these pinned official archives
directly and verifies their published SHA-256 values:

- scrcpy Windows x64 4.1 from `Genymobile/scrcpy` GitHub Releases;
- Android SDK Platform-Tools 37.0.0 from `dl.google.com`.

They are stored separately under:

```text
runtime/
├── scrcpy/
└── platform-tools/
```

Developers may perform the same preparation with:

```powershell
.\prepare-runtime.ps1
```

The legacy `tools/` layout remains a compatibility fallback and is not included
in public source or release artifacts. See [third-party notices](THIRD_PARTY_NOTICES.md)
before creating any redistribution that embeds runtime binaries.

## Basic use

1. Enable Developer options and USB debugging on Android.
2. Connect the device and accept Android's authorization prompt.
3. Start PocketBridge, install the managed runtime when prompted, and select the
   intended device.
4. Choose **Connect** for embedded display or **Open in a separate window** for
   the official scrcpy client.

For ADB over Wi-Fi, first authorize the device by USB, then use **Wi-Fi → Enable
and connect**. PocketBridge runs `adb -s SERIAL tcpip 5555` and connects only to
the selected device's reported Wi-Fi address.

## Build and checks

The repository pins .NET SDK 8.0.423 in `global.json`.

```powershell
dotnet restore .\PocketBridge.sln --locked-mode
dotnet build .\PocketBridge.sln -c Release --no-restore
dotnet run --project .\PocketBridge.Tests\PocketBridge.Tests.csproj -c Release --no-build
dotnet publish .\PocketBridge.App\PocketBridge.App.csproj -c Release --self-contained false
```

The GitHub Actions workflow builds and tests Windows x64, then uploads a ZIP as
a workflow artifact. It does not create a GitHub Release and does not publish
the repository.

## Project layout

```text
PocketBridge.App/             WPF UI and localization
PocketBridge.Core/            models and service contracts
PocketBridge.Infrastructure/  ADB, scrcpy protocol, runtime and file services
PocketBridge.Tests/           dependency-free executable checks
docs/                         technical documentation
licenses/                     exact third-party license and notice copies
```

The surrounding scrcpy checkout used during early development is not part of
the PocketBridge repository. No scrcpy C/Java source tree should be committed
when publishing PocketBridge.

## Security and privacy

Do not post ADB serials, device screenshots, logs, runtime manifests, IP
addresses, signing keys, or local settings in issues. See [SECURITY.md](SECURITY.md)
for private vulnerability reporting guidance.

## License

Original PocketBridge code is licensed under the [Apache License 2.0](LICENSE).
Some protocol code is adapted from scrcpy under Apache-2.0 and carries the
original copyright notices. All other third-party components remain under their
own licenses; see [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md) and
[`licenses/`](licenses/).

Contributions are described in [CONTRIBUTING.md](CONTRIBUTING.md).
