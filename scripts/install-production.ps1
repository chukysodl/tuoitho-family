param(
    [Parameter(Mandatory=$true)][string]$InstallDir,
    [string]$ProfileId = 'm1-child',
    [string]$ChromeExtensionId = '',
    [string]$EdgeExtensionId = ''
)

$ErrorActionPreference = 'Stop'

trap {
    $message = $_.Exception.Message
    try {
        if ($script:InstallLogPath) {
            Add-Content -LiteralPath $script:InstallLogPath -Value ('ERROR: ' + $message) -Encoding UTF8
        }
    } catch { }
    Write-Error $message
    exit 1
}

function Assert-Admin {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        throw 'Production install requires Administrator rights.'
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
    if ($id -notmatch '^[a-p]{32}$') {
        throw "$label extension ID is invalid or missing."
    }
}

function Write-InstallLog([string]$message) {
    $stamp = [DateTimeOffset]::Now.ToString('yyyy-MM-dd HH:mm:ss.fff zzz')
    $line = "$stamp  $message"
    try {
        Add-Content -LiteralPath $script:InstallLogPath -Value $line -Encoding UTF8
    } catch { }
}

function Get-ServiceState([string]$name) {
    $service = Get-Service -Name $name -ErrorAction SilentlyContinue
    if ($null -eq $service) {
        return 'Deleted'
    }
    return $service.Status.ToString()
}

function Wait-ServiceState([string]$name, [string]$expectedState, [int]$timeoutSeconds) {
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds($timeoutSeconds)
    do {
        $state = Get-ServiceState $name
        if ($state -eq $expectedState) {
            return $true
        }
        Start-Sleep -Milliseconds 300
    } while ([DateTimeOffset]::UtcNow -lt $deadline)
    return $false
}

function Invoke-Sc {
    param(
        [Parameter(Mandatory=$true)][string[]]$Arguments,
        [Parameter(Mandatory=$true)][string]$Stage
    )

    Write-InstallLog "$Stage -> sc.exe $($Arguments -join ' ')"
    $output = & sc.exe @Arguments 2>&1
    $exitCode = $LASTEXITCODE
    if ($output) {
        Write-InstallLog (($output | Out-String).Trim())
    }
    if ($exitCode -ne 0) {
        throw "$Stage failed. sc.exe exit code: $exitCode"
    }
}

function Stop-And-Delete-ServiceBounded([string]$name) {
    $state = Get-ServiceState $name
    Write-InstallLog "Existing service state: $state"

    if ($state -eq 'Deleted') {
        return
    }

    if ($state -ne 'Stopped') {
        $output = & sc.exe stop $name 2>&1
        Write-InstallLog (($output | Out-String).Trim())

        if (-not (Wait-ServiceState $name 'Stopped' 20)) {
            $latest = Get-ServiceState $name
            throw "Old service did not stop within 20 seconds. State=$latest. Restart Windows and run the new installer again."
        }
    }

    Invoke-Sc -Arguments @('delete', $name) -Stage 'Delete old service'

    if (-not (Wait-ServiceState $name 'Deleted' 12)) {
        throw 'Old service stayed pending-delete for more than 12 seconds. Restart Windows and run the installer again.'
    }
}

function Start-ServiceBounded([string]$name) {
    $output = & sc.exe start $name 2>&1
    $exitCode = $LASTEXITCODE
    Write-InstallLog (($output | Out-String).Trim())

    if ($exitCode -ne 0) {
        throw "Service start command failed. sc.exe exit code: $exitCode"
    }

    if (-not (Wait-ServiceState $name 'Running' 20)) {
        $latest = Get-ServiceState $name
        throw "New service did not reach RUNNING within 20 seconds. State=$latest. See install.log."
    }

    Write-InstallLog 'Service state: RUNNING'
}

Assert-Admin

$InstallDir = [IO.Path]::GetFullPath($InstallDir)
$serviceExe = Join-Path $InstallDir 'Service\TuoiTho.Service.exe'
$agentExe = Join-Path $InstallDir 'SessionAgent\TuoiTho.SessionAgent.exe'
$parentExe = Join-Path $InstallDir 'Parent\TuoiTho.Parent.exe'
$browserHostExe = Join-Path $InstallDir 'BrowserHost\TuoiTho.BrowserHost.exe'

foreach ($file in @($serviceExe, $agentExe, $parentExe, $browserHostExe)) {
    if (-not (Test-Path -LiteralPath $file)) {
        throw "Missing installer component: $file"
    }
}

