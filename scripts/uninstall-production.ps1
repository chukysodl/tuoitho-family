param(
    [Parameter(Mandatory=$true)][string]$InstallDir,
    [string]$ChromeExtensionId = '',
    [string]$EdgeExtensionId = ''
)

$ErrorActionPreference = 'Stop'

$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = New-Object Security.Principal.WindowsPrincipal($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Uninstall requires Administrator rights.'
}

$InstallDir = [IO.Path]::GetFullPath($InstallDir)
$serviceName = 'TuoiTho.Service'
$service = Get-Service -Name $serviceName -ErrorAction SilentlyContinue

if ($service) {
    if ($service.Status -ne 'Stopped') {
        & sc.exe stop $serviceName | Out-Null
        $deadline = [DateTimeOffset]::UtcNow.AddSeconds(20)
        do {
            Start-Sleep -Milliseconds 300
            $service = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
            if (-not $service -or $service.Status -eq 'Stopped') { break }
        } while ([DateTimeOffset]::UtcNow -lt $deadline)
    }

    $service = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
    if ($service -and $service.Status -ne 'Stopped') {
        throw 'Service did not stop within 20 seconds.'
    }

    & sc.exe delete $serviceName | Out-Null
}

$agentPath = Join-Path $InstallDir 'SessionAgent\TuoiTho.SessionAgent.exe'
$parentPath = Join-Path $InstallDir 'Parent\TuoiTho.Parent.exe'
$hostPath = Join-Path $InstallDir 'BrowserHost\TuoiTho.BrowserHost.exe'
$targets = @($agentPath, $parentPath, $hostPath) | ForEach-Object { [IO.Path]::GetFullPath($_) }

Get-CimInstance Win32_Process | Where-Object {
    $_.ExecutablePath -and ([IO.Path]::GetFullPath($_.ExecutablePath) -in $targets)
} | ForEach-Object {
    Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue
}

$chromePolicy = 'HKLM:\SOFTWARE\Policies\Google\Chrome\ExtensionInstallForcelist'
if (Test-Path $chromePolicy) {
    $current = (Get-ItemProperty $chromePolicy -Name '1000' -ErrorAction SilentlyContinue).'1000'
    if (-not $ChromeExtensionId -or ($current -like "$ChromeExtensionId;*")) {
        Remove-ItemProperty $chromePolicy -Name '1000' -ErrorAction SilentlyContinue
    }
}

$edgePolicy = 'HKLM:\SOFTWARE\Policies\Microsoft\Edge\ExtensionInstallForcelist'
if (Test-Path $edgePolicy) {
    $current = (Get-ItemProperty $edgePolicy -Name '1000' -ErrorAction SilentlyContinue).'1000'
    if (-not $EdgeExtensionId -or ($current -like "$EdgeExtensionId;*")) {
        Remove-ItemProperty $edgePolicy -Name '1000' -ErrorAction SilentlyContinue
    }
}

Remove-Item 'HKLM:\SOFTWARE\Google\Chrome\NativeMessagingHosts\com.tuoitho.browserhost' -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item 'HKLM:\SOFTWARE\Microsoft\Edge\NativeMessagingHosts\com.tuoitho.browserhost' -Recurse -Force -ErrorAction SilentlyContinue

$programData = Join-Path $env:ProgramData 'TuoiTho'
if (Test-Path $programData) {
    & icacls.exe $programData /grant:r '*S-1-5-32-544:(OI)(CI)(F)' '*S-1-5-18:(OI)(CI)(F)' /T /C | Out-Null
    Remove-Item $programData -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Host 'PRODUCTION UNINSTALL CLEANUP: PASS'
