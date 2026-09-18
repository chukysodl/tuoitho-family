$ErrorActionPreference = 'Stop'
$config = Join-Path $env:ProgramData 'TuoiTho\browser-control.json'
if (-not (Test-Path -LiteralPath $config)) { throw 'FAIL: thiếu browser-control.json. Hãy chạy M4-BROWSER-INSTALL.cmd.' }
$value = Get-Content -LiteralPath $config -Raw | ConvertFrom-Json
if ([string]::IsNullOrWhiteSpace($value.ExtensionId) -or [string]::IsNullOrWhiteSpace($value.ProfileId) -or [string]::IsNullOrWhiteSpace($value.ManagedUserSid) -or $value.ManagedSessionId -lt 0) { throw 'FAIL: browser-control.json không hợp lệ.' }
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$hostExe = Join-Path $repo 'src\TuoiTho.BrowserHost\bin\Release\net10.0-windows\TuoiTho.BrowserHost.exe'
$extension = Join-Path $env:LOCALAPPDATA 'TuoiTho\M4\Extension'
$stagedManifest = Join-Path $extension 'manifest.json'
$nativeOk = $true
$identityOk = $false
if (Test-Path -LiteralPath $hostExe -and Test-Path -LiteralPath $stagedManifest) {
    $stagedId = (& $hostExe --extension-id $stagedManifest | Select-Object -Last 1).Trim()
    if ($LASTEXITCODE -eq 0 -and $stagedId -eq $value.ExtensionId) { $identityOk = $true; Write-Host "A. Staged Extension ID: PASS ($stagedId)" }
    else { $nativeOk = $false; Write-Host "A. Staged Extension ID: FAIL — staged=$stagedId config=$($value.ExtensionId)" }
} else { $nativeOk = $false; Write-Host 'A. Staged Extension ID: FAIL — thiếu BrowserHost hoặc manifest.' }

$registrations = @(
    @{ Browser = 'Chrome'; Key = 'HKCU:\Software\Google\Chrome\NativeMessagingHosts\com.tuoitho.browserhost' },
    @{ Browser = 'Edge'; Key = 'HKCU:\Software\Microsoft\Edge\NativeMessagingHosts\com.tuoitho.browserhost' }
)
foreach ($registration in $registrations) {
    try {
        $manifestPath = (Get-ItemProperty -Path $registration.Key -ErrorAction Stop).'(default)'
        if (-not (Test-Path -LiteralPath $manifestPath)) { throw 'thiếu manifest.' }
        $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
        $valid = $manifest.name -eq 'com.tuoitho.browserhost' -and $manifest.type -eq 'stdio' -and $manifest.path -eq $hostExe -and ($manifest.allowed_origins -contains ("chrome-extension://" + $value.ExtensionId + "/")) -and $manifest.allowed_origins.Count -eq 1
        if (-not $valid) { throw 'manifest không khớp cấu hình stable ID.' }
        Write-Host ("B. Native Host " + $registration.Browser + ': PASS')
    } catch {
        $nativeOk = $false
        Write-Host ("B. Native Host " + $registration.Browser + ': FAIL - ' + $_.Exception.Message)
    }
}
if (Test-Path -LiteralPath $stagedManifest) { Write-Host 'C. Extension folder: PASS' } else { $nativeOk = $false; Write-Host 'C. Extension folder: FAIL - thiếu manifest.json' }
$serviceOk = $false
if (Test-Path -LiteralPath $hostExe) {
    $probe = & $hostExe --service-probe 2>$null
    if ($LASTEXITCODE -eq 0 -and -not [string]::IsNullOrWhiteSpace(($probe | Out-String))) {
        $serviceOk = $true
        Write-Host 'D. BrowserHost -> Service: PASS'
        Write-Host 'E. Đánh giá chính sách: PASS'
    } else {
        Write-Host 'D. BrowserHost -> Service: CHƯA XÁC NHẬN'
        Write-Host 'E. Đánh giá chính sách: CHƯA XÁC NHẬN'
    }
} else {
    Write-Host 'D. BrowserHost -> Service: CHƯA XÁC NHẬN (chưa có Release build)'
    Write-Host 'E. Đánh giá chính sách: CHƯA XÁC NHẬN'
}
Write-Host 'F. Extension runtime: CHƯA XÁC NHẬN - Parent WEB sẽ hiển thị KẾT NỐI sau lần kiểm tra đầu tiên.'
if ($nativeOk -and $identityOk -and $serviceOk) { Write-Host 'M4 CHECK: CÀI ĐẶT SẴN SÀNG; runtime extension cần xác nhận trong Parent WEB.' } else { Write-Host 'M4 CHECK: CHƯA HOÀN TẤT — xem dòng FAIL/CHƯA XÁC NHẬN ở trên.'; exit 1 }