# P2 hardware acceptance

Date: 2026-08-14  
Branch: `next`

## Status vocabulary

- **AUTOMATED PASS** — verified by Release build or deterministic automated checks.
- **HARDWARE PASS** — exercised on a named physical Android device with recorded evidence.
- **PARTIAL** — only part of the matrix has hardware evidence.
- **NOT TESTED** — no suitable physical device was available during this acceptance run.
- **NOT SUPPORTED** — the connected device/runtime explicitly reports that capability unavailable; this is not a failure.

## Automated acceptance

| Area | Status | Evidence |
|---|---|---|
| Application icon cache key/isolation/invalidation | AUTOMATED PASS | Cache key includes serial, package and version code; tests cover cross-serial isolation and invalidation. |
| Icon retrieval failure | AUTOMATED PASS | Retrieval returns no image and preserves the placeholder without failing the application list. |
| Async icon UI pipeline | AUTOMATED PASS | Application rows are populated before bounded background icon tasks; UI updates are dispatcher-scoped. |
| Persistent transfer history | AUTOMATED PASS | Atomic JSON persistence, 500-entry load/save bound, round-trip and corrupt-file quarantine checks. |
| Transfer filters | AUTOMATED PASS | Device text and status predicates are combined; Completed/Failed/Cancelled covered. |
| Audio capability decisions | AUTOMATED PASS | Runtime and Android API rules covered. |
| Codec availability | AUTOMATED PASS | Official scrcpy `--list-encoders` output is parsed per serial; unavailable codecs are excluded. |
| Recording validation | AUTOMATED PASS | Unsupported codec/audio/container selections are rejected before recorder launch. |

## Hardware discovery

`tools/adb.exe devices -l` returned no connected devices on 2026-08-14. Therefore no current physical-device claim can be made.

| Hardware case | Status | Reason / required evidence |
|---|---|---|
| Application icons from installed APKs | NOT TESTED | Requires a connected device with PNG and adaptive/vector icon examples. |
| Audio start/stop/repeat/disconnect/reconnect | NOT TESTED | No Android 11+ device available. |
| Embedded video independence during audio | NOT TESTED | No device available. |
| MP4 video-only / video+audio (`ffprobe`) | NOT TESTED | No media could be recorded; `ffprobe` is installed locally. |
| MKV video-only / video+audio (`ffprobe`) | NOT TESTED | No media could be recorded. |
| H.264/H.265/AV1 encoder matrix | NOT TESTED | Must use `scrcpy --list-encoders` and short recordings on the target device. |
| Native/30/Auto and 1280/30/8 Mbps | NOT TESTED | Requires negotiated dimensions, latency observation and restart persistence checks. |
| Two-device group/shortcut/audio/icon/history isolation | NOT TESTED | No devices available; requires two simultaneous devices. |

## Gate

- P2 code: **COMPLETE**.
- P2 hardware: **PARTIAL / NOT TESTED in this run**. Earlier P1 recording evidence does not prove the new audio or codec matrix.
- `NOT SUPPORTED` must be used instead of `FAIL` when a future connected device reports no audio, H.265 or AV1 encoder.
