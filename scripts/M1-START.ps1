param()
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$state = Join-Path $env:TEMP 'tuoitho-m1-state.json'
$modeFile = Join-Path $env:ProgramData 'TuoiTho\run-mode.txt'
$realMode = $false
if (Test-Path $modeFile) {
    try { $realMode = ((Get-Content -LiteralPath $modeFile -Raw).Trim().ToUpperInvariant() -eq 'REAL') } catch { $realMode = $false }
}
$testMode = -not $realMode
$runtimeConfigPath = Join-Path $env:ProgramData 'TuoiTho\runtime-control.json'

# Only this state file may identify old M1 components; M1-STOP verifies every executable path.
& (Join-Path $PSScriptRoot 'M1-STOP.ps1') -Quiet

$sid = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
$session = (Get-Process -Id $PID).SessionId
$env:TimeTracking__ProfileId = 'm1-child'; $env:TimeTracking__SessionId = $session
$env:TimeTracking__IdleThresholdMinutes='1';$env:TimeTracking__IdlePollIntervalSeconds='5'
$env:SessionAgent__ProfileId = 'm1-child'
$env:SessionAgent__M1TestMode = $testMode.ToString().ToLowerInvariant(); $env:SessionAgent__M1WarningPublisherSid=$sid
$env:ParentControl__AllowedParentSids__0 = $sid
$env:M1Bootstrap__Enabled = 'true'; $env:M1Bootstrap__ProfileId = 'm1-child'; $env:M1Bootstrap__ManagedSessionId = $session; $env:M1Bootstrap__ManagedUserSid = $sid; $env:M1Bootstrap__QuotaMinutes = '3'; $env:M1Bootstrap__TestMode = $testMode.ToString().ToLowerInvariant()
$env:M1_PROFILE_ID = 'm1-child'; $env:M1_SESSION_ID = $session
$activityTrace = Join-Path $env:TEMP 'tuoitho-m1-activity.csv'
'Timestamp,RawInputIdleSeconds,WindowsIdleSeconds,ActivityState,RecordedTodaySeconds' | Set-Content $activityTrace

& dotnet build (Join-Path $root 'TuoiTho.sln') --configuration Release
if ($LASTEXITCODE -ne 0) { Write-Output 'M1 START FAIL: Release build failed.'; exit $LASTEXITCODE }
Write-Output 'M1 START PASS: Release build succeeded.'
Write-Output ("M1 START: SID={0} Session={1} TestMode={2} Mode={3}" -f $sid,$session,$testMode,$(if($realMode){'REAL'}else{'TEST'}))

$serviceExe = Join-Path $root 'src/TuoiTho.Service/bin/Release/net10.0-windows/TuoiTho.Service.exe'
$agentExe = Join-Path $root 'src/TuoiTho.SessionAgent/bin/Release/net10.0-windows/TuoiTho.SessionAgent.exe'
$parentExe = Join-Path $root 'src/TuoiTho.Parent/bin/Release/net10.0-windows/TuoiTho.Parent.exe'
$browserHostExe = Join-Path $root 'src/TuoiTho.BrowserHost/bin/Release/net10.0-windows/TuoiTho.BrowserHost.exe'
$env:SessionAgent__ParentExecutablePath = $parentExe

$runtimeControl = [ordered]@{
    TimeTracking = [ordered]@{
        ProfileId = 'm1-child'
        SessionId = $session
        IdleThresholdMinutes = 1
        IdlePollIntervalSeconds = 5
    }
    ParentControl = [ordered]@{
        AllowedParentSids = @($sid)
    }
    M1Bootstrap = [ordered]@{
        Enabled = $true
        ProfileId = 'm1-child'
        ManagedSessionId = $session
        ManagedUserSid = $sid
        QuotaMinutes = 3
        TestMode = $testMode
    }
}
$runtimeConfigDir = Split-Path -Parent $runtimeConfigPath
New-Item -ItemType Directory -Path $runtimeConfigDir -Force | Out-Null
$runtimeControl | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $runtimeConfigPath -Encoding UTF8

$env:SessionAgent__ParentExecutablePath = $parentExe
foreach ($exe in @($serviceExe, $agentExe, $parentExe, $browserHostExe)) {
    if (-not (Test-Path $exe)) { Write-Output "M1 START FAIL: Missing built executable: $exe"; exit 2 }
}

function Test-M1ParentServiceReady {
    param([int]$ManagedSessionId)
    try {
        $pipe = [System.IO.Pipes.NamedPipeClientStream]::new('.', 'TuoiTho.ParentControl', [System.IO.Pipes.PipeDirection]::InOut, [System.IO.Pipes.PipeOptions]::Asynchronous)
        try {
            $pipe.Connect(750)
            $writer = New-Object System.IO.StreamWriter($pipe)
            $writer.AutoFlush = $true
            $writer.WriteLine((@{ Action = 5; ProfileId = 'm1-child'; ManagedSessionId = $ManagedSessionId } | ConvertTo-Json -Compress))
            $reader = New-Object System.IO.StreamReader($pipe)
            $line = $reader.ReadLine()
            if ([string]::IsNullOrWhiteSpace($line)) { return $false }
            $response = $line | ConvertFrom-Json
            return $response.Accepted -eq $true -and $null -ne $response.Status -and $response.Status.ProfileId -eq 'm1-child' -and [int]$response.Status.ManagedSessionId -eq $ManagedSessionId
        }
        finally { $pipe.Dispose() }
    }
    catch { return $false }
}

