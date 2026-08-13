# P1 Hardware Acceptance

Date: 2026-08-13
Branch: `next`
Baseline: `0a54c49d80dd0319a7091de6f20586c021f97b98`
Hardware-fix commit: `912010292bb96fc64c0e27594a9131380ff52cab`

This report separates automated checks from observations made on real Android hardware. Serial numbers, private IP addresses, screenshots, recordings, and user content are intentionally excluded.

## Test devices

| Device | Android | Connection | Notes |
| --- | --- | --- | --- |
| `DEVICE_A` | 4.4.2 / API 19 | USB | uCloudlink G2, `armeabi-v7a`, 320x480. Official scrcpy 4.1 requires API 21 or newer, so scrcpy-dependent scenarios cannot run. |
| `DEVICE_B` | 8.1.0 / API 27 | USB | Xiaomi MI PLAY, `arm64-v8a`, 1080x2280. Used for compatible scrcpy hardware scenarios. |

Only one device was visible to ADB at a time. There was no concurrent two-device test.

## Results

| Area | Device | Result | Notes |
| --- | --- | --- | --- |
| Release build | host | AUTOMATED PASS | `dotnet build PocketBridge.sln -c Release --no-restore`: 0 warnings, 0 errors. |
| Regression checks | host | AUTOMATED PASS | 24/24 checks passed, including unsupported-Android rejection and screenshot filename formatting. |
| Unsupported Android diagnostic | `DEVICE_A` | HARDWARE PASS | Connect is rejected immediately with Android API 19 and the explicit API 21 minimum; no scrcpy session is created. |
| Embedded connect and video | `DEVICE_B` | HARDWARE PASS | H.264 embedded session started and portrait frames updated continuously. Repeated starts after application restart succeeded. |
| Latest-frame-wins | `DEVICE_B` | HARDWARE PASS | Observed `Pending: 0 (max 1)` and no old-frame rollback during the sampled single-device run. |
| Touch, mouse, keyboard | `DEVICE_B` | NOT TESTED | No dedicated harmless text-input fixture was available; do not infer this from video or ADB button tests. |
| Back/Home/Recents/Volume/Power | `DEVICE_B` | NOT TESTED | Commands remain serial-scoped by automated tests, but the complete visible hardware sequence was not captured. |
| Orientation and embedded reconnect | `DEVICE_B` | NOT TESTED | Portrait was covered; landscape/orientation changes and a physical disconnect/reconnect were not executed. |
| Clipboard Off/Manual/Automatic | `DEVICE_B` | NOT TESTED | Directional ASCII/Unicode/emoji/multiline and loop-suppression scenarios require an interactive harmless Android text fixture. |
| One-file upload to Download | `DEVICE_B` | HARDWARE PASS | PocketBridge File Manager uploaded an 86-byte UTF-8 fixture to `/sdcard/Download/`. Pull-back SHA-256 matched (`FB3616EA...B543CA`). Fixture was removed locally and remotely. |
| Drag-and-drop queue matrix | `DEVICE_B` | NOT TESTED | Multiple files, APK, cancel, retry, clear-completed, progress, and custom destination were not all exercised through drag-and-drop UI. Queue/serial scoping is AUTOMATED PASS only. |
| Application Manager lists/search/copy | `DEVICE_A` | HARDWARE PASS | 62 packages loaded; user/system tabs, search filtering, and copy-package were verified. |
| Application Manager mutations | devices | NOT TESTED | Launch/stop/install/update/uninstall/clear-data were intentionally not run without a dedicated disposable test APK. No user application or data was modified. |
| Recording MP4 | `DEVICE_B` | HARDWARE PASS | Start/timer/stop succeeded. `ffprobe` exit 0: H.264, 908x1920, duration 23.78 s, 712027 bytes. |
| Recording MKV | `DEVICE_B` | HARDWARE PASS | Start/timer/stop succeeded. `ffprobe` exit 0: H.264, 908x1920, duration 36.84 s, 596709 bytes. |
| Recording orientation/disconnect | `DEVICE_B` | NOT TESTED | Orientation change and physical device removal during recording were not executed. |
| Screenshot workflow | `DEVICE_B` | HARDWARE PASS | Save, copy (1080x2280 bitmap), open, Show in Explorer, hotkey, Unicode-friendly device name, and safe filename were verified. Test PNGs were removed. |
| Fullscreen | `DEVICE_B` | HARDWARE PASS | F11 entered portrait fullscreen; Esc restored the full PocketBridge layout. |
| Fullscreen extended matrix | `DEVICE_B` | NOT TESTED | Landscape, overlay behavior, button entry, and repeated consecutive entry/exit cycles remain open. |
| Device Information | `DEVICE_A`, `DEVICE_B` | HARDWARE PASS | Manufacturer, model, Android/SDK, ABI, battery, resolution, connection, storage, and uptime matched ADB/system data. Missing IP rendered as `—`. Privacy copy hid serial/IP. |
| ADB Console commands | `DEVICE_A` | HARDWARE PASS | Model, Android release, and `shell echo PocketBridge` returned exit 0; output showed explicit `adb -s [redacted]`. |
| ADB Console utilities | `DEVICE_A`, `DEVICE_B` | HARDWARE PASS | Timestamping, copy output, clear, and cancellation of safe `shell sleep 30` were verified. Device replacement proved a stale serial failed with `device not found` and did not target the new device. |
| ADB Console Up/Down history | devices | NOT TESTED | Automation could not produce reliable Up/Down evidence in the WPF text box. |
| Modeless Close buttons | `DEVICE_A`, `DEVICE_B` | HARDWARE PASS | Explicit Close now closes ADB Console, Device Information, Media Result, Application Manager, and transfer windows opened modelessly. |
| USB to Wi-Fi | devices | NOT TESTED | Physical USB removal and the post-removal mirroring/control matrix require operator interaction. |
| Concurrent multi-device isolation | devices | NOT TESTED | Two Android devices were never simultaneously visible to ADB. |

