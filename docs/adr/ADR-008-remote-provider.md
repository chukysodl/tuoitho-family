# ADR-008: Remote transport provider for TASK-008

- Status: Accepted for the first remote-control adapter
- Date: 2026-09-23

## Context

The Windows Service and Core must keep enforcing the locally persisted time, application, and browser policies when remote connectivity is unavailable. The remote provider is therefore a synchronization/command transport only. Domain command validation, pairing lifecycle, replay protection, local application, and acknowledgements belong behind provider-neutral interfaces.

## Provider comparison

| Provider | Free tier / realtime | Authentication and storage | Self-hosting / portability | Deployment and lock-in |
|---|---|---|---|---|
| Supabase | Current Free plan lists 500 MB Postgres, 50,000 monthly active users, 2 million Realtime messages/month and 200 peak Realtime connections. Free projects pause after one week of inactivity. | Auth plus Postgres and Realtime; Edge Functions can keep privileged command/pairing writes server-side. | Supabase documents Docker self-hosting. Postgres schemas and HTTPS APIs are portable; self-host requires operating the full stack (docs list 4 GB RAM minimum, 8 GB recommended). | Moderate setup. Medium lock-in at the Auth/Realtime edge; low data lock-in with standard Postgres and `IRemoteTransport`. |
| Firebase | Spark requires no payment method; Realtime Database lists 100 simultaneous connections, 1 GB storage and 10 GB/month downloads. Firestore lists 1 GiB storage, 20,000 writes/day and 50,000 reads/day. | Firebase Auth and managed Realtime Database/Firestore. Cloud Functions are not available on Spark, so trusted pairing and command issuance require a paid Blaze project or a separately hosted trusted API. | No first-party self-hosted Firebase service; data model and security rules are Firebase-specific. | Easy to prototype, but the trusted server requirement adds deployment friction; high provider-specific coupling. |
| Cloudflare Workers + D1 / SQLite Durable Objects | Workers Free lists 100,000 requests/day. D1 lists 5 million rows read/day, 100,000 rows written/day and 5 GB storage. SQLite Durable Objects are available on Free and list 100,000 requests/day, with SQLite row limits aligned to D1. | Auth and device/parent relationship logic must be assembled in Workers; Durable Objects offer serialized per-device state and WebSocket messaging. | Worker/DO runtime and APIs are not self-hosted as a compatible service; D1 SQL is SQLite-oriented but event and execution APIs are Cloudflare-specific. | Strong technical fit for per-device realtime actors, but more custom authentication/admin work and higher runtime lock-in for this project. |

Official sources checked on 2026-09-23:

- Supabase plans and limits: https://supabase.com/pricing and https://supabase.com/docs/guides/realtime/limits
- Supabase self-hosting and requirements: https://supabase.com/docs/guides/self-hosting and https://supabase.com/docs/guides/self-hosting/docker
- Firebase plans and quotas: https://firebase.google.com/pricing and https://firebase.google.com/docs/database/usage/limits
- Cloudflare Workers limits/pricing: https://developers.cloudflare.com/workers/platform/limits/ and https://developers.cloudflare.com/workers/platform/pricing/
- Cloudflare D1 and Durable Objects: https://developers.cloudflare.com/d1/platform/pricing/ and https://developers.cloudflare.com/durable-objects/platform/pricing/

Free tiers and limits change. Recheck these pages before public release; the adapter and domain APIs must not encode any quoted quota as a product guarantee.

## Decision

Use Supabase as the first hosted provider through an `IRemoteTransport` adapter. Use Postgres for pairing/device/command/ack/status records, Auth for parent accounts, and Edge Functions (or an equivalent trusted server adapter when self-hosted) for privileged pairing/command operations. Both device and dashboard use bounded HTTPS polling (5/8/15-second intervals), so neither requires a persistent cloud connection or realtime entitlement. Supabase credentials, URLs, DTOs, and client libraries remain outside Core.

The same adapter contract supports a self-hosted Supabase URL or a later provider. Provider-specific schema migrations and Edge Functions stay in a provider folder. SQLite remains the local source of truth; remote policy snapshots are validated and applied locally before acknowledgement.

## Security and offline consequences

Pairing uses a high-entropy, short-lived, one-use code. The server stores only a code hash. A device-specific credential is created locally and protected with current-account Windows DPAPI; pairing binds a parent account to a device. Remote commands are device-bound, authenticated, time-bounded, nonce-bearing and acknowledged. Local durable command IDs/nonces prevent duplicate execution. Local policy evaluation and enforcement never call a cloud permission endpoint and continue unchanged during outages.

Remote status and policy payloads are limited to the fields needed for device management. No browsing/watch/search history, page data, screenshots, cookies, passwords, or personal files are synchronized.
