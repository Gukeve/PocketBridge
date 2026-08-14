# P3 hardware acceptance

Date: 2026-08-14  
Device: Xiaomi MI PLAY, Android 8.1 / API 27, USB serial redacted as `••••0301`  
Runtime: official scrcpy 4.1

| Area | Status | Evidence |
|---|---|---|
| Runtime capability discovery | HARDWARE PASS | `scrcpy --list-displays` and `--list-encoders` completed for the explicit serial. |
| Displays | HARDWARE PASS / NOT SUPPORTED | Display 0 reported as 1080×2280. PocketBridge marks virtual display unavailable on API 27. |
| Encoders | HARDWARE PASS | Device reports H.264 and H.265 encoders; no AV1 encoder. |
| UHID keyboard/mouse | NOT SUPPORTED ON DEVICE | PocketBridge policy requires Android 9/API 28 or newer; Standard scrcpy control remains the fallback. |
| OTG | NOT TESTED | Runtime supports separate OTG mode, but it requires exclusive USB and disables mirroring; no user-authorized exclusive switch was performed. |
| XInput gamepad | NOT TESTED | No controller was detected/available for physical input testing. |
| Input overlay/mappings | PARTIAL | Normalized transforms and persistence are automated; live mapped input was not accepted on hardware. |
| LAN browser live view | NOT TESTED | Code path is implemented, but browser/frame/expiry testing was not performed in this run. |
| Virtual display creation | NOT SUPPORTED ON DEVICE | API 27 is below the PocketBridge capability gate. |
| Multi-device P3 isolation | NOT TESTED | Only MI PLAY was visible through the managed `tools/adb.exe`. |
| Automation execution | NOT TESTED | Store and permission policy are implemented; event-driven action execution is not enabled. |

`NOT SUPPORTED` is not a failure.

## Current checkpoint

At the latest 2026-08-14 verification, `tools/adb.exe devices -l` reported no connected Android devices. Therefore none of the newly implemented runtime mapper, LAN control/revoke, automation dispatcher, or display-scoped embedded-session changes have new hardware evidence. Earlier MI PLAY evidence above is retained as historical evidence only.

Required manual acceptance remains:

- gamepad mapping with a physical XInput controller;
- LAN view/control from another device on the same private network;
- active-session revoke and invalid token/Origin attempts;
- automation connect/disconnect/session/battery behavior;
- disconnect/reconnect cleanup and secondary-display lifecycle;
- two-device immutable-serial isolation when two Android devices are present.
