$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
. (Join-Path $PSScriptRoot 'M4-BROWSER-COMMON.ps1')
$hostExe = Build-M4BrowserHost $root
$paths = Get-M4Paths $root
$stagedManifest = Join-Path $paths.Extension 'manifest.json'
$oldConfigId = '(missing)'
if (Test-Path -LiteralPath $paths.Config) { $oldConfigId = (Get-Content -LiteralPath $paths.Config -Raw | ConvertFrom-Json).ExtensionId }

# Retain a still-valid staged key. If it is truly missing, Repair creates exactly one replacement
# identity and preserves profile/session/SID/TestMode from browser-control.json.
$hasKey = $false
if (Test-Path -LiteralPath $stagedManifest) {
    & $hostExe --extension-id $stagedManifest | Out-Null
    $hasKey = $LASTEXITCODE -eq 0
}
Copy-M4ExtensionPayload $hostExe $paths.Source $paths.Extension -AllowKeyRecovery
if ($hasKey) {
    # Copy-M4ExtensionPayload already restored the trusted key.
} else {
    Write-Host 'Staged key bị mất; tạo một canonical identity mới cho lần repair này.'
}
$newId = (& $hostExe --repair-browser-config $paths.Extension $paths.Config | Select-Object -Last 1).Trim()
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($newId)) { throw 'REPAIR: FAIL — không thể tạo identity canonical.' }
$stagedId = (& $hostExe --extension-id (Join-Path $paths.Extension 'manifest.json') | Select-Object -Last 1).Trim()
if ($LASTEXITCODE -ne 0 -or $stagedId -ne $newId) { throw 'REPAIR: FAIL — staged manifest không khớp identity canonical.' }
Protect-M4BrowserConfig $paths.Config
Register-M4NativeHosts $hostExe $newId $paths
& (Join-Path $PSScriptRoot 'M4-BROWSER-CHECK.ps1')
if ($LASTEXITCODE -ne 0) { throw 'REPAIR: FAIL — Native Messaging chưa khớp identity canonical.' }
Write-Host "OLD CONFIG ID: $oldConfigId"
Write-Host "NEW CANONICAL ID: $newId"
Write-Host "STAGED ID: $stagedId"
Write-Host "NATIVE HOST ID: $newId"
Write-Host 'REPAIR: PASS'
Write-Host "Hãy xóa tất cả mục Tuổi Thơ Browser Control cũ tại chrome://extensions, rồi Load unpacked DUY NHẤT: $($paths.Extension)"
Write-Host 'Repair không xóa hay thay đổi bất kỳ SQLite policy/rule/quota nào.'