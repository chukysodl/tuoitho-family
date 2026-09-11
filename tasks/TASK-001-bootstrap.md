# TASK-001 – Bootstrap solution + CI baseline

## Goal
Create a clean .NET 10 LTS solution structure ready for Windows development.

## Implement
- `TuoiTho.sln`
- projects: `TuoiTho.Core`, `TuoiTho.Storage`, `TuoiTho.Service`, `TuoiTho.SessionAgent`, `TuoiTho.Parent`, `TuoiTho.Tests`
- nullable enabled; warnings policy documented;
- central package/version management if useful;
- SQLite wiring in Storage with initial migration/schema;
- dependency direction: Core must not depend on Windows/UI/cloud;
- basic structured logging with privacy-safe defaults;
- `.gitignore`, build scripts, developer README;
- GitHub Actions build/test on Windows.

## Tests
- solution builds cleanly;
- Core unit-test project runs;
- Storage can create/open a temporary SQLite DB;
- architecture/dependency sanity test if practical.

## PASS
- CI-equivalent local build PASS;
- tests PASS;
- no cloud credential required;
- commit + push branch `task/001-bootstrap`.
