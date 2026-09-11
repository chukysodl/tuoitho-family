# Developer Guide

## Requirements

- Windows development environment for the Service host.
- .NET SDK 10.x.

## Build and test

Run the CI-equivalent local flow from the repository root:

```powershell
./scripts/build.ps1
```

The script restores, builds the solution in Release configuration, and runs all tests without requiring cloud credentials. To run individual commands, use:

```powershell
dotnet restore TuoiTho.sln
dotnet build TuoiTho.sln --configuration Release --no-restore
dotnet test TuoiTho.sln --configuration Release --no-build --no-restore
```

`./scripts/system-check.ps1` verifies the required project layout and dependency boundaries, then repeats the Release build/test checks.

## Warnings policy

`Directory.Build.props` enables nullable reference types, implicit usings, .NET analyzers, and the latest recommended analysis level. All compiler and analyzer warnings are treated as errors. New warnings must be fixed at their source; suppressions require a task-scoped explanation.

## Privacy-safe logging

Hosts emit JSON structured logs for operational lifecycle events. Log fields must be bounded and operational (for example, component, event, outcome, or identifiers created for enforcement). Never add keystrokes, message contents, passwords, screenshots, microphone/camera data, browsing history, or other personal content to logs.

## Local storage

`TuoiTho.Storage.SqliteDatabase` creates the parent directory, opens a local SQLite database, enables foreign keys, and applies numbered migrations transactionally. The bootstrap migration contains only operational policy tables; it does not store unnecessary browsing content.

## GitHub Actions availability

GitHub Actions run `34549792780` for commit `f48f9a8af280179487edb189250c44cf27bdc827` did not allocate a runner or execute any workflow step. GitHub's job annotation states: "The job was not started because recent account payments have failed or your spending limit needs to be increased."

This is an account billing/spending-limit block on the private repository's GitHub-hosted `windows-latest` runner, not a workflow, package, or code failure. The workflow was recognized by GitHub and the equivalent local build, tests, and System Check pass.

Zero-cost compatible options are:

1. Configure a repository self-hosted Windows runner and change `runs-on` to its labels. GitHub does not charge Actions minutes for self-hosted runners, though the maintainer operates the machine.
2. If making the project public is acceptable, keep the current standard GitHub-hosted runner: standard runners are free for public repositories.

Do not mark GitHub Actions as passing until an available runner executes the workflow successfully.
