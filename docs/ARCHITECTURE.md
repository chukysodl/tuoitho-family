# Architecture

## Components

### 1. TuoiTho.Service
Windows Service chạy quyền phù hợp để:
- time accounting;
- policy engine;
- app enforcement;
- local command execution;
- policy sync;
- secure IPC với UI/bridge.

Service không trực tiếp hiển thị UI vào desktop session.

### 2. TuoiTho.SessionAgent
Process trong child user session:
- countdown/warnings;
- lock screen notices;
- request access UI;
- giao tiếp với Service qua authenticated local IPC.

### 3. TuoiTho.Parent
Parent dashboard/admin UI:
- local administration;
- policy editor;
- requests;
- remote status/commands khi remote adapter được cấu hình.

### 4. TuoiTho.Core
Pure domain library:
- models;
- policy evaluation;
- quota calculations;
- schedules;
- validation;
- no Windows-specific code.

### 5. TuoiTho.Storage
SQLite repositories + migrations.

### 6. TuoiTho.BrowserExtension
Manifest V3 extension:
- generic domain control;
- block page;
- YouTube rules;
- request access;
- bridge to local service.

### 7. Remote control and sync
`TuoiTho.Core.Remote` defines provider-neutral pairing, device status, command, acknowledgement, policy snapshot, and `IRemoteTransport` contracts. `TuoiTho.Service` hosts the transport worker and local command bridge; `TuoiTho.Storage` persists replay receipts and applies validated policy snapshots locally. Supabase schema/Edge Functions are isolated under `infra/supabase`; replacing the provider does not add vendor references to Core.

Remote control is a command/sync channel only. SQLite and the existing local policy engines remain authoritative offline.

## Policy evaluation order

1. Emergency parent override/lock.
2. Device schedule + daily quota.
3. App policy.
4. Browser/domain policy.
5. YouTube-specific policy.
6. Temporary grants.

Deny rules take precedence unless an explicit temporary parent grant says otherwise.

## Local data

SQLite stores:
- device config;
- child profile;
- policies;
- quota counters;
- time tracking checkpoints;
- temporary grants;
- pending requests;
- minimal operational audit events.

Do not store unnecessary browsing content.

## Security assumptions

- Child Windows account should be Standard User.
- Parent/admin credentials/PIN must not be stored plaintext.
- Local IPC must authenticate peer/session.
- Remote commands must be signed or authenticated with per-device secret/key.
- Agent/service configuration directories must be protected by ACL.
- All enforcement decisions must fail safely.
