$ErrorActionPreference = 'Stop'
$configPath = Join-Path $env:ProgramData 'TuoiTho\RemoteControl\remote-control.json'
$repo = Split-Path -Parent $PSCommandPath
$failures = [Collections.Generic.List[string]]::new()
$checks = [ordered]@{}

function Show-Check([string]$Name, [bool]$Passed, [string]$Detail, [string]$Repair) {
    $script:checks[$Name] = $Passed
    $mark = if ($Passed) { 'PASS' } else { 'FAIL' }
    Write-Host ("[{0}] {1}: {2}" -f $mark, $Name, $Detail) -ForegroundColor $(if ($Passed) { 'Green' } else { 'Red' })
    if (-not $Passed) { $script:failures.Add("- $Name`: $Repair") }
}

function Get-HttpProbe([string]$Uri, [hashtable]$Headers, [object]$Body) {
    try {
        $response = Invoke-WebRequest -UseBasicParsing -Method Post -Uri $Uri -Headers $Headers -ContentType 'application/json' -Body ($Body | ConvertTo-Json -Compress) -TimeoutSec 12
        return @{ Status = [int]$response.StatusCode; Content = [string]$response.Content }
    } catch {
        $response = $_.Exception.Response
        if ($response) { return @{ Status = [int]$response.StatusCode; Content = '' } }
        return @{ Status = 0; Content = '' }
    }
}

function Get-RemoteDiagnosticsFromPipe {
    $pipe = [IO.Pipes.NamedPipeClientStream]::new('.', 'TuoiTho.ParentControl', [IO.Pipes.PipeDirection]::InOut, [IO.Pipes.PipeOptions]::None)
    try {
        $pipe.Connect(3000)
        $pipe.ReadTimeout = 5000
        $writer = [IO.StreamWriter]::new($pipe, [Text.UTF8Encoding]::new($false), 1024, $true)
        $reader = [IO.StreamReader]::new($pipe, [Text.UTF8Encoding]::new($false), $true, 1024, $true)
        $writer.AutoFlush = $true
        $request = @{ Action = 'getRemoteDiagnostics'; ProfileId = ''; ManagedSessionId = -1 }
        $writer.WriteLine(($request | ConvertTo-Json -Compress))
        $line = $reader.ReadLine()
        if ([string]::IsNullOrWhiteSpace($line) -or $line.Length -gt 16384) { throw 'Empty or oversized Parent status response.' }
        return ($line | ConvertFrom-Json)
    } finally { $pipe.Dispose() }
}

function Test-PublicProjectKey([string]$Key) {
    if ([string]::IsNullOrWhiteSpace($Key) -or $Key.Length -lt 20 -or $Key.Length -gt 512 -or $Key -match '(?i)service_role|sb_secret_') { return $false }
    if ($Key.StartsWith('sb_publishable_', [StringComparison]::Ordinal)) { return $true }
    $segments = $Key.Split('.')
    if ($segments.Count -ne 3) { return $false }
    try {
        $payload = $segments[1].Replace('-', '+').Replace('_', '/')
        $payload = $payload.PadRight([int][Math]::Ceiling($payload.Length / 4.0) * 4, '=')
        $claims = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($payload)) | ConvertFrom-Json
        return $claims.role -eq 'anon'
    } catch { return $false }
}

$serviceProcessRunning = $false
$service = Get-Service -Name 'TuoiTho.Service' -ErrorAction SilentlyContinue
if ($service -and $service.Status -eq 'Running') { $serviceProcessRunning = $true }
if (-not $serviceProcessRunning) {
    $expectedExe = Join-Path $repo 'src\TuoiTho.Service\bin\Release\net10.0-windows\TuoiTho.Service.exe'
    if (Test-Path $expectedExe) {
        $expectedFull = [IO.Path]::GetFullPath($expectedExe)
        $serviceProcessRunning = [bool](Get-CimInstance Win32_Process -Filter "Name='TuoiTho.Service.exe'" -ErrorAction SilentlyContinue | Where-Object { $_.ExecutablePath -and [string]::Equals([IO.Path]::GetFullPath($_.ExecutablePath), $expectedFull, [StringComparison]::OrdinalIgnoreCase) } | Select-Object -First 1)
    }
}
$serviceDetail = if ($serviceProcessRunning) { 'TuoiTho.Service is running.' } else { 'No registered or exact tracked TuoiTho.Service process is running.' }
Show-Check 'Service running' $serviceProcessRunning $serviceDetail 'Start TuoiTho.Service or run the safe M1-START.cmd for a tracked M1 test runtime.'

