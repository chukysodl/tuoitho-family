# Task Status

| Task | Status | Branch / baseline | Evidence | User acceptance |
|---|---|---|---|---|
| TASK-001 | PASS | task/001-bootstrap | Actions 34552234667 PASS | not required |
| TASK-002 | PASS | task/002-time-engine | Actions 34574851490 PASS | not required |
| TASK-003 | PASS | task/003-device-time-policy | Actions 34815667678 PASS | M1 accepted |
| TASK-004 | PASS / COMPLETE | main | Actions 35071468605 PASS | M2 accepted |
| TASK-005 | PASS / COMPLETE — local Parent Dashboard, daily quota, weekly schedule | main (TASK-005 branch head d9bda0c) | TASK-005 Actions 35086748114 PASS; integrated baseline Actions 35803055541 PASS | M3 functionality confirmed by user |
| TASK-006 | PASS / COMPLETE — browser control, custom URL/domain/path, YouTube channels/Shorts/Playables, TikTok creators, stable extension identity | main (TASK-006 branch head e431404) | TASK-006 Actions 35689497912 PASS; integrated baseline Actions 35803055541 PASS | M4 functionality confirmed by user |
| TASK-007 | IMPLEMENTED via TASK-009C — YouTube/TikTok blocked search keywords plus existing channel/Shorts controls | task/009c-installer-keywords | Full Build/Test/System Check Actions `37451574738` PASS | awaiting combined field acceptance |
| TASK-008 | IMPLEMENTED — remote transport, pairing, commands, policy sync, dashboard; TASK-008B deployment tooling implemented, awaiting real provider deployment and M5 acceptance | task/008-remote-control | implementation `912222cae57c6d2a1578c8e3e3b314d1b3397cfb`; baseline Actions 35806837895 PASS; TASK-008B local Release build/full tests/System Check PASS; Deno CLI unavailable locally and branch CI pending push | M5 not run (no Supabase login/project/key or deployed endpoint in workspace) |
| TASK-009 | SUPERSEDED FOR ADMIN TAMPER — DACL/service hardening works as user-mode defense but IObit with elevated rights removed files/registry/service without parent password | task/009-installer-hardening | automated CI PASS; real IObit acceptance FAIL on 2026-10-06 | superseded by TASK-009E account separation |
| TASK-009B | IMPLEMENTED / AWAITING USER ACCEPTANCE — installer post-check repair + boot-time limit + daily blocked hours | task/009b-time-guardrails | Build/Test/System Check PASS; GitHub pre-release `time-guardrails-test-20261006` PASS | yes |
| TASK-009C | IMPLEMENTED / AWAITING USER ACCEPTANCE — repair broken parent-auth + YouTube/TikTok blocked search keywords | task/009c-installer-keywords | Full Build/Test/System Check Actions `37451574738` PASS; pre-release `repair-keywords-test-20261006` PASS | yes |
| TASK-009D | IMPLEMENTED / AWAITING USER ACCEPTANCE — semantic DACL post-check + SessionAgent self-heal | task/009d-postinstall-selfheal | Full Build/Test/System Check PASS; pre-release `postinstall-selfheal-test-20261006` PASS | yes |
| TASK-009E | IMPLEMENTED / AWAITING USER ACCEPTANCE — Account Protection Mode (Parent Administrator / Child Standard User) | task/009e-account-protection | Build/Test/System Check PASS on `c8bc786cef68f06c5cc813f8147ac0fe68e4ef6c`; pre-release `account-protection-test-20261006` PASS | pending real IObit test from configured child account |
| TASK-010 | PENDING | task/010-community-release | — | RC1 |

Verified integrated baseline: `main` at `cf5d3a7954690a6af689ea78adfb1f2c1b4a3dd7`; GitHub Actions run `35803055541` PASS. TASK-008 may proceed because its declared dependency is TASK-005, which is merged and validated. TASK-007 search remains separate work.

