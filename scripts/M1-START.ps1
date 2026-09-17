param()
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$state = Join-Path $env:TEMP 'tuoitho-m1-state.json'

# Only this state file may identify old M1 components; M1-STOP verifies every executable path.
& (Join-Path $PSScriptRoot 'M1-STOP.ps1') -Quiet

$sid = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
$session = (Get-Process -Id $PID).SessionId
$env:TimeTracking__ProfileId = 'm1-child'; $env:TimeTracking__SessionId = $session
$env:TimeTracking__IdleThresholdMinutes='1';$env:TimeTracking__IdlePollIntervalSeconds='5'
$env:SessionAgent__ProfileId = 'm1-child'
$env:SessionAgent__M1TestMode='true';$env:SessionAgent__M1WarningPublisherSid=$sid
$env:ParentControl__AllowedParentSids__0 = $sid
$env:M1Bootstrap__Enabled = 'true'; $env:M1Bootstrap__ProfileId = 'm1-child'; $env:M1Bootstrap__ManagedSessionId = $session; $env:M1Bootstrap__ManagedUserSid = $sid; $env:M1Bootstrap__QuotaMinutes = '3'
$env:M1_PROFILE_ID = 'm1-child'; $env:M1_SESSION_ID = $session
$activityTrace = Join-Path $env:TEMP 'tuoitho-m1-activity.csv'
'Timestamp,RawInputIdleSeconds,WindowsIdleSeconds,ActivityState,RecordedTodaySeconds' | Set-Content $activityTrace

& dotnet build (Join-Path $root 'TuoiTho.sln') --configuration Release
if ($LASTEXITCODE -ne 0) { Write-Output 'M1 START FAIL: Release build failed.'; exit $LASTEXITCODE }
Write-Output 'M1 START PASS: Release build succeeded.'
Write-Output "M1 START: SID=$sid Session=$session TestMode=true Quota=3"

$serviceExe = Join-Path $root 'src/TuoiTho.Service/bin/Release/net10.0-windows/TuoiTho.Service.exe'
$agentExe = Join-Path $root 'src/TuoiTho.SessionAgent/bin/Release/net10.0-windows/TuoiTho.SessionAgent.exe'
$parentExe = Join-Path $root 'src/TuoiTho.Parent/bin/Release/net10.0-windows/TuoiTho.Parent.exe'
$env:SessionAgent__ParentExecutablePath = $parentExe
foreach ($exe in @($serviceExe, $agentExe, $parentExe)) {
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

# Background components stay hidden. Parent is launched only after the fresh Service proves protocol readiness.
$service = Start-Process -FilePath $serviceExe -WorkingDirectory (Split-Path $serviceExe) -WindowStyle Hidden -PassThru
$ready = $false
for ($attempt = 1; $attempt -le 20; $attempt++) {
    if ($service.HasExited) { break }
    if (Test-M1ParentServiceReady $session) { $ready = $true; break }
    Start-Sleep -Milliseconds 500
}
if (-not $ready) {
    if (-not $service.HasExited) { Stop-Process -Id $service.Id }
    Write-Output 'M1 START FAIL: Parent Service did not become ready with the current protocol.'
    exit 3
}

$agent = Start-Process -FilePath $agentExe -WorkingDirectory (Split-Path $agentExe) -WindowStyle Hidden -PassThru
$parent = Start-Process -FilePath $parentExe -WorkingDirectory (Split-Path $parentExe) -PassThru
@{
    Version = 2
    Components = @(
        @{ Name = 'Service'; Pid = $service.Id; ExecutablePath = $serviceExe },
        @{ Name = 'SessionAgent'; Pid = $agent.Id; ExecutablePath = $agentExe },
        @{ Name = 'Parent'; Pid = $parent.Id; ExecutablePath = $parentExe }
    )
    ProfileId = 'm1-child'; SessionId = $session; TestMode = $true
} | ConvertTo-Json -Depth 4 | Set-Content $state
Write-Output 'M1 START PASS: Fresh Service protocol ready; Background components hidden; Parent desktop UI opened.'