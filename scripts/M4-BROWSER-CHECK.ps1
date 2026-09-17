$ErrorActionPreference = 'Stop'
$config = Join-Path $env:ProgramData 'TuoiTho\browser-control.json'
if (-not (Test-Path $config)) { throw 'FAIL: thiếu browser-control.json. Hãy chạy M4-BROWSER-INSTALL.cmd.' }
$value = Get-Content $config -Raw | ConvertFrom-Json
if ([string]::IsNullOrWhiteSpace($value.ExtensionId) -or [string]::IsNullOrWhiteSpace($value.ProfileId) -or [string]::IsNullOrWhiteSpace($value.ManagedUserSid) -or $value.ManagedSessionId -lt 0) { throw 'FAIL: browser-control.json không hợp lệ.' }
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$host = Join-Path $repo 'src\TuoiTho.BrowserHost\bin\Release\net10.0-windows\TuoiTho.BrowserHost.exe'
$registrations = @(@{ Browser = 'Chrome'; Key = 'HKCU:\Software\Google\Chrome\NativeMessagingHosts\com.tuoitho.browserhost' }, @{ Browser = 'Edge'; Key = 'HKCU:\Software\Microsoft\Edge\NativeMessagingHosts\com.tuoitho.browserhost' })
$nativeOk = $true
foreach ($registration in $registrations) {
    try {
        $manifestPath = (Get-ItemProperty $registration.Key -ErrorAction Stop).'(default)'
        $manifest = Get-Content $manifestPath -Raw | ConvertFrom-Json
        $valid = (Test-Path $manifestPath) -and $manifest.name -eq 'com.tuoitho.browserhost' -and $manifest.type -eq 'stdio' -and $manifest.path -eq $host -and ($manifest.allowed_origins -contains "chrome-extension://$($value.ExtensionId)/")
        if (-not $valid) { throw 'manifest không khớp cấu hình.' }
        Write-Host "A. Native Host $($registration.Browser): PASS"
    } catch { $nativeOk = $false; Write-Host "A. Native Host $($registration.Browser): FAIL — $($_.Exception.Message)" }
}
$extension = Join-Path $env:LOCALAPPDATA 'TuoiTho\M4\Extension'
if (-not (Test-Path (Join-Path $extension 'manifest.json'))) { $nativeOk = $false; Write-Host 'A. Extension folder: FAIL — thiếu manifest.json' } else { Write-Host 'A. Extension folder: PASS' }
$serviceOk = $false
if (Test-Path $host) {
    $probe = & $host --service-probe 2>$null
    if ($LASTEXITCODE -eq 0 -and $probe) { $serviceOk = $true; Write-Host "C. BrowserHost -> Service: PASS"; Write-Host "D. Đánh giá chính sách: PASS" } else { Write-Host 'C. BrowserHost -> Service: CHƯA XÁC NHẬN'; Write-Host 'D. Đánh giá chính sách: CHƯA XÁC NHẬN' }
} else { Write-Host 'C. BrowserHost -> Service: CHƯA XÁC NHẬN (chưa có Release build)'; Write-Host 'D. Đánh giá chính sách: CHƯA XÁC NHẬN' }
Write-Host 'B. Extension runtime: CHƯA XÁC NHẬN — hãy mở YouTube/TikTok; Parent WEB sẽ hiển thị KẾT NỐI sau lần kiểm tra đầu tiên.'
if ($nativeOk -and $serviceOk) { Write-Host 'M4 CHECK: CÀI ĐẶT SẴN SÀNG; runtime extension vẫn cần xác nhận trong Parent WEB.' } else { Write-Host 'M4 CHECK: CHƯA HOÀN TẤT — xem dòng FAIL/CHƯA XÁC NHẬN ở trên.' }
