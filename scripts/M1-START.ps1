param()
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
$state=Join-Path $env:TEMP 'tuoitho-m1-state.json'
$sid=[Security.Principal.WindowsIdentity]::GetCurrent().User.Value
$session=(Get-Process -Id $PID).SessionId
$env:TimeTracking__ProfileId='m1-child';$env:TimeTracking__SessionId=$session
$env:SessionAgent__ProfileId='m1-child'
$env:ParentControl__AllowedParentSids__0=$sid
$env:M1Bootstrap__Enabled='true';$env:M1Bootstrap__ProfileId='m1-child';$env:M1Bootstrap__ManagedSessionId=$session;$env:M1Bootstrap__ManagedUserSid=$sid;$env:M1Bootstrap__QuotaMinutes='3'
$service=Start-Process dotnet -ArgumentList 'run','--project',(Join-Path $root 'src/TuoiTho.Service'),'--configuration','Release','--no-build' -WorkingDirectory $root -PassThru
Start-Sleep -Seconds 2
$agent=Start-Process dotnet -ArgumentList 'run','--project',(Join-Path $root 'src/TuoiTho.SessionAgent'),'--configuration','Release','--no-build' -WorkingDirectory $root -PassThru
@{ServicePid=$service.Id;AgentPid=$agent.Id;ProfileId='m1-child';SessionId=$session;TestMode=$true}|ConvertTo-Json|Set-Content $state
$env:M1_PROFILE_ID='m1-child';$env:M1_SESSION_ID=$session
dotnet run --project (Join-Path $root 'src/TuoiTho.Parent') --configuration Release --no-build