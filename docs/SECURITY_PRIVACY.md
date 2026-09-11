# Security & Privacy Rules

## Privacy boundary
Collect only what is necessary to enforce rules and show parent status.

Allowed examples:
- time used today;
- app rule hit / app blocked;
- domain blocked/allowed decision where required;
- request submitted by child;
- command execution status.

Do not collect:
- keystrokes;
- passwords;
- message content;
- screenshots;
- microphone/camera recordings.

## Parent authentication
- First setup requires Windows administrator approval.
- Parent PIN/password hashes use modern password hashing; never plaintext.
- Sensitive policy changes require parent authorization.

## Child account model
Recommend a dedicated Standard User child account.
Do not promise resistance against a child who has local administrator rights.

## Remote security
- Pairing code short-lived.
- Per-device long-lived credentials generated after pairing.
- TLS transport.
- Nonce/timestamp/command id to prevent replay.
- Commands idempotent where practical.
- Device can revoke remote pairing locally with parent authorization.

## Logging
Operational logs must be bounded/rotated and avoid personal content.

## Child warning pipe

The local-only warning pipe allows only LocalSystem (the service) and the managed SessionAgent user SID. Session/profile values inside each JSON warning are validated as an additional boundary; no browsing or application content is sent. The automated ACL test verifies the descriptor and managed-session server creation; a LocalSystem-to-child-user handshake is not impersonated in-process.
