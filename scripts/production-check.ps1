param(
    [string]$InstallDir = (Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::ProgramFiles)) 'QuanLyThoiGian'),
    [int]$WaitSeconds = 12
)

$ErrorActionPreference = 'Stop'
$failures = New-Object 'System.Collections.Generic.List[string]'

function Pass([string]$name, [string]$detail) {
    Write-Host "[PASS] $name - $detail"
}

function Fail([string]$name, [string]$detail) {
    Write-Host "[FAIL] $name - $detail"
    $failures.Add("$($name): $detail")
}

$serviceName = 'TuoiTho.Service'
$deadline = [DateTimeOffset]::UtcNow.AddSeconds([Math]::Max(0, $WaitSeconds))
$service = $null

do {
    $service = Get-CimInstance Win32_Service -Filter "Name='$serviceName'" -ErrorAction SilentlyContinue
    if ($service -and $service.State -eq 'Running') { break }
    Start-Sleep -Milliseconds 500
} while ([DateTimeOffset]::UtcNow -lt $deadline)

if (-not $service) {
    Fail 'Windows Service' 'TuoiTho.Service was not found.'
}
elseif ($service.State -ne 'Running') {
    Fail 'Windows Service' "State=$($service.State)"
}
else {
    Pass 'Windows Service' "Running PID=$($service.ProcessId)"
}

if ($service -and $service.StartMode -eq 'Auto') {
    Pass 'Service startup' 'Automatic'
}
else {
    $startMode = if ($service) { $service.StartMode } else { 'MISSING' }
    Fail 'Service startup' "StartMode=$startMode"
}

$qfailure = (& sc.exe qfailure $serviceName 2>&1 | Out-String)
if ($LASTEXITCODE -eq 0 -and $qfailure -match 'RESTART') {
    Pass 'Service Recovery' 'Restart action is configured.'
}
else {
    Fail 'Service Recovery' 'Restart action could not be confirmed.'
}

$programData = Join-Path $env:ProgramData 'TuoiTho'
$productionPath = Join-Path $programData 'production.json'

if (Test-Path $productionPath) {
    try {
        $production = Get-Content $productionPath -Raw | ConvertFrom-Json
        if ($production.ProductionProtection.Enabled -eq $true -and
            [int]$production.ProductionProtection.PollIntervalSeconds -le 2) {
            Pass 'Production protection' "Enabled, watchdog=$($production.ProductionProtection.PollIntervalSeconds)s"
        }
        else {
            Fail 'Production protection' 'ProductionProtection is not enabled correctly.'
        }
    }
    catch {
        Fail 'Production protection' $_.Exception.Message
    }
}
else {
    Fail 'Production protection' 'production.json is missing.'
}

$authPath = Join-Path $programData 'parent-auth.json'
if (Test-Path $authPath) {
    Pass 'Parent password' 'Local verifier exists.'
}
else {
    Fail 'Parent password' 'Parent password has not been configured.'
}

$agentExe = Join-Path $InstallDir 'SessionAgent\TuoiTho.SessionAgent.exe'
$currentSession = (Get-Process -Id $PID).SessionId
$agentRunning = $false
$deadline = [DateTimeOffset]::UtcNow.AddSeconds([Math]::Max(0, $WaitSeconds))

do {
    foreach ($process in Get-Process -Name 'TuoiTho.SessionAgent' -ErrorAction SilentlyContinue) {
        try {
            if ($process.SessionId -eq $currentSession -and
                $process.Path -and
                [IO.Path]::GetFullPath($process.Path) -eq [IO.Path]::GetFullPath($agentExe)) {
                $agentRunning = $true
                break
            }
        }
        catch { }
    }

    if ($agentRunning) { break }
    Start-Sleep -Milliseconds 500
} while ([DateTimeOffset]::UtcNow -lt $deadline)

if ($agentRunning) {
    Pass 'SessionAgent watchdog' 'Agent is running in the current session.'
}
else {
    Fail 'SessionAgent watchdog' 'Production SessionAgent was not found in the current session.'
}

$policyDb = Join-Path $programData 'tuoitho.db'
if (Test-Path $policyDb) {
    Pass 'Policy data' 'SQLite database exists.'
}
else {
    Fail 'Policy data' 'tuoitho.db is missing.'
}

if ($failures.Count -gt 0) {
    Write-Host ''
    Write-Host "PRODUCTION CHECK: FAIL ($($failures.Count))"
    exit 3
}

Write-Host ''
Write-Host 'PRODUCTION CHECK: PASS'
exit 0
