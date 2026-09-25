param([switch]$Elevated)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSCommandPath
$supabaseRoot = Join-Path $repo 'infra\supabase'
$dashboardUrl = 'https://chukysodl.github.io/tuoitho-family/'
$configDirectory = Join-Path $env:ProgramData 'TuoiTho\RemoteControl'
$configPath = Join-Path $configDirectory 'remote-control.json'

function Test-Administrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    return ([Security.Principal.WindowsPrincipal]::new($identity)).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

if (-not (Test-Administrator)) {
    Write-Host 'M5 setup needs administrator permission to protect machine configuration and restart TuoiTho.Service.'
    $arguments = "-NoLogo -NoProfile -ExecutionPolicy Bypass -File `"$PSCommandPath`" -Elevated"
    $child = Start-Process -FilePath 'powershell.exe' -Verb RunAs -ArgumentList $arguments -Wait -PassThru
    exit $child.ExitCode
}

function Stop-Fail([string]$Message, [int]$Code = 1) {
    Write-Host "M5 SETUP FAIL: $Message" -ForegroundColor Red
    if ($Elevated) {
        Write-Host ''
        Read-Host 'Press Enter to close this administrator window' | Out-Null
    }
    exit $Code
}

$supabaseCommand = Get-Command supabase -ErrorAction SilentlyContinue
$npxCommand = Get-Command npx.cmd -ErrorAction SilentlyContinue
if ($null -eq $npxCommand) { $npxCommand = Get-Command npx -ErrorAction SilentlyContinue }
$useNpx = $false
if ($null -eq $supabaseCommand) {
    $node = Get-Command node -ErrorAction SilentlyContinue
    $npx = $npxCommand
    $nodeMajor = 0
    if ($node) { $version = (& node --version 2>$null); if ($version -match '^v(\d+)') { $nodeMajor = [int]$Matches[1] } }
    if ($npx -and $nodeMajor -ge 20) {
        $useNpx = $true
        Write-Host 'Supabase CLI not found globally; using the official npm CLI runner (Node.js 20+).'
    } else {
        Write-Host 'Supabase CLI is missing. Install it using the official Windows instructions:'
        Write-Host 'https://supabase.com/docs/guides/local-development/cli/getting-started'
        Write-Host 'If Scoop is already installed, run: scoop install supabase'
        Stop-Fail 'Install Supabase CLI, then run M5-REMOTE-SETUP.cmd again.' 2
    }
}

function Invoke-Supabase([string[]]$CliArgs) {
    if ($script:useNpx) { & $script:npxCommand.Source --yes supabase@latest @CliArgs }
    else { & supabase @CliArgs }
    if ($LASTEXITCODE -ne 0) { throw "Supabase CLI failed (exit $LASTEXITCODE): $($CliArgs -join ' ')" }
}

function Get-SupabaseProjects {
    if ($script:useNpx) { $raw = & $script:npxCommand.Source --yes supabase@latest projects list --output json }
    else { $raw = & supabase projects list --output json }
    if ($LASTEXITCODE -ne 0) { throw 'Supabase login is missing or project list is unavailable.' }
    try { return @((($raw | Out-String).Trim() | ConvertFrom-Json)) }
    catch { throw 'Supabase CLI did not return a readable project list.' }
}

function Read-ProjectRef {
    $linkedRefFile = Join-Path $supabaseRoot '.temp\project-ref'
    if (Test-Path $linkedRefFile) {
        $existing = (Get-Content $linkedRefFile -Raw).Trim()
        if ($existing -match '^[a-z0-9]{8,40}$') {
            Write-Host "Using the already linked Supabase project: $existing"
            return $existing
        }
    }

    $projects = $null
    try { $projects = Get-SupabaseProjects }
    catch {
        Write-Host 'Supabase CLI login is needed. Complete the Supabase login prompt; the access token is handled by the CLI and is not saved in this repository.'
        try { Invoke-Supabase @('login') }
        catch { throw 'Supabase login did not complete.' }
        $projects = Get-SupabaseProjects
    }

    if ($projects.Count -eq 1) { return [string]$projects[0].id }
    if ($projects.Count -gt 1) {
        Write-Host 'Choose the Supabase project to link:'
        for ($i = 0; $i -lt $projects.Count; $i++) { Write-Host ("{0}. {1} ({2})" -f ($i + 1), $projects[$i].name, $projects[$i].id) }
        $selection = 0
        if (-not [int]::TryParse((Read-Host 'Project number'), [ref]$selection) -or $selection -lt 1 -or $selection -gt $projects.Count) { throw 'Invalid project selection.' }
        return [string]$projects[$selection - 1].id
    }

    Write-Host 'No Supabase project is visible in this account. Create one at https://supabase.com/dashboard/project/_/settings/general, then copy its Project Ref from the project URL.'
    $ref = (Read-Host 'Project Ref').Trim()
    if ($ref -notmatch '^[a-z0-9]{8,40}$') { throw 'Project Ref format is invalid.' }
    return $ref
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

function Set-RemoteConfigAcl([string]$Path, [bool]$Directory) {
    if ($Directory) { $acl = [Security.AccessControl.DirectorySecurity]::new() }
    else { $acl = [Security.AccessControl.FileSecurity]::new() }
    $acl.SetAccessRuleProtection($true, $false)
    $system = [Security.Principal.SecurityIdentifier]::new('S-1-5-18')
    $admins = [Security.Principal.SecurityIdentifier]::new('S-1-5-32-544')
    $users = [Security.Principal.SecurityIdentifier]::new('S-1-5-32-545')
    $inheritance = if ($Directory) { [Security.AccessControl.InheritanceFlags]::ContainerInherit -bor [Security.AccessControl.InheritanceFlags]::ObjectInherit } else { [Security.AccessControl.InheritanceFlags]::None }
    $propagation = [Security.AccessControl.PropagationFlags]::None
    foreach ($sid in @($system, $admins)) {
        $acl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new($sid, [Security.AccessControl.FileSystemRights]::FullControl, $inheritance, $propagation, [Security.AccessControl.AccessControlType]::Allow))
    }
    $readRights = [Security.AccessControl.FileSystemRights]::ReadAndExecute
    if (-not $Directory) { $readRights = [Security.AccessControl.FileSystemRights]::Read }
    $acl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new($users, $readRights, $inheritance, $propagation, [Security.AccessControl.AccessControlType]::Allow))
    Set-Acl -LiteralPath $Path -AclObject $acl
}

function Restart-TuoiThoServiceSafely {
    $service = Get-CimInstance Win32_Service -Filter "Name='TuoiTho.Service'" -ErrorAction SilentlyContinue
    if ($service) {
        if ($service.PathName -notmatch '(?i)TuoiTho\.Service\.exe') { throw 'Refusing to restart TuoiTho.Service: its configured executable is not TuoiTho.Service.exe.' }
        if ($service.State -eq 'Running') {
            Stop-Service -Name 'TuoiTho.Service' -ErrorAction Stop
            (Get-Service -Name 'TuoiTho.Service').WaitForStatus('Stopped', [TimeSpan]::FromSeconds(20))
        }
        Start-Service -Name 'TuoiTho.Service' -ErrorAction Stop
        (Get-Service -Name 'TuoiTho.Service').WaitForStatus('Running', [TimeSpan]::FromSeconds(30))
        Write-Host 'TuoiTho.Service restarted safely through its registered service entry.'
        return
    }

    $m1State = Join-Path $env:TEMP 'tuoitho-m1-state.json'
    if (Test-Path $m1State) {
        & (Join-Path $repo 'scripts\M1-STOP.ps1')
        if ($LASTEXITCODE -ne 0) { throw 'Tracked M1 components did not stop safely; remote config was saved but Service was not restarted.' }
        & (Join-Path $repo 'scripts\M1-START.ps1')
        if ($LASTEXITCODE -ne 0) { throw 'M1 restart failed. Use scripts\M1-START.cmd after reviewing its diagnostic.' }
        return
    }

    Write-Host 'No TuoiTho Windows service or tracked M1 runtime is running. The protected config is ready and will be read at the next normal Service start.'
}

try {
    Push-Location $supabaseRoot
    try {
        $projectRef = Read-ProjectRef
        Write-Host 'Linking the Supabase project...'
        Invoke-Supabase @('link', '--project-ref', $projectRef)
        Write-Host 'Applying repository database migrations...'
        Invoke-Supabase @('db', 'push')
        Write-Host 'Deploying device-gateway...'
        Invoke-Supabase @('functions', 'deploy', 'device-gateway', '--project-ref', $projectRef)
        Write-Host 'Deploying parent-gateway...'
        Invoke-Supabase @('functions', 'deploy', 'parent-gateway', '--project-ref', $projectRef)
    } finally { Pop-Location }

    $projectUrl = "https://$projectRef.supabase.co"
    Write-Host "Supabase Project URL: $projectUrl"
    Write-Host 'Paste only the project PUBLIC anon/publishable key from Supabase API settings. Never use a secret/service-role key.'
    $publicKey = (Read-Host 'Public anon/publishable key').Trim()
    if (-not (Test-PublicProjectKey $publicKey)) { throw 'The supplied value is not recognized as a public anon/publishable key; no key was written.' }
    $deviceName = (Read-Host "Device display name (Enter keeps $env:COMPUTERNAME)").Trim()
    if ([string]::IsNullOrWhiteSpace($deviceName)) { $deviceName = $env:COMPUTERNAME }
    if ($deviceName.Length -gt 100) { throw 'Device display name is too long (maximum 100 characters).' }

    $parentSids = @()
    if (Test-Path $configPath) {
        try {
            $old = Get-Content $configPath -Raw | ConvertFrom-Json
            if ($old.ParentControl.AllowedParentSids) { $parentSids += @($old.ParentControl.AllowedParentSids) }
        } catch { throw 'Existing protected remote config could not be read; refusing to overwrite it.' }
    }
    $currentSid = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
    $parentSids += $currentSid
    $parentSids = @($parentSids | Where-Object { $_ -match '^S-1-[0-9-]+$' } | Select-Object -Unique)

    New-Item -ItemType Directory -Path $configDirectory -Force | Out-Null
    Set-RemoteConfigAcl $configDirectory $true
    $config = [ordered]@{
        RemoteControl = [ordered]@{ Enabled = $true; SupabaseUrl = $projectUrl; SupabaseAnonKey = $publicKey; DeviceName = $deviceName; PollIntervalSeconds = 5; StatusIntervalSeconds = 15 }
        ParentControl = [ordered]@{ AllowedParentSids = $parentSids }
    }
    $temporaryConfig = Join-Path $configDirectory ('remote-control.' + [Guid]::NewGuid().ToString('N') + '.tmp')
    $json = $config | ConvertTo-Json -Depth 5
    [IO.File]::WriteAllText($temporaryConfig, $json, [Text.UTF8Encoding]::new($false))
    Set-RemoteConfigAcl $temporaryConfig $false
    if (Test-Path $configPath) { Remove-Item -LiteralPath $configPath -Force }
    Move-Item -LiteralPath $temporaryConfig -Destination $configPath -Force

    Restart-TuoiThoServiceSafely
    Write-Host 'REMOTE DEVICE CONFIG: PASS' -ForegroundColor Green
    Write-Host "Config file: $configPath (public settings only; restricted to SYSTEM/Administrators for writes)."
    Write-Host "Phone dashboard: $dashboardUrl"
    Write-Host "For email-confirmed Supabase accounts, set Auth > URL Configuration > Site URL to $dashboardUrl; keep email confirmation enabled."
    Write-Host 'Next: run M5-REMOTE-CHECK.cmd. No TestMode or local policy setting was changed.'
    exit 0
} catch {
    Stop-Fail ($_.Exception.Message) 1
} finally {
    if (Get-Location | Where-Object { $_.Path -eq $supabaseRoot }) { Pop-Location }
}
