# TASK-002 – Windows session/time accounting engine

## Goal
Accurately calculate child computer usage without counting obvious locked/idle periods incorrectly.

## Implement
- Service lifecycle and safe startup/shutdown.
- Detect relevant Windows session state events.
- Track active/locked/logged-out state.
- Configurable idle threshold.
- Persist daily counters transactionally in SQLite.
- Survive service restart/reboot without losing accumulated usage.
- Core time engine must be testable with fake clock/event source.
- DST/date rollover behavior defined and tested.

## Non-goal
No blocking/locking enforcement yet.

## Tests
- active time accumulation;
- lock/unlock;
- idle/resume;
- reboot/service restart;
- midnight rollover;
- duplicate/out-of-order event resilience.

## PASS
Representative automated tests PASS and a System Check proves the service records one local test session correctly.
Commit + push `task/002-time-engine`.
