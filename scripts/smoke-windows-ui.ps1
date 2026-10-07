# Windows PowerShell 5.1 supplies the built-in .NET Framework UI Automation client.
# The caller bounds this helper to 90 seconds and owns the target app's lifecycle.
[CmdletBinding()]
param(
    [Parameter(Mandatory)][int]$AppProcessId,
    [Parameter(Mandatory)][long]$WindowHandle,
    [Parameter(Mandatory)][string]$ReportPath
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest
$clock = [Diagnostics.Stopwatch]::StartNew()
$failure = $null
$report = [ordered]@{
    Succeeded = $false
    Scope = "Free-tier UI Automation on a disposable CI desktop; no external player, account, or payment."
    Actions = @()
    MediaNoPlayerControlsDisabled = $false
    NoAutomaticSampleData = $false
    DockWithinWorkArea = $false
    PointerInsidePreservesExpanded = $false
    ActiveSettingsPreservesExpanded = $false
    SettingsNoHorizontalOverflow = $false
    SettingsTogglePreservesScroll = $false
    SettingsDraftPreserved = $false
    ExplicitSamplePreviewExited = $false
    FocusInitial = $null
    FocusAfterStart = $null
    FocusAfterPause = $null
    FocusAfterReset = $null
    ScratchpadRoundTrip = $false
    ScratchpadSaved = $false
    ScratchpadCleared = $false
    ElapsedSeconds = $null
    Error = $null
}

function Assert-Budget {
    if ($clock.Elapsed.TotalSeconds -gt 75) { throw "Free-tier UI interaction exceeded its 75-second operation budget." }
    $process = Get-Process -Id $AppProcessId -ErrorAction Stop
    if ($process.HasExited) { throw "The owned app exited during UI interaction." }
}

function Find-Control([string]$Name, $ControlType, [bool]$RequireEnabled = $true) {
    Assert-Budget
    $condition = [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::NameProperty, $Name)
    if ($null -ne $ControlType) {
        $condition = [System.Windows.Automation.AndCondition]::new($condition,
            [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::ControlTypeProperty, $ControlType))
    }
    $candidates = $root.FindAll([System.Windows.Automation.TreeScope]::Descendants, $condition)
    foreach ($candidate in $candidates) {
        try {
            $current = $candidate.Current
            if ($current.IsOffscreen -or $current.BoundingRectangle.IsEmpty) { continue }
            if (-not $RequireEnabled -or $current.IsEnabled) { return $candidate }
        } catch [System.Windows.Automation.ElementNotAvailableException] { continue }
    }
    return $null
}

function Wait-Control([string]$Name, $ControlType, [bool]$RequireEnabled = $true) {
    $wait = [Diagnostics.Stopwatch]::StartNew()
    do {
        $control = Find-Control $Name $ControlType $RequireEnabled
        if ($control) { return $control }
        Start-Sleep -Milliseconds 100
    } while ($wait.Elapsed.TotalSeconds -lt 10)
    throw "An accessible usable control did not appear: $Name ($ControlType)."
}

function Invoke-Button([string]$Name) {
    $button = Wait-Control $Name ([System.Windows.Automation.ControlType]::Button)
    $pattern = $button.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
    $pattern.Invoke()
    $report.Actions += "Invoke: $Name"
}

function Assert-NoAutomaticSampleData {
    $condition = [System.Windows.Automation.PropertyCondition]::new(
        [System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Text)
    foreach ($element in $root.FindAll([System.Windows.Automation.TreeScope]::Descendants, $condition)) {
        try {
            $current = $element.Current
            if (-not $current.IsOffscreen -and -not $current.BoundingRectangle.IsEmpty -and
                $current.Name -match '^(DEMO(?:\b|\s)|Golden Hour Drive$|Sample track$|Preview \u00b7 sample data$)') {
                throw "Sample data appeared without an explicit preview choice: $($current.Name)"
            }
        } catch [System.Windows.Automation.ElementNotAvailableException] { continue }
    }
    if (Find-Control "Exit sample data preview" ([System.Windows.Automation.ControlType]::Button) $false) {
        throw "The sample-data preview strip was visible without an explicit preview choice."
    }
}

function Set-Toggle([string]$Name, [bool]$On) {
    $control = Wait-Control $Name $null
    $pattern = $control.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern)
    $expected = if ($On) { [System.Windows.Automation.ToggleState]::On } else { [System.Windows.Automation.ToggleState]::Off }
    if ($pattern.Current.ToggleState -ne $expected) { $pattern.Toggle() }
    $wait = [Diagnostics.Stopwatch]::StartNew()
    do {
        Assert-Budget
        $control = Find-Control $Name $null
        if ($control) {
            $pattern = $control.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern)
            if ($pattern.Current.ToggleState -eq $expected) { return }
        }
        Start-Sleep -Milliseconds 100
    } while ($wait.Elapsed.TotalSeconds -lt 4)
    throw "The setting did not reach the requested toggle state: $Name ($expected)."
}

