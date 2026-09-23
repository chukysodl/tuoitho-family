# TASK-008 — Remote control and policy sync

## Implemented boundary

- `TuoiTho.Core.Remote` contains provider-neutral transport, status, pairing, policy snapshot, command envelope, validation, and acknowledgement contracts.
- `TuoiTho.Service` owns the HTTPS/Supabase adapter, Windows DPAPI-protected device credential, bounded polling worker, command execution, and status publication.
- `TuoiTho.Storage` owns one-use pairing hashes, the durable command/nonce replay ledger, acknowledgements, and atomic remote policy application.
- `infra/supabase` contains provider-only PostgreSQL schema and Edge Functions. No Supabase types, URLs, or clients are referenced by Core.
- `remote-dashboard` is a mobile-first static parent page. It uses Supabase Auth and the owner-checked parent Edge Function; it has no direct table access.

## Command semantics and safety

- `LOCK NOW` applies the existing `SetParentLock` action. In TestMode it is simulated by the existing enforcement path; the UI labels that state explicitly.
- `UNLOCK` clears only Parent Lock. It does not clear Emergency Override or bypass schedule/quota; time policy remains authoritative.
- `GRANT +15/+30/+60` calls the existing local grant operation, so granted active-use time, local-day expiry, SQLite persistence, and policy reevaluation remain shared with local controls.
- `SyncPolicy` validates device/profile/session/revision and replaces the time, app, and browser policy rows in one SQLite transaction. Local managed SID, TestMode, Parent Lock, and Emergency Override cannot be changed by the remote snapshot.
- Commands carry UUID command/device IDs, a random nonce, UTC timestamp/issued/expiry, and kind/payload. The device accepts at most 15-minute TTLs, stores command ID and nonce before execution, and durably records the acknowledgement. Replayed IDs/nonces are not executed again.
- Pending commands remain in the provider until the device reconnects, but expire after 15 minutes. Expired commands are acknowledged as expired without execution.
- Cloud failures are logged and retried; they do not gate local time/app/web policy or alter local decisions.

## Pairing and data boundary

The Parent UI creates a 80-bit random code with a five-minute lifetime. Only its SHA-256 hash is stored locally and remotely; the code is displayed once. The device credential is random, is never stored in plaintext, and is protected with Windows DPAPI for the current Service account. Parent ownership is checked against the authenticated Supabase user. Direct anon/authenticated access to remote tables is revoked; Edge Functions use the server-only service role key.

Remote status contains friendly device name, online/last-seen, used/remaining seconds, policy state, TestMode, and policy revision. A policy snapshot is synchronized because remote policy editing requires it; it contains only parent-configured time/app/browser rules. No visited URLs, browser/watch/search history, screenshots, page contents, cookies, passwords, keystrokes, or personal files are sent.

## Configure Supabase and the device

1. Create a Supabase project and parent account; do not put the service-role key in the device, dashboard, or repository.
2. From the repository root, authenticate/link the CLI, then run `supabase db push` and `supabase functions deploy` using the project’s CLI. The migration and `infra/supabase/config.toml` are the source of truth. `parent-gateway` requires a user JWT. `device-gateway` disables platform JWT verification only because it validates its own per-device credential on authenticated device actions; keep those checks intact.
3. Set `RemoteControl:Enabled=true`, `RemoteControl:SupabaseUrl=https://<project>.supabase.co`, and `RemoteControl:SupabaseAnonKey=<public anon/publishable key>` in the Service environment/config. The URL/key are public project settings; never use a service-role key here. Restart Service.
4. Open Parent → **ĐIỀU KHIỂN TỪ XA** → **Tạo mã ghép nối**. On the mobile dashboard, configure the same project URL/public key, sign in, and enter the one-use code.
5. The dashboard refreshes device status every eight seconds; the Service polls commands every five seconds and publishes status every fifteen seconds. Status online threshold is 45 seconds.

The public-key setting belongs on a trusted parent-controlled dashboard origin. For a community/self-host deployment, host the static `remote-dashboard` over HTTPS and operate the Supabase project and Edge Function secrets yourself. Free-tier availability/limits can change; see [ADR-008](adr/ADR-008-remote-provider.md).

CI type-checks both Edge Functions with Deno. Local `system-check.ps1` does the same when the Deno CLI is installed and explicitly reports a local skip otherwise.

Supabase's current documented CLI flow uses `supabase login`, `supabase link --project-ref <project-id>`, `supabase db push`, and `supabase functions deploy`; check the current [database migration deployment guide](https://supabase.com/docs/guides/deployment/database-migrations) and [function deployment guide](https://supabase.com/docs/guides/functions/deploy) before deployment.

## M5 acceptance — still required

After configuring a real provider, test on a parent phone over a different network:

1. Pair the child device and confirm ONLINE, used time, remaining time, and TestMode/real-mode indication.
2. Press LOCK NOW and wait for the device acknowledgement; while TestMode is on, verify it is simulation-only.
3. Press UNLOCK and confirm schedule/quota still apply; grant +30 and confirm remaining time increases by 30 active minutes.
4. Disconnect the child machine from the Internet. Confirm local time/app/browser policies still operate; reconnect and confirm status/commands recover.

Do not treat unit/fake-transport tests as M5 acceptance. This workspace has no Supabase project credentials, so provider deployment and cross-network phone acceptance have not been run.
