# PocketBridge agent instructions

These rules apply to the whole PocketBridge repository. Keep task-specific plans,
milestone history, acceptance matrices, and temporary TODOs in `docs/`, not here.

## Working model

- Treat the user's request as the goal. Inspect the relevant code and repository state, choose implementation details autonomously when no product decision is required, and carry the work through implementation and proportionate verification.
- Ask only for a blocking product choice, an irreversible or materially risky action, missing external access, or unavailable hardware. Continue independent work while a question is outstanding.
- Fix directly related defects that block the goal, but do not perform unrelated cleanup, redesigns, or scope expansion. Prefer existing models, services, and utilities over parallel implementations.
- Use subagents only for genuinely independent work that benefits from parallelism. The primary agent owns integration, conflict resolution, and final verification.
- Update documentation when a change makes it inaccurate. Never claim device, network, visual, accessibility, or performance acceptance without running that actual test.

## Repository and architecture

- This is the independent PocketBridge project, not an official Genymobile product. Do not modify or publish a surrounding scrcpy checkout as part of PocketBridge work.
- Preserve the solution boundaries: `PocketBridge.Core` contains models/contracts, `PocketBridge.Infrastructure` contains ADB/scrcpy/runtime implementations, `PocketBridge.App` contains WPF UI/composition, and `PocketBridge.Tests` contains the executable regression harness.
- Keep changes small and cohesive. Do not grow `MainViewModel` when a focused service or existing boundary owns the behavior.

## Device and rendering safety

- Every device-bound ADB operation must use the immutable selected serial through `adb -s SERIAL`; every scrcpy process must include `--serial=SERIAL`. Build arguments with `ProcessStartInfo.ArgumentList`; never accept a browser/client supplied serial as authoritative.
- Preserve isolation by serial and, where applicable, display ID. Starting, stopping, disconnecting, recording, transferring, automating, or controlling one target must not affect another.
- Embedded display is a native PocketBridge scrcpy-protocol client. Do not reparent `scrcpy.exe`, poll HWNDs, or use `SetParent`; keep the explicit external scrcpy fallback working.
- Preserve latest-frame-wins bounded rendering, buffer ownership, and monotonic PTS/sequence guards.
- Rendering, pointer input, runtime mappings, and Edit Controls must share the real centered `visibleVideoRect`. Coordinates remain normalized, orientation-independent, DPI-independent, aspect-ratio preserving, and letterbox aware. Keep Multi View adaptive and center each video inside its tile.

## Runtime, dependencies, and licensing

- Target .NET 8 and honor `global.json`. Use locked restore. Do not retarget to a newer major SDK merely because it is installed.
- Public source and framework-dependent artifacts do not bundle native scrcpy, scrcpy-server, Android Platform-Tools/ADB, FFmpeg, SDL, libusb, PDBs, or local runtime caches. Runtime preparation downloads pinned official archives and verifies integrity.
- Before adding or updating third-party code, packages, bindings, binaries, fonts, icons, or assets, verify the exact source/version/license/copyright and redistribution terms; update lock files, `THIRD_PARTY_NOTICES.md`, and exact files under `licenses/`. Preserve upstream headers and Apache NOTICE obligations. Never assume another component uses scrcpy's license.
- PocketBridge original code is Apache-2.0; that license does not override third-party licenses. Preserve the independent-project/non-endorsement notice.

## Localization and security

- All user-facing UI text and recovery guidance must be available in base English, `ru-RU`, and `zh-CN`; keep resource-key parity tests passing. Technical protocol identifiers may remain invariant.
- LAN control stays opt-in and local-network scoped. Preserve authenticated sessions, expiry, exact Origin validation, per-message permission checks, timestamp/nonce/sequence replay protection, immediate revoke, bounded clients/messages/queues/rates/timeouts, server-owned target resolution, and redacted audit records. Do not add cloud relay, NAT traversal, UPnP, public exposure, clipboard/file transfer, or arbitrary commands without a separately approved security design.
- Automation uses only typed allowlisted actions, is device- and permission-scoped, requires explicit authorization for destructive actions, and audits every execution/rejection. Never introduce unrestricted shell/command execution.
- Do not commit or expose tokens, credentials, pairing codes, real serials, private IP/MAC addresses, local paths/usernames, settings, logs, dumps, screenshots, signing material, or device content.

## Verification and delivery

- Scale checks to risk. A small local fix needs focused build/tests; transport, rendering/input, multi-device, runtime/update, security/LAN, dependency, packaging, or release changes require the corresponding regression, integration, security, artifact, and license checks.
- Standard automated gate:

  ```powershell
  dotnet restore .\PocketBridge.sln --locked-mode
  dotnet build .\PocketBridge.sln -c Release --no-restore
  dotnet run --project .\PocketBridge.Tests\PocketBridge.Tests.csproj -c Release --no-build
  git diff --check
  ```

- `PocketBridge.Tests` is an executable harness; its `dotnet run` PASS count is authoritative. Clearly separate automated/static results, emulator smoke evidence, physical-device acceptance, and real-network acceptance.
- Before an RC/release artifact, also scan tracked files and history as appropriate for secrets/local data/runtime binaries, verify license inventory, perform a clean first-launch/shutdown smoke, and inspect ZIP contents against the no-native-runtime policy.
- Development normally occurs on `next`; treat `main` and stable tags/releases as protected. Do not merge to `main`, move tags, publish a repository, or create/update a GitHub Release without explicit user authorization. Push only the intended development branch when delivery is requested, and verify exact-HEAD CI/artifacts for release-path changes.

Detailed architecture and acceptance evidence live in `docs/EMBEDDED_DISPLAY.md`,
`docs/P3_SECURITY_REVIEW.md`, and the `docs/*_HARDWARE_ACCEPTANCE.md` files.