function Move-Cursor([int]$X, [int]$Y) {
    Assert-Budget
    if (-not [NotchlingUiSmoke.Native]::SetCursorPos($X, $Y)) { throw "Windows rejected the owned UI test's pointer move." }
    $actual = [NotchlingUiSmoke.Native+Point]::new()
    if (-not [NotchlingUiSmoke.Native]::GetCursorPos([ref]$actual) -or $actual.X -ne $X -or $actual.Y -ne $Y) {
        throw "The disposable desktop did not move its pointer to the requested regression-test location."
    }
}

function Get-SettingsScroll {
    $content = Wait-Control "Notchling settings content" $null
    return $content.GetCurrentPattern([System.Windows.Automation.ScrollPattern]::Pattern)
}

function Scroll-Settings([double]$Percent) {
    $pattern = Get-SettingsScroll
    if ($pattern.Current.HorizontallyScrollable) { throw "Settings exposes horizontal scrolling instead of adapting to the notch width." }
    if ($pattern.Current.VerticallyScrollable) {
        $pattern.SetScrollPercent([System.Windows.Automation.ScrollPattern]::NoScroll, $Percent)
        Start-Sleep -Milliseconds 100
    }
}

function Scroll-ToSetting([string]$Name, $ControlType) {
    $control = Find-Control $Name $ControlType
    if ($control) { return $control }
    for ($percent = 0; $percent -le 100; $percent += 10) {
        Scroll-Settings $percent
        $control = Find-Control $Name $ControlType
        if ($control) { return $control }
    }
    throw "A Settings control could not be reached by vertical scrolling: $Name."
}

function Assert-HorizontalBounds {
    Assert-Budget
    $bounds = [NotchlingUiSmoke.Native]::WindowBounds([IntPtr]::new($WindowHandle))
    $elements = $root.FindAll([System.Windows.Automation.TreeScope]::Descendants,
        [System.Windows.Automation.Condition]::TrueCondition)
    foreach ($element in $elements) {
        try {
            $current = $element.Current
            if ($current.IsOffscreen -or $current.BoundingRectangle.IsEmpty) { continue }
            $rectangle = $current.BoundingRectangle
            if ($rectangle.Left -lt $bounds.Left - 2 -or $rectangle.Right -gt $bounds.Right + 2) {
                throw "Visible Settings content exceeds the native notch width: '$($current.Name)' ($($rectangle.Left)..$($rectangle.Right), notch $($bounds.Left)..$($bounds.Right))."
            }
        } catch [System.Windows.Automation.ElementNotAvailableException] { continue }
    }
}

