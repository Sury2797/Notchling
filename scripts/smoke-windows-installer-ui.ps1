# Exercise the real native wizard, then cancel before any installation begins.
# Windows PowerShell 5.1 provides the built-in UI Automation client.
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$SetupPath,
    [Parameter(Mandatory)][string]$DocumentsDirectory,
    [Parameter(Mandatory)][string]$ReportPath
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if ($env:OS -ne 'Windows_NT' -or $env:GITHUB_ACTIONS -ne 'true') { throw 'Interactive installer qualification requires a disposable Windows Actions desktop.' }
$clock = [Diagnostics.Stopwatch]::StartNew()
$owned = @{}
$launcher = $null
$root = $null
$failure = $null
$report = [ordered]@{
    Succeeded = $false
    Scope = 'Actual welcome, full formatted terms, explicit acceptance, full privacy and cancellation on the current disposable CI desktop. No installation or prerequisite execution; consumer hardware, other DPI settings and visual design quality remain separate checks.'
    Stage = 'Initialize native UI Automation'
    OS = [Environment]::OSVersion.VersionString
    WindowTitle = $null
    Dpi = $null
    WindowBounds = $null
    WorkArea = $null
    WindowWithinWorkArea = $false
    BrandedWindowIcon = $false
    WelcomeBranding = $false
    TermsComplete = $false
    TermsNativeHeading = $null
    AcceptanceRequired = $false
    AcceptanceEnablesNext = $false
    PrivacyComplete = $false
    PrivacyNativeHeading = $null
    CancelledBeforeInstallation = $false
    InstallationDirectoryCreated = $false
    Screenshots = @()
    VisibleControls = @()
    ElapsedSeconds = $null
    Error = $null
    ScriptStackTrace = $null
}

Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
namespace NotchlingInstallerUi {
    public static class Native {
        [StructLayout(LayoutKind.Sequential)] public struct Rect { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)] public struct MonitorInfo { public int Size; public Rect Monitor, Work; public int Flags; }
        private delegate bool EnumWindowCallback(IntPtr handle, IntPtr parameter);
        [DllImport("user32.dll")] private static extern bool EnumChildWindows(IntPtr parent, EnumWindowCallback callback, IntPtr parameter);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr window, StringBuilder text, int maximum);
        [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr window);
        [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
        [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr window, out Rect rect);
        [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
        [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr window);
        [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr context);
        [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr window);
        [DllImport("user32.dll", EntryPoint = "GetClassLongPtrW")] private static extern IntPtr GetClassLong64(IntPtr window, int index);
        [DllImport("user32.dll", EntryPoint = "GetClassLongW")] private static extern uint GetClassLong32(IntPtr window, int index);
        [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "SendMessageTimeoutW", SetLastError = true)]
        private static extern IntPtr SendText(IntPtr window, uint message, IntPtr wparam, StringBuilder text, uint flags, uint timeout, out IntPtr result);
        [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "SendMessageTimeoutW", SetLastError = true)]
        private static extern IntPtr SendPointer(IntPtr window, uint message, IntPtr wparam, IntPtr lparam, uint flags, uint timeout, out IntPtr result);
        private static IntPtr Send(IntPtr window, uint message, IntPtr wparam, IntPtr lparam) {
            IntPtr result;
            if (SendPointer(window, message, wparam, lparam, 2, 2000, out result) == IntPtr.Zero) throw new InvalidOperationException("Owned native wizard control did not respond.");
            return result;
        }
        public static Rect WorkArea(IntPtr window) {
            MonitorInfo info = new MonitorInfo(); info.Size = Marshal.SizeOf(typeof(MonitorInfo));
            if (!GetMonitorInfo(MonitorFromWindow(window, 2), ref info)) throw new InvalidOperationException("Could not read the wizard monitor work area.");
            return info.Work;
        }
        public static bool HasIcon(IntPtr window) {
            if (Send(window, 0x007F, new IntPtr(1), IntPtr.Zero) != IntPtr.Zero || Send(window, 0x007F, new IntPtr(0), IntPtr.Zero) != IntPtr.Zero) return true;
            return IntPtr.Size == 8 ? GetClassLong64(window, -14) != IntPtr.Zero : GetClassLong32(window, -14) != 0;
        }
        public static IntPtr[] RichEditControls(IntPtr window) {
            List<IntPtr> result = new List<IntPtr>();
            EnumChildWindows(window, delegate(IntPtr handle, IntPtr unused) {
                StringBuilder name = new StringBuilder(256); GetClassName(handle, name, name.Capacity);
                if (IsWindowVisible(handle) && name.ToString().IndexOf("RichEdit", StringComparison.OrdinalIgnoreCase) >= 0) result.Add(handle);
                return true;
            }, IntPtr.Zero);
            return result.ToArray();
        }
        public static string ReadText(IntPtr window) {
            long length = Send(window, 0x000E, IntPtr.Zero, IntPtr.Zero).ToInt64();
            if (length < 0 || length > 262144) throw new InvalidOperationException("Native legal text exceeded the bounded test read.");
            StringBuilder text = new StringBuilder((int)length + 1); IntPtr copied;
            if (SendText(window, 0x000D, new IntPtr(text.Capacity), text, 2, 2000, out copied) == IntPtr.Zero) throw new InvalidOperationException("Native legal text could not be read.");
            return text.ToString();
        }

    }
}
'@
[NotchlingInstallerUi.Native]::SetProcessDpiAwarenessContext([IntPtr]::new(-4)) | Out-Null
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Drawing

