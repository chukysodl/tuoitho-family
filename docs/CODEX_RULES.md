# CODEX WORK RULES

These rules apply to every task.

1. Read `PROJECT_HANDOFF.md`, `docs/PRODUCT_REQUIREMENTS.md`, `docs/ARCHITECTURE.md` and the assigned task before coding.
2. Work only on the assigned task. Do not silently expand scope.
3. Preserve local-first behavior.
4. Do not add surveillance features.
5. Prefer free/open-source dependencies. Document every new dependency and license.
6. No hard-coded cloud vendor in Core.
7. Add/update tests for every behavior changed.
8. Run formatting/static analysis/tests/build relevant to the solution.
9. If tests/build fail, diagnose and self-fix before stopping.
10. Update task status/docs when architecture/API changes.
11. Do not claim PASS unless acceptance criteria are actually met.
12. Commit and push the task branch.

## Required finish report

Return exactly these sections:
- IMPLEMENTED
- TESTS
- BUILD
- SYSTEM CHECK
- RISKS / TODO
- COMMIT
- PUSH
- PASS / FAIL

Include commit SHA and branch.
