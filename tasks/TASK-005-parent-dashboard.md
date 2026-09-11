# TASK-005 – Local Parent Dashboard + child requests

## Goal
Make the Windows product usable without editing config files.

## Implement
Responsive local Parent UI with:
- device status;
- used/remaining time today;
- weekly schedule editor;
- daily quota editor;
- Grant Time;
- Lock Now / unlock override;
- app rule editor;
- pending child requests;
- parent PIN/auth;
- clear Vietnamese-first UI, architecture ready for localization.

Dashboard may run locally first; remote comes later.

## Security
- sensitive actions require parent auth;
- CSRF/local API protections as applicable;
- do not expose admin API to LAN by default.

## Tests
UI/service integration tests where practical + Core/API tests.

## User acceptance M3
Parent can perform all common local management without touching JSON/registry/database.

## PASS
Tests/build/system check PASS; commit + push `task/005-parent-dashboard`.
