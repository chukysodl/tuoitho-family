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
| TASK-008 | IMPLEMENTED — remote transport, pairing, commands, policy sync, dashboard; awaiting provider deployment and M5 acceptance | task/008-remote-control | local Release/build/tests/system-check + fake transport; Actions pending | M5 not run (no Supabase project credentials in workspace) |
| TASK-009 | PENDING | task/009-installer-hardening | — | yes |
| TASK-010 | PENDING | task/010-community-release | — | RC1 |

Verified integrated baseline: `main` at `cf5d3a7954690a6af689ea78adfb1f2c1b4a3dd7`; GitHub Actions run `35803055541` PASS. TASK-008 may proceed because its declared dependency is TASK-005, which is merged and validated. TASK-007 search remains separate work.
