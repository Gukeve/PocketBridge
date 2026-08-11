# Contributing to PocketBridge

Thank you for contributing. By submitting a contribution, you agree that your
contribution is licensed under the Apache License 2.0 unless a file is clearly
identified as third-party material under another license. Do not submit code you
do not have the right to license.

## Development setup

1. Use Windows 10/11 x64 and the .NET SDK pinned in `global.json`.
2. Run `dotnet restore .\PocketBridge.sln --locked-mode`.
3. Build and run the executable checks shown below.
4. Run `.\prepare-runtime.ps1` only when physical-device testing is required.

Runtime binaries are downloaded artifacts and must never be committed.

## Change rules

- Scope every device-dependent ADB and scrcpy operation to the selected serial.
- Preserve simultaneous sessions for different devices and prevent duplicate
  sessions for one serial.
- Put user-facing text in the `ru-RU`, `en-US`, and `zh-CN` resources.
- Do not commit `runtime/`, `tools/`, `bin/`, `obj/`, screenshots, logs, dumps,
  local settings, IP addresses, device serials, credentials, or signing keys.
- Use pinned official Genymobile/scrcpy and Google Android download sources.
- Do not embed the scrcpy window through HWND polling or `SetParent`.
- Keep the explicit external scrcpy mode working.
- Preserve the one-frame latest-frame renderer and monotonic PTS/sequence guards.

## Third-party code

Before adding or updating a package, binary, copied algorithm, generated binding,
font, icon, or image:

1. record its exact version, canonical source, license, and copyright owner;
2. confirm redistribution compatibility;
3. add or update the exact license copy in `licenses/`;
4. update `THIRD_PARTY_NOTICES.md` and `packages.lock.json`;
5. mark copied or adapted source files and retain required notices.

Do not assume that a dependency uses the same license as scrcpy. Do not replace
LGPL, MIT, BSD, ISC, zlib, or Android package notices with Apache-2.0.

## Verification

```powershell
dotnet restore .\PocketBridge.sln --locked-mode
dotnet build .\PocketBridge.sln -c Release --no-restore
dotnet run --project .\PocketBridge.Tests\PocketBridge.Tests.csproj -c Release --no-build
dotnet publish .\PocketBridge.App\PocketBridge.App.csproj -c Release --self-contained false --no-restore
```

For device-facing changes, describe the device class and Android version but
redact the real serial number, account data, Wi-Fi address, and screenshots that
contain personal information. Clearly separate static/build evidence from
physical-device evidence.

## Security reports

Do not open a public issue for a suspected vulnerability. Follow
[SECURITY.md](SECURITY.md).
