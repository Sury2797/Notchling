# Runs the published app on a disposable Windows CI desktop, not on a user's workspace.
# Runtime registration is test-machine setup; no runtime files enter the app artifact.
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$AppPath,
    [string]$AssetsPath = "src/Notch.Windows/obj/project.assets.json",
    [string]$ReportPath = "artifacts/windows-smoke.json",
    [switch]$InstallRuntimeFromNuGet,
    [ValidateRange(5, 120)][int]$LaunchTimeoutSeconds = 30,
    [ValidateRange(3, 30)][int]$SampleSeconds = 5
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest
$child = $null
$failure = $null
$report = [ordered]@{
    Succeeded = $false
    Scope = "Published application on a disposable Windows CI desktop; not Windows 10 hardware qualification or animation benchmarking."
    OS = [Environment]::OSVersion.VersionString
    AppPath = $AppPath
    RuntimePackages = @()
    WindowTitle = $null
    WindowBounds = $null
    VisibleWindow = $false
    WindowIconPresent = $false
    ResponsiveSamples = 0
    FirstVisibleWindowMilliseconds = $null
    MeasurementSeconds = $null
    CpuPercentAllCores = $null
    AverageWorkingSetMiB = $null
    AveragePrivateMiB = $null
    PeakPrivateMiB = $null
    LastHandleCount = $null
    UIInteractions = [ordered]@{ Succeeded = $false; Status = "Not run"; Error = $null }
    Teardown = "No app process created."
    StartupLog = $null
    CrashEvents = @()
    DiagnosticsError = $null
    Error = $null
}

function Invoke-FreeTierUiSmoke([int]$AppProcessId, [IntPtr]$AppWindowHandle) {
    $helperScript = Join-Path $PSScriptRoot "smoke-windows-ui.ps1"
    $helperReportPath = Join-Path ([IO.Path]::GetDirectoryName([IO.Path]::GetFullPath($ReportPath))) "windows-ui-smoke.json"
    $windowsPowerShell = Join-Path ([Environment]::GetFolderPath("System")) "WindowsPowerShell/v1.0/powershell.exe"
    if (-not (Test-Path -LiteralPath $helperScript -PathType Leaf)) { throw "The UI interaction helper is missing." }
    if (Test-Path -LiteralPath $helperReportPath) { Remove-Item -LiteralPath $helperReportPath }
    $helperInfo = [Diagnostics.ProcessStartInfo]::new($windowsPowerShell)
    $helperInfo.UseShellExecute = $false
    $helperInfo.CreateNoWindow = $true
    $helperInfo.RedirectStandardOutput = $true
    $helperInfo.RedirectStandardError = $true
    foreach ($argument in @("-NoLogo", "-NoProfile", "-NonInteractive", "-Mta", "-ExecutionPolicy", "Bypass", "-File", $helperScript,
        "-AppProcessId", [string]$AppProcessId, "-WindowHandle", [string]$AppWindowHandle.ToInt64(), "-ReportPath", $helperReportPath)) {
        $helperInfo.ArgumentList.Add($argument)
    }
    $helper = [Diagnostics.Process]::Start($helperInfo)
    if (-not $helper) { throw "Windows did not create the UI interaction helper." }
    try {
        $standardOutput = $helper.StandardOutput.ReadToEndAsync()
        $standardError = $helper.StandardError.ReadToEndAsync()
        if (-not $helper.WaitForExit(45000)) {
            $helper.Kill()
            $helper.WaitForExit(5000) | Out-Null
            $report.UIInteractions = [ordered]@{ Succeeded = $false; Status = "Timed out"; Error = "Owned UI Automation helper exceeded its 45-second watchdog." }
            throw $report.UIInteractions.Error
        }
        if (Test-Path -LiteralPath $helperReportPath -PathType Leaf) {
            $report.UIInteractions = Get-Content -LiteralPath $helperReportPath -Raw | ConvertFrom-Json
        }
        if ($helper.ExitCode -ne 0 -or -not $report.UIInteractions.Succeeded) {
            $details = if ($report.UIInteractions.Error) { [string]$report.UIInteractions.Error } else { $standardError.GetAwaiter().GetResult() }
            if ($details.Length -gt 3000) { $details = $details.Substring(0, 3000) }
            throw "Free-tier UI interaction failed (helper exit code $($helper.ExitCode)): $details"
        }
    } finally {
        if (-not $helper.HasExited) {
            $helper.Kill()
            $helper.WaitForExit(5000) | Out-Null
        }
        $helper.Dispose()
    }
}