$remote = $null
$publicKey = $null
$supabaseUrl = $null
$configValid = $false
if (Test-Path $configPath) {
    try {
        $config = Get-Content $configPath -Raw | ConvertFrom-Json
        $remote = $config.RemoteControl
        $publicKey = [string]$remote.SupabaseAnonKey
        $supabaseUrl = [string]$remote.SupabaseUrl
        $uri = $null
        $urlValid = [Uri]::TryCreate($supabaseUrl, [UriKind]::Absolute, [ref]$uri) -and $uri.Scheme -eq 'https' -and [string]::IsNullOrEmpty($uri.UserInfo) -and [string]::IsNullOrEmpty($uri.Query) -and [string]::IsNullOrEmpty($uri.Fragment)
        $keyValid = Test-PublicProjectKey $publicKey
        $configValid = ($remote.Enabled -eq $true) -and $urlValid -and $keyValid
    } catch { $configValid = $false }
}
 $remoteEnabled = $null -ne $remote -and $remote.Enabled -eq $true
 $remoteDetail = if ($remoteEnabled) { 'RemoteControl.Enabled is true.' } else { 'RemoteControl.Enabled is false or its config is missing.' }
 $urlConfigured = $null -ne $supabaseUrl -and $urlValid
 $urlDetail = if ($urlConfigured) { 'HTTPS endpoint is configured; value hidden.' } else { 'No valid HTTPS Supabase URL is configured.' }
 $keyConfigured = $null -ne $publicKey -and $keyValid
 $keyDetail = if ($keyConfigured) { 'Public key is present; value hidden.' } else { 'No valid public anon/publishable key is configured; value hidden.' }
Show-Check 'Remote Enabled' $remoteEnabled $remoteDetail 'Run M5-REMOTE-SETUP.cmd and complete its Supabase configuration.'
Show-Check 'Supabase URL configured' $urlConfigured $urlDetail 'Rerun M5-REMOTE-SETUP.cmd and use the project shown by Supabase.'
Show-Check 'Public key configured' $keyConfigured $keyDetail 'Enter only an anon/publishable key. Never enter a secret/service-role key.'

$runtime = $null
try {
    $response = Get-RemoteDiagnosticsFromPipe
    if ($response.Accepted -eq $true) { $runtime = $response.RemoteDiagnostics }
} catch { }
$deviceIdPresent = $null -ne $runtime -and $runtime.DeviceIdentityReady -eq $true -and -not [string]::IsNullOrWhiteSpace([string]$runtime.DeviceId)
$deviceIdentityDetail = if ($deviceIdPresent) { 'Protected device identity is initialized.' } else { 'No initialized device identity was returned by the local Service.' }
Show-Check 'Device identity present' $deviceIdPresent $deviceIdentityDetail 'Start the Service with RemoteControl enabled; do not copy or delete its credential database.'

