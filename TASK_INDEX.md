# Task Index

| Task | Title | Depends on | User test? |
|---|---|---|---|
| TASK-001 | Bootstrap solution + CI baseline | none | No |
| TASK-002 | Windows session/time accounting engine | 001 | No |
| TASK-003 | Device schedule + daily quota enforcement | 002 | **Yes – M1** |
| TASK-004 | Application/game policy engine | 003 | **Yes – M2** |
| TASK-005 | Local Parent Dashboard + time requests | 004 | **Yes – M3; complete** |
| TASK-006 | Managed browser + custom websites + YouTube channel/Shorts/Playables + TikTok controls | 005 | **Yes – M4; complete** |
| TASK-007 | YouTube search-keyword controls | 006 | **Yes; outstanding** |
| TASK-008 | Remote control/sync adapter | 005 | **Yes – M5; code gates then external provider/phone acceptance; may run parallel with 007** |
| TASK-009 | Installer, startup, ACL, tamper hardening | 008 | **Yes** |
| TASK-010 | Release pipeline + community docs | 009 | **Yes – RC1** |

Do not start the next dependent task before its dependency is merged and PASS. Tasks whose dependency is already PASS may proceed in parallel as shown above.
