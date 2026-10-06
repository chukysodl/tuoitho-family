param(
    [string]$InstallDir = (Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::ProgramFiles)) 'QuanLyThoiGian'),
    [int]$WaitSeconds = 30
)

$ErrorActionPreference = 'Stop'
$failures = New-Object 'System.Collections.Generic.List[string]'
$programData = Join-Path $env:ProgramData 'TuoiTho'
New-Item -ItemType Directory -Path $programData -Force | Out-Null
$script:CheckLogPath = Join-Path $programData 'postinstall-check.log'
Set-Content -LiteralPath $script:CheckLogPath -Value ("=== PRODUCTION CHECK " + [DateTimeOffset]::Now.ToString("O") + " ===") -Encoding UTF8

function Write-CheckLine([string]$line) {
    Write-Host $line
    try { Add-Content -LiteralPath $script:CheckLogPath -Value $line -Encoding UTF8 } catch { }
}

function Pass([string]$name, [string]$detail) {
    Write-CheckLine "[PASS] $name - $detail"
}

function Fail([string]$name, [string]$detail) {
    Write-CheckLine "[FAIL] $name - $detail"
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

$hardenedAcl = $false
$sdshow = ''
$aclDeadline = [DateTimeOffset]::UtcNow.AddSeconds([Math]::Max(4, $WaitSeconds))
do {
    $sdshow = (& sc.exe sdshow $serviceName 2>&1 | Out-String).Trim()
    if ($LASTEXITCODE -eq 0 -and
        $sdshow -match '\(A;;GA;;;SY\)' -and
        $sdshow -match '\(A;;GRRP;;;BA\)' -and
        $sdshow -notmatch '\(A;;GA;;;BA\)') {
        $hardenedAcl = $true
        break
    }
    Start-Sleep -Milliseconds 500
} while ([DateTimeOffset]::UtcNow -lt $aclDeadline)

if ($hardenedAcl) {
    Pass 'Tamper protection' 'Service DACL is hardened.'
}
else {
    Fail 'Tamper protection' "Service DACL is not hardened yet. Current=$sdshow"
}

$qfailure = (& sc.exe qfailure $serviceName 2>&1 | Out-String)
if ($LASTEXITCODE -eq 0 -and $qfailure -match 'RESTART') {
    Pass 'Service Recovery' 'Restart action is configured.'
}
else {
    Fail 'Service Recovery' 'Restart action could not be confirmed.'
}

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

$agentExe = [IO.Path]::GetFullPath((Join-Path $InstallDir 'SessionAgent\TuoiTho.SessionAgent.exe'))
$agentRunning = $false
$agentSession = $null
$deadline = [DateTimeOffset]::UtcNow.AddSeconds([Math]::Max(0, $WaitSeconds))

do {
    foreach ($process in Get-Process -Name 'TuoiTho.SessionAgent' -ErrorAction SilentlyContinue) {
        try {
            if ($process.Path -and
                [IO.Path]::GetFullPath($process.Path) -eq $agentExe -and
                $process.SessionId -gt 0) {
                $agentRunning = $true
                $agentSession = $process.SessionId
                break
            }
        }
        catch { }
    }

    if ($agentRunning) { break }
    Start-Sleep -Milliseconds 500
} while ([DateTimeOffset]::UtcNow -lt $deadline)

if ($agentRunning) {
    Pass 'SessionAgent watchdog' "Agent is running in interactive session $agentSession."
}
else {
    Fail 'SessionAgent watchdog' 'Production SessionAgent was not found in an interactive session.'
}

$policyDb = Join-Path $programData 'tuoitho.db'
$dbDeadline = [DateTimeOffset]::UtcNow.AddSeconds([Math]::Max(4, $WaitSeconds))
do {
    if (Test-Path $policyDb) { break }
    Start-Sleep -Milliseconds 500
} while ([DateTimeOffset]::UtcNow -lt $dbDeadline)

if (Test-Path $policyDb) {
    Pass 'Policy data' 'SQLite database exists.'
}
else {
    Fail 'Policy data' 'tuoitho.db is missing.'
}

if ($failures.Count -gt 0) {
    Write-CheckLine ''
    Write-CheckLine "PRODUCTION CHECK: FAIL ($($failures.Count))"
    Write-CheckLine "Log: $script:CheckLogPath"
    exit 3
}

Write-CheckLine ''
Write-CheckLine 'PRODUCTION CHECK: PASS'
Write-CheckLine "Log: $script:CheckLogPath"
exit 0