$databaseReady = $false
$deviceGatewayReady = $false
$parentGatewayReady = $false
$pairingReady = $false
if ($configValid) {
    $headers = @{ apikey = $publicKey; Authorization = "Bearer $publicKey" }
    $deviceProbe = Get-HttpProbe ("$supabaseUrl/functions/v1/device-gateway") $headers @{ action = 'health' }
    if ($deviceProbe.Status -eq 200) {
        try { $health = $deviceProbe.Content | ConvertFrom-Json; $databaseReady = $health.ready -eq $true -and $health.database -eq 'ready'; $deviceGatewayReady = $databaseReady } catch { }
    }
    $parentProbe = Get-HttpProbe ("$supabaseUrl/functions/v1/parent-gateway") $headers @{ action = 'health' }
    # A 401 proves the deployed authenticated function/gateway is reachable without a parent login token.
    $parentGatewayReady = $parentProbe.Status -eq 401
    $pairingReady = $databaseReady -and $deviceGatewayReady -and $parentGatewayReady
}
$databaseDetail = if ($databaseReady) { 'device-gateway confirmed the migrated remote_devices table.' } else { 'Database health probe did not confirm the migrated table.' }
$deviceGatewayDetail = if ($deviceGatewayReady) { 'Function and database health probe succeeded.' } else { 'device-gateway health probe did not succeed.' }
$parentGatewayDetail = if ($parentGatewayReady) { 'Authenticated endpoint returned the expected unauthenticated response.' } else { 'parent-gateway did not return the expected unauthenticated response.' }
$pairingDetail = if ($pairingReady) { 'Database schema and both gateway endpoints are reachable.' } else { 'The database and both gateway checks are not all ready.' }
Show-Check 'Database reachable' $databaseReady $databaseDetail 'Rerun M5-REMOTE-SETUP.cmd; verify migration deployment and Supabase project health.'
Show-Check 'device-gateway reachable' $deviceGatewayReady $deviceGatewayDetail 'Rerun M5-REMOTE-SETUP.cmd to deploy device-gateway.'
Show-Check 'parent-gateway reachable' $parentGatewayReady $parentGatewayDetail 'Rerun M5-REMOTE-SETUP.cmd to deploy parent-gateway; check the Supabase project URL.'
Show-Check 'Pairing path ready' $pairingReady $pairingDetail 'Resolve database and Edge Function checks above before pairing.'

$now = [DateTimeOffset]::UtcNow
$pollTime = $null; $publishTime = $null
if ($runtime -and $runtime.LastCommandPollAtUtc) { $pollTime = [DateTimeOffset]::Parse([string]$runtime.LastCommandPollAtUtc).ToUniversalTime() }
if ($runtime -and $runtime.LastStatusPublishedAtUtc) { $publishTime = [DateTimeOffset]::Parse([string]$runtime.LastStatusPublishedAtUtc).ToUniversalTime() }
$pollReady = $null -ne $pollTime -and ($now - $pollTime).TotalSeconds -le 120
$publishReady = $null -ne $publishTime -and ($now - $publishTime).TotalSeconds -le 120
Show-Check 'Status publication working' $publishReady ($(if ($publishReady) { 'Recent successful publish: ' + $publishTime.ToLocalTime().ToString('HH:mm:ss') } else { 'No recent successful status publish.' })) 'Keep Service online with the network available, then wait up to 30 seconds and rerun this check.'
Show-Check 'Command polling working' $pollReady ($(if ($pollReady) { 'Recent successful poll: ' + $pollTime.ToLocalTime().ToString('HH:mm:ss') } else { 'No recent successful command poll.' })) 'Check Service logs/network; keep the Supabase project online and rerun this check.'
$testMode = $null -ne $runtime -and $runtime.TestMode -eq $true
$testModeDetail = if ($null -eq $runtime -or $null -eq $runtime.TestMode) { 'Service did not return the current TestMode state.' } elseif ($testMode) { 'TestMode is enabled; remote LOCK NOW remains simulation-only.' } else { 'CHẾ ĐỘ THỰC đang bật; M5 first acceptance requires safe TestMode.' }
Show-Check 'TestMode' $testMode $testModeDetail 'Enable TestMode using the existing local M1/test setup before M5. This script never changes TestMode.'

if ($failures.Count -gt 0) {
    Write-Host 'Repairs:' -ForegroundColor Yellow
    $failures | ForEach-Object { Write-Host $_ }
    Write-Host 'M5 REMOTE CHECK: NOT READY' -ForegroundColor Red
    exit 1
}
Write-Host 'M5 REMOTE CHECK: READY' -ForegroundColor Green
exit 0
