# Time Accounting Semantics

TASK-002 records only aggregate active duration needed for family time management. It does not inspect content, keystrokes, messages, screenshots, audio, or video.

## Counted states

Only `Active` intervals are recorded. `Idle`, `Locked`, `LoggedOut`, and `Unknown` intervals are excluded. The Windows adapter makes the idle threshold configurable and backdates an idle transition to the instant the threshold was crossed, avoiding additional polling-delay overcount.

## Persistence and restart

Every accepted event writes the updated session checkpoint and all affected daily increments in one SQLite transaction. On service restart, an active checkpoint continues to the next observed active snapshot. If a reboot is detected from the new boot timestamp, recovery is capped at the new boot time, so powered-off time is not counted.

## Dates and DST

Elapsed time is calculated in UTC, then split into the machine's local calendar dates. This means daylight-saving spring-forward and fall-back transitions record the actual elapsed duration, not the apparent wall-clock difference. An interval crossing local midnight is split between its two local dates.

## Event resilience

Duplicate events at the same timestamp/state and events older than the checkpoint are ignored. A same-timestamp state change is accepted without adding duration. TASK-002 does not enforce limits, lock sessions, or block applications/websites.