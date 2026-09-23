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
- Pairing code is 80-bit random, valid for five minutes, single-use, and persisted only as SHA-256 hash.
- Per-device bearer credentials are randomly generated and protected by Windows DPAPI for the current Service account; the provider stores only a hash.
- Supabase Edge Functions are the only table access path; the parent gateway requires a Supabase-authenticated owner. The device gateway authenticates device ID + credential hash for poll/status/ack and uses a server-only service-role key.
- Device commands include command/device UUID, nonce, UTC issued/timestamp/expiry, and acknowledgement. SQLite enforces unique command IDs and device nonces before execution; maximum TTL is 15 minutes.
- `UNLOCK` removes Parent Lock only. Schedule/quota remain in force; Emergency Override is a separate local action. Remote SyncPolicy cannot retarget a profile/session or change managed SID, TestMode, Parent Lock, or Emergency Override.
- Cloud transport outages never bypass or gate local policy. Device commands are retried only while unexpired.
- Supabase provider tables are not directly accessible to anon/authenticated roles; row ownership is checked by the Edge Function against the signed-in user.

Remote status is limited to device name/identity, online/last-seen, exact used/remaining seconds, policy state, TestMode and policy revisions. Parent-authored policy snapshots are sent only for synchronization. No browsing/watch/search history, visited URLs, page content, screenshots, cookies, credentials, keystrokes, or personal files are transmitted.

## Logging
Operational logs must be bounded/rotated and avoid personal content.

## Child warning pipe

The local-only warning pipe allows only LocalSystem (the service) and the managed SessionAgent user SID. Session/profile values inside each JSON warning are validated as an additional boundary; no browsing or application content is sent. The automated ACL test verifies the descriptor and managed-session server creation; a LocalSystem-to-child-user handshake is not impersonated in-process.

## Parent control pipe

Parent actions use a separate local named pipe. Its ACL permits only configured Parent SIDs and LocalSystem; the Service obtains the caller SID from the pipe impersonation token and never trusts a SID in JSON. Profile and managed-session identifiers are still validated against the persisted policy. No Windows password is stored.

For provider deployment steps and the remaining cross-network M5 acceptance, see [TASK-008 Remote Control](TASK_008_REMOTE_CONTROL.md).
