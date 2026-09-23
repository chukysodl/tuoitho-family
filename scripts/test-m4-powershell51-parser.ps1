[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$scripts = @(
    'M4-BROWSER-REPAIR.ps1',
    'M4-BROWSER-UPDATE.ps1',
    'M4-BROWSER-INSTALL.ps1',
    'M4-BROWSER-CHECK.ps1',
    'M4-BROWSER-COMMON.ps1'
)

foreach ($scriptName in $scripts) {
    $scriptPath = Join-Path $PSScriptRoot $scriptName
    $escapedPath = $scriptPath.Replace("'", "''")
    $command = @"
`$tokens = `$null
`$errors = `$null
[System.Management.Automation.Language.Parser]::ParseFile('$escapedPath', [ref]`$tokens, [ref]`$errors) | Out-Null
if (`$errors.Count -gt 0) {
    `$errors | ForEach-Object { Write-Error `$_.Message }
    exit 1
}
"@

    & powershell.exe -NoProfile -NonInteractive -Command $command
    if ($LASTEXITCODE -ne 0) { throw "PowerShell 5.1 parser failed: $scriptName" }
    Write-Output "POWERSHELL 5.1 PARSER PASS: $scriptName"
}
