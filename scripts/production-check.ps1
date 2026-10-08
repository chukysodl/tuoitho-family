param(
    [string]$InstallDir = (Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::ProgramFiles)) 'QuanLyThoiGian'),
    [int]$WaitSeconds = 30
)

$ErrorActionPreference = 'Stop'
$coreFailures = New-Object 'System.Collections.Generic.List[string]'
$warnings = New-Object 'System.Collections.Generic.List[string]'
$programData = Join-Path $env:ProgramData 'TuoiTho'
New-Item -ItemType Directory -Path $programData -Force | Out-Null
$script:CheckLogPath = Join-Path $programData 'postinstall-check.log'
$script:SummaryPath = Join-Path $programData 'postinstall-summary.txt'
Set-Content -LiteralPath $script:CheckLogPath -Value ('=== PRODUCTION CHECK ' + [DateTimeOffset]::Now.ToString('O') + ' ===') -Encoding UTF8
Remove-Item -LiteralPath $script:SummaryPath -Force -ErrorAction SilentlyContinue

function Write-CheckLine([string]$line) {
    Write-Host $line
    try { Add-Content -LiteralPath $script:CheckLogPath -Value $line -Encoding UTF8 } catch { }
}

function Pass([string]$name, [string]$detail) { Write-CheckLine "[PASS] $name - $detail" }
function CoreFail([string]$name, [string]$detail) { Write-CheckLine "[FAIL] $name - $detail"; $coreFailures.Add("$name - $detail") }
function Warn([string]$name, [string]$detail) { Write-CheckLine "[WARN] $name - $detail"; $warnings.Add("$name - $detail") }

function Get-ServiceAceRights([string]$sddl, [string]$trustee) {
    $pattern = '\(A;;(?<rights>[^;]*);;;' + [regex]::Escape($trustee) + '\)'
    $match = [regex]::Match($sddl, $pattern, [Text.RegularExpressions.RegexOptions]::IgnoreCase)
    if (-not $match.Success) { return $null }
    return $match.Groups['rights'].Value.ToUpperInvariant()
}

function Test-AgentRunning([string]$agentExe) {
    $expected = [IO.Path]::GetFullPath($agentExe)
    foreach ($process in Get-Process -Name 'TuoiTho.SessionAgent' -ErrorAction SilentlyContinue) {
        try {
            if ($process.SessionId -gt 0 -and $process.Path -and [IO.Path]::GetFullPath($process.Path) -eq $expected) { return $process }
        } catch { }
    }
    return $null
}

$serviceName = 'TuoiTho.Service'
$deadline = [DateTimeOffset]::UtcNow.AddSeconds([Math]::Max(4, $WaitSeconds))
$service = $null
do {
    $service = Get-CimInstance Win32_Service -Filter "Name='$serviceName'" -ErrorAction SilentlyContinue
    if ($service -and $service.State -eq 'Running') { break }
    Start-Sleep -Milliseconds 500
} while ([DateTimeOffset]::UtcNow -lt $deadline)

if (-not $service) {
    CoreFail 'Windows Service' 'TuoiTho.Service was not found.'
}
elseif ($service.State -ne 'Running') {
    Write-CheckLine "[INFO] Windows Service state=$($service.State). Attempting one self-heal start."
    & sc.exe start $serviceName | Out-Null
    Start-Sleep -Seconds 2
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds(12)
    do {
        $service = Get-CimInstance Win32_Service -Filter "Name='$serviceName'" -ErrorAction SilentlyContinue
        if ($service -and $service.State -eq 'Running') { break }
        Start-Sleep -Milliseconds 500
    } while ([DateTimeOffset]::UtcNow -lt $deadline)

    if ($service -and $service.State -eq 'Running') {
        Pass 'Windows Service' "Self-healed to Running PID=$($service.ProcessId)"
    } else {
        $state = if ($service) { $service.State } else { 'MISSING' }
        CoreFail 'Windows Service' "State=$state after self-heal start attempt"
    }
}
else { Pass 'Windows Service' "Running PID=$($service.ProcessId)" }

if ($service -and $service.StartMode -eq 'Auto') { Pass 'Service startup' 'Automatic' }
else {
    $startMode = if ($service) { $service.StartMode } else { 'MISSING' }
    CoreFail 'Service startup' "StartMode=$startMode"
}

$sdshow = (& sc.exe sdshow $serviceName 2>&1 | Out-String).Trim()
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($sdshow)) {
    CoreFail 'Tamper protection' 'Service DACL could not be read.'
} else {
    $systemRights = Get-ServiceAceRights $sdshow 'SY'
    $adminRights = Get-ServiceAceRights $sdshow 'BA'
    if ([string]::IsNullOrWhiteSpace($systemRights) -or [string]::IsNullOrWhiteSpace($adminRights)) {
        CoreFail 'Tamper protection' "Required SYSTEM/Administrators ACE is missing. Current=$sdshow"
    } else {
        $dangerousAdminRights = @('GA','GW','WP','DC','SD','WD','WO')
        $foundDangerous = @($dangerousAdminRights | Where-Object { $adminRights.Contains($_) })
        if ($foundDangerous.Count -gt 0) {
            CoreFail 'Tamper protection' "Administrators still have protected service rights: $($foundDangerous -join ','). BA=$adminRights"
        } else {
            Pass 'Tamper protection' "Service DACL hardened. BA=$adminRights"
        }
    }
}