$programData = Join-Path $env:ProgramData 'TuoiTho'
New-Item -ItemType Directory -Path $programData -Force | Out-Null
$script:InstallLogPath = Join-Path $programData 'install.log'
Write-InstallLog '=== Production configuration started ==='
Write-InstallLog "InstallDir=$InstallDir"

$chromePresent = Test-BrowserInstalled 'chrome.exe'
$edgePresent = Test-BrowserInstalled 'msedge.exe'
$chromeManaged = $chromePresent -and -not [string]::IsNullOrWhiteSpace($ChromeExtensionId)
$edgeManaged = $edgePresent -and -not [string]::IsNullOrWhiteSpace($EdgeExtensionId)

if ($chromeManaged) {
    Assert-ExtensionId $ChromeExtensionId 'Chrome'
}
if ($edgeManaged) {
    Assert-ExtensionId $EdgeExtensionId 'Edge'
}

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

$productionPath = Join-Path $programData 'production.json'
$production | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $productionPath -Encoding UTF8
Write-InstallLog "Production config written: $productionPath"

$extensionIds = @()
if ($chromeManaged) {
    $extensionIds += $ChromeExtensionId
}
if ($edgeManaged -and $EdgeExtensionId -notin $extensionIds) {
    $extensionIds += $EdgeExtensionId
}

Write-InstallLog "Browser: ChromePresent=$chromePresent ChromeManaged=$chromeManaged EdgePresent=$edgePresent EdgeManaged=$edgeManaged"

if ($extensionIds.Count -gt 0) {
    $browserConfig = [ordered]@{
        ExtensionId = $extensionIds[0]
        ProfileId = $ProfileId
        ManagedSessionId = 0
        ManagedUserSid = $currentSid
        TestMode = $false
        AdditionalExtensionIds = @($extensionIds | Select-Object -Skip 1)
    }

    $browserConfigPath = Join-Path $programData 'browser-control.json'
    $browserConfig | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $browserConfigPath -Encoding UTF8

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
    $native | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $chromeManifest -Encoding UTF8
    $native | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $edgeManifest -Encoding UTF8

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

Write-InstallLog 'Applying ProgramData ACL.'
& icacls.exe $programData /inheritance:r /grant:r '*S-1-5-18:(OI)(CI)(F)' '*S-1-5-32-544:(OI)(CI)(F)' '*S-1-5-32-545:(OI)(CI)(RX)' /T /C | Out-Null
if ($LASTEXITCODE -ne 0) {
    throw 'Could not apply ProgramData ACL.'
}

$serviceName = 'TuoiTho.Service'
Write-InstallLog 'Stopping and deleting old service with hard timeouts.'
Stop-And-Delete-ServiceBounded $serviceName

$binaryPath = '"' + $serviceExe + '"'
Invoke-Sc -Arguments @(
    'create',
    $serviceName,
    'binPath=',
    $binaryPath,
    'start=',
    'auto',
    'obj=',
    'LocalSystem',
    'DisplayName=',
    'Quan ly thoi gian - Dich vu bao ve'
) -Stage 'Create service'

Invoke-Sc -Arguments @(
    'description',
    $serviceName,
    'Quan ly thoi gian protection service.'
) -Stage 'Set service description'

Invoke-Sc -Arguments @(
    'failure',
    $serviceName,
    'reset=',
    '86400',
    'actions=',
    'restart/2000/restart/2000/restart/5000'
) -Stage 'Configure service recovery'

Invoke-Sc -Arguments @(
    'failureflag',
    $serviceName,
    '1'
) -Stage 'Enable failure actions'

Write-InstallLog 'Starting service with 20 second timeout.'
Start-ServiceBounded $serviceName

Write-InstallLog 'PRODUCTION INSTALL CONFIG: PASS'
Write-Host 'PRODUCTION INSTALL CONFIG: PASS'
Write-Host "Service: $serviceName (Automatic + Recovery)"
Write-Host 'SessionAgent watchdog: 2 seconds'
Write-Host "Chrome force install: $chromeManaged"
Write-Host "Edge force install: $edgeManaged"

if ($chromePresent -and -not $chromeManaged) {
    Write-Host 'Chrome: PRE-STORE TEST - force install skipped because no real Store ID exists yet.'
}
if ($edgePresent -and -not $edgeManaged) {
    Write-Host 'Edge: PRE-STORE TEST - force install skipped because no real Store ID exists yet.'
}
