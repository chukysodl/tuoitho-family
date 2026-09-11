# TASK-010 – Community release pipeline

## Goal
Prepare first public release candidate as a free/open project.

## Implement
- release build pipeline;
- versioning;
- checksums;
- signed-build strategy documented (do not fake code signing if certificate unavailable);
- CONTRIBUTING.md;
- SECURITY.md reporting process;
- privacy statement;
- architecture/developer setup docs;
- user quick-start guide;
- issue templates;
- choose/document open-source license consistent with keeping the project freely usable; current recommendation: GPL-3.0-or-later, but record final decision in ADR;
- dependency/license inventory;
- RC release notes and known limitations.

## PASS
Fresh-machine build instructions verified; release artifact reproducible; all test suites PASS; create RC tag/release only when repository policy permits.
Commit + push `task/010-community-release`.