$qfailure = (& sc.exe qfailure $serviceName 2>&1 | Out-String)
if ($LASTEXITCODE -eq 0 -and $qfailure -match 'RESTART') { Pass 'Service Recovery' 'Restart action is configured.' }
else { CoreFail 'Service Recovery' 'Restart action could not be confirmed.' }

$productionPath = Join-Path $programData 'production.json'
if (Test-Path $productionPath) {
    Pass 'Production protection' 'Machine configuration file exists. Effective protection is verified through running Service + hardened DACL.'
} else {
    CoreFail 'Production protection' 'production.json is missing.'
}

$authPath = Join-Path $programData 'parent-auth.json'
$adminTool = Join-Path $InstallDir 'AdminTool\TuoiTho.AdminTool.exe'
if (-not (Test-Path $authPath)) { CoreFail 'Parent password' 'parent-auth.json is missing.' }
elseif (-not (Test-Path $adminTool)) { CoreFail 'Parent password' 'AdminTool is missing.' }
else {
    & $adminTool --auth-state | Out-Null
    if ($LASTEXITCODE -eq 0) { Pass 'Parent password' 'Local verifier is valid.' }
    else { CoreFail 'Parent password' "Local verifier is invalid. AuthState=$LASTEXITCODE" }
}

$policyDb = Join-Path $programData 'tuoitho.db'
$dbDeadline = [DateTimeOffset]::UtcNow.AddSeconds([Math]::Max(4, $WaitSeconds))
do {
    if (Test-Path $policyDb) { break }
    Start-Sleep -Milliseconds 500
} while ([DateTimeOffset]::UtcNow -lt $dbDeadline)
if (Test-Path $policyDb) { Pass 'Policy data' 'SQLite database exists.' }
else { CoreFail 'Policy data' 'tuoitho.db is missing.' }

$agentExe = [IO.Path]::GetFullPath((Join-Path $InstallDir 'SessionAgent\TuoiTho.SessionAgent.exe'))
$agent = $null
$agentDeadline = [DateTimeOffset]::UtcNow.AddSeconds([Math]::Min(12, [Math]::Max(4, $WaitSeconds)))
do {
    $agent = Test-AgentRunning $agentExe
    if ($agent) { break }
    Start-Sleep -Milliseconds 500
} while ([DateTimeOffset]::UtcNow -lt $agentDeadline)

if (-not $agent -and (Test-Path $agentExe) -and [Diagnostics.Process]::GetCurrentProcess().SessionId -gt 0) {
    try {
        $profileId = 'm1-child'
        $parentExe = Join-Path $InstallDir 'Parent\TuoiTho.Parent.exe'
        $argumentLine = '--profile-id "' + $profileId + '" --publisher-sid S-1-5-18 --real'
        if (Test-Path $parentExe) { $argumentLine += ' --parent-exe "' + $parentExe.Replace('"', '') + '"' }
        Start-Process -FilePath $agentExe -ArgumentList $argumentLine -WindowStyle Hidden
        Write-CheckLine '[INFO] SessionAgent self-heal launch requested in current interactive session.'
        $selfHealDeadline = [DateTimeOffset]::UtcNow.AddSeconds(8)
        do {
            Start-Sleep -Milliseconds 500
            $agent = Test-AgentRunning $agentExe
            if ($agent) { break }
        } while ([DateTimeOffset]::UtcNow -lt $selfHealDeadline)
    } catch { Write-CheckLine ("[INFO] SessionAgent self-heal launch failed: " + $_.Exception.Message) }
}

if ($agent) { Pass 'SessionAgent watchdog' "Agent is running in interactive session $($agent.SessionId)." }
else { Warn 'SessionAgent watchdog' 'Interactive agent is not running yet. Core Service remains protected and will retry automatically; sign out/in or reboot once if the lock overlay does not appear.' }

if ($coreFailures.Count -gt 0) {
    $summary = @('Core protection check failed:') + @($coreFailures | ForEach-Object { '- ' + $_ })
    if ($warnings.Count -gt 0) { $summary += ''; $summary += 'Warnings:'; $summary += @($warnings | ForEach-Object { '- ' + $_ }) }
    $summary | Set-Content -LiteralPath $script:SummaryPath -Encoding UTF8
    Write-CheckLine ''
    Write-CheckLine ("PRODUCTION CHECK: FAIL ($($coreFailures.Count) core failure(s))")
    Write-CheckLine "Summary: $script:SummaryPath"
    Write-CheckLine "Log: $script:CheckLogPath"
    exit 3
}

if ($warnings.Count -gt 0) {
    (@('Core protection: PASS','Warnings:') + @($warnings | ForEach-Object { '- ' + $_ })) | Set-Content -LiteralPath $script:SummaryPath -Encoding UTF8
    Write-CheckLine ''
    Write-CheckLine ("PRODUCTION CHECK: PASS WITH WARNINGS ($($warnings.Count))")
    Write-CheckLine "Log: $script:CheckLogPath"
    exit 0
}

@('Core protection: PASS','SessionAgent: PASS') | Set-Content -LiteralPath $script:SummaryPath -Encoding UTF8
Write-CheckLine ''
Write-CheckLine 'PRODUCTION CHECK: PASS'
Write-CheckLine "Log: $script:CheckLogPath"
exit 0