function Assert-Budget {
    if ($clock.Elapsed.TotalSeconds -gt 90) { throw 'Interactive installer qualification exceeded its 90-second operation budget.' }
}
function Remember-Process([int]$Id) {
    $process = Get-Process -Id $Id -ErrorAction SilentlyContinue
    if ($process -and -not $process.HasExited -and $process.StartTime.ToUniversalTime().Ticks -ge $launcher.StartTime.ToUniversalTime().Ticks) { $owned[$Id] = $process.StartTime.ToUniversalTime().Ticks }
}
function Find-OwnedWindow {
    Assert-Budget
    # Setup's loader creates a child .tmp executable. Record only descendants
    # of the process this helper started, and retain creation times for cleanup.
    $processes = @(Get-CimInstance Win32_Process)
    do {
        $added = $false
        foreach ($process in $processes) {
            $id = [int]$process.ProcessId
            if (-not $owned.ContainsKey($id) -and $owned.ContainsKey([int]$process.ParentProcessId)) {
                Remember-Process $id
                if ($owned.ContainsKey($id)) { $added = $true }
            }
        }
    } while ($added)
    foreach ($surface in [System.Windows.Automation.AutomationElement]::RootElement.FindAll([System.Windows.Automation.TreeScope]::Children, [System.Windows.Automation.Condition]::TrueCondition)) {
        try {
            $current = $surface.Current
            if ($owned.ContainsKey($current.ProcessId) -and -not $current.IsOffscreen -and $current.Name -eq 'Notchling Setup') { return $surface }
        } catch [System.Windows.Automation.ElementNotAvailableException] { continue }
    }
    return $null
}
function Find-Control([string]$Name, $Type = $null, [bool]$Enabled = $true) {
    Assert-Budget
    foreach ($control in $root.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)) {
        try {
            $current = $control.Current
            if ($current.IsOffscreen -or $current.BoundingRectangle.IsEmpty -or ($Enabled -and -not $current.IsEnabled)) { continue }
            if ($null -ne $Type -and $current.ControlType -ne $Type) { continue }
            if (($current.Name -replace '&', '') -match $Name) { return $control }
        } catch [System.Windows.Automation.ElementNotAvailableException] { continue }
    }
    return $null
}
function Wait-Control([string]$Name, $Type = $null, [bool]$Enabled = $true) {
    $wait = [Diagnostics.Stopwatch]::StartNew()
    do {
        $control = Find-Control $Name $Type $Enabled
        if ($control) { return $control }
        Start-Sleep -Milliseconds 100
    } while ($wait.Elapsed.TotalSeconds -lt 8)
    throw "Owned installer control did not appear: $Name."
}
function Invoke-Control($Control) {
    Assert-Budget
    $provider = $null
    if (-not $Control.TryGetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern, [ref]$provider)) { throw "Installer control is not accessible for invocation: $($Control.Current.Name)." }
    $provider.Invoke()
}
function Assert-PageBounds {
    $window = [IntPtr]::new($root.Current.NativeWindowHandle)
    $bounds = [NotchlingInstallerUi.Native+Rect]::new()
    if (-not [NotchlingInstallerUi.Native]::GetWindowRect($window, [ref]$bounds)) { throw 'Installer window bounds are unavailable.' }
    $work = [NotchlingInstallerUi.Native]::WorkArea($window)
    # DWM shadows can extend a few physical pixels beyond the visible frame.
    if ($bounds.Left -lt $work.Left - 8 -or $bounds.Top -lt $work.Top - 8 -or $bounds.Right -gt $work.Right + 8 -or $bounds.Bottom -gt $work.Bottom + 8) { throw 'Installer navigation does not fit within the current monitor work area.' }
    foreach ($name in @('^Next(?: >)?$', '^Cancel$')) {
        $button = Wait-Control $name ([System.Windows.Automation.ControlType]::Button) $false
        $rectangle = $button.Current.BoundingRectangle
        if ($rectangle.Left -lt $work.Left -or $rectangle.Top -lt $work.Top -or $rectangle.Right -gt $work.Right -or $rectangle.Bottom -gt $work.Bottom) { throw "Installer action is clipped outside the monitor work area: $name." }
    }
    $report.WindowBounds = [ordered]@{ Left = $bounds.Left; Top = $bounds.Top; Right = $bounds.Right; Bottom = $bounds.Bottom }
    $report.WorkArea = [ordered]@{ Left = $work.Left; Top = $work.Top; Right = $work.Right; Bottom = $work.Bottom }
    $report.WindowWithinWorkArea = $true
    $report.Dpi = [NotchlingInstallerUi.Native]::GetDpiForWindow($window)
}
function Capture-Page([string]$Name) {
    Assert-PageBounds
    $window = [IntPtr]::new($root.Current.NativeWindowHandle)
    [NotchlingInstallerUi.Native]::SetForegroundWindow($window) | Out-Null
    Start-Sleep -Milliseconds 150
    $bounds = $report.WindowBounds
    $directory = Join-Path ([IO.Path]::GetDirectoryName($ReportPath)) 'screenshots'
    [IO.Directory]::CreateDirectory($directory) | Out-Null
    $path = Join-Path $directory ("installer-$Name.png")
    $bitmap = [Drawing.Bitmap]::new(($bounds.Right - $bounds.Left), ($bounds.Bottom - $bounds.Top))
    $graphics = [Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.CopyFromScreen($bounds.Left, $bounds.Top, 0, 0, $bitmap.Size)
        $bitmap.Save($path, [Drawing.Imaging.ImageFormat]::Png)
        $report.Screenshots += $path
    } finally { $graphics.Dispose(); $bitmap.Dispose() }
}
function Read-HeadingFormat([IntPtr]$Handle, [string]$Title) {
    # EM_GETCHARFORMAT is a WM_USER message. Its pointer would not be
    # marshalled into the installer's process. Read the actual text provider's
    # heading range and font attributes through UI Automation instead.
    foreach ($element in $root.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)) {
        try {
            if ([long]$element.Current.NativeWindowHandle -ne $Handle.ToInt64()) { continue }
            $provider = $null
            if (-not $element.TryGetCurrentPattern([System.Windows.Automation.TextPattern]::Pattern, [ref]$provider)) { continue }
            $range = $provider.DocumentRange.Clone()
            $range.MoveEndpointByRange([System.Windows.Automation.TextPatternRangeEndpoint]::End, $range, [System.Windows.Automation.TextPatternRangeEndpoint]::Start)
            $range.MoveEndpointByUnit([System.Windows.Automation.TextPatternRangeEndpoint]::End, [System.Windows.Automation.TextUnit]::Character, $Title.Length) | Out-Null
            if ($range.GetText(-1) -cne $Title) { throw 'The native heading text range did not match the complete document title.' }
            $font = $range.GetAttributeValue([System.Windows.Automation.TextPattern]::FontNameAttribute)
            $points = $range.GetAttributeValue([System.Windows.Automation.TextPattern]::FontSizeAttribute)
            $weight = $range.GetAttributeValue([System.Windows.Automation.TextPattern]::FontWeightAttribute)
            if ($font -ne 'Segoe UI' -or $points -isnot [double] -or $points -lt 15 -or $weight -isnot [int] -or $weight -lt 700) { throw 'The actual legal heading does not expose the intended native Segoe UI typography.' }
            return [ordered]@{ Font = $font; Points = $points; Bold = $weight -ge 700; Reader = 'Owned UI Automation TextPattern heading range and font attributes.' }
        } catch [System.Windows.Automation.ElementNotAvailableException] { continue }
    }
    throw 'The actual native legal viewer did not expose its accessible text formatting.'
}
function Verify-LegalPage([string]$Name, [string]$Title) {
    $expectedPath = Join-Path $DocumentsDirectory ($Name + '.txt')
    $expected = [IO.File]::ReadAllText($expectedPath, [Text.Encoding]::UTF8)
    $normalize = { param([string]$Text) (($Text -replace '\s+', ' ').Trim()) }
    foreach ($handle in [NotchlingInstallerUi.Native]::RichEditControls([IntPtr]::new($root.Current.NativeWindowHandle))) {
        $ownerId = [uint32]0
        [NotchlingInstallerUi.Native]::GetWindowThreadProcessId($handle, [ref]$ownerId) | Out-Null
        if (-not $owned.ContainsKey([int]$ownerId)) { throw 'Legal viewer is not owned by the launched installer.' }
        $actual = [NotchlingInstallerUi.Native]::ReadText($handle)
        if (-not $actual.StartsWith($Title, [StringComparison]::Ordinal)) { continue }
        if ((& $normalize $actual) -cne (& $normalize $expected)) { throw "The actual $Name viewer omitted or changed generated full legal text." }
        if ($actual -match '(?m)^#{1,6}\s|\*\*|\]\(|\| ---') { throw "Markdown syntax is still visible in the $Name viewer." }
        $format = Read-HeadingFormat $handle $Title
        return [ordered]@{ Font = $format.Font; Points = $format.Points; Bold = $format.Bold; Characters = $actual.Length; ExactGeneratedText = $true; Reader = 'Bounded Win32 RichEdit text and owned UI Automation TextPattern formatting/navigation.' }
    }
    throw "The actual $Name page did not expose its native legal viewer."
}

