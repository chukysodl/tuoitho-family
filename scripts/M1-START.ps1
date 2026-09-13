param()
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
$state=Join-Path $env:TEMP 'tuoitho-m1-state.json'
$sid=[Security.Principal.WindowsIdentity]::GetCurrent().User.Value
$session=(Get-Process -Id $PID).SessionId

$env:TimeTracking__ProfileId='m1-child';$env:TimeTracking__SessionId=$session
$env:TimeTracking__IdleThresholdMinutes='1';$env:TimeTracking__IdlePollIntervalSeconds='5'
$env:SessionAgent__ProfileId='m1-child'
$env:SessionAgent__M1TestMode='true';$env:SessionAgent__M1WarningPublisherSid=$sid
$env:ParentControl__AllowedParentSids__0=$sid
$env:M1Bootstrap__Enabled='true';$env:M1Bootstrap__ProfileId='m1-child';$env:M1Bootstrap__ManagedSessionId=$session;$env:M1Bootstrap__ManagedUserSid=$sid;$env:M1Bootstrap__QuotaMinutes='3'
$env:M1_PROFILE_ID='m1-child';$env:M1_SESSION_ID=$session
$activityTrace=Join-Path $env:TEMP 'tuoitho-m1-activity.csv'
'Timestamp,RawInputIdleSeconds,WindowsIdleSeconds,ActivityState,RecordedTodaySeconds'|Set-Content $activityTrace

& dotnet build (Join-Path $root 'TuoiTho.sln') --configuration Release
if($LASTEXITCODE -ne 0){Write-Output 'M1 START FAIL: Release build failed.';exit $LASTEXITCODE}
Write-Output 'M1 START PASS: Release build succeeded.'
Write-Output "M1 START: SID=$sid Session=$session TestMode=true Quota=3"

$serviceExe=Join-Path $root 'src/TuoiTho.Service/bin/Release/net10.0-windows/TuoiTho.Service.exe'
$agentExe=Join-Path $root 'src/TuoiTho.SessionAgent/bin/Release/net10.0-windows/TuoiTho.SessionAgent.exe'
$parentExe=Join-Path $root 'src/TuoiTho.Parent/bin/Release/net10.0-windows/TuoiTho.Parent.exe'
foreach($exe in @($serviceExe,$agentExe,$parentExe)){
    if(-not (Test-Path $exe)){Write-Output "M1 START FAIL: Missing built executable: $exe";exit 2}
}

# Background components must stay invisible. Only the Parent desktop UI is shown.
$service=Start-Process -FilePath $serviceExe -WorkingDirectory (Split-Path $serviceExe) -WindowStyle Hidden -PassThru
Start-Sleep -Seconds 2
$agent=Start-Process -FilePath $agentExe -WorkingDirectory (Split-Path $agentExe) -WindowStyle Hidden -PassThru
$parent=Start-Process -FilePath $parentExe -WorkingDirectory (Split-Path $parentExe) -PassThru

@{ServicePid=$service.Id;AgentPid=$agent.Id;ParentPid=$parent.Id;ProfileId='m1-child';SessionId=$session;TestMode=$true}|ConvertTo-Json|Set-Content $state
Write-Output 'M1 START PASS: Background components hidden; Parent desktop UI opened.'