function Read-StartupDiagnostics {
    # The CI profile is disposable. Read only this app's startup log and matching crash events.
    $diagnosticErrors = [Collections.Generic.List[string]]::new()
    $details = [Collections.Generic.List[string]]::new()
    $startupLog = Join-Path ([Environment]::GetFolderPath("LocalApplicationData")) "Notchling/Diagnostics/startup.log"
    if (Test-Path -LiteralPath $startupLog -PathType Leaf) {
        try {
            $content = (Get-Content -LiteralPath $startupLog -Tail 100) -join [Environment]::NewLine
            if ($content.Length -gt 6000) { $content = $content.Substring($content.Length - 6000) }
            $report.StartupLog = $content
            if (-not [string]::IsNullOrWhiteSpace($content)) { $details.Add("App startup log: $content") }
        } catch { $diagnosticErrors.Add("Startup log: $($_.Exception.Message)") }
    }
    if ($child) {
        try {
            # Event publication can trail the process exit. This bounded delay is diagnostic only.
            if ($child.HasExited) { Start-Sleep -Milliseconds 300 }
            $decimalProcessId = [regex]::Escape([string]$child.Id)
            $hexProcessId = [regex]::Escape(('0x{0:x}' -f $child.Id))
            $processPattern = "(?i)(?:\b$hexProcessId\b|(?:Process\s+(?:ID|Id)|ProcessId)\s*[:=]?\s*$decimalProcessId\b)"
            $events = @(Get-WinEvent -FilterHashtable @{
                LogName = "Application"
                StartTime = (Get-Date).AddMinutes(-2)
                Level = 2
            } -MaxEvents 50 -ErrorAction Stop | Where-Object {
                $_.ProviderName -in @("Application Error", ".NET Runtime") -and
                ($_.Message -match '(?i)Notchling\.Windows' -or $_.Message -match $processPattern)
            } | Select-Object -First 3)
            foreach ($event in $events) {
                $message = [string]$event.Message
                if ($message.Length -gt 4000) { $message = $message.Substring(0, 4000) }
                $report.CrashEvents += [pscustomobject]@{
                    Provider = $event.ProviderName
                    EventId = $event.Id
                    TimeCreated = $event.TimeCreated.ToUniversalTime().ToString("O")
                    Message = $message
                }
                $details.Add("$($event.ProviderName) event $($event.Id): $message")
            }
        } catch {
            # Missing events are expected for launch failures outside the app; never mask the failure.
            if ($_.FullyQualifiedErrorId -notlike "NoMatchingEventsFound*") {
                $diagnosticErrors.Add("Windows crash events: $($_.Exception.Message)")
            }
        }
    }
    if ($diagnosticErrors.Count -gt 0) { $report.DiagnosticsError = $diagnosticErrors -join "; " }
    if ($details.Count -gt 0) {
        $combined = $details -join [Environment]::NewLine
        if ($combined.Length -gt 10000) { $combined = $combined.Substring(0, 10000) }
        $report.Error += [Environment]::NewLine + $combined
    }
}

