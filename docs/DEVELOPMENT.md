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
