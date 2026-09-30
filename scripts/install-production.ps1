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
    if ($id -notmatch '^[a-p]{32}
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
$chromeManaged = $chromePresent -and -not [string]::IsNullOrWhiteSpace($ChromeExtensionId)
$edgeManaged = $edgePresent -and -not [string]::IsNullOrWhiteSpace($EdgeExtensionId)
if ($chromeManaged) { Assert-ExtensionId $ChromeExtensionId 'Chrome' }
if ($edgeManaged) { Assert-ExtensionId $EdgeExtensionId 'Edge' }

$programData = Join-Path $env:ProgramData 'TuoiTho'
New-Item -ItemType Directory -Path $programData -Force | Out-Null
$script:InstallLogPath = Join-Path $programData 'install.log'
Write-InstallLog '=== Bắt đầu cấu hình production ==='
Write-InstallLog "InstallDir=$InstallDir"

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
if ($chromeManaged) { $extensionIds += $ChromeExtensionId }
if ($edgeManaged -and $EdgeExtensionId -notin $extensionIds) { $extensionIds += $EdgeExtensionId }
Write-InstallLog "Browser detection: ChromePresent=$chromePresent ChromeManaged=$chromeManaged EdgePresent=$edgePresent EdgeManaged=$edgeManaged"
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

    if ($chromeManaged) {
        New-Item 'HKLM:\SOFTWARE\Google\Chrome\NativeMessagingHosts\com.tuoitho.browserhost' -Force | Out-Null
        Set-ItemProperty 'HKLM:\SOFTWARE\Google\Chrome\NativeMessagingHosts\com.tuoitho.browserhost' -Name '(default)' -Value $chromeManifest
        New-Item 'HKLM:\SOFTWARE\Policies\Google\Chrome\ExtensionInstallForcelist' -Force | Out-Null
        Set-ItemProperty 'HKLM:\SOFTWARE\Policies\Google\Chrome\ExtensionInstallForcelist' -Name '1000' -Value "$ChromeExtensionId;https://clients2.google.com/service/update2/crx"
    }

    if ($edgeManaged) {
        New-Item 'HKLM:\SOFTWARE\Microsoft\Edge\NativeMessagingHosts\com.tuoitho.browserhost' -Force | Out-Null
        Set-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Edge\NativeMessagingHosts\com.tuoitho.browserhost' -Name '(default)' -Value $edgeManifest
        New-Item 'HKLM:\SOFTWARE\Policies\Microsoft\Edge\ExtensionInstallForcelist' -Force | Out-Null
        Set-ItemProperty 'HKLM:\SOFTWARE\Policies\Microsoft\Edge\ExtensionInstallForcelist' -Name '1000' -Value "$EdgeExtensionId;https://edge.microsoft.com/extensionwebstorebase/v1/crx"
    }
}

& icacls.exe $programData /inheritance:r /grant:r '*S-1-5-18:(OI)(CI)(F)' '*S-1-5-32-544:(OI)(CI)(F)' '*S-1-5-32-545:(OI)(CI)(RX)' /T /C | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Không thể áp dụng ACL cho dữ liệu production.' }

$serviceName = 'TuoiTho.Service'
Write-InstallLog 'Bước service: dừng/xóa bản cũ có giới hạn thời gian.'
Stop-And-Delete-ServiceBounded $serviceName

Write-InstallLog 'Bước service: tạo Windows Service mới.'
$binaryPath = '"' + $serviceExe + '"'
Invoke-Sc @('create',$serviceName,'binPath=',$binaryPath,'start=','auto','obj=','LocalSystem','DisplayName=','Quản lý thời gian - Dịch vụ bảo vệ') 'Tạo service'
Invoke-Sc @('description',$serviceName,'Bảo vệ thời gian, ứng dụng và trình duyệt.') 'Đặt mô tả service'
Invoke-Sc @('failure',$serviceName,'reset=','86400','actions=','restart/2000/restart/2000/restart/5000') 'Cấu hình Service Recovery'
Invoke-Sc @('failureflag',$serviceName,'1') 'Bật failure actions'

Write-InstallLog 'Bước service: khởi động có timeout 20 giây.'
Start-ServiceBounded $serviceName

Write-InstallLog 'PRODUCTION INSTALL CONFIG: PASS'
Write-Host 'PRODUCTION INSTALL CONFIG: PASS'
Write-Host "Service: $serviceName (Automatic + Recovery)"
Write-Host 'SessionAgent watchdog: 2 giây'
Write-Host "Chrome force install: $chromeManaged"
Write-Host "Edge force install: $edgeManaged"
if ($chromePresent -and -not $chromeManaged) { Write-Host 'Chrome: PRE-STORE TEST - chưa ép extension vì chưa có Store ID thật.' }
if ($edgePresent -and -not $edgeManaged) { Write-Host 'Edge: PRE-STORE TEST - chưa ép extension vì chưa có Store ID thật.' }
) { throw "$label extension ID chưa hợp lệ hoặc chưa được cấu hình." }
}

