# TASK-009 – Installer, startup and tamper hardening

## Goal
Produce a reliable Windows install suitable for non-technical families.

## Implement
- installer installs Service, SessionAgent, Parent UI and browser-control assets;
- admin elevation only when necessary;
- service auto-start;
- correct filesystem/registry ACLs;
- child Standard User cannot simply stop/uninstall service or edit policy DB;
- parent/admin can repair/uninstall cleanly;
- extension deployment/policy setup documented/automated where supported;
- rollback on failed install;
- logs suitable for troubleshooting without private content;
- offline installation; no runtime download surprises.

## Security boundary
Do not claim tamper-proof against a local Administrator.

## Tests
Clean install, upgrade, repair, uninstall and reboot checks on supported Windows version(s).

## PASS
Installer system-check matrix PASS; commit + push `task/009-installer-hardening`.
