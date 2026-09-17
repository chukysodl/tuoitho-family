param([switch]$Quiet)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$state = Join-Path $env:TEMP 'tuoitho-m1-state.json'
if (-not (Test-Path $state)) {
    if (-not $Quiet) { Write-Output 'M1 STOP: No tracked M1 components were found.' }
    return
}

$expected = @{
    Service = Join-Path $root 'src/TuoiTho.Service/bin/Release/net10.0-windows/TuoiTho.Service.exe'
    SessionAgent = Join-Path $root 'src/TuoiTho.SessionAgent/bin/Release/net10.0-windows/TuoiTho.SessionAgent.exe'
    Parent = Join-Path $root 'src/TuoiTho.Parent/bin/Release/net10.0-windows/TuoiTho.Parent.exe'
}
$tracked = Get-Content $state -Raw | ConvertFrom-Json
$components = @()
if ($null -ne $tracked.Components) {
    $components = @($tracked.Components)
} else {
    # Legacy state did not save executable paths. Reconstruct only expected TuoiTho paths;
    # a PID is never stopped unless its live executable exactly matches one of them.
    $components = @(
        [pscustomobject]@{ Name = 'Service'; Pid = $tracked.ServicePid; ExecutablePath = $expected.Service },
        [pscustomobject]@{ Name = 'SessionAgent'; Pid = $tracked.AgentPid; ExecutablePath = $expected.SessionAgent },
        [pscustomobject]@{ Name = 'Parent'; Pid = $tracked.ParentPid; ExecutablePath = $expected.Parent }
    )
}

foreach ($component in $components) {
    if ([string]::IsNullOrWhiteSpace([string]$component.Name) -or -not $expected.ContainsKey([string]$component.Name)) { throw 'M1 STOP FAIL: State contains an unrecognized component.' }
    $expectedPath = $expected[[string]$component.Name]
    if (-not [string]::Equals([string]$component.ExecutablePath, $expectedPath, [StringComparison]::OrdinalIgnoreCase)) { throw "M1 STOP FAIL: Refusing to stop $($component.Name); state executable is not a tracked TuoiTho binary." }
    $process = Get-Process -Id ([int]$component.Pid) -ErrorAction SilentlyContinue
    if ($null -eq $process) { continue }
    $actualPath = $process.Path
    if (-not [string]::Equals($actualPath, $expectedPath, [StringComparison]::OrdinalIgnoreCase)) { throw "M1 STOP FAIL: Refusing to stop PID $($component.Pid); executable path does not match tracked $($component.Name)." }
    Stop-Process -Id $process.Id
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds(8)
    while ($null -ne (Get-Process -Id $process.Id -ErrorAction SilentlyContinue) -and [DateTimeOffset]::UtcNow -lt $deadline) {
        Start-Sleep -Milliseconds 200
    }
    if ($null -ne (Get-Process -Id $process.Id -ErrorAction SilentlyContinue)) { throw "M1 STOP FAIL: Tracked $($component.Name) did not stop within 8 seconds." }
}
Remove-Item $state -Force
if (-not $Quiet) { Write-Output 'M1 STOP PASS: Only tracked TuoiTho M1 components were stopped. TestMode was never disabled; no lock/logout/reboot was performed.' }