TASK-008B deployment helpers are implemented and locally validated on `task/008-remote-control`: one-step Supabase migration/function deployment, protected public device configuration, GitHub Pages static dashboard deployment, and a secret-safe M5 preflight. Local Release build, full tests, and System Check passed; local Deno type-check was unavailable and remains for CI. These do not constitute provider deployment or M5 acceptance. Keep TASK-008 out of `main` until the device preflight is READY and the parent phone passes cross-network acceptance.


TASK-009 Tamper Protection v2 is implemented and CI-validated. Normal protected state grants LocalSystem full service control while Administrators retain read/start but not full stop/delete/change-config rights; install files are read/execute for Administrators. Correct parent-password authorization asks the running protection service to open a bounded maintenance window, temporarily restoring Administrator write/full service rights for official repair/uninstall. This is intentionally user-mode hardening, not a kernel/PPL claim; real-world acceptance still requires testing against IObit and other elevated removal paths.

TASK-009B adds a configurable post-boot wall-clock limit (0 disables), up to 6 daily repeating blocked windows including overnight ranges, and parent override precedence. It also changes the post-install protection check to wait longer, validate SessionAgent in any interactive session, write `C:\\ProgramData\\TuoiTho\\postinstall-check.log`, retry once, and preserve the installation instead of throwing a fatal installer runtime error when the diagnostic is delayed.


TASK-009C:
- Installer 1.0.2 detects an existing install whose parent-auth verifier is missing/corrupt. A temporary self-contained AdminTool offers parent-auth recovery before file replacement, creates a new verifier under elevated parent control, hardens its ACL again, and requests the maintenance window. Fresh installs keep the normal first-password flow.
- Web policy adds parent-authored YouTubeSearchKeyword and TikTokSearchKeyword block rules.
- Search queries are evaluated locally inside browser extension 0.3.0. They are not added to BrowserNavigationRequest, are not sent to the Windows Service, and are not persisted as browsing/search history.
- YouTube checks committed /results?search_query=... routes. TikTok checks /search routes using q/keyword/search_query parameters.
- The extension refreshes policy revision before keyword evaluation so newly blocked terms take effect on the next search.


TASK-009D:
- Post-install protection no longer compares exact SDDL text. It parses the effective Administrators service ACE and rejects dangerous Stop/ChangeConfig/Delete/WriteDACL/WriteOwner/full-control rights.
- SessionAgent launch failures no longer terminate the core Windows Service; the watchdog logs and retries.
- Post-install check attempts an interactive SessionAgent self-heal launch, but a delayed interactive agent is a warning instead of a false core-install failure.
- Core failures are separated from warnings and written to ProgramData/TuoiTho/postinstall-summary.txt; Setup 1.0.3 shows the exact core failure summary when needed.
- PowerShell checker text is ASCII-safe for Windows PowerShell 5.1 parsing.


TASK-009E:
- Added a dedicated TÀI KHOẢN / Account Protection Mode in the Parent UI.
- Enumerates local Windows accounts and shows Administrator vs Standard User state, UAC state, configured child account, and an explicit SAFE/UNSAFE result.
- Parent can select a different local account and make it the managed child account. The operation requires the app parent password and Windows Administrator elevation, refuses the currently logged-in parent account, refuses the built-in Administrator account, and refuses any change that could remove the last enabled Administrator.
- The selected child SID is persisted in ProgramData/TuoiTho/account-protection.json with SYSTEM/Administrators write access and Users read access.
- The Service reads the configured child SID and never rebinds child enforcement to an Administrator or unrelated interactive session. Any stale SessionAgent found in a parent/non-child session is stopped by exact session/path match.
- If Account Protection has not yet been configured, Administrator sessions are still skipped instead of being silently treated as the child.
- Security boundary is explicit: a child Standard User should be unable to authorize IObit/Revo/service/registry elevation without parent Administrator credentials; a determined Windows Administrator is not claimed to be undefeatable by user-mode software.
