$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$project = Join-Path $root 'src\TuoiTho.BrowserHost\TuoiTho.BrowserHost.csproj'
& dotnet build $project --configuration Release
if ($LASTEXITCODE -ne 0) { throw 'Không thể build TuoiTho.BrowserHost.' }

$hostExe = Join-Path $root 'src\TuoiTho.BrowserHost\bin\Release\net10.0-windows\TuoiTho.BrowserHost.exe'
if (-not (Test-Path $hostExe)) { throw 'Không tìm thấy BrowserHost sau build.' }

# Keep the generated extension key out of the repository. Browsers load this staged folder.
$extensionSource = Join-Path $root 'browser-extension'
$extension = Join-Path $env:LOCALAPPDATA 'TuoiTho\M4\Extension'
New-Item -ItemType Directory -Force $extension | Out-Null
Copy-Item (Join-Path $extensionSource '*') $extension -Recurse -Force

$configDir = Join-Path $env:ProgramData 'TuoiTho'
New-Item -ItemType Directory -Force $configDir | Out-Null
$config = Join-Path $configDir 'browser-control.json'
$extensionId = (& $hostExe --bootstrap-config $extension $config | Select-Object -Last 1).Trim()
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($extensionId)) { throw 'Không thể tạo cấu hình M4.' }

$currentSid = [System.Security.Principal.WindowsIdentity]::GetCurrent().User.Value
& icacls $config /inheritance:r /grant:r "*${currentSid}:(R,W)" '*S-1-5-18:(F)' | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Không thể bảo vệ browser-control.json bằng Windows ACL.' }

$nativeDir = Join-Path $env:LOCALAPPDATA 'TuoiTho\NativeMessaging'
New-Item -ItemType Directory -Force $nativeDir | Out-Null
$native = @{
    name = 'com.tuoitho.browserhost'
    description = 'TuoiTho local browser control'
    path = $hostExe
    type = 'stdio'
    allowed_origins = @("chrome-extension://$extensionId/")
} | ConvertTo-Json -Depth 3
$chrome = Join-Path $nativeDir 'com.tuoitho.browserhost.chrome.json'
$edge = Join-Path $nativeDir 'com.tuoitho.browserhost.edge.json'
Set-Content $chrome $native -Encoding utf8
Set-Content $edge $native -Encoding utf8
reg add 'HKCU\Software\Google\Chrome\NativeMessagingHosts\com.tuoitho.browserhost' /ve /t REG_SZ /d $chrome /f | Out-Null
reg add 'HKCU\Software\Microsoft\Edge\NativeMessagingHosts\com.tuoitho.browserhost' /ve /t REG_SZ /d $edge /f | Out-Null

& (Join-Path $PSScriptRoot 'M4-BROWSER-CHECK.ps1')
if ($LASTEXITCODE -ne 0) { throw 'Kiểm tra đăng ký Native Messaging thất bại.' }
Write-Host "`nPASS: M4 đã tự cấu hình BrowserHost, browser-control.json và Chrome/Edge Native Messaging."
Write-Host "Extension ID: $extensionId"
Write-Host "Load unpacked: $extension"
Write-Host 'Chrome: chrome://extensions | Edge: edge://extensions | bật Developer mode → Load unpacked.'
Write-Host 'Không cần SETX, sửa JSON, SID hoặc Session thủ công.'
