# TASK-004 – Application/game policy engine

## Goal
Parent controls each app/game independently.

## Rules
Each application supports:
- Allow;
- Block;
- schedule;
- daily quota;
- temporary allow.

Examples must be possible: Chess=Allow, Chinese Chess=Allow, Minecraft=Block.

## Implement
- discover/select installed apps and running executables;
- stable app identity using normalized executable path + publisher/signature/hash metadata where useful;
- protect against simple rename/path tricks as reasonably possible for MVP;
- default unknown-app policy configurable, initially `BlockUnknown`;
- process-start enforcement by Service;
- friendly child notification when blocked;
- child may send `Request App Access`;
- system-critical Windows processes must never be accidentally terminated;
- maintain explicit protected/system allow rules.

## Tests
- allow/block decisions;
- unknown app;
- per-app schedule/quota;
- temporary grant;
- critical-process protection;
- malformed/missing executable metadata.

## User acceptance M2
Demonstrate at least 3 apps: one always allowed, one blocked, one limited by time.

## PASS
Tests/build/system check PASS; commit + push `task/004-app-control`.