function Initialize-TestRuntime {
    if (-not (Test-Path -LiteralPath $AssetsPath -PathType Leaf)) {
        throw "Restore the Windows project first; runtime package metadata is missing: $AssetsPath"
    }
    $assets = Get-Content -LiteralPath $AssetsPath -Raw | ConvertFrom-Json
    $library = @($assets.libraries.PSObject.Properties | Where-Object Name -Like "Microsoft.WindowsAppSDK.Runtime/*")
    if ($library.Count -ne 1) { throw "Expected exactly one pinned Windows App SDK runtime in project.assets.json." }
    $packageRoot = $null
    foreach ($folder in $assets.packageFolders.PSObject.Properties.Name) {
        $candidate = Join-Path $folder $library[0].Value.path
        if (Test-Path -LiteralPath $candidate -PathType Container) { $packageRoot = $candidate; break }
    }
    if (-not $packageRoot) { throw "The pinned Windows App SDK runtime is absent from the restored NuGet cache." }

    # Appx uses Windows PowerShell on systems where the module is not PowerShell 7 compatible.
    Import-Module Appx -UseWindowsPowerShell -WarningAction SilentlyContinue
    $packageDirectory = Join-Path $packageRoot "tools/MSIX/win10-x64"
    $msixPaths = @(Get-ChildItem -LiteralPath $packageDirectory -Filter "*.msix" -File)
    if ($msixPaths.Count -ne 4) { throw "Expected framework, DDLM, Main, and Singleton x64 runtime packages." }
    $packages = foreach ($msix in $msixPaths) {
        $archive = [IO.Compression.ZipFile]::OpenRead($msix.FullName)
        try {
            $entry = $archive.GetEntry("AppxManifest.xml")
            if (-not $entry) { throw "Runtime package manifest is missing: $($msix.Name)" }
            $reader = [IO.StreamReader]::new($entry.Open())
            try { [xml]$manifest = $reader.ReadToEnd() } finally { $reader.Dispose() }
        } finally { $archive.Dispose() }
        $namespaces = [Xml.XmlNamespaceManager]::new($manifest.NameTable)
        $namespaces.AddNamespace("appx", "http://schemas.microsoft.com/appx/manifest/foundation/windows10")
        $identity = $manifest.SelectSingleNode("/appx:Package/appx:Identity", $namespaces)
        $dependencies = @($manifest.SelectNodes("/appx:Package/appx:Dependencies/appx:PackageDependency", $namespaces))
        if (-not $identity -or $identity.GetAttribute("ProcessorArchitecture") -ne "x64" -or
            $identity.GetAttribute("Publisher") -ne "CN=Microsoft Corporation, O=Microsoft Corporation, L=Redmond, S=Washington, C=US") {
            throw "Unexpected runtime package identity: $($msix.Name)"
        }
        [pscustomobject]@{
            Name = $identity.GetAttribute("Name")
            Version = [version]$identity.GetAttribute("Version")
            DependencyCount = $dependencies.Count
            Path = $msix.FullName
        }
    }
    # The framework has zero package dependencies; all three companions require it.
    foreach ($package in ($packages | Sort-Object DependencyCount, Name)) {
        $installed = @(Get-AppxPackage -Name $package.Name | Where-Object {
            $_.Architecture -eq "X64" -and [version]$_.Version -ge $package.Version
        })
        if ($installed.Count -eq 0) {
            if (-not $InstallRuntimeFromNuGet) {
                throw "The CI desktop needs $($package.Name) >= $($package.Version). Use -InstallRuntimeFromNuGet for test-machine setup."
            }
            Write-Host "Registering Microsoft runtime for CI: $($package.Name) $($package.Version)"
            # Windows validates the MSIX signature; no unsigned/developer bypass is enabled.
            Add-AppxPackage -Path $package.Path -ErrorAction Stop
            $installed = @(Get-AppxPackage -Name $package.Name | Where-Object {
                $_.Architecture -eq "X64" -and [version]$_.Version -ge $package.Version
            })
            if ($installed.Count -eq 0) { throw "Runtime registration did not produce the required package: $($package.Name)" }
        }
        $report.RuntimePackages += @($installed | Select-Object -ExpandProperty PackageFullName)
    }
}

