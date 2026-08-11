# Scrcpy ecosystem reference feature audit

Snapshot: 2026-08-11. This document is a product and architecture study, not a code-origin declaration.
PocketBridge is an independent project and is not affiliated with or endorsed by Genymobile or any project listed below.

## Originality and license boundary

- No source code, artwork, icons, screenshots, branding, README text, or UI assets were copied from the reference projects.
- Publicly documented behavior, workflows, and architecture were used as product references only.
- GPL-3.0 projects are clean-room references: PocketBridge does not incorporate their implementation.
- MIT and Apache-2.0 code was not copied either. If that changes later, the exact file, revision, copyright, license, and adaptation must be recorded in `THIRD_PARTY_NOTICES.md` before merge.
- scrcpy-derived protocol code already present in PocketBridge remains separately identified in source headers and notices.

## Projects reviewed

| Project | Snapshot/release reviewed | License | Distinct ideas relevant to PocketBridge | Architecture notes | Use decision |
|---|---:|---|---|---|---|
| [NetrisTV/ws-scrcpy](https://github.com/NetrisTV/ws-scrcpy) | v0.8.1 | MIT | Browser/WebSocket access, ADB terminal, device list, multiple decoder choices | Node/TypeScript server, web client, modified scrcpy WebSocket transport | UX and transport-security reference only; its unauthenticated network design is not suitable as a PocketBridge default |
| [barry-ran/QtScrcpy](https://github.com/barry-ran/QtScrcpy) | v4.1.0 | Apache-2.0 | Multi-device sessions, group input, key mapping, recording, clipboard, drag/drop, background recording | C++/Qt, asynchronous signals, FFmpeg decode, OpenGL render | Reference for independent session ownership and serial-scoped actions; no copied code |
| [SimonAKing/scrcpy-gui](https://github.com/SimonAKing/scrcpy-gui) | 1.5.1 | GPL-3.0 | Device aliases, remembered IP, tray workflow, simultaneous devices, quality presets | Electron/Vue GUI around scrcpy | Clean-room UX reference only |
| [zwc456baby/ScrcpyForAndroid](https://github.com/zwc456baby/ScrcpyForAndroid) | v1.5.1 | GPL-3.0 | Remote Android-to-Android control, direct address entry, resolution/bitrate presets, navbar adaptation | Android Java client and embedded server fork | Clean-room behavior reference only |
| [viarotel-org/escrcpy](https://github.com/viarotel-org/escrcpy) | v3.0.8 | Apache-2.0 | Embedded mirror, visual multi-device orchestration, batch actions, draggable contextual controls, mapping and automation | Electron/Vue/TypeScript with modular device operations | Reference for feature discoverability and multi-view UX; no copied code |
| [rom1v/sndcpy](https://github.com/rom1v/sndcpy) | v1.1 | MIT | Historical Android audio forwarding and capture-policy limitations | Android capture service plus ADB/VLC forwarding | Historical reference only; current scrcpy native audio is preferred |
| [kil0bit-kb/scrcpy-gui](https://github.com/kil0bit-kb/scrcpy-gui) | v4.0.0 | MIT | Runtime discovery, persistent presets, Wireless Debugging pairing/history, nicknames, advanced scrcpy modes | Tauri/React/Rust (earlier releases used other stacks) | Reference for capability-driven settings and runtime health; no copied code |
| [srevinsaju/guiscrcpy](https://github.com/srevinsaju/guiscrcpy) | v2023.1.1, archived | GPL-3.0 | Independent control panels, one-handed controls, configuration persistence, swipe panel | Python/Qt panels around an external scrcpy process | Clean-room ergonomics reference only |
| [Miuzarte/ScrcpyForAndroid](https://github.com/Miuzarte/ScrcpyForAndroid) | v0.5.2 | Apache-2.0 | Device-bound profiles, mDNS pairing discovery, clipboard, audio, PiP, bidirectional files, streaming terminal, recording | Android/Kotlin client with native ADB and replaceable server | Reference for profile/capability model and graceful degradation; no copied code |
| [AkiChase/scrcpy-mask](https://github.com/AkiChase/scrcpy-mask) | v0.9.0 | Apache-2.0 | Visual keyboard/mouse mapping, combo mappings, scripts, external control | Rust/Bevy/React with an extended scrcpy server | P3 reference; custom server dependency is intentionally avoided for current milestones |
| [wsvn53/scrcpy-mobile](https://github.com/wsvn53/scrcpy-mobile) | v2.3 | MIT | Pairing code, clipboard, audio, navigation controls, URL presets, hardware decode, unstable-network gestures | Native mobile port with ADB key management | Reference for Wireless Debugging and reconnect UX; no copied code |

## Current PocketBridge baseline

PocketBridge already has USB ADB discovery, multiple listed devices, serial-scoped commands, TCP/IP connection, embedded H.264 display and control, multiple retained embedded sessions, external scrcpy, APK install, file browser, screenshots, runtime bootstrap, and RU/EN/zh-CN localization. The embedded renderer uses a latest-frame-wins handoff and must keep that bounded behavior.

## Feature matrix

Complexity: S (small), M (medium), L (large), XL (research/architecture). Recommended implementation always means an original PocketBridge implementation.

| Feature | Source/reference | Exists? | Useful? | Complexity | Dependencies | License boundary | Recommended implementation |
|---|---|---:|---:|---:|---|---|---|
| USB discovery and explicit serial routing | scrcpy, QtScrcpy | Yes | Required | S | adb | Existing own implementation | Preserve and test every new action for serial scoping |
| Direct ADB TCP/IP | All desktop GUIs | Yes | Required | S | adb | Behavior only | Keep validated IPv4/port flow |
| USB to TCP/IP transition | QtScrcpy | Partial | High | M | adb | Behavior only | Persist endpoint in profile after successful connection |
| Wireless Debugging pairing code | kil0bit, scrcpy-mobile, Miuzarte | No | High | M | adb pair, Android 11+ | Behavior only | Dedicated pairing dialog; never store pairing codes |
| Remembered endpoints and reconnect | SimonAKing, kil0bit | No | High | M | settings store | Clean-room | Store last successful endpoint per profile and explicit reconnect preference |
| Friendly device aliases | SimonAKing, kil0bit | No | High | S | settings store | Clean-room | `DeviceProfile` keyed by serial; never mutate ADB identity |
| Device connection history | kil0bit | No | Medium | M | settings store | Clean-room | Bounded list, no secrets/pairing codes |
| Embedded display | escrcpy, current PocketBridge | Yes | Required | L | scrcpy-server, FFmpeg | Existing attributed protocol adaptation | Preserve current transport and latest-frame-wins renderer |
| External display | scrcpy GUIs | Yes | Required | S | scrcpy.exe | Official runtime | Pass serial and profile-derived flags through argument builder |
| 1/2/4 multi-view grid | QtScrcpy, escrcpy | Backend partial | High | L | existing session manager | Clean-room UI | Bind one display control per retained session; cap at four and keep controls serial-local |
| Group actions/input broadcast | QtScrcpy, escrcpy | No | Medium | XL | input routing/safety | Clean-room | P2 opt-in mode with prominent target count; never default |
| Fullscreen embedded mode | QtScrcpy, guiscrcpy | No | High | M | WPF window state | Clean-room | F11/Ctrl+Shift+F, Esc restore, lightweight overlay |
| Always on top | scrcpy, QtScrcpy | External only | Medium | S | WPF/scrcpy | Public behavior | Store per-device preference |
| Resolution/max size | all GUIs | External partial, embedded fixed | High | M | scrcpy parameters | Public behavior | Profile presets plus validated custom value; pass into embedded server startup |
| FPS cap | scrcpy GUIs | Embedded fixed | High | M | scrcpy parameters | Public behavior | Profile preset/custom cap; lower background previews later if measured necessary |
| Bitrate | most GUIs | External partial | High | M | scrcpy parameters | Public behavior | Normalized integer bps and display presets |
| H.264/H.265/AV1 | scrcpy, kil0bit | H.264 only embedded | Medium | XL | decoder/runtime/device encoder | Public behavior | Report actual capabilities; do not expose nonfunctional codecs |
| Orientation/rotation lock | QtScrcpy, escrcpy | No | Medium | M | scrcpy controls/options | Public behavior | P2 after stable profile/settings foundation |
| Screen off/stay awake | scrcpy, QtScrcpy | External partial, embedded fixed | High | M | scrcpy options | Public behavior | Profile settings applied to both session types |
| Clipboard Android to PC | QtScrcpy, mobile ports | No | High | L | scrcpy control protocol or adb fallback | Clean-room/protocol work | Extend protocol only from official scrcpy documentation/source with attribution |
| Clipboard PC to Android | QtScrcpy, mobile ports | Text input only | High | L | scrcpy control protocol | Clean-room/protocol work | Explicit paste plus optional sync; origin token/hash prevents loops |
| APK drag/drop | QtScrcpy | Yes, APK only | High | S | adb install | Existing own implementation | Keep confirmation and selected serial |
| General file drag/drop queue | QtScrcpy, kil0bit | No | High | M | adb push | Clean-room | Queue immutable jobs containing target serial and destination |
| File browser/push/pull | Miuzarte | Yes | High | M | adb | Existing own implementation | Add progress/cancellation later without changing sandbox root |
| Application manager | escrcpy control bar | No | High | L | `cmd package`, `am`, APK metadata | Clean-room | Query packages in service; destructive actions require confirmation |
| Screenshots/save/open/copy | QtScrcpy | Save only | High | S | adb/WPF clipboard | Behavior only | Add result actions and configurable folder |
| Screen recording | scrcpy, QtScrcpy, mobile ports | No | High | M | official scrcpy.exe | Public behavior | One recording process per serial; visual timer; no duplicate embedded decode pipeline |
| Audio forwarding | scrcpy, sndcpy | External runtime only | High | L | scrcpy API 30+, audio codec | Official runtime/public behavior | Detect SDK; video must continue if audio unsupported |
| Device information | common GUIs | Partial | High | M | adb shell properties | Clean-room | Parallel, serial-scoped queries with unknown/error states |
| ADB console/history | ws-scrcpy, QtScrcpy, Miuzarte | No | Medium | M | adb | Clean-room | Advanced-only, fixed visible target, local bounded history, no auto destructive commands |
| Configurable shortcuts | QtScrcpy, scrcpy | No | Medium | M | WPF input bindings | Public behavior | Start with non-system defaults; model actions independently of keys |
| Key mapping/gamepad | QtScrcpy, scrcpy-mask | No | Medium | XL | mapping engine/HID | Clean-room | P3, separate module and file format designed in PocketBridge |
| Virtual display/camera/OTG | scrcpy, kil0bit | No | Experimental | XL | runtime/device capabilities | Public behavior | Capability-gated P3 experiments only |
| ADB-over-web remote access | ws-scrcpy | No | Low/default unsafe | XL | auth, TLS, network service | MIT project, but no code copied | Do not add until a threat model and authenticated encrypted transport exist |

## Architectural conclusions

1. Serial is the primary key. Every queued transfer, application action, console command, profile, and session must carry an immutable target serial.
2. Multi-view is a presentation over independent `IEmbeddedDisplaySession` instances, not one shared mutable target.
3. Profiles and feature services belong outside `MainWindow`; the window coordinates navigation and binding only.
4. Embedded quality settings must enter the session factory/transport as validated options rather than global mutable fields.
5. Recording should initially delegate to the official `scrcpy.exe --record` pipeline to avoid a second decoder/encoder path.
6. Capability-dependent options must be hidden or marked unavailable. Unsupported audio or codec negotiation must never fail video startup.
7. Dangerous ADB/application operations require a visible target and confirmation.

## Source handling record

No third-party source was incorporated during this audit. Therefore this audit adds no new runtime dependency, copied copyright notice, or `THIRD_PARTY_NOTICES.md` entry.
