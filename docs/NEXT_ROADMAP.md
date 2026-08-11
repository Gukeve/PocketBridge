# PocketBridge next roadmap

The `next` branch is experimental. `main` and `v0.9.0-beta` remain stable and are not modified by this roadmap.

## Delivery principles

- Preserve the current PocketBridge visual language and embedded renderer.
- Keep all device operations serial-scoped.
- Prefer small services and immutable operation targets over growing `MainViewModel` into a god object.
- Build and test after each milestone. Hardware/device acceptance is reported separately from static/build proof.
- No GPL implementation is copied. New behavior is implemented independently.

## P0 — required foundation

### P0.1 Persistent device profiles

- `DeviceProfile` keyed by serial.
- Friendly name and preferred connection/display settings.
- Atomic settings persistence with migration-safe defaults.
- Profiles survive temporary disconnects; pairing codes and secrets are never stored.

Acceptance: alias and preferences round-trip through JSON; two serials never share settings.

### P0.2 Multi-device dashboard

- Single Device and Multi View modes.
- Up to four independent embedded sessions in adaptive 1/2/4 layouts.
- Double-click a tile to focus Single Device.
- Stop/disconnect affects only its serial; latest-frame-wins remains bounded per tile.

Acceptance: tests prove independent sessions; hardware check with two devices verifies display and target isolation.

### P0.3 Display preferences

- Max size: Native/1920/1600/1280/1024/custom.
- FPS: 60/45/30/24/custom.
- Bitrate: Auto/4/8/12/16 Mbps/custom.
- H.264 is the only embedded codec shown as supported until decoder negotiation exists.
- Stay awake, screen off, always on top, and audio preference stored per profile.

Acceptance: validated options generate correct external arguments and embedded server arguments.

### P0.4 Operation targeting and resilience

- File/APK/clipboard/recording jobs capture target serial at creation.
- Disconnect cancels or fails only affected work and leaves the application running.
- Clear status messages distinguish unavailable runtime, unauthorized device, disconnect, and unsupported capability.

## P1 — high-value control-center features

### P1.1 Drag-and-drop transfer queue

- APK confirmation/install.
- General files push to `/sdcard/Download/`.
- Multiple files, progress, cancellation, immutable target serial.

### P1.2 Clipboard

- Explicit copy Android → Windows and paste Windows → Android.
- Optional automatic synchronization, disabled independently.
- Loop prevention based on origin/version/hash and throttling.

### P1.3 Recording and screenshot workflow

- Use official scrcpy recording pipeline per serial.
- REC indicator and elapsed time.
- Screenshot Save/Copy/Open/Show in Explorer and configurable folder.

### P1.4 Wireless improvements

- Remember successful endpoints and reconnect preference.
- Android 11+ `adb pair` dialog with pairing code never persisted.
- USB → TCP/IP transition and explicit connection state.

### P1.5 Application manager

- User/system package lists and package metadata.
- Launch, force-stop, uninstall, update APK, copy package name.
- Confirmation for stop/uninstall/clear data; clear data is not part of the first slice.

### P1.6 Fullscreen and shortcuts

- Real embedded fullscreen with Esc restore and minimal overlay.
- Initial action model for screenshot, recording, fullscreen, and paste shortcuts.

### P1.7 Device information

- Friendly name, manufacturer, model, Android/SDK, serial, connection/IP, resolution, battery, storage, ABI.
- Compact dedicated page; unknown values remain explicit.

## P2 — additional

- Group actions with prominent target count and opt-in broadcast.
- Rotation/orientation controls and multiple Android displays.
- Transfer history and richer cancellation/retry.
- ADB console with visible fixed target and bounded history.
- Configurable shortcuts UI.
- App icons if retrieval can be bounded and cached safely.
- Audio controls and recording mux options after capability work.

## P3 — experimental

- Visual key mapping/gamepad/HID.
- Virtual displays, desktop mode, camera mirroring, OTG.
- LAN discovery and authenticated remote/web access.
- Automation/scripts only after a permission model and destructive-action policy exist.

## First implementation slice

Implemented on `next` in this stage:

- P0.1 persistent, serial-keyed profiles with aliases and atomic JSON persistence;
- P0.2 adaptive 1/2/4 Multi View over independent embedded sessions, with double-click focus;
- P0.3 validated resolution, FPS, bitrate, H.264 capability disclosure, and external-session preferences;
- the first P1.4 slice: remembered last successful endpoint and Android 11+ pairing-code flow without persisting the code.

Remaining P0/P1 work continues in later logical commits. Features requiring device capabilities are not considered hardware-accepted until the manual matrix below is executed.

## Verification matrix

| Gate | Automated/local | Hardware/manual |
|---|---|---|
| Profile persistence and serial isolation | Unit checks | Rename two real devices and restart |
| Argument generation | Unit checks | External scrcpy launch |
| Multi-session retention | Unit checks | Two-device simultaneous display/control |
| Latest-frame-wins | Existing bounded handoff plus regression checks | Observe latency/CPU/memory with 1/2/4 sessions |
| APK and files | Serial-scoping checks | Drop APK/file with two connected devices |
| Localization | Resource-key/build validation | Inspect RU/EN/zh-CN at supported window sizes |
| Release | Locked restore, Release build, test executable | Clean runtime bootstrap and application smoke test |
