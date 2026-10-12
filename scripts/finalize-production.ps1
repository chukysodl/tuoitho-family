param(
    [Parameter(Mandatory=$true)][string]$InstallDir,
    [int]$WaitSeconds = 20
)

$ErrorActionPreference = 'Stop'

function Assert-Admin {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        throw 'Finalize protection requires Administrator rights.'
    }
}

function Wait-ServiceRunning([string]$name, [int]$timeoutSeconds) {
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds([Math]::Max(4, $timeoutSeconds))
    do {
        $service = Get-CimInstance Win32_Service -Filter "Name='$name'" -ErrorAction SilentlyContinue
        if ($service -and $service.State -eq 'Running') {
            return $service
        }
        Start-Sleep -Milliseconds 500
    } while ([DateTimeOffset]::UtcNow -lt $deadline)
    return $null
}

function Run-Checked([string]$fileName, [string[]]$arguments, [string]$stage) {
    $output = & $fileName @arguments 2>&1
    $exitCode = $LASTEXITCODE
    if ($exitCode -ne 0) {
        $detail = ($output | Out-String).Trim()
        throw "$stage failed. ExitCode=$exitCode $detail"
    }
}

Assert-Admin

$InstallDir = [IO.Path]::GetFullPath($InstallDir)
$serviceName = 'TuoiTho.Service'
$programData = Join-Path $env:ProgramData 'TuoiTho'
$parentAuth = Join-Path $programData 'parent-auth.json'
$logPath = Join-Path $programData 'finalize-protection.log'

New-Item -ItemType Directory -Path $programData -Force | Out-Null
Set-Content -LiteralPath $logPath -Value ('=== FINALIZE PROTECTION ' + [DateTimeOffset]::Now.ToString('O') + ' ===') -Encoding UTF8

try {
    $service = Get-CimInstance Win32_Service -Filter "Name='$serviceName'" -ErrorAction SilentlyContinue
    if (-not $service) {
        throw 'TuoiTho.Service was not found.'
    }

    if ($service.StartMode -ne 'Auto') {
        Run-Checked 'sc.exe' @('config', $serviceName, 'start=', 'auto') 'Set service startup'
    }

    if ($service.State -ne 'Running') {
        & sc.exe start $serviceName | Out-Null
        $service = Wait-ServiceRunning $serviceName $WaitSeconds
        if (-not $service) {
            throw 'TuoiTho.Service did not reach RUNNING state.'
        }
    }

    $hardenedServiceSddl = 'D:(A;;GA;;;SY)(A;;GRRP;;;BA)(A;;GR;;;AU)'
    Run-Checked 'sc.exe' @('sdset', $serviceName, $hardenedServiceSddl) 'Harden service DACL'

    Run-Checked 'icacls.exe' @(
        $InstallDir,
        '/inheritance:r',
        '/grant:r',
        '*S-1-5-18:(OI)(CI)(F)',
        '*S-1-5-32-544:(OI)(CI)(RX)',
        '*S-1-5-32-545:(OI)(CI)(RX)',
        '/T',
        '/C',
        '/Q'
    ) 'Harden install directory ACL'

    if (Test-Path -LiteralPath $parentAuth) {
        Run-Checked 'icacls.exe' @(
            $parentAuth,
            '/inheritance:r',
            '/grant:r',
            '*S-1-5-18:(F)',
            '*S-1-5-32-544:(R)',
            '/C',
            '/Q'
        ) 'Harden parent auth ACL'
    }

    $service = Wait-ServiceRunning $serviceName 8
    if (-not $service) {
        throw 'TuoiTho.Service stopped after hardening.'
    }

    Add-Content -LiteralPath $logPath -Value 'FINALIZE PROTECTION: PASS' -Encoding UTF8
    exit 0
}
catch {
    Add-Content -LiteralPath $logPath -Value ('FINALIZE PROTECTION: FAIL - ' + $_.Exception.Message) -Encoding UTF8
    Write-Error $_.Exception.Message
    exit 9
}
