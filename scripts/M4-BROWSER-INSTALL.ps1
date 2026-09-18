$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
. (Join-Path $PSScriptRoot 'M4-BROWSER-COMMON.ps1')
$hostExe = Build-M4BrowserHost $root
$paths = Get-M4Paths $root
$stagedManifest = Join-Path $paths.Extension 'manifest.json'

# Existing configuration is a trusted identity contract. A missing staged key is a repair case,
# never a reason for a normal install/update to create a second Chromium extension ID.
if ((Test-Path -LiteralPath $paths.Config) -and -not (Test-Path -LiteralPath $stagedManifest)) {
    throw 'browser-control.json đã tồn tại nhưng staged key bị mất. Hãy chạy M4-BROWSER-REPAIR.cmd.'
}

Copy-M4ExtensionPayload $hostExe $paths.Source $paths.Extension
$extensionId = (& $hostExe --bootstrap-config $paths.Extension $paths.Config | Select-Object -Last 1).Trim()
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($extensionId)) { throw 'Không thể tạo hoặc xác nhận cấu hình M4.' }
Protect-M4BrowserConfig $paths.Config
Register-M4NativeHosts $hostExe $extensionId $paths

& (Join-Path $PSScriptRoot 'M4-BROWSER-CHECK.ps1')
if ($LASTEXITCODE -ne 0) { throw 'Kiểm tra đăng ký Native Messaging thất bại.' }
Write-Host "`nM4 INSTALL: PASS"
Write-Host "Extension ID: $extensionId"
Write-Host "Load unpacked: $($paths.Extension)"
Write-Host 'Chrome: chrome://extensions | Edge: edge://extensions | bật Developer mode → Load unpacked.'
Write-Host 'Không cần SETX, sửa JSON, SID hoặc Session thủ công.'
Write-Host 'Lần cập nhật sau dùng M4-BROWSER-UPDATE.cmd; không chép manifest nguồn trực tiếp vào thư mục staged.'