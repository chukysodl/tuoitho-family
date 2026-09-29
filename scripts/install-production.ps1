param(
    [Parameter(Mandatory=$true)][string]$InstallDir,
    [string]$ProfileId = 'm1-child',
    [string]$ChromeExtensionId = '',
    [string]$EdgeExtensionId = ''
)
$ErrorActionPreference = 'Stop'

function Assert-Admin {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = [Security.Principal.WindowsPrincipal]::new($identity)
    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        throw 'Cài đặt production yêu cầu quyền Administrator.'
    }
}

function Test-BrowserInstalled([string]$exeName) {
    $pf = [Environment]::GetFolderPath([Environment+SpecialFolder]::ProgramFiles)
    $pfx86 = [Environment]::GetFolderPath([Environment+SpecialFolder]::ProgramFilesX86)
    return $null -ne (Get-Command $exeName -ErrorAction SilentlyContinue) -or
        (Test-Path (Join-Path $pf "Google\Chrome\Application\$exeName")) -or
        (Test-Path (Join-Path $pfx86 "Google\Chrome\Application\$exeName")) -or
        (Test-Path (Join-Path $pf "Microsoft\Edge\Application\$exeName")) -or
        (Test-Path (Join-Path $pfx86 "Microsoft\Edge\Application\$exeName"))
}

function Assert-ExtensionId([string]$id, [string]$label) {
    if ($id -notmatch '^[a-p]{32}$') { throw "$label extension ID chưa hợp lệ hoặc chưa được cấu hình." }
}

Assert-Admin
$InstallDir = [IO.Path]::GetFullPath($InstallDir)
$serviceExe = Join-Path $InstallDir 'Service\TuoiTho.Service.exe'
$agentExe = Join-Path $InstallDir 'SessionAgent\TuoiTho.SessionAgent.exe'
$parentExe = Join-Path $InstallDir 'Parent\TuoiTho.Parent.exe'
$browserHostExe = Join-Path $InstallDir 'BrowserHost\TuoiTho.BrowserHost.exe'
foreach ($file in @($serviceExe,$agentExe,$parentExe,$browserHostExe)) {
    if (-not (Test-Path -LiteralPath $file)) { throw "Thiếu thành phần cài đặt: $file" }
}

$chromePresent = Test-BrowserInstalled 'chrome.exe'
$edgePresent = Test-BrowserInstalled 'msedge.exe'
if ($chromePresent) { Assert-ExtensionId $ChromeExtensionId 'Chrome' }
if ($edgePresent) { Assert-ExtensionId $EdgeExtensionId 'Edge' }

$programData = Join-Path $env:ProgramData 'TuoiTho'
New-Item -ItemType Directory -Path $programData -Force | Out-Null

$currentSid = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
$production = [ordered]@{
    TimeTracking = [ordered]@{
        ProfileId = $ProfileId
        IdleThresholdMinutes = 5
        IdlePollIntervalSeconds = 5
    }
    ParentControl = [ordered]@{
        AllowedParentSids = @($currentSid)
    }
    M1Bootstrap = [ordered]@{
        Enabled = $false
    }
    ProductionProtection = [ordered]@{
        Enabled = $true
        ProfileId = $ProfileId
        SessionAgentPath = $agentExe
        ParentExecutablePath = $parentExe
        PollIntervalSeconds = 2
    }
}
$production | ConvertTo-Json -Depth 8 | Set-Content (Join-Path $programData 'production.json') -Encoding UTF8

