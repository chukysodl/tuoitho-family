param(
    [string]$OutputRoot = (Join-Path (Split-Path -Parent $PSScriptRoot) 'artifacts\production'),
    [string]$Runtime = 'win-x64'
)
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path

if (Test-Path $OutputRoot) { Remove-Item $OutputRoot -Recurse -Force }
New-Item -ItemType Directory -Path $OutputRoot -Force | Out-Null

$projects = [ordered]@{
    Service      = 'src\TuoiTho.Service\TuoiTho.Service.csproj'
    SessionAgent = 'src\TuoiTho.SessionAgent\TuoiTho.SessionAgent.csproj'
    Parent       = 'src\TuoiTho.Parent\TuoiTho.Parent.csproj'
    BrowserHost  = 'src\TuoiTho.BrowserHost\TuoiTho.BrowserHost.csproj'
    AdminTool    = 'src\TuoiTho.AdminTool\TuoiTho.AdminTool.csproj'
}

foreach ($entry in $projects.GetEnumerator()) {
    $destination = Join-Path $OutputRoot $entry.Key
    New-Item -ItemType Directory -Path $destination -Force | Out-Null
    $publishArgs = @(
        'publish',
        (Join-Path $root $entry.Value),
        '--configuration', 'Release',
        '--runtime', $Runtime,
        '--self-contained', 'true',
        '-p:PublishSingleFile=true',
        '-p:IncludeNativeLibrariesForSelfExtract=true',
        '-p:PublishTrimmed=false',
        '-p:DebugType=None',
        '-p:DebugSymbols=false',
        '--output', $destination
    )
    & dotnet @publishArgs
    if ($LASTEXITCODE -ne 0) { throw "Publish failed: $($entry.Key)" }
}

$extensionDestination = Join-Path $OutputRoot 'BrowserExtension'
Copy-Item (Join-Path $root 'browser-extension') $extensionDestination -Recurse -Force

$metadata = [ordered]@{
    BuiltAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
    Runtime = $Runtime
    ProductName = 'Quản lý thời gian'
    SelfContained = $true
    Components = @($projects.Keys)
}
$metadata | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $OutputRoot 'build-info.json') -Encoding UTF8

Write-Host "PRODUCTION PUBLISH: PASS -> $OutputRoot"