function Write-InstallLog([string]$message) {
    $stamp = [DateTimeOffset]::Now.ToString('yyyy-MM-dd HH:mm:ss.fff zzz')
    $line = "$stamp  $message"
    try { Add-Content -LiteralPath $script:InstallLogPath -Value $line -Encoding UTF8 } catch { }
}

function Get-ServiceSnapshot([string]$name) {
    Get-CimInstance Win32_Service -Filter "Name='$name'" -ErrorAction SilentlyContinue
}

function Wait-ServiceState([string]$name, [string]$expectedState, [int]$timeoutSeconds) {
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds($timeoutSeconds)
    do {
        $snapshot = Get-ServiceSnapshot $name
        if ($null -eq $snapshot) {
            if ($expectedState -eq 'Deleted') { return $true }
        }
        elseif ($snapshot.State -eq $expectedState) {
            return $true
        }
        Start-Sleep -Milliseconds 300
    } while ([DateTimeOffset]::UtcNow -lt $deadline)
    return $false
}

function Invoke-Sc([string[]]$arguments, [string]$stage) {
    Write-InstallLog "$stage -> sc.exe $($arguments -join ' ')"
    $output = & sc.exe @arguments 2>&1
    $exit = $LASTEXITCODE
    if ($output) { Write-InstallLog (($output | Out-String).Trim()) }
    if ($exit -ne 0) { throw "$stage thất bại (sc.exe exit $exit)." }
}

function Stop-And-Delete-ServiceBounded([string]$name) {
    $snapshot = Get-ServiceSnapshot $name
    if ($null -eq $snapshot) {
        Write-InstallLog "Không có service cũ: $name"
        return
    }

    Write-InstallLog "Service cũ: state=$($snapshot.State), pid=$($snapshot.ProcessId)"
    if ($snapshot.State -ne 'Stopped') {
        $output = & sc.exe stop $name 2>&1
        Write-InstallLog (($output | Out-String).Trim())
        if (-not (Wait-ServiceState $name 'Stopped' 20)) {
            $latest = Get-ServiceSnapshot $name
            throw "Service cũ không dừng trong 20 giây (state=$($latest.State), pid=$($latest.ProcessId)). Hãy khởi động lại Windows rồi chạy bộ cài mới."
        }
    }

    Invoke-Sc @('delete',$name) 'Xóa service cũ'
    if (-not (Wait-ServiceState $name 'Deleted' 12)) {
        throw 'Service cũ đang chờ xóa quá lâu. Hãy khởi động lại Windows rồi chạy bộ cài mới.'
    }
}

function Start-ServiceBounded([string]$name) {
    $output = & sc.exe start $name 2>&1
    Write-InstallLog (($output | Out-String).Trim())
    if (-not (Wait-ServiceState $name 'Running' 20)) {
        $latest = Get-ServiceSnapshot $name
        $state = if ($null -eq $latest) { 'MISSING' } else { $latest.State }
        throw "Service mới không chạy trong 20 giây (state=$state). Xem $script:InstallLogPath."
    }
    $snapshot = Get-ServiceSnapshot $name
    Write-InstallLog "Service RUNNING pid=$($snapshot.ProcessId)"
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
$chromeManaged = $chromePresent -and -not [string]::IsNullOrWhiteSpace($ChromeExtensionId)
$edgeManaged = $edgePresent -and -not [string]::IsNullOrWhiteSpace($EdgeExtensionId)
if ($chromeManaged) { Assert-ExtensionId $ChromeExtensionId 'Chrome' }
if ($edgeManaged) { Assert-ExtensionId $EdgeExtensionId 'Edge' }

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
if ($chromeManaged) { $extensionIds += $ChromeExtensionId }
if ($edgeManaged -and $EdgeExtensionId -notin $extensionIds) { $extensionIds += $EdgeExtensionId }
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

    if ($chromeManaged) {
        New-Item 'HKLM:\SOFTWARE\Google\Chrome\NativeMessagingHosts\com.tuoitho.browserhost' -Force | Out-Null
        Set-ItemProperty 'HKLM:\SOFTWARE\Google\Chrome\NativeMessagingHosts\com.tuoitho.browserhost' -Name '(default)' -Value $chromeManifest
        New-Item 'HKLM:\SOFTWARE\Policies\Google\Chrome\ExtensionInstallForcelist' -Force | Out-Null
        Set-ItemProperty 'HKLM:\SOFTWARE\Policies\Google\Chrome\ExtensionInstallForcelist' -Name '1000' -Value "$ChromeExtensionId;https://clients2.google.com/service/update2/crx"
    }

    if ($edgeManaged) {
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
Write-Host "Chrome force install: $chromeManaged"
Write-Host "Edge force install: $edgeManaged"
if ($chromePresent -and -not $chromeManaged) { Write-Host 'Chrome: PRE-STORE TEST - chưa ép extension vì chưa có Store ID thật.' }
if ($edgePresent -and -not $edgeManaged) { Write-Host 'Edge: PRE-STORE TEST - chưa ép extension vì chưa có Store ID thật.' }
