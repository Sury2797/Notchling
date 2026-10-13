# Actual upgrade/relaunch on a disposable Windows CI account. Public unsigned
# discovery is tested separately; an unpublished artifact cannot claim GitHub
# release provenance. Only a COPY receives a temporary trusted test signature.
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$SetupPath,
    [Parameter(Mandatory)][ValidateSet('x64', 'x86', 'arm64')][string]$AppArchitecture,
    [Parameter(Mandatory)][ValidateSet('x64', 'x86', 'ARM64')][string]$Platform,
    [string]$ReportDirectory = 'artifacts'
)
$ErrorActionPreference = 'Stop'; Set-StrictMode -Version Latest
if ([Environment]::OSVersion.Platform -ne 'Win32NT' -or $env:GITHUB_ACTIONS -ne 'true' -or -not $env:RUNNER_TEMP) {
    throw 'Updater qualification is restricted to a disposable Windows Actions account.'
}
# Explicit native OS modules avoid an inherited PowerShell 7-only PSModulePath.
$nativeModules = Join-Path $PSHOME 'Modules'
Import-Module -Name (Join-Path $nativeModules 'Microsoft.PowerShell.Security\Microsoft.PowerShell.Security.psd1') -ErrorAction Stop
Import-Module -Name (Join-Path $nativeModules 'PKI\PKI.psd1') -ErrorAction Stop
function Get-FileSha256([string]$Path) {
    $stream = [IO.File]::Open($Path, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
    $hasher = [Security.Cryptography.SHA256]::Create()
    try { return [BitConverter]::ToString($hasher.ComputeHash($stream)).Replace('-', '') }
    finally { $hasher.Dispose(); $stream.Dispose() }
}
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
$report = [ordered]@{ Succeeded = $false; Stage = 'Initialize'; Architecture = $AppArchitecture;
    Scope = 'Actual versioned Inno upgrade, graceful owned-app shutdown, hash/publisher verification, workspace retention and relaunch. Baseline is the same source built as 0.0.0; previous-release migrations and the unpublished candidate public Update button are not claimed.';
    TemporarySignatureCopyOnly = $false; TrustedPublisherVerified = $false; GracefulShutdown = $false;
    ActualInnoUpgrade = $false; ActualRelaunch = $false; WorkspaceRetained = $false; OriginalInstallerUnchanged = $false; Error = $null }
$certificate = $null; $child = $null; $relaunched = $null; $worker = $null; $failure = $null
$setup = (Resolve-Path -LiteralPath $SetupPath).Path
$ReportDirectory = [IO.Path]::GetFullPath($ReportDirectory)
New-Item -ItemType Directory -Path $ReportDirectory -Force | Out-Null
$fixture = Join-Path $env:RUNNER_TEMP ('Notchling.UpdateSmoke-' + [Guid]::NewGuid().ToString('N'))
$install = Join-Path $fixture 'installed'; $publish = Join-Path $fixture 'baseline-publish'; $baselineRelease = Join-Path $fixture 'baseline-setup'
New-Item -ItemType Directory -Path $fixture -Force | Out-Null
$originalHash = (Get-FileSha256 $setup)
[xml]$project = Get-Content -LiteralPath 'src/Notch.Windows/Notch.Windows.csproj'
$currentVersion = [string]$project.Project.PropertyGroup.Version
$report.ExpectedVersion = $currentVersion; $report.BaselineVersion = '0.0.0'
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
namespace NotchlingUpdateSmoke {
    public static class Native {
        [StructLayout(LayoutKind.Sequential)] private struct Input { public uint Type; public InputData Data; }
        [StructLayout(LayoutKind.Explicit)] private struct InputData { [FieldOffset(0)] public MouseInput Mouse; [FieldOffset(0)] public KeyboardInput Keyboard; }
        [StructLayout(LayoutKind.Sequential)] private struct MouseInput { public int X, Y; public uint Data, Flags, Time; public UIntPtr Extra; }
        [StructLayout(LayoutKind.Sequential)] private struct KeyboardInput { public ushort Key, Scan; public uint Flags, Time; public UIntPtr Extra; }
        [DllImport("user32.dll", SetLastError=true)] private static extern uint SendInput(uint count, Input[] inputs, int size);
        [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
        [DllImport("user32.dll")] public static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
        public static void RightClick(int x, int y) {
            if (!SetCursorPos(x,y)) throw new InvalidOperationException("Owned context-menu pointer placement failed.");
            var input = new[] { new Input { Type=0, Data=new InputData { Mouse=new MouseInput { Flags=8 } } },
                                new Input { Type=0, Data=new InputData { Mouse=new MouseInput { Flags=16 } } } };
            if (SendInput(2,input,Marshal.SizeOf(typeof(Input))) != 2) throw new InvalidOperationException("Owned context-menu pointer input was not delivered.");
        }
    }
}
'@
function Invoke-Checked([string]$Program, [string[]]$Arguments) {
    & $Program @Arguments
    if ($LASTEXITCODE -ne 0) { throw "Updater fixture command failed: $Program (code $LASTEXITCODE)." }
}
function Find-AppWindow([Diagnostics.Process]$Process, [int]$Seconds = 20) {
    $condition = [Windows.Automation.AndCondition]::new(
        [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ProcessIdProperty, $Process.Id),
        [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::NameProperty, ('Notchling ' + [char]0x2014 + ' Desktop notch')))
    $deadline = [DateTime]::UtcNow.AddSeconds($Seconds)
    do {
        if ($Process.HasExited) { throw "The owned app exited before its window appeared (code $($Process.ExitCode))." }
        $window = [Windows.Automation.AutomationElement]::RootElement.FindFirst([Windows.Automation.TreeScope]::Children, $condition)
        if ($window -and -not $window.Current.IsOffscreen -and $window.Current.BoundingRectangle.Width -gt 0) { return $window }
        Start-Sleep -Milliseconds 100
    } while ([DateTime]::UtcNow -lt $deadline)
    throw 'The owned update fixture app did not display its native window.'
}
function Wait-AppReady([Diagnostics.Process]$Process) {
    $window = Find-AppWindow $Process
    $pinCondition = [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::NameProperty, 'Keep Notchling expanded')
    $openCondition = [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::NameProperty, 'Open Notchling')
    $deadline = [DateTime]::UtcNow.AddSeconds(15)
    do {
        if ($Process.HasExited) { throw 'The owned app exited before its ready controls appeared.' }
        $pin = $window.FindFirst([Windows.Automation.TreeScope]::Descendants, $pinCondition)
        if ($pin -and $pin.Current.IsEnabled -and -not $pin.Current.IsOffscreen) { return $window }
        $open = $window.FindFirst([Windows.Automation.TreeScope]::Descendants, $openCondition)
        if ($open -and $open.Current.IsEnabled -and -not $open.Current.IsOffscreen) {
            try { $open.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke() } catch [Windows.Automation.ElementNotAvailableException] { }
        }
        Start-Sleep -Milliseconds 150
    } while ([DateTime]::UtcNow -lt $deadline)
    throw 'The owned app did not expose its genuinely ready expanded controls.'
}
function Quit-OwnedApp([Diagnostics.Process]$Process) {
    $window = Wait-AppReady $Process
    $bounds = $window.Current.BoundingRectangle
    [NotchlingUpdateSmoke.Native]::RightClick([int]($bounds.Left + $bounds.Width / 2), [int]($bounds.Top + [Math]::Min(20, $bounds.Height / 2)))
    $processCondition = [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ProcessIdProperty, $Process.Id)
    $nameCondition = [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::NameProperty, 'Quit Notchling')
    $deadline = [DateTime]::UtcNow.AddSeconds(6); $quit = $null
    do {
        foreach ($surface in [Windows.Automation.AutomationElement]::RootElement.FindAll([Windows.Automation.TreeScope]::Children, $processCondition)) {
            foreach ($candidate in $surface.FindAll([Windows.Automation.TreeScope]::Descendants, $nameCondition)) {
                try {
                    $pattern = $null
                    if ($candidate.Current.IsEnabled -and -not $candidate.Current.IsOffscreen -and
                        $candidate.TryGetCurrentPattern([Windows.Automation.InvokePattern]::Pattern, [ref]$pattern)) { $quit = $candidate; break }
                } catch [Windows.Automation.ElementNotAvailableException] { }
            }
            if ($quit) { break }
        }
        if ($quit) { break }
        Start-Sleep -Milliseconds 100
    } while ([DateTime]::UtcNow -lt $deadline)
    if (-not $quit) { throw 'The owned app context menu did not expose its genuine Quit action.' }
    $pattern = $quit.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern)
    try { $pattern.Invoke() } catch [Windows.Automation.ElementNotAvailableException] { }
    if (-not $Process.WaitForExit(10000)) { throw 'The actual Quit action did not finish graceful save/shutdown. No forced termination counts as a pass.' }
    if ($Process.ExitCode -ne 0) { throw "The owned app failed graceful shutdown (code $($Process.ExitCode))." }
}
function Open-CertificateStore([string]$Name) {
    $store = [Security.Cryptography.X509Certificates.X509Store]::new($Name, 'CurrentUser')
    $store.Open([Security.Cryptography.X509Certificates.OpenFlags]::ReadWrite); return $store
}
$originalDpi = [NotchlingUpdateSmoke.Native]::SetThreadDpiAwarenessContext([IntPtr]::new(-4))
try {
    $report.Stage = 'Build the same source with a genuine older application file version'
    Invoke-Checked 'dotnet' @('publish', 'src/Notch.Windows/Notch.Windows.csproj', '--configuration', 'Release', '--runtime', "win-$AppArchitecture", '--self-contained', 'false',
        "-p:Platform=$Platform", '-p:Version=0.0.0', '-p:AssemblyVersion=0.0.0.0', '-p:FileVersion=0.0.0.0', '-p:WindowsPackageType=None', '-p:WindowsAppSDKSelfContained=false', '--output', $publish)
    $iscc = 'C:\Program Files (x86)\Inno Setup 6\ISCC.exe'
    Invoke-Checked $iscc @('/DEvaluationBuild', "/DAppArchitecture=$AppArchitecture", '/DAppVersion=0.0.0', "/DPublishDirectory=$publish", "/DReleaseDirectory=$baselineRelease",
        "/DNativeInteropPath=$PWD/artifacts/setup-interop/Notchling.Setup.Interop.dll", "/DInstallerDocumentsDirectory=$PWD/artifacts/installer-documents", 'packaging/windows/Notch.iss')
    $baseline = Join-Path $baselineRelease "Notchling-0.0.0-windows-$AppArchitecture-evaluation-setup.exe"
    $installed = Start-Process -FilePath $baseline -ArgumentList @('/SP-', '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', "/DIR=`"$install`"") -PassThru -Wait
    if ($installed.ExitCode -ne 0) { throw "The older fixture installation failed (code $($installed.ExitCode))." }
    $oldApp = Join-Path $install 'app\0.0.0\Notchling.Windows.exe'
    if ([Diagnostics.FileVersionInfo]::GetVersionInfo($oldApp).FileVersion -ne '0.0.0.0') { throw 'The actual baseline application did not have its older file version.' }
    $report.Stage = 'Normalize and snapshot the real notebook through graceful shutdown'
    $child = Start-Process -FilePath $oldApp -WorkingDirectory ([IO.Path]::GetDirectoryName($oldApp)) -PassThru
    Wait-AppReady $child | Out-Null; Quit-OwnedApp $child; $child.Dispose(); $child = $null
    $workspace = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'Notch\workspace.json'
    if (-not (Test-Path -LiteralPath $workspace -PathType Leaf)) { throw 'The actual app did not save its notebook before the upgrade.' }
    $workspaceHash = (Get-FileSha256 $workspace)
    $report.WorkspaceSha256Before = $workspaceHash
    $report.Stage = 'Sign only the disposable installer copy and validate trusted publisher'
    $installer = Join-Path $fixture 'Notchling-setup.exe'; Copy-Item -LiteralPath $setup -Destination $installer
    $certificateParameters = @{ Type = 'CodeSigningCert'; Subject = ('CN=Notchling disposable updater test ' + [Guid]::NewGuid().ToString('N'));
        CertStoreLocation = 'Cert:\CurrentUser\My'; KeyExportPolicy = 'NonExportable'; HashAlgorithm = 'SHA256'; NotAfter = (Get-Date).AddHours(2) }
    $certificate = New-SelfSignedCertificate @certificateParameters
    foreach ($name in @('Root', 'TrustedPublisher')) { $store = Open-CertificateStore $name; try { $store.Add($certificate) } finally { $store.Dispose() } }
    $signature = Set-AuthenticodeSignature -LiteralPath $installer -Certificate $certificate -HashAlgorithm SHA256
    $signature = Get-AuthenticodeSignature -LiteralPath $installer
    if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Thumbprint -ne $certificate.Thumbprint) { throw "The disposable signed copy was not trusted (status $($signature.Status))." }
    $report.TemporarySignatureCopyOnly = $true; $report.TrustedPublisherVerified = $true
    $hasher = [Security.Cryptography.SHA256]::Create()
    try { $pin = [BitConverter]::ToString($hasher.ComputeHash($certificate.GetPublicKey())).Replace('-', '') } finally { $hasher.Dispose() }
    $report.Stage = 'Prepare the actual detached update helper while the old app is running'
    $child = Start-Process -FilePath $oldApp -WorkingDirectory ([IO.Path]::GetDirectoryName($oldApp)) -PassThru
    Wait-AppReady $child | Out-Null
    $helper = Join-Path $fixture 'update-handoff.ps1'; Copy-Item -LiteralPath (Join-Path $publish 'Update\update-handoff.ps1') -Destination $helper
    $nonce = [Guid]::NewGuid().ToString('N'); $ready = Join-Path $fixture ('ready-' + $nonce)
    $requestPath = Join-Path $fixture ('request-' + $nonce + '.json')
    $expected = Join-Path $install "app\$currentVersion\Notchling.Windows.exe"
    $request = @{ SchemaVersion = 1; ParentId = $child.Id; ParentStartedUtcTicks = $child.StartTime.ToUniversalTime().Ticks; CurrentExecutable = $oldApp;
        ExpectedExecutable = $expected; InstallDirectory = $install; Version = $currentVersion; Architecture = $AppArchitecture;
        InstallerPath = $installer; Sha256 = (Get-FileSha256 $installer); SizeBytes = (Get-Item -LiteralPath $installer).Length;
        Trust = 'SignedPublisher'; Provenance = $null; PublisherKeySha256 = $pin; ReadyPath = $ready; Nonce = $nonce }
    [IO.File]::WriteAllText($requestPath, ($request | ConvertTo-Json -Depth 5), [Text.UTF8Encoding]::new($false))
    $powershell = Join-Path ([Environment]::GetFolderPath('System')) 'WindowsPowerShell\v1.0\powershell.exe'
    $worker = Start-Process -FilePath $powershell -ArgumentList @('-NoLogo', '-NoProfile', '-NonInteractive', '-ExecutionPolicy', 'Bypass', '-File', "`"$helper`"", '-RequestPath', "`"$requestPath`"") -PassThru -WindowStyle Hidden
    $deadline = [DateTime]::UtcNow.AddSeconds(30)
    while (-not (Test-Path -LiteralPath $ready)) {
        if ($worker.HasExited) { throw "The actual handoff helper failed readiness (code $($worker.ExitCode))." }
        if ([DateTime]::UtcNow -ge $deadline) { throw 'The actual handoff helper did not become ready within 30 seconds.' }
        Start-Sleep -Milliseconds 100
    }
    if ([IO.File]::ReadAllText($ready) -cne $nonce) { throw 'The actual handoff readiness response did not match this fixture.' }
    $report.Stage = 'Use genuine app Quit; require real Inno upgrade and relaunch'
    Quit-OwnedApp $child; $report.GracefulShutdown = $true
    if (-not $worker.WaitForExit(90000)) { throw 'The real detached updater did not finish Setup/relaunch within 90 seconds.' }
    if ($worker.ExitCode -ne 0) { throw "The real detached updater failed (code $($worker.ExitCode))." }
    $resultPath = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'Notchling\Updates\latest-result.json'
    $result = Get-Content -LiteralPath $resultPath -Raw | ConvertFrom-Json
    if ($result.state -ne 'Completed' -or $result.version -ne $currentVersion -or $result.architecture -ne $AppArchitecture -or $result.installerExitCode -ne 0) { throw 'The actual updater did not record successful architecture/version-matched installation.' }
    Copy-Item -LiteralPath $resultPath -Destination (Join-Path $ReportDirectory 'windows-update-result.json')
    Copy-Item -LiteralPath (Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'Notchling\Updates\setup-update.log') -Destination (Join-Path $ReportDirectory 'windows-update-setup.log')
    $report.ActualInnoUpgrade = $true
    if ([Diagnostics.FileVersionInfo]::GetVersionInfo($expected).FileVersion -ne ($currentVersion + '.0')) { throw 'The upgraded executable did not have the new real application file version.' }
    $deadline = [DateTime]::UtcNow.AddSeconds(20)
    do {
        $matches = @(Get-Process -Name 'Notchling.Windows' -ErrorAction SilentlyContinue | Where-Object { try { $_.MainModule.FileName -ieq $expected -and $_.StartTime.ToUniversalTime() -ge $child.StartTime.ToUniversalTime() } catch { $false } })
        if ($matches.Count -eq 1) { $relaunched = $matches[0]; break }
        Start-Sleep -Milliseconds 100
    } while ([DateTime]::UtcNow -lt $deadline)
    if (-not $relaunched) { throw 'The actual updater did not relaunch precisely one new version process.' }
    Wait-AppReady $relaunched | Out-Null; $report.ActualRelaunch = $true
    Quit-OwnedApp $relaunched
    $report.WorkspaceSha256After = (Get-FileSha256 $workspace)
    if ($report.WorkspaceSha256After -ne $workspaceHash) { throw 'The actual versioned upgrade/relaunch altered the retained notebook bytes.' }
    $report.WorkspaceRetained = $true
    $report.OriginalInstallerUnchanged = (Get-FileSha256 $setup) -eq $originalHash
    if (-not $report.OriginalInstallerUnchanged) { throw 'The publishable evaluation installer changed during the disposable signature fixture.' }
    $report.Succeeded = $true; $report.Stage = 'Completed'
}
catch { $failure = $_; $report.Error = $_.Exception.Message }
finally {
    foreach ($process in @($worker, $child, $relaunched)) {
        if ($process) { try { if (-not $process.HasExited) { $process.Kill(); $process.WaitForExit(5000) | Out-Null } } catch { } finally { $process.Dispose() } }
    }
    if ($certificate) {
        foreach ($name in @('Root', 'TrustedPublisher')) { try { $store = Open-CertificateStore $name; try { $store.Remove($certificate) } finally { $store.Dispose() } } catch { $report.CertificateCleanupError = $_.Exception.Message; $report.Succeeded = $false } }
        try { Remove-Item -LiteralPath ('Cert:\CurrentUser\My\' + $certificate.Thumbprint) -DeleteKey -Force } catch { $report.CertificateCleanupError = $_.Exception.Message; $report.Succeeded = $false }
        $certificate.Dispose()
    }
    $uninstaller = Join-Path $install 'unins000.exe'
    if (Test-Path -LiteralPath $uninstaller) {
        try { $removed = Start-Process -FilePath $uninstaller -ArgumentList @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART') -PassThru -Wait; if ($removed.ExitCode -ne 0) { throw "Updater fixture uninstall failed: $($removed.ExitCode)." } }
        catch { $report.CleanupError = $_.Exception.Message; $report.Succeeded = $false }
    }
    if ($originalDpi -ne [IntPtr]::Zero) { [NotchlingUpdateSmoke.Native]::SetThreadDpiAwarenessContext($originalDpi) | Out-Null }
    $report | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $ReportDirectory 'windows-update-handoff.json') -Encoding UTF8
    $detail = "Stage=$($report.Stage); Architecture=$AppArchitecture; Success=$($report.Succeeded); GracefulShutdown=$($report.GracefulShutdown); ActualInnoUpgrade=$($report.ActualInnoUpgrade); ActualRelaunch=$($report.ActualRelaunch); WorkspaceRetained=$($report.WorkspaceRetained); OriginalUnsignedInstallerUnchanged=$($report.OriginalInstallerUnchanged); Error=$($report.Error)"
    $detail = $detail.Replace('%', '%25').Replace("`r", '%0D').Replace("`n", '%0A')
    if ($report.Succeeded) { Write-Output "::notice title=Actual in-app update helper qualification::$detail" }
    else { Write-Output "::error title=Actual in-app update helper qualification::$detail" }
}
if ($failure) { throw $failure }
if (-not $report.Succeeded) { throw 'Actual update helper qualification or cleanup failed.' }
