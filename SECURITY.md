# Security policy

## Supported versions

Until the first stable release, security fixes are provided only for the latest
commit on the default branch. After tagged releases begin, this table must be
updated to identify supported release lines.

## Reporting a vulnerability

Please do not disclose a vulnerability in a public issue, discussion, pull
request, log, or screenshot.

Use GitHub's **Private vulnerability reporting** or a private draft Security
Advisory for this repository. The repository owner must enable that feature
before publication. If it is unavailable, do not send device data publicly;
wait for the maintainer to publish a dedicated security contact.

Include only the minimum information needed to reproduce the issue:

- affected PocketBridge version or commit;
- Windows and Android versions;
- reproduction steps and expected impact;
- a sanitized proof of concept, if needed.

Remove credentials, ADB serials, pairing codes, IP/MAC addresses, user names,
local filesystem paths, account content, device screenshots, application data,
and signing material. Never attach an entire ADB log or debug dump without
reviewing it first.

The maintainers should acknowledge a private report within 7 days and provide a
status update within 14 days. These are targets, not a warranty or service-level
agreement.

## Scope reminders

PocketBridge invokes ADB and can install APKs, transfer files, enable ADB TCP/IP,
and send device-control input. Reports involving command scoping, path handling,
archive extraction, update integrity, or unintended access to a different device
are considered security-sensitive.

Vulnerabilities in scrcpy, FFmpeg, Android Platform Tools, .NET, or another
upstream component should also be reported through that project's security
process. Do not include confidential PocketBridge report data in an upstream
public issue.