try {
    if (-not $IsWindows) { throw "This smoke test requires a Windows desktop." }
    if ($env:GITHUB_ACTIONS -ne "true") {
        throw "This smoke test is restricted to disposable GitHub Actions runners to protect local app data."
    }
    if (-not [Environment]::Is64BitProcess) { throw "Use 64-bit PowerShell for the x64 application." }
    $app = (Resolve-Path -LiteralPath $AppPath).Path
    if ([IO.Path]::GetFileName($app) -ne "Notchling.Windows.exe") { throw "Specify the published Notchling.Windows.exe." }
    $report.AppPath = $app
    # GetFolderPath uses the Windows known folder, not the LOCALAPPDATA environment variable.
    # Require a fresh CI profile rather than pretending an environment override isolates data.
    $workspace = Join-Path ([Environment]::GetFolderPath("LocalApplicationData")) "Notch"
    if (Test-Path -LiteralPath $workspace) { throw "Refusing to launch against an existing app workspace: $workspace" }
    if (@(Get-Process -Name "Notchling.Windows", "Notch.Windows" -ErrorAction SilentlyContinue).Count -ne 0) {
        throw "Refusing to run while another app instance exists."
    }
    Initialize-TestRuntime

    if (-not ("NotchlingSmoke.Native" -as [type])) {
        Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
using System.Text;
namespace NotchlingSmoke {
    public static class Native {
        private delegate bool EnumWindowProc(IntPtr window, IntPtr state);
        [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowProc callback, IntPtr state);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr window, StringBuilder text, int capacity);
        [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr window);
        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out Rect rect);
        [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SendMessageTimeout(IntPtr window, uint message, UIntPtr wParam, IntPtr lParam, uint flags, uint timeout, out UIntPtr result);
        [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
        public static IntPtr FindWindow(int processId, string title) {
            IntPtr found = IntPtr.Zero;
            EnumWindowProc callback = (window, state) => {
                GetWindowThreadProcessId(window, out uint owner);
                if (owner != (uint)processId) return true;
                var text = new StringBuilder(512);
                GetWindowText(window, text, text.Capacity);
                if (text.ToString() != title) return true;
                found = window; return false;
            };
            EnumWindows(callback, IntPtr.Zero);
            GC.KeepAlive(callback);
            return found;
        }
        public static bool Responds(IntPtr window) => SendMessageTimeout(window, 0, UIntPtr.Zero, IntPtr.Zero, 3, 1000, out _) != IntPtr.Zero;
        public static bool HasIcon(IntPtr window) => SendMessageTimeout(window, 0x7f, UIntPtr.Zero, IntPtr.Zero, 3, 1000, out UIntPtr icon) != IntPtr.Zero && icon != UIntPtr.Zero;
        public static int[] Bounds(IntPtr window) {
            if (!GetWindowRect(window, out Rect rect)) throw new InvalidOperationException("Unable to read window bounds.");
            return new[] { rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top };
        }
    }
}
'@
    }

    $title = "Notchling — Desktop notch"
    $launchClock = [Diagnostics.Stopwatch]::StartNew()
    $startInfo = [Diagnostics.ProcessStartInfo]::new($app)
    $startInfo.WorkingDirectory = [IO.Path]::GetDirectoryName($app)
    $startInfo.UseShellExecute = $false
    $child = [Diagnostics.Process]::Start($startInfo)
    if (-not $child) { throw "Windows did not create the application process." }
    $window = [IntPtr]::Zero
    while ($launchClock.Elapsed.TotalSeconds -lt $LaunchTimeoutSeconds) {
        $child.Refresh()
        if ($child.HasExited) { throw "The published app exited before showing its window (exit code $($child.ExitCode))." }
        $window = [NotchlingSmoke.Native]::FindWindow($child.Id, $title)
        if ($window -ne [IntPtr]::Zero -and [NotchlingSmoke.Native]::IsWindowVisible($window)) { break }
        Start-Sleep -Milliseconds 100
    }
    if ($window -eq [IntPtr]::Zero -or -not [NotchlingSmoke.Native]::IsWindowVisible($window)) {
        throw "No visible native app window appeared within $LaunchTimeoutSeconds seconds. Check runtime deployment or Windows crash logs."
    }
    $launchClock.Stop()
    $report.FirstVisibleWindowMilliseconds = [Math]::Round($launchClock.Elapsed.TotalMilliseconds, 1)
    $report.WindowTitle = $title
    $report.VisibleWindow = $true
    $bounds = [NotchlingSmoke.Native]::Bounds($window)
    $report.WindowBounds = [ordered]@{ Left = $bounds[0]; Top = $bounds[1]; Width = $bounds[2]; Height = $bounds[3] }
    if ($bounds[2] -le 0 -or $bounds[3] -le 0) { throw "The native window has empty bounds." }
    $report.WindowIconPresent = [NotchlingSmoke.Native]::HasIcon($window)
    if (-not $report.WindowIconPresent) { throw "The published app did not install its native window icon." }

    $child.Refresh()
    $cpuBefore = $child.TotalProcessorTime.TotalSeconds
    $measurementClock = [Diagnostics.Stopwatch]::StartNew()
    $workingSet = [Collections.Generic.List[double]]::new()
    $privateBytes = [Collections.Generic.List[double]]::new()
    for ($sample = 0; $sample -lt $SampleSeconds; $sample++) {
        Start-Sleep -Seconds 1
        $child.Refresh()
        if ($child.HasExited) { throw "The app exited during live responsiveness sampling (exit code $($child.ExitCode))." }
        if (-not [NotchlingSmoke.Native]::Responds($window)) { throw "The app UI stopped responding to WM_NULL within one second." }
        $report.ResponsiveSamples++
        $workingSet.Add($child.WorkingSet64 / 1MB)
        $privateBytes.Add($child.PrivateMemorySize64 / 1MB)
    }
    $measurementClock.Stop()
    $child.Refresh()
    $report.MeasurementSeconds = [Math]::Round($measurementClock.Elapsed.TotalSeconds, 2)
    $report.CpuPercentAllCores = [Math]::Round(100 * ($child.TotalProcessorTime.TotalSeconds - $cpuBefore) / $measurementClock.Elapsed.TotalSeconds / [Environment]::ProcessorCount, 3)
    $report.AverageWorkingSetMiB = [Math]::Round(($workingSet | Measure-Object -Average).Average, 2)
    $report.AveragePrivateMiB = [Math]::Round(($privateBytes | Measure-Object -Average).Average, 2)
    $report.PeakPrivateMiB = [Math]::Round(($privateBytes | Measure-Object -Maximum).Maximum, 2)
    $report.LastHandleCount = $child.HandleCount
    Invoke-FreeTierUiSmoke $child.Id $window
    $report.Succeeded = $true
} catch {
    $failure = $_
    $report.Error = $_.Exception.Message
    if ($child) {
        try { Read-StartupDiagnostics }
        catch { $report.DiagnosticsError = "Startup diagnostics could not be collected: $($_.Exception.Message)" }
    }
} finally {
    if ($child) {
        try {
            $child.Refresh()
            if (-not $child.HasExited) {
                # Close normally first. A tray app may intentionally hide instead of exiting.
                $closeRequested = $child.CloseMainWindow()
                if ($child.WaitForExit(2000)) {
                    $report.Teardown = "Owned test process exited after WM_CLOSE."
                } else {
                    $child.Kill()
                    if (-not $child.WaitForExit(5000)) { throw "The owned test process did not terminate." }
                    $report.Teardown = "Owned test process was terminated after bounded WM_CLOSE; this does not test graceful save/shutdown."
                }
            } else { $report.Teardown = "Owned test process had already exited." }
        } catch {
            $report.Teardown = "Test process cleanup failed: $($_.Exception.Message)"
            $report.Succeeded = $false
            if (-not $failure) { $failure = $_; $report.Error = $_.Exception.Message }
        } finally { $child.Dispose() }
    }
    $reportFullPath = [IO.Path]::GetFullPath($ReportPath)
    [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($reportFullPath)) | Out-Null
    $report | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $reportFullPath -Encoding utf8
    [pscustomobject]$report | Format-List
    if ($env:GITHUB_ACTIONS -eq 'true') {
        Write-Output "::notice title=Installed Windows smoke result::Succeeded=$($report.Succeeded); StartupMs=$($report.FirstVisibleWindowMilliseconds); ResponsiveSamples=$($report.ResponsiveSamples); FreeUiInteractions=$($report.UIInteractions.Succeeded); WorkingSetMiB=$($report.AverageWorkingSetMiB); PrivateMiB=$($report.AveragePrivateMiB); CpuAllCoresPercent=$($report.CpuPercentAllCores); SamplingSeconds=$($report.MeasurementSeconds)"
        if ($failure) {
            $message = $report.Error.Replace('%', '%25').Replace("`r", '%0D').Replace("`n", '%0A')
            Write-Output "::error title=Installed Windows launch failure::$message"
        }
    }
    if ($env:GITHUB_STEP_SUMMARY) {
        @"
## Installed Windows application smoke test

| Check / measurement | Result |
| --- | --- |
| Installed application launched and responded | $($report.Succeeded) |
| Visible native window / icon | $($report.VisibleWindow) / $($report.WindowIconPresent) |
| First visible window | $($report.FirstVisibleWindowMilliseconds) ms |
| Responsive samples | $($report.ResponsiveSamples) |
| Free media, Pomodoro, and scratchpad UI interaction | $($report.UIInteractions.Succeeded) |
| Average working set | $($report.AverageWorkingSetMiB) MiB |
| Average private memory | $($report.AveragePrivateMiB) MiB |
| CPU, normalized across all cores | $($report.CpuPercentAllCores)% |
| Sampling duration | $($report.MeasurementSeconds) seconds |

Scope: Windows hosted CI desktop only; this is a startup smoke test, not Windows 10 qualification, sustained idle profiling, or rendered animation benchmarking.
"@ | Add-Content -LiteralPath $env:GITHUB_STEP_SUMMARY -Encoding utf8
    }
}
if ($failure) { throw $failure }