function Assert-DockBounds {
    Assert-Budget
    $bounds = [NotchlingUiSmoke.Native]::WindowBounds([IntPtr]::new($WindowHandle))
    $work = [NotchlingUiSmoke.Native]::WorkArea([IntPtr]::new($WindowHandle))
    $dockHeight = 70 * [NotchlingUiSmoke.Native]::GetDpiForWindow([IntPtr]::new($WindowHandle)) / 96
    if ($bounds.Left -lt $work.Left -or $bounds.Top -lt $work.Top -or $bounds.Right -gt $work.Right -or $bounds.Bottom -gt $work.Bottom) {
        throw "The native notch or its dock exceeds the monitor work area."
    }
    foreach ($name in @("Home", "Media", "Focus", "Scratchpad", "All tools", "Settings", "Keep Notchling expanded")) {
        $condition = [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::NameProperty, $name)
        $dockControl = $null
        foreach ($candidate in $root.FindAll([System.Windows.Automation.TreeScope]::Descendants, $condition)) {
            try {
                $current = $candidate.Current
                if (-not $current.IsOffscreen -and $current.IsEnabled -and -not $current.BoundingRectangle.IsEmpty -and
                    $current.BoundingRectangle.Top -ge $bounds.Bottom - $dockHeight) { $dockControl = $candidate; break }
            } catch [System.Windows.Automation.ElementNotAvailableException] { continue }
        }
        if (-not $dockControl) { throw "An enabled Free dock control is absent from the visible bottom dock: $name." }
        $rectangle = $dockControl.Current.BoundingRectangle
        if ($rectangle.Left -lt $bounds.Left - 2 -or $rectangle.Right -gt $bounds.Right + 2 -or
            $rectangle.Top -lt $bounds.Top - 2 -or $rectangle.Bottom -gt $bounds.Bottom + 2) {
            throw "An accessible Free dock control is clipped outside the native window: $name."
        }
    }
}

