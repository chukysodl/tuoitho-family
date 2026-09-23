# Shared helpers for M4 browser deployment. Never copy browser-extension\manifest.json directly
# into the staged folder: use Copy-M4ExtensionPayload so the staged Chromium identity key survives.
function Build-M4BrowserHost {
    param([string]$RepositoryRoot)
    $project = Join-Path $RepositoryRoot 'src\TuoiTho.BrowserHost\TuoiTho.BrowserHost.csproj'
    & dotnet build $project --configuration Release | Out-Host
    if ($LASTEXITCODE -ne 0) { throw 'Không thể build TuoiTho.BrowserHost.' }
    $browserHostPath = Join-Path $RepositoryRoot 'src\TuoiTho.BrowserHost\bin\Release\net10.0-windows\TuoiTho.BrowserHost.exe'
    if (-not (Test-Path -LiteralPath $browserHostPath)) { throw 'Không tìm thấy BrowserHost sau build.' }
    return $browserHostPath
}

function Get-M4Paths {
    param([string]$RepositoryRoot)
    return @{
        Source = Join-Path $RepositoryRoot 'browser-extension'
        Extension = Join-Path $env:LOCALAPPDATA 'TuoiTho\M4\Extension'
        ConfigDirectory = Join-Path $env:ProgramData 'TuoiTho'
        Config = Join-Path (Join-Path $env:ProgramData 'TuoiTho') 'browser-control.json'
        NativeDirectory = Join-Path $env:LOCALAPPDATA 'TuoiTho\NativeMessaging'
    }
}

function Copy-M4ExtensionPayload {
    param([string]$BrowserHost, [string]$Source, [string]$Extension, [switch]$RequireExistingKey, [switch]$AllowKeyRecovery)
    $manifest = Join-Path $Extension 'manifest.json'
    $backup = $null
    if (Test-Path -LiteralPath $manifest) {
        $backup = Join-Path ([System.IO.Path]::GetTempPath()) ("tuoitho-m4-manifest-" + [Guid]::NewGuid().ToString('N') + '.json')
        Copy-Item -LiteralPath $manifest -Destination $backup -Force
        & $BrowserHost --extension-id $backup | Out-Null
        if ($LASTEXITCODE -ne 0) {
            Remove-Item -LiteralPath $backup -Force -ErrorAction SilentlyContinue
            $backup = $null
            if (-not $AllowKeyRecovery) { throw 'Staged extension key không hợp lệ. Hãy chạy M4-BROWSER-REPAIR.cmd.' }
        }
    }
    if ($RequireExistingKey -and $null -eq $backup) { throw 'Không có staged extension key. Hãy chạy M4-BROWSER-REPAIR.cmd.' }

    New-Item -ItemType Directory -Force $Extension | Out-Null
    Copy-Item (Join-Path $Source '*') $Extension -Recurse -Force
    if ($null -ne $backup) {
        try {
            & $BrowserHost --preserve-extension-key $backup $manifest | Out-Null
            if ($LASTEXITCODE -ne 0) { throw 'Không thể khôi phục staged extension key.' }
        }
        finally { Remove-Item -LiteralPath $backup -Force -ErrorAction SilentlyContinue }
    }
}

function Protect-M4BrowserConfig {
    param([string]$Config)
    $sid = [System.Security.Principal.WindowsIdentity]::GetCurrent().User.Value
    & icacls $Config /inheritance:r /grant:r "*${sid}:(R,W)" '*S-1-5-18:(F)' | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Không thể bảo vệ browser-control.json bằng Windows ACL.' }
}

function Register-M4NativeHosts {
    param([string]$BrowserHost, [string]$ExtensionId, [hashtable]$Paths)
    New-Item -ItemType Directory -Force $Paths.NativeDirectory | Out-Null
    $native = @{
        name = 'com.tuoitho.browserhost'
        description = 'TuoiTho local browser control'
        path = $BrowserHost
        type = 'stdio'
        allowed_origins = @("chrome-extension://$ExtensionId/")
    } | ConvertTo-Json -Depth 3
    $chrome = Join-Path $Paths.NativeDirectory 'com.tuoitho.browserhost.chrome.json'
    $edge = Join-Path $Paths.NativeDirectory 'com.tuoitho.browserhost.edge.json'
    Set-Content -LiteralPath $chrome -Value $native -Encoding utf8
    Set-Content -LiteralPath $edge -Value $native -Encoding utf8
    reg add 'HKCU\Software\Google\Chrome\NativeMessagingHosts\com.tuoitho.browserhost' /ve /t REG_SZ /d $chrome /f | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Không thể đăng ký Chrome Native Messaging.' }
    reg add 'HKCU\Software\Microsoft\Edge\NativeMessagingHosts\com.tuoitho.browserhost' /ve /t REG_SZ /d $edge /f | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Không thể đăng ký Edge Native Messaging.' }
}
