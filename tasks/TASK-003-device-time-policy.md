# TASK-003 – Device schedule + daily quota enforcement

## Goal
Enforce when and how long the child can use the computer.

## Implement
- weekly allowed-time windows;
- daily quota minutes;
- temporary grant +15/+30/+60/custom;
- warning thresholds (default 15/5/1 minutes configurable);
- SessionAgent warning UI;
- when denied/exhausted: lock child session safely;
- Parent emergency unlock/override with authenticated local action;
- fail-safe policy when service/UI restarts;
- clear reason shown: outside schedule / quota exhausted / parent lock.

## Tests
Policy engine must use fake clock. Cover schedule boundaries, quota exhaustion, temporary grant and next-day reset.

## User acceptance milestone M1
Parent sets 30-minute quota and schedule; child receives warnings; after expiry session locks; parent can grant extra time; reboot does not reset quota.

## PASS
Automated tests + build PASS, then prepare a short M1 test guide.
Commit + push `task/003-device-time-policy`.
