$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
. (Join-Path $PSScriptRoot 'M4-BROWSER-COMMON.ps1')
$hostExe = Build-M4BrowserHost $root
$paths = Get-M4Paths $root
$stagedManifest = Join-Path $paths.Extension 'manifest.json'
if (-not (Test-Path -LiteralPath $paths.Config)) { throw 'Chưa có browser-control.json. Hãy chạy M4-BROWSER-INSTALL.cmd lần đầu.' }
if (-not (Test-Path -LiteralPath $stagedManifest)) { throw 'Staged manifest/key bị mất. Hãy chạy M4-BROWSER-REPAIR.cmd.' }
$before = (& $hostExe --extension-id $stagedManifest | Select-Object -Last 1).Trim()
if ($LASTEXITCODE -ne 0) { throw 'Staged extension key không hợp lệ. Hãy chạy M4-BROWSER-REPAIR.cmd.' }
$configured = (Get-Content -LiteralPath $paths.Config -Raw | ConvertFrom-Json).ExtensionId
if ($before -ne $configured) { throw 'Extension ID staged không khớp browser-control.json. Hãy chạy M4-BROWSER-REPAIR.cmd.' }

Copy-M4ExtensionPayload $hostExe $paths.Source $paths.Extension -RequireExistingKey
$after = (& $hostExe --bootstrap-config $paths.Extension $paths.Config | Select-Object -Last 1).Trim()
if ($LASTEXITCODE -ne 0 -or $after -ne $before -or $after -ne $configured) { throw 'IDENTITY: FAIL — update không được đăng ký extension ID mới.' }
Protect-M4BrowserConfig $paths.Config
Register-M4NativeHosts $hostExe $after $paths
& (Join-Path $PSScriptRoot 'M4-BROWSER-CHECK.ps1')
if ($LASTEXITCODE -ne 0) { throw 'Kiểm tra Native Messaging thất bại sau update.' }
Write-Host 'M4 UPDATE:'
Write-Host "Extension ID before: $before"
Write-Host "Extension ID after:  $after"
Write-Host 'IDENTITY: PASS'
Write-Host 'FILES: PASS'