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

Historical note: GitHub Actions run `34549792780` for commit `f48f9a8af280179487edb189250c44cf27bdc827` did not allocate a runner because the repository was private and GitHub reported a billing/spending-limit block.

The repository was made public on 2026-09-11. GitHub Actions run `34552234667` then passed every workflow step for commit `322562891d59d23d939df3952a5ab85a10259789`: checkout, .NET setup, restore, build, test, and System Check.

The selected zero-cost configuration is the current standard GitHub-hosted `windows-latest` runner on this public repository. If the repository must become private again, use a repository self-hosted Windows runner or restore an account billing allowance before relying on GitHub-hosted Windows runners.
