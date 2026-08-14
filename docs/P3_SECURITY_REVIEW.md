# P3 security review

Date: 2026-08-14  
Scope: experimental LAN view and permission-based automation on `next`.

## Trust boundaries

- ADB and scrcpy remain local, serial-scoped implementation details. Remote clients never receive ADB credentials or an ADB console.
- LAN access is disabled by default and is started only from the local UI.
- LAN control is opt-in and permission-scoped. View-only remains the default permission set.
- Automation rules use typed actions and risk classes. There is no arbitrary script or shell action.

## Threats and mitigations

| Threat | Current mitigation | Residual risk / gate |
|---|---|---|
| LAN attacker scans the host | Default bind is `127.0.0.1`; wildcard and public IP binds are rejected; a private interface must be entered explicitly. | Windows firewall/interface changes remain operator-controlled. |
| Stolen token | 256-bit random token, SHA-256 stored in the session object, explicit expiry and revoke. | The bootstrap URL contains the token for browser WebSocket compatibility; treat it as sensitive and avoid browser history/screen sharing. TLS is not provided on raw LAN HTTP. |
| Pairing-code guessing | Six-digit code is display/confirmation metadata only and is not accepted as the bearer credential. | Do not downgrade authentication to the pairing code. |
| Malicious browser / XSS | Embedded static client, restrictive CSP, `nosniff`, no-store; no user HTML rendering. | Re-review CSP before adding uploads, clipboard or third-party assets. |
| CSRF / WebSocket hijacking | Exact configured `Origin`, active bearer session and endpoint permission are checked before upgrade and again for each control message. | Adversarial browser testing remains required. |
| Replay | Every control message carries a strictly increasing positive sequence, unique bounded nonce and timestamp within ±30 seconds. Duplicate, reordered, stale and future messages are rejected. | Real-browser/network concurrency remains unverified. |
| Accidental public bind | `0.0.0.0`, `*`, `+` and non-private addresses are rejected. | IPv6 private-interface support is not yet enabled. |
| Automation privilege escalation | Typed actions, explicit risk, destructive opt-in plus permission required; no unrestricted scripting. | Runtime rule execution must re-evaluate policy at execution time, not only at edit time. |
| Cross-device serial confusion | Remote/audit surfaces use aliases; frame publication is tied to the emitting embedded session. Existing ADB commands retain immutable serial targeting. | Any future remote control request must resolve a server-owned opaque session id to one immutable serial. Never accept serial text from the browser. |
| Resource exhaustion | 4096-byte control messages, one complete JSON message, 8 combined clients, 30 control requests/second/session, 60-second control idle timeout, 16 frame aliases and 128 remembered nonces/session. | Real flood testing remains unverified. |
| Sensitive audit data | Atomically persisted, bounded 500 records; actor, sanitized alias, category, severity, action and outcome only. Tokens, clipboard/file contents, arbitrary bodies and full serial are excluded. Corrupt files are quarantined; export uses the sanitized records. | Operator must still protect the local profile directory. |

## Security disposition

The code enforces active-session, expiry, token/session binding, exact WebSocket Origin, action-specific permission, timestamp, nonce, sequence and rate checks for every typed control message. Revoke aborts already-open view/control sockets. Static adversarial checks pass. Security status is **CODE COMPLETE / HARDWARE-NETWORK NOT VERIFIED** until testing from a second LAN peer covers active revoke, invalid credentials/Origin, malformed/flood traffic and reconnect races. Uploads, remote clipboard, arbitrary commands, cloud relay and browser-provided serials remain unsupported.
