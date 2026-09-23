[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [switch]$SkipBuild,
    [switch]$SkipTests
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
Set-Location -LiteralPath $repositoryRoot

$requiredPaths = @(
    'TuoiTho.sln',
    'src/TuoiTho.Core/TuoiTho.Core.csproj',
    'src/TuoiTho.Storage/TuoiTho.Storage.csproj',
    'src/TuoiTho.Service/TuoiTho.Service.csproj',
    'src/TuoiTho.SessionAgent/TuoiTho.SessionAgent.csproj',
    'src/TuoiTho.Parent/TuoiTho.Parent.csproj',
    'tests/TuoiTho.Tests/TuoiTho.Tests.csproj'
)

foreach ($path in $requiredPaths) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Required bootstrap path is missing: $path"
    }
}

& (Join-Path $PSScriptRoot 'test-m4-powershell51-parser.ps1')
if ($LASTEXITCODE -ne 0) { throw 'System Check PowerShell 5.1 parser test failed.' }

$sdkVersion = dotnet --version
if ($LASTEXITCODE -ne 0 -or $sdkVersion -notmatch '^10\.') {
    throw ".NET 10 SDK is required; found '$sdkVersion'."
}

$coreProject = Get-Content -LiteralPath 'src/TuoiTho.Core/TuoiTho.Core.csproj' -Raw
foreach ($forbiddenReference in @('Microsoft.Windows', 'System.Windows', 'Microsoft.AspNetCore', 'TuoiTho.Parent', 'TuoiTho.Service', 'TuoiTho.SessionAgent')) {
    if ($coreProject -match [regex]::Escape($forbiddenReference)) {
        throw "Core dependency boundary violation: $forbiddenReference"
    }
}

$solutionListing = dotnet sln .\TuoiTho.sln list
if ($LASTEXITCODE -ne 0) { throw "Could not inspect TuoiTho.sln." }
foreach ($projectName in @('TuoiTho.Core.csproj', 'TuoiTho.Storage.csproj', 'TuoiTho.Service.csproj', 'TuoiTho.SessionAgent.csproj', 'TuoiTho.Parent.csproj', 'TuoiTho.Tests.csproj')) {
    if (-not ($solutionListing -match [regex]::Escape($projectName))) {
        throw "Project is not registered in TuoiTho.sln: $projectName"
    }
}

if (-not $SkipBuild) {
    dotnet build .\TuoiTho.sln --configuration $Configuration --no-restore
    if ($LASTEXITCODE -ne 0) { throw "System Check build failed with exit code $LASTEXITCODE." }
}

if (-not $SkipTests) {
    dotnet test .\TuoiTho.sln --configuration $Configuration --no-build --no-restore
    if ($LASTEXITCODE -ne 0) { throw "System Check tests failed with exit code $LASTEXITCODE." }

    dotnet test .\TuoiTho.sln --configuration $Configuration --no-build --no-restore --filter "FullyQualifiedName~SystemCheckRecordsOneLocalSession"
    if ($LASTEXITCODE -ne 0) { throw "System Check time-accounting proof failed with exit code $LASTEXITCODE." }

    Write-Output "Running the real Windows adapter check. It skips only if Windows reports no interactive console session."
    dotnet test .\TuoiTho.sln --configuration $Configuration --no-build --no-restore --filter "FullyQualifiedName~SystemCheckWindowsAdapterPersistsRealInitialSnapshot|FullyQualifiedName~SystemCheckM1TestModeWarningSimulatedLockThenGrantAllows" --logger "console;verbosity=detailed"
    if ($LASTEXITCODE -ne 0) { throw "System Check Windows adapter proof failed with exit code $LASTEXITCODE." }
}

$node = Get-Command 'node' -ErrorAction SilentlyContinue
if (-not $node) { throw 'Node.js is required to validate the mobile remote dashboard.' }
node --check .\remote-dashboard\app.js
if ($LASTEXITCODE -ne 0) { throw 'Remote dashboard JavaScript syntax check failed.' }
node --test .\tests\remote-dashboard\remote-dashboard.test.cjs
if ($LASTEXITCODE -ne 0) { throw 'Remote dashboard privacy/security fixture tests failed.' }

$deno = Get-Command 'deno' -ErrorAction SilentlyContinue
if ($deno) {
    deno check --no-lock .\infra\supabase\functions\device-gateway\index.ts .\infra\supabase\functions\parent-gateway\index.ts
    if ($LASTEXITCODE -ne 0) { throw 'Supabase Edge Function type check failed.' }
} else {
    Write-Output 'Deno Edge Function type check skipped locally (Deno CLI unavailable); GitHub Actions installs Deno and runs this check.'
}

Write-Output "SYSTEM CHECK PASS ($Configuration)"
