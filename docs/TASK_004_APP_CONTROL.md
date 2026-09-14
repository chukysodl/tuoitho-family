# TASK-004 – Application/game policy engine and M2 safe simulation

## Goal

Provide local-first, allowlist-first application policy for one managed child profile. A parent can explicitly allow an application such as chess, block an application such as Minecraft, and inspect only applications observed in the configured child Windows session. Unknown applications default to `BlockUnknown`; the stored default can later be changed to `AllowUnknown` through the parent UI.

## Architecture

- `TuoiTho.Core`: stable executable identity, app rule/default-policy models, and a deterministic policy engine.
- `TuoiTho.Storage`: SQLite migration and repositories for default policy, rules, and child-session-scoped observations.
- `TuoiTho.Service`: discovers executable metadata only from the managed session, records observations, evaluates the policy, and publishes a simulation result. It performs no process termination or launch prevention in this task.
- `TuoiTho.Parent`: an `ỨNG DỤNG` tab displays observed applications, their decision, and controls for allow, block, rule removal, refresh, and default-policy visibility.

Identity uses normalized executable path plus filename and optional hash/publisher/product metadata; display text alone is never a rule key. Explicit block wins, then explicit allow, then the default policy.

## Privacy rules

The discovery model stores only executable path, filename, optional product/publisher/hash metadata, session/profile scope, and first/last-seen timestamps. It never collects command-line arguments, window titles, documents, browser URLs, typed input, screenshots, or application content. Processes outside the managed session—including services and other Windows users—are ignored.

## Checkpoints

1. Core identity and deterministic decision engine.
2. SQLite migration/repository persistence and restart coverage.
3. Managed-session-only discovery and safe simulation publisher.
4. Parent `ỨNG DỤNG` UI and M2 simulation validation.

## M2 test plan

With `TestMode=true`, set default to `BlockUnknown`; allow Notepad, block Calculator, then observe an additional executable. The UI must show `WOULD_ALLOW`, `WOULD_BLOCK`, and `BLOCK_UNKNOWN` respectively while all applications remain usable. No process is terminated, launch is prevented, session is locked, or Windows configuration is changed.

## PASS criteria

- Explicit allow/block and default-policy decisions are deterministic and tested.
- Rules, defaults, and observations persist across SQLite reopen.
- Discovery ignores foreign/system sessions and stores no prohibited fields.
- Parent UI can manage an observed rule and shows the latest simulation decision.
- TestMode proves simulation only; no process-control API is invoked.
- Focused tests, full test suite, Release build, System Check, and GitHub Actions pass.