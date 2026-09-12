param()
$ErrorActionPreference='Stop'
$state=Join-Path $env:TEMP 'tuoitho-m1-state.json'
if(Test-Path $state){$s=Get-Content $state|ConvertFrom-Json;foreach($id in @($s.AgentPid,$s.ServicePid)){if(Get-Process -Id $id -ErrorAction SilentlyContinue){Stop-Process -Id $id}};Remove-Item $state -Force}
Write-Output 'M1 components stopped. TestMode was never disabled; no lock/logout/reboot was performed.'