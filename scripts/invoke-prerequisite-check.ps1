# CI adapter: preserve Windows PowerShell's actual failure in public annotations
# instead of replacing it with a generic external-command exit-code message.
[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidateSet('Unit', 'Integration', 'RuntimeFallback')][string]$Mode
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if ($env:GITHUB_ACTIONS -ne 'true' -or -not $IsWindows) {
    throw 'This prerequisite-check adapter requires a disposable Windows GitHub Actions runner.'
}
$desktopPowerShell = Join-Path $env:WINDIR 'System32/WindowsPowerShell/v1.0/powershell.exe'
$fixture = Join-Path $PSScriptRoot 'test-prerequisites.ps1'
$PSNativeCommandUseErrorActionPreference = $false
$output = @(& $desktopPowerShell -NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File $fixture -Mode $Mode -OutputDirectory artifacts/prerequisite-tests 2>&1)
$resultCode = $LASTEXITCODE
$output | ForEach-Object { Write-Output $_.ToString() }
if ($resultCode -ne 0) {
    $details = ($output | Select-Object -Last 22 | ForEach-Object ToString) -join ' | '
    $details = $details.Replace('%', '%25').Replace("`r", '%0D').Replace("`n", '%0A')
    Write-Output "::error title=Windows prerequisite fixture $Mode::Exit $resultCode. $details"
    throw "Windows prerequisite fixture $Mode failed. See its detailed annotation and diagnostic artifact."
}
