# Install, qualify and uninstall an architecture-specific app on an owned CI desktop.
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$SetupPath,
    [Parameter(Mandatory)][ValidateSet('x64', 'x86', 'arm64')][string]$AppArchitecture,
    [string]$ReportDirectory = 'artifacts'
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if (-not $IsWindows -or $env:GITHUB_ACTIONS -ne 'true') { throw 'Installer qualification is restricted to a disposable Windows Actions desktop.' }
$setup = (Resolve-Path -LiteralPath $SetupPath).Path
$ReportDirectory = [IO.Path]::GetFullPath($ReportDirectory)
if ([IO.Path]::GetFileName($setup) -notmatch ('^Notchling-\d+\.\d+\.\d+-windows-' + $AppArchitecture + '-(?:evaluation-)?setup\.exe$')) { throw 'Installer filename does not match the requested app architecture.' }
$install = Join-Path $env:RUNNER_TEMP ('Notchling.InstallerSmoke-' + $AppArchitecture)
New-Item -ItemType Directory -Path $ReportDirectory -Force | Out-Null
$log = Join-Path $ReportDirectory 'installer-smoke.log'
$prerequisiteLog = Join-Path $env:LOCALAPPDATA 'Notchling/Setup/Logs/setup-prerequisites.log'
$prerequisiteLines = @()
function ConvertTo-WorkflowData([string]$Message) {
  return $Message.Replace('%', '%25').Replace("`r", '%0D').Replace("`n", '%0A')
}
$result = Start-Process -FilePath $setup -ArgumentList @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/SP-', "/DIR=`"$install`"", "/LOG=`"$log`"") -Wait -PassThru
if (Test-Path -LiteralPath $prerequisiteLog) {
  if ((Get-Item -LiteralPath $prerequisiteLog).Length -gt 1048576) {
    throw 'The prerequisite diagnostic log exceeded its 1 MiB cloud-test limit.'
  }
  Copy-Item -LiteralPath $prerequisiteLog -Destination (Join-Path $ReportDirectory 'setup-prerequisites.log')
  $prerequisiteLines = @(Get-Content -LiteralPath $prerequisiteLog)
}
if ($result.ExitCode -ne 0) {
  $details = if ($prerequisiteLines.Count -gt 0) { ($prerequisiteLines | Select-Object -Last 12) -join ' | ' } else { (Get-Content $log -Tail 12) -join ' | ' }
  $failure = ConvertTo-WorkflowData "Architecture-specific setup failed: $($result.ExitCode). $details"
  Write-Output "::error::$failure"
  throw 'Architecture-specific setup failed. See installer-smoke.log.'
}
if ($prerequisiteLines.Count -eq 0) { throw 'Successful setup did not produce prerequisite verification evidence.' }
$downloadedBytes = 0L
foreach ($line in $prerequisiteLines) {
  if ($line -match '\bDownloaded (\d+) bytes from [a-z0-9.-]+\.') { $downloadedBytes += [long]$Matches[1] }
}
$dotNetInstalled = @($prerequisiteLines | Where-Object { $_ -match 'Downloading the current stable \.NET 10 runtime from Microsoft\.' }).Count -gt 0
$dotNetReused = -not $dotNetInstalled -and @($prerequisiteLines | Where-Object { $_ -match 'Shared \.NET runtime already present:' }).Count -gt 0
$scope = if ($dotNetReused) { 'Shared .NET was reused; missing-.NET installation was not exercised.' } else { 'A missing .NET prerequisite was installed during this run.' }
$summary = "Setup prerequisite download bytes: $downloadedBytes; .NET runtime reused: $dotNetReused. $scope $AppArchitecture cloud qualification does not certify consumer Windows 10/11 hardware or denied-UAC/offline behavior."
Write-Output "::notice::$(ConvertTo-WorkflowData $summary)"
$recognized = 'Shared \.NET runtime already present:|Downloading the current stable \.NET 10 runtime|Downloading Windows App Runtime|Downloaded \d+ bytes from|Verified Windows App Runtime installer version:|Windows App Runtime 1\.8 framework, Main, Singleton and DDLM are registered|All prerequisites verified for the current user\.'
foreach ($line in ($prerequisiteLines | Where-Object { $_ -match $recognized } | Select-Object -Last 7)) {
  Write-Output "::notice::$(ConvertTo-WorkflowData $line)"
}
$app = Get-ChildItem (Join-Path $install 'app') -Filter Notchling.Windows.exe -Recurse | Select-Object -First 1 -ExpandProperty FullName
if (-not $app) { throw 'The installer did not create the application.' }
try {
  & (Join-Path $PSScriptRoot 'smoke-windows.ps1') -AppPath $app -AppArchitecture $AppArchitecture -ReportPath (Join-Path $ReportDirectory 'windows-smoke.json')
} finally {
  $uninstall = Join-Path $install 'unins000.exe'
  if (Test-Path $uninstall) {
    $cleanup = Start-Process -FilePath $uninstall -ArgumentList @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART') -Wait -PassThru
    if ($cleanup.ExitCode -ne 0) { throw "Smoke-test uninstall failed: $($cleanup.ExitCode)." }
  }
}