function Read-FocusClock {
    Assert-Budget
    $condition = [System.Windows.Automation.PropertyCondition]::new(
        [System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Text)
    foreach ($element in $root.FindAll([System.Windows.Automation.TreeScope]::Descendants, $condition)) {
        try {
            $current = $element.Current
            if (-not $current.IsOffscreen -and -not $current.BoundingRectangle.IsEmpty -and $current.Name -match '^\d{2}:\d{2}$') {
                return $current.Name
            }
        } catch [System.Windows.Automation.ElementNotAvailableException] { continue }
    }
    return $null
}

function Wait-FocusClock([string]$Expected, [bool]$Different = $false) {
    $wait = [Diagnostics.Stopwatch]::StartNew()
    do {
        $value = Read-FocusClock
        if ($value -and (($Different -and $value -ne $Expected) -or (-not $Different -and $value -eq $Expected))) { return $value }
        Start-Sleep -Milliseconds 100
    } while ($wait.Elapsed.TotalSeconds -lt 4)
    throw "The visible Pomodoro clock did not reach the expected state (expected $Expected, different=$Different)."
}

function Wait-ScratchpadSave([string]$Expected) {
    $wait = [Diagnostics.Stopwatch]::StartNew()
    $workspaceFile = Join-Path ([Environment]::GetFolderPath("LocalApplicationData")) "Notch/workspace.json"
    do {
        Assert-Budget
        if (Test-Path -LiteralPath $workspaceFile -PathType Leaf) {
            try {
                $saved = Get-Content -LiteralPath $workspaceFile -Raw | ConvertFrom-Json
                if ($saved.Scratchpad -eq $Expected) { return }
            } catch { } # Atomic replacement can briefly race the read; retry within the bound.
        }
        Start-Sleep -Milliseconds 100
    } while ($wait.Elapsed.TotalSeconds -lt 4)
    throw "The scratchpad did not persist the expected plain text in the disposable workspace."
}

try {
    if ($env:GITHUB_ACTIONS -ne "true") { throw "UI smoke interaction requires a disposable GitHub Actions desktop." }
    if ($PSVersionTable.PSEdition -ne "Desktop") { throw "Run this helper with Windows PowerShell 5.1." }
    $process = Get-Process -Id $AppProcessId -ErrorAction Stop
    if ([IO.Path]::GetFileName($process.Path) -ne "Notchling.Windows.exe") { throw "The supplied process is not the expected app." }
    Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
    Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
namespace NotchlingUiSmoke {
    public static class Native {
        [StructLayout(LayoutKind.Sequential)] public struct Rect { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)] public struct Point { public int X, Y; }
        [StructLayout(LayoutKind.Sequential)] private struct MonitorInfo { public int Size; public Rect Monitor, Work; public uint Flags; }
        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out Rect bounds);
        [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);
        [DllImport("user32.dll", EntryPoint="GetMonitorInfoW")] private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo information);
        [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
        [DllImport("user32.dll")] public static extern bool GetCursorPos(out Point point);
        [DllImport("user32.dll")] public static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
        [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr window);
        [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr window);
        public static Rect WindowBounds(IntPtr window) {
            Rect bounds; if (!GetWindowRect(window, out bounds)) throw new InvalidOperationException("Unable to read the owned HWND bounds.");
            return bounds;
        }
        public static Rect WorkArea(IntPtr window) {
            var information = new MonitorInfo(); information.Size = Marshal.SizeOf(typeof(MonitorInfo));
            if (!GetMonitorInfo(MonitorFromWindow(window, 2), ref information)) throw new InvalidOperationException("Unable to read the owned HWND monitor work area.");
            return information.Work;
        }
    }
}
'@
    # UI Automation uses physical screen coordinates. Match those coordinates in
    # the native monitor and pointer APIs when a CI desktop uses display scaling.
    $originalDpiContext = [NotchlingUiSmoke.Native]::SetThreadDpiAwarenessContext([IntPtr]::new(-4))
    $originalCursor = [NotchlingUiSmoke.Native+Point]::new()
    $restoreCursor = [NotchlingUiSmoke.Native]::GetCursorPos([ref]$originalCursor)
    $root = [System.Windows.Automation.AutomationElement]::FromHandle([IntPtr]::new($WindowHandle))
    if (-not $root -or $root.Current.ProcessId -ne $AppProcessId) { throw "The supplied native window is not owned by the test app." }

    Invoke-Button "Open Notchling"
    # Match the unique accessible name, then require its actual Toggle pattern.
    $pin = Wait-Control "Keep Notchling expanded" $null
    $toggle = $pin.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern)
    if ($toggle.Current.ToggleState -ne [System.Windows.Automation.ToggleState]::On) { $toggle.Toggle() }
    $pinWait = [Diagnostics.Stopwatch]::StartNew()
    $preferencesFile = Join-Path ([Environment]::GetFolderPath("LocalApplicationData")) "Notch/preferences.json"
    while ($true) {
        Assert-Budget
        $pinned = $false
        if (Test-Path -LiteralPath $preferencesFile -PathType Leaf) {
            try { $pinned = (Get-Content -LiteralPath $preferencesFile -Raw | ConvertFrom-Json).Pinned -eq $true } catch { }
        }
        if ($toggle.Current.ToggleState -eq [System.Windows.Automation.ToggleState]::On -and $pinned) { break }
        if ($pinWait.Elapsed.TotalSeconds -ge 10) {
            $savedPreferences = if (Test-Path -LiteralPath $preferencesFile) { Get-Content -LiteralPath $preferencesFile -Raw } else { "preferences file absent" }
            throw "The expanded notch could not be pinned for interaction. Toggle state: $($toggle.Current.ToggleState); persisted pin: $pinned; preferences: $savedPreferences"
        }
        Start-Sleep -Milliseconds 100
    }
    $report.Actions += "Pinned expanded notch"
    Wait-Control "Notchling Free" ([System.Windows.Automation.ControlType]::Text) | Out-Null
    Assert-NoAutomaticSampleData
    $report.NoAutomaticSampleData = $true
    Assert-DockBounds
    $report.DockWithinWorkArea = $true

    # Unpinned navigation must stay expanded while the cursor crosses children and
    # the native gap above the dock. Neither point is outside the owned HWND.
    Set-Toggle "Keep Notchling expanded" $false
    $bounds = [NotchlingUiSmoke.Native]::WindowBounds([IntPtr]::new($WindowHandle))
    $insideX = [int](($bounds.Left + $bounds.Right) / 2)
    $displayScale = [NotchlingUiSmoke.Native]::GetDpiForWindow([IntPtr]::new($WindowHandle)) / 96
    if ($displayScale -le 0) { throw "The owned HWND did not expose a valid display DPI." }
    Move-Cursor $insideX ($bounds.Top + [int][Math]::Round(50 * $displayScale))
    Start-Sleep -Milliseconds 100
    Move-Cursor $insideX ($bounds.Bottom - [int][Math]::Round(65 * $displayScale))
    Start-Sleep -Milliseconds 1600
    Wait-Control "Notchling Free" ([System.Windows.Automation.ControlType]::Text) | Out-Null
    $report.PointerInsidePreservesExpanded = $true

    Invoke-Button "Settings"
    Get-SettingsScroll | Out-Null
    [NotchlingUiSmoke.Native]::SetForegroundWindow([IntPtr]::new($WindowHandle)) | Out-Null
    $activeWait = [Diagnostics.Stopwatch]::StartNew()
    while ([NotchlingUiSmoke.Native]::GetForegroundWindow().ToInt64() -ne $WindowHandle) {
        Assert-Budget
        if ($activeWait.Elapsed.TotalSeconds -ge 4) { throw "The Settings window could not become active for its collapse regression check." }
        Start-Sleep -Milliseconds 100
    }
    $work = [NotchlingUiSmoke.Native]::WorkArea([IntPtr]::new($WindowHandle))
    $settingsBounds = [NotchlingUiSmoke.Native]::WindowBounds([IntPtr]::new($WindowHandle))
    $outsideX = $work.Right - 8; $outsideY = $work.Bottom - 8
    if ($outsideX -ge $settingsBounds.Left -and $outsideX -lt $settingsBounds.Right -and
        $outsideY -ge $settingsBounds.Top -and $outsideY -lt $settingsBounds.Bottom) {
        throw "The disposable desktop has no outside-window pointer position for this Settings regression check."
    }
    Move-Cursor $outsideX $outsideY
    Start-Sleep -Milliseconds 1600
    Get-SettingsScroll | Out-Null
    $report.ActiveSettingsPreservesExpanded = $true
    Set-Toggle "Keep Notchling expanded" $true

    # Check each vertical section: disabled horizontal scrolling alone would not
    # detect oversized controls in the old infinitely measured row layout.
    for ($percent = 0; $percent -le 100; $percent += 10) {
        Scroll-Settings $percent
        Assert-HorizontalBounds
    }
    $report.SettingsNoHorizontalOverflow = $true
    $number = Scroll-ToSetting "Pomodoro length (minutes)" $null
    $numberValue = $number.GetCurrentPattern([System.Windows.Automation.RangeValuePattern]::Pattern)
    $initialMinutes = $numberValue.Current.Value
    $numberValue.SetValue(37)
    $motion = Scroll-ToSetting "Reduce motion" $null
    $motionValue = $motion.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern)
    $initialMotion = $motionValue.Current.ToggleState -eq [System.Windows.Automation.ToggleState]::On
    $scrollBefore = (Get-SettingsScroll).Current.VerticalScrollPercent
    Set-Toggle "Reduce motion" (-not $initialMotion)
    Start-Sleep -Milliseconds 700
    $scrollAfter = (Get-SettingsScroll).Current.VerticalScrollPercent
    if ([Math]::Abs($scrollAfter - $scrollBefore) -gt 1) { throw "Changing a Settings toggle reset the vertical scroll position." }
    Set-Toggle "Reduce motion" $initialMotion
    $report.SettingsTogglePreservesScroll = $true
    Invoke-Button "Home"
    Invoke-Button "Settings"
    $number = Scroll-ToSetting "Pomodoro length (minutes)" $null
    $numberValue = $number.GetCurrentPattern([System.Windows.Automation.RangeValuePattern]::Pattern)
    if ($numberValue.Current.Value -ne 37) { throw "An unapplied Settings draft was lost when navigating away and back." }
    $numberValue.SetValue($initialMinutes)
    $report.SettingsDraftPreserved = $true

    $preview = Scroll-ToSetting "Sample-data preview" $null
    $expander = $preview.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern)
    $expander.Expand()
    Scroll-ToSetting "Preview sample data for this session" $null | Out-Null
    Set-Toggle "Preview sample data for this session" $true
    Wait-Control "Preview · sample data" ([System.Windows.Automation.ControlType]::Text) | Out-Null
    Invoke-Button "Exit sample data preview"
    $previewWait = [Diagnostics.Stopwatch]::StartNew()
    while (Find-Control "Exit sample data preview" ([System.Windows.Automation.ControlType]::Button) $false) {
        Assert-Budget
        if ($previewWait.Elapsed.TotalSeconds -ge 4) { throw "Exiting sample-data preview did not remove its visible preview strip." }
        Start-Sleep -Milliseconds 100
    }
    Assert-NoAutomaticSampleData
    $report.ExplicitSamplePreviewExited = $true
    $report.Actions += "Verified active Settings, work-area dock bounds, vertical layout, toggle scroll position, unapplied draft retention, and explicit preview exit"

    Invoke-Button "Media"
    Wait-Control "Nothing playing" ([System.Windows.Automation.ControlType]::Text) | Out-Null
    foreach ($name in @("Previous", "Play", "Next")) {
        $control = Wait-Control $name ([System.Windows.Automation.ControlType]::Button) $false
        if ($control.Current.IsEnabled) { throw "No-player media control incorrectly accepts commands: $name" }
    }
    $report.MediaNoPlayerControlsDisabled = $true
    $report.Actions += "Verified genuine no-player media state"

    Invoke-Button "Focus"
    Wait-Control "Start / pause Pomodoro" ([System.Windows.Automation.ControlType]::Button) | Out-Null
    $report.FocusInitial = Wait-FocusClock "25:00"
    Invoke-Button "Start / pause Pomodoro"
    $report.FocusAfterStart = Wait-FocusClock $report.FocusInitial $true
    Invoke-Button "Start / pause Pomodoro"
    Start-Sleep -Milliseconds 200
    $paused = Read-FocusClock
    Start-Sleep -Milliseconds 1200
    $report.FocusAfterPause = Read-FocusClock
    if (-not $paused -or $report.FocusAfterPause -ne $paused) { throw "The Pomodoro kept counting down after pause." }
    Invoke-Button "Reset"
    $report.FocusAfterReset = Wait-FocusClock $report.FocusInitial
    $report.Actions += "Verified Pomodoro start, pause, and reset"

    Invoke-Button "Home"
    Invoke-Button "Scratchpad"
    $editor = Wait-Control "Scratchpad" ([System.Windows.Automation.ControlType]::Edit)
    $value = $editor.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern)
    if ($value.Current.IsReadOnly) { throw "The Free scratchpad is read-only." }
    $scratchpadText = "Notchling disposable cloud smoke " + [guid]::NewGuid().ToString("N")
    $value.SetValue($scratchpadText)
    Wait-ScratchpadSave $scratchpadText
    $report.ScratchpadSaved = $true
    Invoke-Button "Home"
    Invoke-Button "Scratchpad"
    $editor = Wait-Control "Scratchpad" ([System.Windows.Automation.ControlType]::Edit)
    $value = $editor.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern)
    if ($value.Current.Value -ne $scratchpadText) { throw "The scratchpad lost its text after navigating away and back." }
    $report.ScratchpadRoundTrip = $true
    $value.SetValue("")
    Wait-ScratchpadSave ""
    $report.ScratchpadCleared = $true
    $report.Actions += "Verified scratchpad edit, durable save, navigation, and cleanup"
    $report.Succeeded = $true
} catch {
    $failure = $_
    $report.Error = $_.Exception.Message
} finally {
    if (Get-Variable -Name restoreCursor -ErrorAction SilentlyContinue) {
        if ($restoreCursor) { [NotchlingUiSmoke.Native]::SetCursorPos($originalCursor.X, $originalCursor.Y) | Out-Null }
    }
    if (Get-Variable -Name originalDpiContext -ErrorAction SilentlyContinue) {
        if ($originalDpiContext -ne [IntPtr]::Zero) { [NotchlingUiSmoke.Native]::SetThreadDpiAwarenessContext($originalDpiContext) | Out-Null }
    }
    $report.ElapsedSeconds = [Math]::Round($clock.Elapsed.TotalSeconds, 2)
    $reportFullPath = [IO.Path]::GetFullPath($ReportPath)
    [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($reportFullPath)) | Out-Null
    $report | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $reportFullPath -Encoding utf8
}
if ($failure) { Write-Error -Message $report.Error -ErrorAction Continue; exit 1 }
exit 0