## Single-view performance sample

`DEVICE_B`, USB, portrait, embedded H.264:

| Metric | Observed value |
| --- | ---: |
| Decoded FPS | 10.8 |
| Rendered FPS | 10.8 |
| Dropped stale frames | 0 |
| Pending frames | 0 (maximum 1) |
| Approximate render latency | 0.3 ms |
| PocketBridge CPU | 0.36% host-total sample |
| Working set | 338 MB |
| Private memory | 279.6 MB |

This is a short acceptance sample, not a benchmark. Long-run memory stability and Multi View performance are NOT TESTED.

## Defects reproduced and fixed

1. Unsupported Android produced a generic timeout. PocketBridge now checks the selected serial's API level before every scrcpy-dependent start and reports the official API 21 minimum. Retested on `DEVICE_A`.
2. Close buttons using only `IsCancel` did not close modeless windows. Explicit close handlers were added and retested.
3. Screenshot literals were interpreted as `DateTime` format characters, corrupting `PocketBridge` in the filename. Token formatting was isolated and regression-tested; the corrected filename was retested on `DEVICE_B`.
4. Recording Stop force-killed scrcpy, producing an MP4 with `moov atom not found`. Stop now requests graceful window closure and uses a bounded force-kill fallback. MP4 and MKV both passed `ffprobe` after the fix.

## Acceptance gate

- AUTOMATED PASS: build and 24/24 regression checks.
- HARDWARE PASS: the individual rows marked HARDWARE PASS above.
- NOT TESTED: all rows explicitly marked NOT TESTED above.
- Overall P1 hardware acceptance: **PARTIAL**.

P1 is not fully hardware-verified until clipboard, complete embedded input/control, drag-and-drop queue edge cases, disposable-APK lifecycle, recording disconnect/orientation, extended fullscreen, physical USB-to-Wi-Fi, concurrent multi-device isolation, and long-run performance are exercised.
