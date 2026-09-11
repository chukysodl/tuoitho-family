# TASK-006 – Managed browser + website allowlist

## Goal
Browser can only access websites permitted by parent when strict mode is enabled.

## Implement
- Chrome/Edge Manifest V3 extension architecture;
- generic domain rule engine;
- `AllowlistOnly` mode: unknown domain -> block page;
- allow domain/subdomain patterns with validation;
- child can request website access;
- local authenticated bridge/service API for current policy;
- cache signed/versioned policy locally so browser control still works offline;
- protect extension deployment using supported browser policy where feasible;
- document browser-management limitation and require un-managed browsers to be blocked through App Policy;
- no HTTPS interception/MITM certificates.

## Tests
- allowed domain;
- blocked/unknown domain;
- subdomain behavior;
- malformed URL;
- extension loses local-service connection;
- offline cached policy;
- request access flow.

## PASS
Chrome and Edge manual system check + automated tests/build PASS.
Commit + push `task/006-web-control`.
