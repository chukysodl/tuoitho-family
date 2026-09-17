$ErrorActionPreference = 'Stop'
$config = Join-Path $env:ProgramData 'TuoiTho\browser-control.json'
if (-not (Test-Path $config)) { throw 'FAIL: thiếu browser-control.json. Hãy chạy M4-BROWSER-INSTALL.cmd.' }
$value = Get-Content $config -Raw | ConvertFrom-Json
if ([string]::IsNullOrWhiteSpace($value.ExtensionId) -or
    [string]::IsNullOrWhiteSpace($value.ProfileId) -or
    [string]::IsNullOrWhiteSpace($value.ManagedUserSid) -or
    $value.ManagedSessionId -lt 0) {
    throw 'FAIL: browser-control.json không hợp lệ.'
}

$expectedHost = Join-Path (Resolve-Path (Join-Path $PSScriptRoot '..')).Path 'src\TuoiTho.BrowserHost\bin\Release\net10.0-windows\TuoiTho.BrowserHost.exe'
$registrations = @(
    @{ Browser = 'Chrome'; Key = 'HKCU:\Software\Google\Chrome\NativeMessagingHosts\com.tuoitho.browserhost' },
    @{ Browser = 'Edge'; Key = 'HKCU:\Software\Microsoft\Edge\NativeMessagingHosts\com.tuoitho.browserhost' }
)
foreach ($registration in $registrations) {
    $manifestPath = (Get-ItemProperty $registration.Key -ErrorAction Stop).'(default)'
    if (-not (Test-Path $manifestPath)) { throw "FAIL: thiếu Native Host manifest $($registration.Browser): $manifestPath" }
    $manifest = Get-Content $manifestPath -Raw | ConvertFrom-Json
    if ($manifest.name -ne 'com.tuoitho.browserhost' -or $manifest.type -ne 'stdio' -or $manifest.path -ne $expectedHost) {
        throw "FAIL: Native Host manifest $($registration.Browser) không hợp lệ."
    }
    if ($manifest.allowed_origins -notcontains "chrome-extension://$($value.ExtensionId)/") {
        throw "FAIL: Native Host manifest $($registration.Browser) không khớp Extension ID."
    }
}

$extension = Join-Path $env:LOCALAPPDATA 'TuoiTho\M4\Extension'
if (-not (Test-Path (Join-Path $extension 'manifest.json'))) { throw 'FAIL: thiếu thư mục extension M4.' }
Write-Host "PASS: browser-control.json, Chrome và Edge Native Messaging đã khớp Extension ID $($value.ExtensionId)."
