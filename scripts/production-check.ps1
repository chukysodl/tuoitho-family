param(
    [string]$InstallDir = (Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::ProgramFiles) 'QuanLyThoiGian'),
    [int]$WaitSeconds = 12
)
$ErrorActionPreference = 'Stop'
$failures = [System.Collections.Generic.List[string]]::new()

function Pass([string]$name, [string]$detail) {
    Write-Host "[PASS] $name - $detail"
}
function Fail([string]$name, [string]$detail) {
    Write-Host "[FAIL] $name - $detail"
    $failures.Add("$name: $detail")
}

$serviceName = 'TuoiTho.Service'
$deadline = [DateTimeOffset]::UtcNow.AddSeconds([Math]::Max(0,$WaitSeconds))
$service = $null
do {
    $service = Get-CimInstance Win32_Service -Filter "Name='$serviceName'" -ErrorAction SilentlyContinue
    if ($service -and $service.State -eq 'Running') { break }
    Start-Sleep -Milliseconds 500
} while ([DateTimeOffset]::UtcNow -lt $deadline)

if (-not $service) {
    Fail 'Windows Service' 'Không tìm thấy TuoiTho.Service.'
} elseif ($service.State -ne 'Running') {
    Fail 'Windows Service' "State=$($service.State)"
} else {
    Pass 'Windows Service' "Running PID=$($service.ProcessId)"
}

if ($service -and $service.StartMode -eq 'Auto') {
    Pass 'Service startup' 'Automatic'
} else {
    Fail 'Service startup' "StartMode=$($service.StartMode)"
}

$qfailure = (& sc.exe qfailure $serviceName 2>&1 | Out-String)
if ($LASTEXITCODE -eq 0 -and $qfailure -match 'RESTART') {
    Pass 'Service Recovery' 'Có restart action khi process lỗi.'
} else {
    Fail 'Service Recovery' 'Không xác nhận được restart action.'
}

$programData = Join-Path $env:ProgramData 'TuoiTho'
$productionPath = Join-Path $programData 'production.json'
if (Test-Path $productionPath) {
    try {
        $production = Get-Content $productionPath -Raw | ConvertFrom-Json
        if ($production.ProductionProtection.Enabled -eq $true -and
            [int]$production.ProductionProtection.PollIntervalSeconds -le 2) {
            Pass 'Production protection' "Enabled, watchdog=$($production.ProductionProtection.PollIntervalSeconds)s"
        } else {
            Fail 'Production protection' 'ProductionProtection chưa bật đúng.'
        }
    } catch {
        Fail 'Production protection' $_.Exception.Message
    }
} else {
    Fail 'Production protection' 'Thiếu production.json.'
}

$authPath = Join-Path $programData 'parent-auth.json'
if (Test-Path $authPath) {
    Pass 'Mật khẩu phụ huynh' 'Đã có verifier cục bộ.'
} else {
    Fail 'Mật khẩu phụ huynh' 'Chưa thiết lập mật khẩu.'
}

$agentExe = Join-Path $InstallDir 'SessionAgent\TuoiTho.SessionAgent.exe'
$currentSession = (Get-Process -Id $PID).SessionId
$agentRunning = $false
$deadline = [DateTimeOffset]::UtcNow.AddSeconds([Math]::Max(0,$WaitSeconds))
do {
    foreach ($process in Get-Process -Name 'TuoiTho.SessionAgent' -ErrorAction SilentlyContinue) {
        try {
            if ($process.SessionId -eq $currentSession -and
                $process.Path -and
                [IO.Path]::GetFullPath($process.Path) -eq [IO.Path]::GetFullPath($agentExe)) {
                $agentRunning = $true
                break
            }
        } catch { }
    }
    if ($agentRunning) { break }
    Start-Sleep -Milliseconds 500
} while ([DateTimeOffset]::UtcNow -lt $deadline)

if ($agentRunning) {
    Pass 'SessionAgent watchdog' 'Agent đang chạy trong phiên hiện tại.'
} else {
    Fail 'SessionAgent watchdog' 'Không thấy SessionAgent production trong phiên hiện tại.'
}

$policyDb = Join-Path $programData 'tuoitho.db'
if (Test-Path $policyDb) {
    Pass 'Dữ liệu policy' 'SQLite database tồn tại.'
} else {
    Fail 'Dữ liệu policy' 'Chưa thấy tuoitho.db.'
}

if ($failures.Count -gt 0) {
    Write-Host ''
    Write-Host "PRODUCTION CHECK: FAIL ($($failures.Count))"
    exit 3
}

Write-Host ''
Write-Host 'PRODUCTION CHECK: PASS'
exit 0
