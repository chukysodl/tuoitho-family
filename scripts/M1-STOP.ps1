param([switch]$Quiet)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$state = Join-Path $env:TEMP 'tuoitho-m1-state.json'
$expected = @{
    Service = Join-Path $root 'src/TuoiTho.Service/bin/Release/net10.0-windows/TuoiTho.Service.exe'
    SessionAgent = Join-Path $root 'src/TuoiTho.SessionAgent/bin/Release/net10.0-windows/TuoiTho.SessionAgent.exe'
    Parent = Join-Path $root 'src/TuoiTho.Parent/bin/Release/net10.0-windows/TuoiTho.Parent.exe'
}
$stopped = @()
function Stop-ExactM1Process {
    param([string]$Name, [int]$ProcessId, [string]$ExpectedPath)
    $process = Get-Process -Id $ProcessId -ErrorAction SilentlyContinue
    if ($null -eq $process) { return $false }
    if (-not [string]::Equals($process.Path, $ExpectedPath, [StringComparison]::OrdinalIgnoreCase)) { throw "M1 STOP FAIL: Refusing to stop PID $ProcessId; executable path does not match tracked $Name." }
    Stop-Process -Id $process.Id
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds(8)
    while ($null -ne (Get-Process -Id $process.Id -ErrorAction SilentlyContinue) -and [DateTimeOffset]::UtcNow -lt $deadline) { Start-Sleep -Milliseconds 200 }
    if ($null -ne (Get-Process -Id $process.Id -ErrorAction SilentlyContinue)) { throw "M1 STOP FAIL: Tracked $Name did not stop within 8 seconds." }
    $script:stopped += "$Name ($ProcessId)"
    return $true
}
function Stop-ExactPathFallback {
    foreach ($entry in $expected.GetEnumerator()) {
        Get-Process -Name ([IO.Path]::GetFileNameWithoutExtension($entry.Value)) -ErrorAction SilentlyContinue | ForEach-Object {
            if ([string]::Equals($_.Path, $entry.Value, [StringComparison]::OrdinalIgnoreCase)) { [void](Stop-ExactM1Process $entry.Key $_.Id $entry.Value) }
        }
    }
}
$validState = $false
if (Test-Path $state) {
    try {
        $tracked = Get-Content $state -Raw | ConvertFrom-Json
        if ($null -ne $tracked.Components) {
            foreach ($component in @($tracked.Components)) {
                if ([string]::IsNullOrWhiteSpace([string]$component.Name) -or -not $expected.ContainsKey([string]$component.Name)) { throw 'unrecognized component' }
                $expectedPath = $expected[[string]$component.Name]
                if (-not [string]::Equals([string]$component.ExecutablePath, $expectedPath, [StringComparison]::OrdinalIgnoreCase)) { throw 'state executable path mismatch' }
                [void](Stop-ExactM1Process $component.Name ([int]$component.Pid) $expectedPath)
            }
            $validState = $true
        }
    } catch { if (-not $Quiet) { Write-Output 'M1 STOP: State missing or invalid; using exact-path fallback.' } }
}
if (-not $validState) { Stop-ExactPathFallback }
if (Test-Path $state) { Remove-Item $state -Force }
if (-not $Quiet) {
    if ($stopped.Count -gt 0) { Write-Output ('M1 STOP PASS: Stopped only current-repo M1 components: ' + ($stopped -join ', ') + '. TestMode was never disabled; no lock/logout/reboot was performed.') }
    else { Write-Output 'M1 STOP: No matching M1 components found in the current repo.' }
}
