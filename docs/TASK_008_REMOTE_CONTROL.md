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

## M5 deployment and device configuration

Run `M5-REMOTE-SETUP.cmd` from the repository root. It checks for the Supabase CLI (or Node.js 20+ for the official `npx` runner), requests Supabase CLI login only when needed, chooses an existing linked/single project when possible, and otherwise asks for a project selection/ref. It then stops on the first failed `supabase link`, `supabase db push`, or Edge Function deploy. The CLI handles its own access token and database-password prompts; this setup script never saves them in the repository.

The only project value requested for the device is its **public anon/publishable key**. Setup rejects recognized `service_role`/secret keys and writes `RemoteControl.Enabled`, project URL, public key, display name, and the current authenticated Parent Windows SID to `%ProgramData%\TuoiTho\RemoteControl\remote-control.json`. The directory grants writes only to SYSTEM/Administrators. Service reads the file after restart. It never contains a service-role key, Supabase access token, database password, or device credential. Setup does not change TestMode or any local policy. If a tracked M1 runtime is running, setup restarts it only through `M1-STOP.ps1`/`M1-START.ps1`; otherwise it restarts only the registered `TuoiTho.Service` service or leaves the file for the next service start.

The mobile dashboard is static content in `remote-dashboard/`. `.github/workflows/remote-dashboard-pages.yml` publishes it over GitHub Pages HTTPS from `main` or the TASK-008 branch; for the first deployment, repository Pages must use **GitHub Actions** as its source. No Supabase key is embedded in the published files. The phone asks for the Project URL and public anon/publishable key and stores them in that browser's local storage. Supabase's current CLI flow is documented at [Supabase CLI](https://supabase.com/docs/guides/local-development/cli/getting-started); the deploy commands are documented in [Deploy database migrations](https://supabase.com/docs/guides/deployment/database-migrations) and [Deploy Edge Functions](https://supabase.com/docs/guides/functions/deploy).

The M5 setup prints the expected dashboard URL: `https://chukysodl.github.io/tuoitho-family/`. Set the Supabase Auth Site URL and allowed redirect URL to that HTTPS address so email confirmation returns to the dashboard. Keep email confirmation enabled. Parent signup uses a password chosen by the parent; the app never supplies or stores a default password. Pairing binds only the authenticated Supabase user returned by `auth.getUser()`.

After setup, `M5-REMOTE-CHECK.cmd` reports each local, database, gateway, polling, and status-publication check and ends with exactly `M5 REMOTE CHECK: READY` or `M5 REMOTE CHECK: NOT READY`. It never prints a project key or token. The device exposes only a minimal, read-only gateway health probe to prove that the deployed migration/table is reachable; parent-gateway remains authenticated.

In Parent → **ĐIỀU KHIỂN TỪ XA**, the code is shown once with a live five-minute countdown. It is one-use; the panel identifies TestMode/real mode and displays last successful poll/status times without exposing credentials. The dashboard updates status every eight seconds; Service polls commands every five seconds and publishes status every fifteen seconds. Status online threshold is 45 seconds.

The public-key setting belongs on a trusted parent-controlled dashboard origin. For a community/self-host deployment, host the static `remote-dashboard` over HTTPS and operate the Supabase project and Edge Function secrets yourself. Free-tier availability/limits can change; see [ADR-008](adr/ADR-008-remote-provider.md).

CI type-checks both Edge Functions with Deno. Local `system-check.ps1` does the same when the Deno CLI is installed and explicitly reports a local skip otherwise. Implementation commit `912222cae57c6d2a1578c8e3e3b314d1b3397cfb` passed Actions run `35806837895`.

Supabase's current documented CLI flow uses `supabase login`, `supabase link --project-ref <project-id>`, `supabase db push`, and `supabase functions deploy`; check the current [database migration deployment guide](https://supabase.com/docs/guides/deployment/database-migrations) and [function deployment guide](https://supabase.com/docs/guides/functions/deploy) before deployment.

## M5 acceptance — still required

After configuring and deploying a real provider, follow the short nontechnical flow in `M5-TEST-GUIDE.txt` and test on a parent phone over a different network:

1. Pair the child device and confirm ONLINE, used time, remaining time, and TestMode/real-mode indication.
2. Press LOCK NOW and wait for the device acknowledgement; while TestMode is on, verify it is simulation-only.
3. Press UNLOCK and confirm schedule/quota still apply; grant +30 and confirm remaining time increases by 30 active minutes.
4. Disconnect the child machine from the Internet. Confirm local time/app/browser policies still operate; reconnect and confirm status/commands recover.

Do not treat unit/fake-transport tests as M5 acceptance. This workspace has no Supabase login, Project Ref, or public key, so this branch has not deployed a provider, reached a real `M5 REMOTE CHECK: READY`, or completed cross-network phone acceptance. Do not merge this branch to `main` until those external checks and parent-phone acceptance pass.
