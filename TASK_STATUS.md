# Task Status

| Task | Status | Branch / baseline | Evidence | User acceptance |
|---|---|---|---|---|
| TASK-001 | PASS | task/001-bootstrap | Actions 34552234667 PASS | not required |
| TASK-002 | PASS | task/002-time-engine | Actions 34574851490 PASS | not required |
| TASK-003 | PASS | task/003-device-time-policy | Actions 34815667678 PASS | M1 accepted |
| TASK-004 | PASS / COMPLETE | main | Actions 35071468605 PASS | M2 accepted |
| TASK-005 | PASS / COMPLETE — local Parent Dashboard, daily quota, weekly schedule | main (TASK-005 branch head d9bda0c) | TASK-005 Actions 35086748114 PASS; integrated baseline Actions 35803055541 PASS | M3 functionality confirmed by user |
| TASK-006 | PASS / COMPLETE — browser control, custom URL/domain/path, YouTube channels/Shorts/Playables, TikTok creators, stable extension identity | main (TASK-006 branch head e431404) | TASK-006 Actions 35689497912 PASS; integrated baseline Actions 35803055541 PASS | M4 functionality confirmed by user |
| TASK-007 | PARTIAL — YouTube channel and Shorts behavior is delivered through TASK-006; YouTube search-keyword controls are not implemented | included in main through TASK-006 | no separate TASK-007 implementation/CI | channel/Shorts confirmed; search remains outstanding |
| TASK-008 | IMPLEMENTED — remote transport, pairing, commands, policy sync, dashboard; TASK-008B deployment tooling implemented, awaiting real provider deployment and M5 acceptance | task/008-remote-control | implementation `912222cae57c6d2a1578c8e3e3b314d1b3397cfb`; baseline Actions 35806837895 PASS; TASK-008B local Release build/full tests/System Check PASS; Deno CLI unavailable locally and branch CI pending push | M5 not run (no Supabase login/project/key or deployed endpoint in workspace) |
| TASK-009 | PENDING | task/009-installer-hardening | — | yes |
| TASK-010 | PENDING | task/010-community-release | — | RC1 |

Verified integrated baseline: `main` at `cf5d3a7954690a6af689ea78adfb1f2c1b4a3dd7`; GitHub Actions run `35803055541` PASS. TASK-008 may proceed because its declared dependency is TASK-005, which is merged and validated. TASK-007 search remains separate work.

TASK-008B deployment helpers are implemented and locally validated on `task/008-remote-control`: one-step Supabase migration/function deployment, protected public device configuration, GitHub Pages static dashboard deployment, and a secret-safe M5 preflight. Local Release build, full tests, and System Check passed; local Deno type-check was unavailable and remains for CI. These do not constitute provider deployment or M5 acceptance. Keep TASK-008 out of `main` until the device preflight is READY and the parent phone passes cross-network acceptance.