function Test-M1BrowserPolicyReady {
    param([string]$BrowserHost)
    try {
        $output = & $BrowserHost --service-probe 2>&1
        if ($LASTEXITCODE -eq 0 -and (($output | Out-String) -match '"(Allowed|allowed)"')) { return @{ Ready = $true; Detail = 'PASS' } }
        return @{ Ready = $false; Detail = (($output | Out-String).Trim()) }
    } catch { return @{ Ready = $false; Detail = $_.Exception.Message } }
}
# Background components stay hidden. In REAL mode the enforcement engine must run
# as a Windows Service under LocalSystem so WTS session identity verification and
# WTSDisconnectSession can safely operate on the managed child session.
$service = $null
$servicePid = 0
if ($realMode) {
    $serviceName = 'TuoiTho.Service'
    $existingService = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
    if ($existingService -and $existingService.Status -eq 'Running') {
        Stop-Service -Name $serviceName -Force
        $existingService.WaitForStatus('Stopped', [TimeSpan]::FromSeconds(20))
    }

    if ($null -eq $existingService) {
        $createOutput = & sc.exe create $serviceName "binPath= `"$serviceExe`"" "start= auto" "obj= LocalSystem" 2>&1
        if ($LASTEXITCODE -ne 0) {
            Write-Output ('M1 START FAIL: Could not create LocalSystem service. ' + ($createOutput | Out-String).Trim())
            exit 5
        }
    } else {
        $configOutput = & sc.exe config $serviceName "binPath= `"$serviceExe`"" "start= auto" "obj= LocalSystem" 2>&1
        if ($LASTEXITCODE -ne 0) {
            Write-Output ('M1 START FAIL: Could not configure LocalSystem service. ' + ($configOutput | Out-String).Trim())
            exit 5
        }
    }

    & sc.exe start $serviceName | Out-Null
    if ($LASTEXITCODE -ne 0) {
        Write-Output 'M1 START FAIL: Could not start TuoiTho.Service as LocalSystem.'
        exit 5
    }

    for ($attempt = 1; $attempt -le 20; $attempt++) {
        Start-Sleep -Milliseconds 250
        $svcInfo = Get-CimInstance Win32_Service -Filter "Name='TuoiTho.Service'" -ErrorAction SilentlyContinue
        if ($svcInfo -and $svcInfo.State -eq 'Running' -and [int]$svcInfo.ProcessId -gt 0) {
            $servicePid = [int]$svcInfo.ProcessId
            break
        }
    }
    if ($servicePid -le 0) {
        Write-Output 'M1 START FAIL: LocalSystem service did not reach Running state.'
        exit 5
    }
    Write-Output ("M1 START: ServiceHost=LocalSystem PID={0}" -f $servicePid)
} else {
    $service = Start-Process -FilePath $serviceExe -WorkingDirectory (Split-Path $serviceExe) -WindowStyle Hidden -PassThru
    $servicePid = $service.Id
}

$ready = $false
for ($attempt = 1; $attempt -le 20; $attempt++) {
    if (-not $realMode -and $service.HasExited) { break }
    if (Test-M1ParentServiceReady $session) { $ready = $true; break }
    Start-Sleep -Milliseconds 500
}
if (-not $ready) {
    if ($realMode) {
        Stop-Service -Name 'TuoiTho.Service' -Force -ErrorAction SilentlyContinue
    } elseif ($null -ne $service -and -not $service.HasExited) {
        Stop-Process -Id $service.Id
    }
    Write-Output 'M1 START FAIL: Parent Service did not become ready with the current protocol.'
    exit 3
}
Write-Output 'M1 START: ParentControl: PASS'

$browserConfig = Join-Path $env:ProgramData 'TuoiTho\browser-control.json'
if (Test-Path $browserConfig) {
    $browserReady = $false; $browserDetail = ''
    for ($attempt = 1; $attempt -le 12; $attempt++) {
        if (-not $realMode -and $service.HasExited) { break }
        $probe = Test-M1BrowserPolicyReady $browserHostExe
        $browserDetail = $probe.Detail
        if ($probe.Ready) { $browserReady = $true; break }
        Start-Sleep -Milliseconds 500
    }
    if (-not $browserReady) {
        if ($realMode) { Stop-Service -Name 'TuoiTho.Service' -Force -ErrorAction SilentlyContinue }
        elseif ($null -ne $service -and -not $service.HasExited) { Stop-Process -Id $service.Id }
        Write-Output ("M1 START FAIL: BrowserPolicy: FAIL - " + $browserDetail)
        exit 4
    }
    Write-Output 'M1 START: BrowserPolicy: PASS'
} else { Write-Output 'M1 START: BrowserPolicy: SKIPPED (browser-control.json chưa được cài).' }

$agent = Start-Process -FilePath $agentExe -WorkingDirectory (Split-Path $agentExe) -WindowStyle Hidden -PassThru
$parent = Start-Process -FilePath $parentExe -WorkingDirectory (Split-Path $parentExe) -PassThru
@{
    Version = 2
    Components = @(
        @{ Name = 'Service'; Pid = $servicePid; ExecutablePath = $serviceExe },
        @{ Name = 'SessionAgent'; Pid = $agent.Id; ExecutablePath = $agentExe },
        @{ Name = 'Parent'; Pid = $parent.Id; ExecutablePath = $parentExe }
    )
    ProfileId = 'm1-child'; SessionId = $session; TestMode = $testMode; Mode = $(if($realMode){'REAL'}else{'TEST'})
} | ConvertTo-Json -Depth 4 | Set-Content $state
Write-Output 'M1 START PASS: Fresh Service protocol ready; Background components hidden; Parent desktop UI opened.'
