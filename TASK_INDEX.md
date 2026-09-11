# Task Index

| Task | Title | Depends on | User test? |
|---|---|---|---|
| TASK-001 | Bootstrap solution + CI baseline | none | No |
| TASK-002 | Windows session/time accounting engine | 001 | No |
| TASK-003 | Device schedule + daily quota enforcement | 002 | **Yes – M1** |
| TASK-004 | Application/game policy engine | 003 | **Yes – M2** |
| TASK-005 | Local Parent Dashboard + requests | 004 | **Yes – M3** |
| TASK-006 | Managed browser + website allowlist | 005 | **Yes** |
| TASK-007 | YouTube channel/search controls | 006 | **Yes – M4** |
| TASK-008 | Optional remote control/sync adapter | 005 | **Yes – M5** |
| TASK-009 | Installer, startup, ACL, tamper hardening | 008 | **Yes** |
| TASK-010 | Release pipeline + community docs | 009 | **Yes – RC1** |

Do not start TASK-00N+1 until TASK-00N is merged/PASS, except where dependency column explicitly permits parallel work.
