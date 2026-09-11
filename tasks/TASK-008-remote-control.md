# TASK-008 – Optional remote control and sync

## Goal
Parent can lock/grant/update policy remotely without making core features depend on paid infrastructure.

## Architecture requirement
Create provider-neutral `IRemoteTransport` / equivalent. Core and enforcement must not know Supabase/Cloudflare/Firebase specifics.

## Implement
- secure pairing flow;
- device identity/credential;
- heartbeat/status;
- commands: LockNow, GrantTime, SyncPolicy;
- command id + timestamp/nonce/replay protection;
- ack/result;
- offline queue where safe;
- one free-tier reference provider chosen after a small ADR comparing at least Supabase/Cloudflare/Firebase or another viable free option;
- local-only mode remains fully functional;
- self-host path documented.

## Data minimization
Remote backend stores only data needed for pairing/status/policy/commands. No surveillance content.

## Tests
Mock transport contract tests + provider integration test that can be skipped when credentials absent.

## User acceptance M5
From another network/device, parent can see device online state, Lock Now and Grant Time; if Internet is removed, existing local policy still works.

## PASS
Security tests/build/system check PASS; commit + push `task/008-remote-control`.
