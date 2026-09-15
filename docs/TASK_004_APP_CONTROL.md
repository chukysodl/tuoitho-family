# TASK-004 – Application/game policy engine and M2 safe simulation

## Goal

Provide local-first, allowlist-first application policy for one managed child profile. A parent can explicitly allow an application such as chess, block an application such as Minecraft, and inspect only applications observed in the configured child Windows session. Unknown applications default to `BlockUnknown`; the stored default can later be changed to `AllowUnknown` through the parent UI.

## Architecture

- `TuoiTho.Core`: stable executable identity, app rule/default-policy models, and a deterministic policy engine.
- `TuoiTho.Storage`: SQLite migration and repositories for default policy, rules, and child-session-scoped observations.
- `TuoiTho.Service`: discovers executable metadata only from the managed session, classifies it as `UserApplication`, `BackgroundHelper`, or `SystemProtected`, records observations, evaluates the policy, and publishes a simulation result. It performs no process termination or launch prevention in this task.
- `TuoiTho.Parent`: the `ỨNG DỤNG` tab shows `UserApplication` entries by default, offers an optional background-helper view, never shows `SystemProtected` entries, and provides allow, block, rule removal, refresh, and default-policy visibility.

Identity uses normalized executable path plus filename and optional hash/publisher/product metadata; display text alone is never a rule key. A managed-session process with a visible top-level window is a user-application candidate. Known Windows infrastructure, `C:\\Windows\\SystemApps`, every foreign session, and the TuoiTho control plane (including future control tools within the trusted install directory) are system-protected; background helpers are allowed for this M2 simulation. `BlockUnknown` applies only to controllable user applications. Explicit block wins, then explicit allow, then the default policy.

## Privacy rules

The discovery model stores only executable path, filename, optional product/publisher/hash metadata, session/profile scope, and first/last-seen timestamps. It never collects command-line arguments, window titles, documents, browser URLs, typed input, screenshots, or application content. Processes outside the managed session—including services and other Windows users—are ignored.

## Checkpoints

1. Core identity and deterministic decision engine.
2. SQLite migration/repository persistence and restart coverage.
3. Managed-session-only discovery and safe simulation publisher.
4. Parent `ỨNG DỤNG` UI and M2 simulation validation.

## M2 test plan

With `TestMode=true`, set default to `BlockUnknown`; allow Notepad, block Calculator, then observe an additional executable. The UI must show Vietnamese parent-facing simulation results while all applications remain usable. No process is terminated, launch is prevented, session is locked, or Windows configuration is changed.

## PASS criteria

- Explicit allow/block and default-policy decisions are deterministic and tested.
- Rules, defaults, and observations persist across SQLite reopen.
- Discovery ignores foreign/system sessions and stores no prohibited fields.
- Parent UI can manage an observed rule and shows the latest simulation decision.
- TestMode proves simulation only; no process-control API is invoked.
- Focused tests, full test suite, Release build, System Check, and GitHub Actions pass.
## TASK-004A closeout

TASK-004A is PASS and received real M2 acceptance at `f94c7b1`. The normal parent list shows manageable user applications, protects Windows infrastructure and the TuoiTho control plane, and remains simulation-only. TASK-004B1 continues on the same branch; it does not merge to `main` yet.

## TASK-004B1 safe explicit-block test mode

`AppEnforcementMode` distinguishes `Simulation`, `ExplicitBlockOnly`, and the model-only `AllowlistProduction`. The M2 control starts **OFF** on every Service start and is never persisted. While `TestMode=true`, an authenticated Parent can arm only `ExplicitBlockOnly`: explicit block rules may be closed for an exact verified PID/path/session; explicit allow and unreviewed applications remain allowed. A missing/non-test policy, failed verification, protected/background/control-plane classification, foreign session, or recovery tool fails closed with `SKIPPED_SAFE` audit data.

The worker polls at a bounded one-second interval. It re-verifies managed session, observed classification, executable identity, and current control state before asking the app to close. It tries a graceful close first, then terminates only the still-matching PID without a process-tree or name-wide kill. The audit holds only timestamp, profile, session, executable path, decision, and action; it never includes windows, input, document, URL, screen, or message data.
## TASK-004B1 M2 proof

On the real M2 test session, Calculator stayed open with the switch off, closed when its explicit rule was armed, closed again on relaunch while armed, and stayed open again after disarming. Explicitly allowed Notepad stayed open while the switch was armed. The test finished by disarming the switch and stopping only the tracked M1 components.
