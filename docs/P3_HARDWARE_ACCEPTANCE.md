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

## Reproducible completion checklist

### One Android device

1. Open Edit Controls over the embedded stream; create, move, resize, duplicate and delete point, region, swipe, joystick and free-look controls.
2. Verify keyboard, mouse and available XInput bindings execute at the same visible-video positions before and after portrait/landscape rotation and window resize.
3. Export the profile, delete it, import it and compare every normalized coordinate, radius, duration, sensitivity and dead-zone.
4. Enumerate displays; independently open/stop display 0 and a supported secondary display embedded and externally. Launch a disposable package on the selected secondary display.
5. If capability reports supported, create and remove an Android virtual display. Record API/runtime output; unsupported is not a PocketBridge failure.
6. Exercise DeviceConnected, DeviceDisconnected, SessionStarted, BatteryBelowThreshold and WifiAvailable rules; verify action-specific permission denials and audit entries.
7. Disconnect/reconnect during embedded/display/automation activity and verify cleanup without affecting unrelated sessions.

### Two Android devices

1. Assign separate aliases and mapping profiles.
2. Run simultaneous mapped inputs and display sessions; confirm every ADB/scrcpy operation remains scoped to its immutable serial.
3. Target LAN control by the server-owned alias and verify it cannot affect the other device.

### Private-LAN adversarial peer

1. Test viewer-only and explicitly permissioned controller sessions from a second PC/phone.
2. Attempt invalid, expired, revoked and wrong-session tokens; missing/unexpected Origin; touch/key/button escalation; duplicate/reordered sequence; reused nonce; stale/future timestamp; malformed/oversized JSON and rapid reconnect/flood.
3. Revoke while sockets are open and verify no subsequent command executes.
4. Confirm audit records success/failure without token, payload, clipboard/file contents or full serial.

Current completion-pass hardware status: **NOT VERIFIED**. The managed ADB currently reports one Android emulator (`emulator-5556`), which is suitable for limited smoke checks but is not physical-device, gamepad, secondary-display or private-LAN acceptance. Historical MI PLAY evidence above is not evidence for the new completion changes.

Completion-pass emulator smoke evidence: Android API 36 answered through an explicit `adb -s emulator-5556` target; `dumpsys display` reported the active built-in display 0 at 1080 × 2424 and exposed a landscape override. No mapped input, secondary display, virtual display, gamepad or LAN peer was exercised by this read-only probe.
