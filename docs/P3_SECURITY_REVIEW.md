# P3 security review

Date: 2026-08-14  
Scope: experimental LAN view and permission-based automation on `next`.

## Trust boundaries

- ADB and scrcpy remain local, serial-scoped implementation details. Remote clients never receive ADB credentials or an ADB console.
- LAN access is disabled by default and is started only from the local UI.
- A remote session is view-only in the first milestone. Control permissions exist in the model but are not exposed until authenticated control routing is independently accepted.
- Automation rules use typed actions and risk classes. There is no arbitrary script or shell action.

## Threats and mitigations

| Threat | Current mitigation | Residual risk / gate |
|---|---|---|
| LAN attacker scans the host | Default bind is `127.0.0.1`; wildcard and public IP binds are rejected; a private interface must be entered explicitly. | Windows firewall/interface changes remain operator-controlled. |
| Stolen token | 256-bit random token, SHA-256 stored in the session object, explicit expiry and revoke. | The bootstrap URL contains the token for browser WebSocket compatibility; treat it as sensitive and avoid browser history/screen sharing. TLS is not provided on raw LAN HTTP. |
| Pairing-code guessing | Six-digit code is display/confirmation metadata only and is not accepted as the bearer credential. | Do not downgrade authentication to the pairing code. |
| Malicious browser / XSS | Embedded static client, restrictive CSP, `nosniff`, no-store; no user HTML rendering. | Re-review CSP before adding uploads, clipboard or third-party assets. |
| CSRF / WebSocket hijacking | State-changing remote endpoints are not implemented; WebSocket requires the bearer token. | Before LAN control, validate `Origin`, require per-message permission checks and add replay/nonces. |
| Replay | Token expiry and revocation bound the replay window. | Control messages need monotonic sequence/nonces before implementation. |
| Accidental public bind | `0.0.0.0`, `*`, `+` and non-private addresses are rejected. | IPv6 private-interface support is not yet enabled. |
| Automation privilege escalation | Typed actions, explicit risk, destructive opt-in plus permission required; no unrestricted scripting. | Runtime rule execution must re-evaluate policy at execution time, not only at edit time. |
| Cross-device serial confusion | Remote/audit surfaces use aliases; frame publication is tied to the emitting embedded session. Existing ADB commands retain immutable serial targeting. | Any future remote control request must resolve a server-owned opaque session id to one immutable serial. Never accept serial text from the browser. |
| Sensitive audit data | Bounded 500 records; actor, alias, action and result only. Tokens, clipboard/file contents and full serial are excluded. | Persistence is not enabled for P3 audit in this slice. |

## Security disposition

The current view-only localhost/private-LAN prototype is suitable for experimental testing, not production-grade remote access. LAN control, uploads, clipboard and browser-driven device actions remain disabled pending Origin validation, per-message authorization, replay protection and an adversarial test pass.
