# Dependencies

Runtime and test packages are centrally versioned in `Directory.Packages.props`.

| Package | Version | Purpose | License |
|---|---:|---|---|
| Microsoft.Data.Sqlite | 10.0.11 | Local SQLite access and migrations | MIT |
| Microsoft.Extensions.Hosting | 10.0.11 | Service/session host composition | MIT |
| Microsoft.Extensions.Hosting.WindowsServices | 10.0.11 | Windows Service integration | MIT |
| Microsoft.NET.Test.Sdk | 17.14.1 | Test execution infrastructure | MIT |
| Microsoft.Win32.SystemEvents | 10.0.11 | Windows session-switch event notifications | MIT |
| xunit | 2.9.3 | Unit test framework | Apache-2.0 |
| xunit.runner.visualstudio | 3.1.4 | Visual Studio/VSTest adapter | Apache-2.0 |
| coverlet.collector | 6.0.4 | Optional test coverage collector | MIT |

The ASP.NET Core shared framework is supplied by the .NET SDK/runtime and is not copied into the repository. No cloud SDK or credential is required by the bootstrap solution.