$extensionIds = @()
if ($chromePresent) { $extensionIds += $ChromeExtensionId }
if ($edgePresent -and $EdgeExtensionId -notin $extensionIds) { $extensionIds += $EdgeExtensionId }
if ($extensionIds.Count -gt 0) {
    $browserConfig = [ordered]@{
        ExtensionId = $extensionIds[0]
        ProfileId = $ProfileId
        ManagedSessionId = 0
        ManagedUserSid = $currentSid
        TestMode = $false
        AdditionalExtensionIds = @($extensionIds | Select-Object -Skip 1)
    }
    $browserConfig | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $programData 'browser-control.json') -Encoding UTF8

    $origins = @($extensionIds | ForEach-Object { "chrome-extension://$_/" })
    $native = [ordered]@{
        name = 'com.tuoitho.browserhost'
        description = 'Quan ly thoi gian browser control'
        path = $browserHostExe
        type = 'stdio'
        allowed_origins = $origins
    }
    $nativeDir = Join-Path $programData 'NativeMessaging'
    New-Item -ItemType Directory -Path $nativeDir -Force | Out-Null
    $chromeManifest = Join-Path $nativeDir 'com.tuoitho.browserhost.chrome.json'
    $edgeManifest = Join-Path $nativeDir 'com.tuoitho.browserhost.edge.json'
    $native | ConvertTo-Json -Depth 4 | Set-Content $chromeManifest -Encoding UTF8
    $native | ConvertTo-Json -Depth 4 | Set-Content $edgeManifest -Encoding UTF8

    if ($chromePresent) {
        New-Item 'HKLM:\SOFTWARE\Google\Chrome\NativeMessagingHosts\com.tuoitho.browserhost' -Force | Out-Null
        Set-ItemProperty 'HKLM:\SOFTWARE\Google\Chrome\NativeMessagingHosts\com.tuoitho.browserhost' -Name '(default)' -Value $chromeManifest
        New-Item 'HKLM:\SOFTWARE\Policies\Google\Chrome\ExtensionInstallForcelist' -Force | Out-Null
        Set-ItemProperty 'HKLM:\SOFTWARE\Policies\Google\Chrome\ExtensionInstallForcelist' -Name '1000' -Value "$ChromeExtensionId;https://clients2.google.com/service/update2/crx"
    }

    if ($edgePresent) {
        New-Item 'HKLM:\SOFTWARE\Microsoft\Edge\NativeMessagingHosts\com.tuoitho.browserhost' -Force | Out-Null
        Set-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Edge\NativeMessagingHosts\com.tuoitho.browserhost' -Name '(default)' -Value $edgeManifest
        New-Item 'HKLM:\SOFTWARE\Policies\Microsoft\Edge\ExtensionInstallForcelist' -Force | Out-Null
        Set-ItemProperty 'HKLM:\SOFTWARE\Policies\Microsoft\Edge\ExtensionInstallForcelist' -Name '1000' -Value "$EdgeExtensionId;https://edge.microsoft.com/extensionwebstorebase/v1/crx"
    }
}

& icacls.exe $programData /inheritance:r /grant:r '*S-1-5-18:(OI)(CI)(F)' '*S-1-5-32-544:(OI)(CI)(F)' '*S-1-5-32-545:(OI)(CI)(RX)' /T /C | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Không thể áp dụng ACL cho dữ liệu production.' }

$serviceName = 'TuoiTho.Service'
$existing = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
if ($existing) {
    if ($existing.Status -ne 'Stopped') {
        Stop-Service -Name $serviceName -Force
        $existing.WaitForStatus('Stopped',[TimeSpan]::FromSeconds(20))
    }
    & sc.exe delete $serviceName | Out-Null
    Start-Sleep -Milliseconds 800
}
New-Service -Name $serviceName -BinaryPathName ('"' + $serviceExe + '"') -DisplayName 'Quản lý thời gian - Dịch vụ bảo vệ' -StartupType Automatic | Out-Null
& sc.exe description $serviceName 'Bảo vệ thời gian, ứng dụng và trình duyệt.' | Out-Null
& sc.exe failure $serviceName reset= 86400 actions= restart/2000/restart/2000/restart/5000 | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Không cấu hình được Service Recovery.' }
& sc.exe failureflag $serviceName 1 | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Không bật được failure actions cho service.' }
Start-Service -Name $serviceName

Write-Host 'PRODUCTION INSTALL CONFIG: PASS'
Write-Host "Service: $serviceName (Automatic + Recovery)"
Write-Host 'SessionAgent watchdog: 2 giây'
Write-Host "Chrome force install: $chromePresent"
Write-Host "Edge force install: $edgePresent"