try {
    $SetupPath = (Resolve-Path -LiteralPath $SetupPath).Path
    $DocumentsDirectory = (Resolve-Path -LiteralPath $DocumentsDirectory).Path
    $ReportPath = [IO.Path]::GetFullPath($ReportPath)
    [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($ReportPath)) | Out-Null
    $destination = Join-Path $env:RUNNER_TEMP ('Notchling.WizardProbe-' + [Guid]::NewGuid().ToString('N'))
    $log = Join-Path ([IO.Path]::GetDirectoryName($ReportPath)) 'installer-ui.log'
    $launcher = Start-Process -FilePath $SetupPath -ArgumentList @('/SP-', '/NORESTART', "/DIR=`"$destination`"", "/LOG=`"$log`"") -PassThru
    Remember-Process $launcher.Id
    $report.Stage = 'Branded welcome and monitor bounds'
    $wait = [Diagnostics.Stopwatch]::StartNew()
    do {
        $root = Find-OwnedWindow
        if ($root) { break }
        Start-Sleep -Milliseconds 200
    } while ($wait.Elapsed.TotalSeconds -lt 20)
    if (-not $root) { throw 'The launched installer did not show its branded native wizard.' }
    $report.WindowTitle = $root.Current.Name
    Wait-Control '^Notchling$' ([System.Windows.Automation.ControlType]::Text) | Out-Null
    Wait-Control 'A dynamic island for your Windows desktop' ([System.Windows.Automation.ControlType]::Text) | Out-Null
    $report.WelcomeBranding = $true
    $report.BrandedWindowIcon = [NotchlingInstallerUi.Native]::HasIcon([IntPtr]::new($root.Current.NativeWindowHandle))
    if (-not $report.BrandedWindowIcon) { throw 'The native wizard did not expose an application window icon.' }
    Capture-Page 'welcome'
    Invoke-Control (Wait-Control '^Next(?: >)?$' ([System.Windows.Automation.ControlType]::Button))
    $report.Stage = 'Formatted terms and explicit acceptance'
    Wait-Control '^Terms of use$' ([System.Windows.Automation.ControlType]::Text) | Out-Null
    $report.TermsNativeHeading = Verify-LegalPage 'product-terms' 'Notchling application terms'
    $report.TermsComplete = $true
    $next = Wait-Control '^Next(?: >)?$' ([System.Windows.Automation.ControlType]::Button) $false
    if ($next.Current.IsEnabled) { throw 'Terms were accepted automatically before the explicit user choice.' }
    $report.AcceptanceRequired = $true
    Capture-Page 'terms'
    $accept = Wait-Control '^I accept the terms$' ([System.Windows.Automation.ControlType]::RadioButton)
    $selection = $null
    if ($accept.TryGetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern, [ref]$selection)) { $selection.Select() } else { Invoke-Control $accept }
    $next = Wait-Control '^Next(?: >)?$' ([System.Windows.Automation.ControlType]::Button)
    $report.AcceptanceEnablesNext = $true
    Invoke-Control $next
    $report.Stage = 'Formatted privacy and local data controls'
    Wait-Control '^Privacy and your controls$' ([System.Windows.Automation.ControlType]::Text) | Out-Null
    $report.PrivacyNativeHeading = Verify-LegalPage 'privacy' 'Notchling privacy and data handling'
    $report.PrivacyComplete = $true
    Capture-Page 'privacy'
    $report.Stage = 'Cancel before preparation or installation'
    Invoke-Control (Wait-Control '^Cancel$' ([System.Windows.Automation.ControlType]::Button))
    $wait.Restart()
    do {
        Assert-Budget
        foreach ($surface in [System.Windows.Automation.AutomationElement]::RootElement.FindAll([System.Windows.Automation.TreeScope]::Children, [System.Windows.Automation.Condition]::TrueCondition)) {
            try {
                if (-not $owned.ContainsKey($surface.Current.ProcessId) -or $surface.Current.IsOffscreen) { continue }
                foreach ($button in $surface.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)) {
                    if (-not $button.Current.IsOffscreen -and ($button.Current.Name -replace '&', '') -eq 'Yes' -and $button.Current.ControlType -eq [System.Windows.Automation.ControlType]::Button) { Invoke-Control $button }
                }
            } catch [System.Windows.Automation.ElementNotAvailableException] { continue }
        }
        $alive = @(foreach ($id in $owned.Keys) { $process = Get-Process -Id $id -ErrorAction SilentlyContinue; if ($process -and -not $process.HasExited -and $process.StartTime.ToUniversalTime().Ticks -eq $owned[$id]) { $process } })
        if ($alive.Count -eq 0) { break }
        Start-Sleep -Milliseconds 150
    } while ($wait.Elapsed.TotalSeconds -lt 10)
    if ($alive.Count -ne 0) { throw 'The owned installer did not exit after cancellation.' }
    $report.InstallationDirectoryCreated = Test-Path -LiteralPath $destination
    if ($report.InstallationDirectoryCreated) { throw 'The presentation probe began installation before cancellation.' }
    $report.CancelledBeforeInstallation = $true
    $report.Succeeded = $true
    $report.Stage = 'Complete'
} catch {
    $failure = $_
    $report.Error = $_.Exception.Message
    $report.ScriptStackTrace = $_.ScriptStackTrace
    if ($root) {
        try {
            foreach ($element in $root.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)) {
                if (-not $element.Current.IsOffscreen -and -not [string]::IsNullOrWhiteSpace($element.Current.Name)) { $report.VisibleControls += $element.Current.Name.Substring(0, [Math]::Min(160, $element.Current.Name.Length)) }
                if ($report.VisibleControls.Count -ge 18) { break }
            }
        } catch { }
    }
} finally {
    # Terminate only live processes this helper launched/discovered, with the
    # same creation time. Never close another installer's coincidental title.
    foreach ($id in @($owned.Keys)) {
        $process = Get-Process -Id $id -ErrorAction SilentlyContinue
        if ($process -and -not $process.HasExited -and $process.StartTime.ToUniversalTime().Ticks -eq $owned[$id]) { Stop-Process -Id $id -Force -ErrorAction SilentlyContinue }
    }
    if ($launcher) { $launcher.Dispose() }
    $report.ElapsedSeconds = [Math]::Round($clock.Elapsed.TotalSeconds, 3)
    $report | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $ReportPath -Encoding UTF8
}
if ($failure) { throw $failure }
