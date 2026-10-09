# Windows PowerShell 5.1 supplies the built-in .NET Framework UI Automation client.
# The caller bounds this all-tools helper to 180 seconds and owns the app lifecycle.
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
    Scope = "Public-testing all-tools UI Automation on a disposable CI desktop; native controls, pointer behavior and empty-provider guidance. No real external player, provider account or payment."
    Stage = "Initialize owned UI Automation client"
    Operation = $null
    PatternRejections = @()
    UIStateSnapshots = @()
    PointerInputMechanism = "SendInput absolute mouse movement over the physical virtual desktop."
    VerifiedPointerMoves = 0
    Actions = @()
    MediaNoPlayerControlsDisabled = $false
    NoAutomaticSampleData = $false
    DockWithinWorkArea = $false
    PointerInsidePreservesExpanded = $false
    UnpinnedSettingsCollapses = $false
    BlankDockFlankCollapses = $false
    RepeatedHoverReopens = $false
    KeyboardEditingGrace = $false
    KeyboardEditingLeaseExpires = $false
    PublicTestingAccess = $false
    ToolsOpened = @()
    ConnectionGuidance = @()
    Screenshots = @()
    CredentialActionAlignment = $false
    ConnectionDraftPreserved = $false
    NotesRoundTrip = $false
    AwakeRoundTrip = $false
    NativeVolumeStatus = "Not run"
    SettingsNoHorizontalOverflow = $false
    SettingsTogglePreservesScroll = $false
    SettingsDraftPreserved = $false
    FeaturedNavigationStartsAtTop = $false
    UnsignedUpdateGuidance = $false
    UpdateActionPreservesSettingsScroll = $false
    UpdateActionCreatedNoDownload = $false
    EvaluationUpdateStatus = "Not run"
    ExplicitSamplePreviewExited = $false
    FocusInitial = $null
    FocusAfterStart = $null
    FocusAfterPause = $null
    FocusAfterReset = $null
    ScratchpadRoundTrip = $false
    ScratchpadSaved = $false
    ScratchpadCleared = $false
    ElapsedSeconds = $null
    ScriptStackTrace = $null
    Error = $null
}

function Assert-Budget {
    if ($clock.Elapsed.TotalSeconds -gt 150) { throw "Public-testing UI interaction exceeded its 150-second operation budget." }
    $process = Get-Process -Id $AppProcessId -ErrorAction Stop
    if ($process.HasExited) { throw "The owned app exited during UI interaction." }
}

function Find-Control([string]$Name, $ControlType, [bool]$RequireEnabled = $true, $Pattern = $null) {
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
            if ($RequireEnabled -and -not $current.IsEnabled) { continue }
            # Settings intentionally exposes both descriptive text and its input
            # with the same name. Only the input can satisfy the required action.
            if ($null -ne $Pattern) {
                $provider = $null
                if (-not $candidate.TryGetCurrentPattern($Pattern, [ref]$provider)) {
                    $description = "'$Name': class=$($current.ClassName), type=$($current.ControlType.ProgrammaticName), required=$($Pattern.ProgrammaticName)"
                    if ($report.PatternRejections -notcontains $description) {
                        $report.PatternRejections = @($report.PatternRejections + $description | Select-Object -Last 12)
                    }
                    continue
                }
            }
            return $candidate
        } catch [System.Windows.Automation.ElementNotAvailableException] { continue }
    }
    return $null
}

function Wait-Control([string]$Name, $ControlType, [bool]$RequireEnabled = $true, $Pattern = $null) {
    $report.Operation = "Wait for '$Name' ($ControlType; required pattern: $Pattern)"
    $wait = [Diagnostics.Stopwatch]::StartNew()
    do {
        $control = Find-Control $Name $ControlType $RequireEnabled $Pattern
        if ($control) { return $control }
        Start-Sleep -Milliseconds 100
    } while ($wait.Elapsed.TotalSeconds -lt 10)
    $state = Get-UiStateSnapshot ("Missing control: " + $Name)
    throw "An accessible usable control did not appear: $Name ($ControlType; required pattern: $Pattern). UI state: $state"
}

function Get-UiStateSnapshot([string]$Reason) {
    $snapshot = [ordered]@{ Reason = $Reason; Window = $null; Pointer = $null; ForegroundWindow = $null; VisibleControls = @(); Error = $null }
    try {
        $bounds = [NotchlingUiSmoke.Native]::WindowBounds([IntPtr]::new($WindowHandle))
        $snapshot.Window = [ordered]@{ Left = $bounds.Left; Top = $bounds.Top; Width = $bounds.Right - $bounds.Left; Height = $bounds.Bottom - $bounds.Top }
        $point = [NotchlingUiSmoke.Native+Point]::new()
        if ([NotchlingUiSmoke.Native]::GetCursorPos([ref]$point)) { $snapshot.Pointer = [ordered]@{ X = $point.X; Y = $point.Y } }
        $snapshot.ForegroundWindow = [NotchlingUiSmoke.Native]::GetForegroundWindow().ToInt64()
        foreach ($element in $root.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)) {
            try {
                $current = $element.Current
                if ($current.IsOffscreen -or $current.BoundingRectangle.IsEmpty -or [string]::IsNullOrWhiteSpace($current.Name)) { continue }
                $name = $current.Name
                if ($name.Length -gt 100) { $name = $name.Substring(0, 100) }
                $snapshot.VisibleControls += "$name [$($current.ControlType.ProgrammaticName); enabled=$($current.IsEnabled)]"
                if ($snapshot.VisibleControls.Count -ge 12) { break }
            } catch [System.Windows.Automation.ElementNotAvailableException] { continue }
        }
    } catch { $snapshot.Error = $_.Exception.Message }
    $report.UIStateSnapshots += [pscustomobject]$snapshot
    return $snapshot | ConvertTo-Json -Depth 4 -Compress
}

function Invoke-Button([string]$Name) {
    $button = Wait-Control $Name ([System.Windows.Automation.ControlType]::Button) $true ([System.Windows.Automation.InvokePattern]::Pattern)
    $pattern = $button.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
    $report.Operation = "Invoke button '$Name'"
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
    $control = Wait-Control $Name $null $true ([System.Windows.Automation.TogglePattern]::Pattern)
    $pattern = $control.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern)
    $expected = if ($On) { [System.Windows.Automation.ToggleState]::On } else { [System.Windows.Automation.ToggleState]::Off }
    $report.Operation = "Set toggle '$Name' to $expected"
    if ($pattern.Current.ToggleState -ne $expected) { $pattern.Toggle() }
    $wait = [Diagnostics.Stopwatch]::StartNew()
    do {
        Assert-Budget
        $control = Find-Control $Name $null $true ([System.Windows.Automation.TogglePattern]::Pattern)
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
    # SetCursorPos guarantees placement without proving that the hosted WinUI
    # input site received pointer input. Inject actual mouse input instead,
    # then require Windows to report the exact requested physical pixel.
    [NotchlingUiSmoke.Native]::MoveCursorWithInput($X, $Y)
    $wait = [Diagnostics.Stopwatch]::StartNew()
    do {
        Assert-Budget
        $actual = [NotchlingUiSmoke.Native+Point]::new()
        if ([NotchlingUiSmoke.Native]::GetCursorPos([ref]$actual) -and $actual.X -eq $X -and $actual.Y -eq $Y) {
            $report.VerifiedPointerMoves++
            return
        }
        Start-Sleep -Milliseconds 10
    } while ($wait.Elapsed.TotalSeconds -lt 1)
    $state = Get-UiStateSnapshot "Injected mouse did not reach requested pixel ($X, $Y)"
    throw "The disposable desktop did not deliver injected mouse input to the requested regression-test location. UI state: $state"
}

function Visit-Panel {
    $bounds = [NotchlingUiSmoke.Native]::WindowBounds([IntPtr]::new($WindowHandle))
    Move-Cursor ([int](($bounds.Left + $bounds.Right) / 2)) ($bounds.Top + [int][Math]::Round(30 * $displayScale))
    Start-Sleep -Milliseconds 250
}

function Move-Outside {
    $work = [NotchlingUiSmoke.Native]::WorkArea([IntPtr]::new($WindowHandle))
    $bounds = [NotchlingUiSmoke.Native]::WindowBounds([IntPtr]::new($WindowHandle))
    $outsideX = $work.Right - 8; $outsideY = $work.Bottom - 8
    if ($outsideX -ge $bounds.Left -and $outsideX -lt $bounds.Right -and
        $outsideY -ge $bounds.Top -and $outsideY -lt $bounds.Bottom) {
        throw "The disposable desktop has no outside-window pointer position for this regression check."
    }
    Move-Cursor $outsideX $outsideY
}

function Wait-Collapsed {
    $wait = [Diagnostics.Stopwatch]::StartNew()
    $settledSamples = 0
    do {
        Assert-Budget
        $compact = Find-Control "Open Notchling" ([System.Windows.Automation.ControlType]::Button) $true ([System.Windows.Automation.InvokePattern]::Pattern)
        if ($compact) {
            $bounds = [NotchlingUiSmoke.Native]::WindowBounds([IntPtr]::new($WindowHandle))
            # During collapse the compact button already exists, but its HWND
            # can still be 90 DIP high. A hover at that moving rectangle's center
            # would land below the final 40-DIP island and correctly fail to open.
            if (($bounds.Bottom - $bounds.Top) -le 42 * $displayScale) {
                $settledSamples++
                if ($settledSamples -ge 2) { return }
            } else { $settledSamples = 0 }
        } else {
            $settledSamples = 0
        }
        Start-Sleep -Milliseconds 100
    } while ($wait.Elapsed.TotalSeconds -lt 9)
    $state = Get-UiStateSnapshot "Unpinned collapse did not settle"
    throw "The unpinned notch remained expanded after the pointer left and its keyboard editing lease expired. UI state: $state"
}

function Find-AutomationId([string]$Id, $Pattern = $null) {
    Assert-Budget
    $condition = [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::AutomationIdProperty, $Id)
    foreach ($candidate in $root.FindAll([System.Windows.Automation.TreeScope]::Descendants, $condition)) {
        try {
            $current = $candidate.Current
            if ($current.IsOffscreen -or -not $current.IsEnabled -or $current.BoundingRectangle.IsEmpty) { continue }
            if ($null -ne $Pattern) {
                $provider = $null
                if (-not $candidate.TryGetCurrentPattern($Pattern, [ref]$provider)) { continue }
            }
            return $candidate
        } catch [System.Windows.Automation.ElementNotAvailableException] { continue }
    }
    return $null
}

function Open-CatalogTool([string]$Module) {
    Invoke-Button "All tools"
    $report.Operation = "Open available catalog tool '$Module'"
    $content = Wait-Control "Notchling tool content" $null $true ([System.Windows.Automation.ScrollPattern]::Pattern)
    $scroll = $content.GetCurrentPattern([System.Windows.Automation.ScrollPattern]::Pattern)
    if ($scroll.Current.HorizontallyScrollable) { throw "The tool catalog exposes horizontal scrolling." }
    $tool = $null
    for ($percent = 0; $percent -le 100; $percent += 10) {
        if ($scroll.Current.VerticallyScrollable) {
            $scroll.SetScrollPercent([System.Windows.Automation.ScrollPattern]::NoScroll, $percent)
            Start-Sleep -Milliseconds 70
        }
        $tool = Find-AutomationId ("Tool" + $Module) ([System.Windows.Automation.InvokePattern]::Pattern)
        if ($tool) { break }
    }
    if (-not $tool) { throw "An unlocked public-testing catalog tool could not be reached: $Module." }
    if ($tool.Current.Name -match '(?i)locked|requires premium|opens plan settings') {
        throw "A public-testing tool still exposes a paywall: $Module ($($tool.Current.Name))."
    }
    $tool.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    $report.Actions += "Open catalog tool: $Module"
    Start-Sleep -Milliseconds 150
    if ($Module -in @("Home", "Media", "Revenue", "Analytics", "Coding", "Calendar", "Weather", "Focus")) {
        Assert-FeaturedStartsAtTop
    }
    Assert-NoAutomaticSampleData
    if (Find-Control "Notchling error" $null $false) { throw "Opening public-testing tool '$Module' displayed an application error." }
}

function Assert-FeaturedStartsAtTop {
    $wait = [Diagnostics.Stopwatch]::StartNew()
    do {
        $content = Find-AutomationId "ModuleScroll" ([System.Windows.Automation.ScrollPattern]::Pattern)
        if ($content) {
            $scroll = $content.GetCurrentPattern([System.Windows.Automation.ScrollPattern]::Pattern)
            if ($scroll.Current.HorizontallyScrollable) { throw "A featured tool exposes horizontal scrolling." }
            if (-not $scroll.Current.VerticallyScrollable -or $scroll.Current.VerticalScrollPercent -le .1) { return }
        }
        Start-Sleep -Milliseconds 100
    } while ($wait.Elapsed.TotalSeconds -lt 2)
    throw "Navigating to a featured tool retained another tool's vertical scroll offset or omitted its accessible viewport."
}

function Assert-CredentialActions([string]$Name) {
    $input = Scroll-ToSetting $Name ([System.Windows.Automation.ControlType]::Edit)
    $save = Scroll-ToSetting ("Save " + $Name) ([System.Windows.Automation.ControlType]::Button) ([System.Windows.Automation.InvokePattern]::Pattern)
    $remove = Scroll-ToSetting ("Remove " + $Name) ([System.Windows.Automation.ControlType]::Button) ([System.Windows.Automation.InvokePattern]::Pattern)
    # Re-find after scrolling so the rectangles describe one current viewport.
    $input = Find-Control $Name ([System.Windows.Automation.ControlType]::Edit)
    $save = Find-Control ("Save " + $Name) ([System.Windows.Automation.ControlType]::Button)
    $remove = Find-Control ("Remove " + $Name) ([System.Windows.Automation.ControlType]::Button)
    if (-not $input -or -not $save -or -not $remove) { throw "A credential input and its action pair could not fit together: $Name." }
    $entry = $input.Current.BoundingRectangle; $saveBounds = $save.Current.BoundingRectangle; $removeBounds = $remove.Current.BoundingRectangle
    if ($saveBounds.Height -gt 48 * $displayScale -or $removeBounds.Height -gt 48 * $displayScale) {
        throw "Credential actions are oversized instead of compact form controls: $Name."
    }
    if ([Math]::Abs($saveBounds.Top - $removeBounds.Top) -gt 2 * $displayScale -or
        [Math]::Abs($saveBounds.Height - $removeBounds.Height) -gt 2 * $displayScale) {
        throw "The credential Save / Remove action pair is not vertically aligned: $Name."
    }
    $sameLine = [Math]::Abs(($entry.Top + $entry.Height / 2) - ($saveBounds.Top + $saveBounds.Height / 2)) -le 2 * $displayScale
    $stacked = $saveBounds.Top -ge $entry.Bottom - 2 * $displayScale
    if (-not $sameLine -and -not $stacked) { throw "Credential actions neither align with the input nor stack cleanly beneath it: $Name." }
    Assert-HorizontalBounds
}

function Wait-ModuleControl([string]$Name, $ControlType, [bool]$RequireEnabled = $true, $Pattern = $null) {
    $control = Find-Control $Name $ControlType $RequireEnabled $Pattern
    if ($control) { return $control }
    $scrollElement = Find-AutomationId "ModuleScroll" ([System.Windows.Automation.ScrollPattern]::Pattern)
    if (-not $scrollElement) { $scrollElement = Find-Control "Notchling tool content" $null $true ([System.Windows.Automation.ScrollPattern]::Pattern) }
    if ($scrollElement) {
        $scroll = $scrollElement.GetCurrentPattern([System.Windows.Automation.ScrollPattern]::Pattern)
        if ($scroll.Current.HorizontallyScrollable) { throw "The current tool exposes horizontal scrolling: $Name." }
        for ($percent = 0; $percent -le 100; $percent += 10) {
            if ($scroll.Current.VerticallyScrollable) {
                $scroll.SetScrollPercent([System.Windows.Automation.ScrollPattern]::NoScroll, $percent)
                Start-Sleep -Milliseconds 70
            }
            $control = Find-Control $Name $ControlType $RequireEnabled $Pattern
            if ($control) { return $control }
        }
    }
    return Wait-Control $Name $ControlType $RequireEnabled $Pattern
}

function Read-Workspace {
    $workspaceFile = Join-Path ([Environment]::GetFolderPath("LocalApplicationData")) "Notch/workspace.json"
    if (Test-Path -LiteralPath $workspaceFile -PathType Leaf) {
        try { return Get-Content -LiteralPath $workspaceFile -Raw | ConvertFrom-Json } catch { }
    }
    return $null
}

function Save-OwnedScreenshot([string]$Name) {
    $bitmap = $null; $graphics = $null
    try {
        Assert-Budget
        Add-Type -AssemblyName System.Drawing
        $bounds = [NotchlingUiSmoke.Native]::WindowBounds([IntPtr]::new($WindowHandle))
        $width = $bounds.Right - $bounds.Left; $height = $bounds.Bottom - $bounds.Top
        if ($width -le 0 -or $height -le 0 -or $width -gt 4096 -or $height -gt 4096 -or [long]$width * $height -gt 16777216) {
            throw "The owned HWND rectangle exceeds the screenshot memory bound."
        }
        $directory = Join-Path ([IO.Path]::GetDirectoryName([IO.Path]::GetFullPath($ReportPath))) "screenshots"
        [IO.Directory]::CreateDirectory($directory) | Out-Null
        $destination = Join-Path $directory ($Name + ".png")
        $bitmap = [Drawing.Bitmap]::new($width, $height, [Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $graphics = [Drawing.Graphics]::FromImage($bitmap)
        $graphics.CopyFromScreen($bounds.Left, $bounds.Top, 0, 0, $bitmap.Size, [Drawing.CopyPixelOperation]::SourceCopy)
        $graphics.Dispose(); $graphics = $null
        $bitmap.Save($destination, [Drawing.Imaging.ImageFormat]::Png)
        # A cloud desktop can produce uniform black captures. Retain the file but
        # report that limitation instead of treating it as visual qualification.
        $colors = [Collections.Generic.HashSet[int]]::new()
        for ($row = 0; $row -lt 16; $row++) {
            for ($column = 0; $column -lt 16; $column++) {
                $sampleX = [int][Math]::Min($width - 1, [Math]::Floor(($column + .5) * $width / 16))
                $sampleY = [int][Math]::Min($height - 1, [Math]::Floor(($row + .5) * $height / 16))
                $colors.Add($bitmap.GetPixel($sampleX, $sampleY).ToArgb()) | Out-Null
            }
        }
        $status = if ($colors.Count -le 1) { "Unavailable as visual evidence: sampled capture is uniform." } else { "PNG captured; visual content requires review." }
        $report.Screenshots += [pscustomobject]@{ Name = $Name; Path = $destination; Width = $width; Height = $height; Status = $status }
    } catch {
        # Functional assertions remain authoritative if a hosted desktop denies
        # screen copying or has no usable drawing surface.
        $report.Screenshots += [pscustomobject]@{ Name = $Name; Path = $null; Width = $null; Height = $null; Status = "Unavailable: $($_.Exception.Message)" }
    } finally {
        if ($graphics) { $graphics.Dispose() }
        if ($bitmap) { $bitmap.Dispose() }
    }
}

function Get-SettingsScroll {
    $content = Wait-Control "Notchling settings content" $null $true ([System.Windows.Automation.ScrollPattern]::Pattern)
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

function Scroll-ToSetting([string]$Name, $ControlType, $Pattern = $null) {
    $report.Operation = "Scroll to '$Name' ($ControlType; required pattern: $Pattern)"
    $control = Find-Control $Name $ControlType $true $Pattern
    if ($control) { return $control }
    for ($percent = 0; $percent -le 100; $percent += 10) {
        Scroll-Settings $percent
        $control = Find-Control $Name $ControlType $true $Pattern
        if ($control) { return $control }
    }
    throw "A Settings control could not be reached by vertical scrolling: $Name (required pattern: $Pattern)."
}

function Scroll-ToSettingId([string]$Id) {
    $control = Find-AutomationId $Id
    if ($control) { return $control }
    for ($percent = 0; $percent -le 100; $percent += 10) {
        Scroll-Settings $percent
        $control = Find-AutomationId $Id
        if ($control) { return $control }
    }
    throw "An accessible connection-status row could not be reached: $Id."
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
                throw "Visible tool content exceeds the native notch width: '$($current.Name)' ($($rectangle.Left)..$($rectangle.Right), notch $($bounds.Left)..$($bounds.Right))."
            }
        } catch [System.Windows.Automation.ElementNotAvailableException] { continue }
    }
}

function Get-UpdateDownloadSnapshot {
    # Inspect only the app-owned updater directory on this disposable desktop.
    # The unsigned path must return before creating an installer download.
    $updateDirectory = Join-Path ([IO.Path]::GetTempPath()) "Notchling.Update"
    if (Test-Path -LiteralPath $updateDirectory -PathType Container) {
        Get-ChildItem -LiteralPath $updateDirectory -Recurse -File | ForEach-Object {
            "$($_.FullName)|$($_.Length)|$($_.LastWriteTimeUtc.Ticks)"
        } | Sort-Object
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
    foreach ($name in @("Home", "Media", "Focus", "Calendar", "Shelf", "Clipboard", "All tools", "Settings", "Keep Notchling expanded")) {
        $condition = [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::NameProperty, $name)
        $dockControl = $null
        foreach ($candidate in $root.FindAll([System.Windows.Automation.TreeScope]::Descendants, $condition)) {
            try {
                $current = $candidate.Current
                if (-not $current.IsOffscreen -and $current.IsEnabled -and -not $current.BoundingRectangle.IsEmpty -and
                    $current.BoundingRectangle.Top -ge $bounds.Bottom - $dockHeight) { $dockControl = $candidate; break }
            } catch [System.Windows.Automation.ElementNotAvailableException] { continue }
        }
        if (-not $dockControl) { throw "An enabled public-testing dock control is absent from the visible bottom dock: $name." }
        $rectangle = $dockControl.Current.BoundingRectangle
        if ($rectangle.Left -lt $bounds.Left - 2 -or $rectangle.Right -gt $bounds.Right + 2 -or
            $rectangle.Top -lt $bounds.Top - 2 -or $rectangle.Bottom -gt $bounds.Bottom + 2) {
            throw "An accessible public-testing dock control is clipped outside the native window: $name."
        }
    }
}

function Read-FocusClock {
    Assert-Budget
    # Public testing exposes four clocks; reading the first MM:SS text could
    # accidentally accept a countdown or stopwatch as the Pomodoro timer.
    $clockText = Find-AutomationId "FocusTimeText"
    if ($clockText -and $clockText.Current.Name -match '^\d{2}:\d{2}$') { return $clockText.Current.Name }
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
        [StructLayout(LayoutKind.Sequential)] private struct MouseInput { public int X, Y; public uint Data, Flags, Time; public IntPtr Extra; }
        [StructLayout(LayoutKind.Sequential)] private struct KeyboardInput { public ushort Key, Scan; public uint Flags, Time; public IntPtr Extra; }
        [StructLayout(LayoutKind.Explicit)] private struct InputUnion {
            [FieldOffset(0)] public MouseInput Mouse;
            [FieldOffset(0)] public KeyboardInput Keyboard;
        }
        [StructLayout(LayoutKind.Sequential)] private struct Input { public uint Type; public InputUnion Data; }
        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out Rect bounds);
        [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);
        [DllImport("user32.dll", EntryPoint="GetMonitorInfoW")] private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo information);
        [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
        [DllImport("user32.dll")] public static extern bool GetCursorPos(out Point point);
        [DllImport("user32.dll")] public static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
        [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr window);
        [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr window);
        [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
        [DllImport("user32.dll", SetLastError=true)] private static extern uint SendInput(uint count, Input[] inputs, int size);
        public static int NormalizeMouseCoordinate(int coordinate, int origin, int extent) {
            var offset = (long)coordinate - origin;
            if (extent <= 0 || offset < 0 || offset >= extent)
                throw new ArgumentOutOfRangeException("coordinate", "The requested pixel is outside the physical virtual desktop.");
            // Windows maps normalized input into pixel intervals. Aim at the
            // middle of this pixel's interval, including negative monitor origins.
            return Math.Max(0, Math.Min(65535, (int)Math.Floor((offset + .5) * 65536 / extent)));
        }
        public static void MoveCursorWithInput(int x, int y) {
            var left = GetSystemMetrics(76); var top = GetSystemMetrics(77);
            var width = GetSystemMetrics(78); var height = GetSystemMetrics(79);
            var inputs = new Input[1];
            inputs[0].Type = 0;
            inputs[0].Data.Mouse.X = NormalizeMouseCoordinate(x, left, width);
            inputs[0].Data.Mouse.Y = NormalizeMouseCoordinate(y, top, height);
            inputs[0].Data.Mouse.Flags = 0x0001 | 0x8000 | 0x4000; // MOVE | ABSOLUTE | VIRTUALDESK
            if (SendInput(1, inputs, Marshal.SizeOf(typeof(Input))) != 1)
                throw new InvalidOperationException("Windows rejected mouse input on the owned disposable test desktop (Win32 " + Marshal.GetLastWin32Error() + ").");
        }
        public static void TypeCharacter(char value) {
            var inputs = new Input[2];
            inputs[0].Type = inputs[1].Type = 1;
            inputs[0].Data.Keyboard.Scan = inputs[1].Data.Keyboard.Scan = value;
            inputs[0].Data.Keyboard.Flags = 4; inputs[1].Data.Keyboard.Flags = 6;
            if (SendInput(2, inputs, Marshal.SizeOf(typeof(Input))) != 2)
                throw new InvalidOperationException("Windows rejected keyboard input on the owned disposable test desktop.");
        }
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

    $report.Stage = "Open and pin the native notch"
    Invoke-Button "Open Notchling"
    # Match the unique accessible name, then require its actual Toggle pattern.
    $pin = Wait-Control "Keep Notchling expanded" $null $true ([System.Windows.Automation.TogglePattern]::Pattern)
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
    $report.Stage = "Verify startup uses real data and the public-testing dock fits"
    Wait-Control "Notchling" ([System.Windows.Automation.ControlType]::Text) | Out-Null
    Assert-NoAutomaticSampleData
    $report.NoAutomaticSampleData = $true
    Assert-DockBounds
    $report.DockWithinWorkArea = $true
    $report.Actions += "Verified real-data startup and in-bounds public-testing dock"

    # Unpinned navigation must stay expanded while the cursor crosses children and
    # the native gap above the dock. Neither point is outside the owned HWND.
    $report.Stage = "Unpinned pointer movement inside the native surface"
    Set-Toggle "Keep Notchling expanded" $false
    $bounds = [NotchlingUiSmoke.Native]::WindowBounds([IntPtr]::new($WindowHandle))
    $insideX = [int](($bounds.Left + $bounds.Right) / 2)
    $displayScale = [NotchlingUiSmoke.Native]::GetDpiForWindow([IntPtr]::new($WindowHandle)) / 96
    if ($displayScale -le 0) { throw "The owned HWND did not expose a valid display DPI." }
    Move-Cursor $insideX ($bounds.Top + [int][Math]::Round(50 * $displayScale))
    Start-Sleep -Milliseconds 100
    Move-Cursor $insideX ($bounds.Bottom - [int][Math]::Round(65 * $displayScale))
    Start-Sleep -Milliseconds 1600
    Wait-Control "Notchling" ([System.Windows.Automation.ControlType]::Text) | Out-Null
    $report.PointerInsidePreservesExpanded = $true
    $report.Actions += "Verified pointer inside the native dock gap preserves expansion"

    $report.Stage = "Unpinned Settings collapses after a real pointer visit and leave"
    Invoke-Button "Settings"
    Get-SettingsScroll | Out-Null
    Wait-Control ("Public testing " + [char]0x00B7 + " All tools unlocked") ([System.Windows.Automation.ControlType]::Text) | Out-Null
    $report.PublicTestingAccess = $true
    Visit-Panel
    Move-Outside
    Wait-Collapsed
    $report.UnpinnedSettingsCollapses = $true
    $report.Actions += "Verified active unpinned Settings collapses after pointer leave"

    # The HWND contains transparent bottom flanks. Those pixels must not keep a
    # panel open simply because they lie within the full rectangular HWND bounds.
    $report.Stage = "Transparent dock flank does not retain expansion"
    Invoke-Button "Open Notchling"
    Visit-Panel
    $bounds = [NotchlingUiSmoke.Native]::WindowBounds([IntPtr]::new($WindowHandle))
    Move-Cursor ($bounds.Left + 2) ($bounds.Bottom - [int][Math]::Round(35 * $displayScale))
    Wait-Collapsed
    $report.BlankDockFlankCollapses = $true
    $report.Actions += "Verified transparent dock flank counts as pointer leave"

    $report.Stage = "Repeated actual-pointer hover opens and leaves without sticking"
    for ($cycle = 0; $cycle -lt 2; $cycle++) {
        $compact = Wait-Control "Open Notchling" ([System.Windows.Automation.ControlType]::Button) $true ([System.Windows.Automation.InvokePattern]::Pattern)
        $compactBounds = $compact.Current.BoundingRectangle
        Move-Cursor ([int]($compactBounds.Left + $compactBounds.Width / 2)) ([int]($compactBounds.Top + $compactBounds.Height / 2))
        Get-SettingsScroll | Out-Null
        Visit-Panel
        Move-Outside
        Wait-Collapsed
    }
    $report.RepeatedHoverReopens = $true
    $report.Actions += "Verified two real-pointer hover open / leave cycles"
    Invoke-Button "Open Notchling"
    Set-Toggle "Keep Notchling expanded" $true

    # Check each vertical section: disabled horizontal scrolling alone would not
    # detect oversized controls in the old infinitely measured row layout.
    $report.Stage = "Settings vertical layout and horizontal overflow"
    for ($percent = 0; $percent -le 100; $percent += 10) {
        Scroll-Settings $percent
        Assert-HorizontalBounds
    }
    $report.SettingsNoHorizontalOverflow = $true
    $report.Actions += "Verified Settings has no horizontal overflow at eleven vertical positions"
    $report.Stage = "Settings toggle retains scroll and unapplied number draft"
    $number = Scroll-ToSetting "Pomodoro length (minutes)" $null ([System.Windows.Automation.RangeValuePattern]::Pattern)
    $numberValue = $number.GetCurrentPattern([System.Windows.Automation.RangeValuePattern]::Pattern)
    $initialMinutes = $numberValue.Current.Value
    $numberValue.SetValue(37)
    $motion = Scroll-ToSetting "Reduce motion" $null ([System.Windows.Automation.TogglePattern]::Pattern)
    $motionValue = $motion.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern)
    $initialMotion = $motionValue.Current.ToggleState -eq [System.Windows.Automation.ToggleState]::On
    $scrollBefore = (Get-SettingsScroll).Current.VerticalScrollPercent
    Set-Toggle "Reduce motion" (-not $initialMotion)
    Start-Sleep -Milliseconds 700
    $scrollAfter = (Get-SettingsScroll).Current.VerticalScrollPercent
    if ([Math]::Abs($scrollAfter - $scrollBefore) -gt 1) { throw "Changing a Settings toggle reset the vertical scroll position." }
    Set-Toggle "Reduce motion" $initialMotion
    $report.SettingsTogglePreservesScroll = $true
    $report.Actions += "Verified Settings toggle preserves vertical scroll position"
    Invoke-Button "Home"
    Invoke-Button "Settings"
    $number = Scroll-ToSetting "Pomodoro length (minutes)" $null ([System.Windows.Automation.RangeValuePattern]::Pattern)
    $numberValue = $number.GetCurrentPattern([System.Windows.Automation.RangeValuePattern]::Pattern)
    if ($numberValue.Current.Value -ne 37) { throw "An unapplied Settings draft was lost when navigating away and back." }
    $numberValue.SetValue($initialMinutes)
    $report.SettingsDraftPreserved = $true
    $report.Actions += "Verified unapplied Settings number draft survives navigation"

    $report.Stage = "Compact credential controls, draft preservation and real keyboard editing lease"
    $connections = Scroll-ToSetting "Provider connections" $null ([System.Windows.Automation.ExpandCollapsePattern]::Pattern)
    $connections.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Expand()
    Assert-CredentialActions "Stripe read-only key"
    Assert-CredentialActions "Analytics bearer token"
    $report.CredentialActionAlignment = $true
    Save-OwnedScreenshot "settings-connections"
    $endpoint = Scroll-ToSetting "HTTPS analytics endpoint" ([System.Windows.Automation.ControlType]::Edit) ([System.Windows.Automation.ValuePattern]::Pattern)
    $endpointValue = $endpoint.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern)
    $initialEndpoint = $endpointValue.Current.Value
    $endpointDraft = "https://example.invalid/notchling-ui-draft"
    $endpointValue.SetValue($endpointDraft)
    Invoke-Button "Home"
    Invoke-Button "Settings"
    $endpoint = Scroll-ToSetting "HTTPS analytics endpoint" ([System.Windows.Automation.ControlType]::Edit) ([System.Windows.Automation.ValuePattern]::Pattern)
    if ($endpoint.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value -ne $endpointDraft) {
        throw "Navigating away discarded the unapplied analytics endpoint draft."
    }
    $report.ConnectionDraftPreserved = $true
    Set-Toggle "Keep Notchling expanded" $false
    Visit-Panel
    [NotchlingUiSmoke.Native]::SetForegroundWindow([IntPtr]::new($WindowHandle)) | Out-Null
    $endpoint.SetFocus()
    [NotchlingUiSmoke.Native]::TypeCharacter('x')
    $typedWait = [Diagnostics.Stopwatch]::StartNew()
    do {
        Assert-Budget
        $endpoint = Find-Control "HTTPS analytics endpoint" ([System.Windows.Automation.ControlType]::Edit) $true ([System.Windows.Automation.ValuePattern]::Pattern)
        if ($endpoint -and $endpoint.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value -ne $endpointDraft) { break }
        if ($typedWait.Elapsed.TotalSeconds -ge 3) { throw "The disposable desktop did not deliver real keyboard input to the owned endpoint editor." }
        Start-Sleep -Milliseconds 100
    } while ($true)
    Move-Outside
    Start-Sleep -Milliseconds 1000
    Get-SettingsScroll | Out-Null
    $report.KeyboardEditingGrace = $true
    Wait-Collapsed
    $report.KeyboardEditingLeaseExpires = $true
    Invoke-Button "Open Notchling"
    Set-Toggle "Keep Notchling expanded" $true
    $endpoint = Scroll-ToSetting "HTTPS analytics endpoint" ([System.Windows.Automation.ControlType]::Edit) ([System.Windows.Automation.ValuePattern]::Pattern)
    $endpoint.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($initialEndpoint)
    $report.Actions += "Verified compact credential alignment, endpoint draft retention, actual typing grace and eventual collapse after editing stops"

    $report.Stage = "Honest native and unconfigured-provider connection diagnostics"
    $connectionStatus = Scroll-ToSetting "Connection status" $null ([System.Windows.Automation.ExpandCollapsePattern]::Pattern)
    $connectionStatus.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Expand()
    Scroll-ToSetting "Check connections" ([System.Windows.Automation.ControlType]::Button) ([System.Windows.Automation.InvokePattern]::Pattern) | Out-Null
    Invoke-Button "Check connections"
    Start-Sleep -Milliseconds 300
    Wait-Control "Check connections" ([System.Windows.Automation.ControlType]::Button) $true ([System.Windows.Automation.InvokePattern]::Pattern) | Out-Null
    foreach ($provider in @("media", "audio", "clipboard", "stripe", "analytics", "calendar", "coding", "weather")) {
        $state = (Scroll-ToSettingId ("ConnectionState" + $provider)).Current.Name
        $detail = (Scroll-ToSettingId ("ConnectionDetail" + $provider)).Current.Name
        if ($state -notin @("Setup needed", "Ready", "Connected", "Unavailable", "Needs attention")) {
            throw "Connection diagnostics did not complete with a usable state: $provider ($state)."
        }
        if ([string]::IsNullOrWhiteSpace($detail)) { throw "Connection diagnostics omitted actionable details: $provider." }
        if ($provider -in @("stripe", "analytics", "calendar", "coding", "weather") -and $state -ne "Setup needed") {
            throw "The empty-provider fixture claims a configured connection without credentials or imports: $provider ($state)."
        }
        $report.ConnectionGuidance += [pscustomobject]@{ Provider = $provider; State = $state; Detail = $detail }
    }
    if (Find-Control "Notchling error" $null $false) { throw "Missing optional connections produced an application error instead of setup guidance." }
    $report.Actions += "Verified eight connection diagnostics; absent provider credentials or imports remain setup guidance"

    $report.Stage = "Unsigned evaluation update guidance without error or scroll reset"
    # A PowerShell 7 CI parent can pass incompatible module paths to this 5.1
    # helper. Resolve certificate inspection from the running desktop shell.
    Import-Module (Join-Path $PSHOME 'Modules/Microsoft.PowerShell.Security/Microsoft.PowerShell.Security.psd1') -ErrorAction Stop
    $installedSignature = Get-AuthenticodeSignature -LiteralPath $process.Path
    if ($installedSignature.Status -eq [System.Management.Automation.SignatureStatus]::Valid) {
        # A future signed smoke run must not exercise the stable update channel.
        # Record that this evaluation-specific regression was not performed.
        $report.EvaluationUpdateStatus = "Skipped: installed application is signed; this regression covers unsigned evaluation builds."
    } else {
        if ($installedSignature.Status -ne [System.Management.Automation.SignatureStatus]::NotSigned) {
            throw "This evaluation-update regression requires the unsigned CI application."
        }
        $support = Scroll-ToSetting "Updates and troubleshooting" $null ([System.Windows.Automation.ExpandCollapsePattern]::Pattern)
        $supportExpansion = $support.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern)
        $supportExpansion.Expand()
        $updateButton = Scroll-ToSetting "Check for updates" ([System.Windows.Automation.ControlType]::Button) ([System.Windows.Automation.InvokePattern]::Pattern)
        Start-Sleep -Milliseconds 300
        $support = Wait-Control "Updates and troubleshooting" $null $true ([System.Windows.Automation.ExpandCollapsePattern]::Pattern)
        $supportTop = $support.Current.BoundingRectangle.Top
        $downloadsBefore = @(Get-UpdateDownloadSnapshot)
        if (Find-Control "Notchling error" $null $false) { throw "The evaluation-update fixture already has a visible application error." }
        Invoke-Button "Check for updates"
        $manualUpdateGuidance = "This evaluation build uses manual updates. Open Release page to download the latest installer."
        # Check the actual Settings live region, rather than accepting only the
        # unrelated footer that exposes the same informational status.
        Wait-Control $manualUpdateGuidance ([System.Windows.Automation.ControlType]::Text) | Out-Null
        $updateStatus = $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants,
            [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::AutomationIdProperty, "UpdateStatus"))
        if ($null -eq $updateStatus -or $updateStatus.Current.IsOffscreen -or $updateStatus.Current.Name -ne $manualUpdateGuidance) {
            throw "The visible Settings live region did not expose the manual-update guidance."
        }
        Wait-Control "Release page" ([System.Windows.Automation.ControlType]::Button) | Out-Null
        if (Find-Control "Notchling error" $null $false) { throw "An unsigned evaluation update check displayed a red application error instead of manual-update guidance." }
        $report.UnsignedUpdateGuidance = $true
        Wait-Control "Check for updates" ([System.Windows.Automation.ControlType]::Button) $true ([System.Windows.Automation.InvokePattern]::Pattern) | Out-Null
        Start-Sleep -Milliseconds 300
        $support = Wait-Control "Updates and troubleshooting" $null $true ([System.Windows.Automation.ExpandCollapsePattern]::Pattern)
        # Adding guidance can change content height and therefore scroll percent.
        # The preceding section header must retain its viewport location.
        if ([Math]::Abs($support.Current.BoundingRectangle.Top - $supportTop) -gt 2 * $displayScale) {
            throw "Checking for evaluation updates reset the Settings viewport."
        }
        $report.UpdateActionPreservesSettingsScroll = $true
        $downloadsAfter = @(Get-UpdateDownloadSnapshot)
        if ([string]::Join("`n", [string[]]$downloadsBefore) -ne [string]::Join("`n", [string[]]$downloadsAfter)) {
            throw "An unsigned evaluation update check created or changed an installer download."
        }
        $report.UpdateActionCreatedNoDownload = $true
        $report.EvaluationUpdateStatus = "Passed: unsigned evaluation action provides manual-update guidance without an error or installer download."
        $report.Actions += "Verified unsigned update guidance, no application error, retained Settings viewport, and no installer download"
    }

    $report.Stage = "Explicit sample-data preview and exit to real data"
    $preview = Scroll-ToSetting "Sample-data preview" $null ([System.Windows.Automation.ExpandCollapsePattern]::Pattern)
    $expander = $preview.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern)
    $expander.Expand()
    Scroll-ToSetting "Preview sample data for this session" $null ([System.Windows.Automation.TogglePattern]::Pattern) | Out-Null
    Set-Toggle "Preview sample data for this session" $true
    Wait-Control ("Preview " + [char]0x00B7 + " sample data") ([System.Windows.Automation.ControlType]::Text) | Out-Null
    Invoke-Button "Exit sample data preview"
    $previewWait = [Diagnostics.Stopwatch]::StartNew()
    while (Find-Control "Exit sample data preview" ([System.Windows.Automation.ControlType]::Button) $false) {
        Assert-Budget
        if ($previewWait.Elapsed.TotalSeconds -ge 4) { throw "Exiting sample-data preview did not remove its visible preview strip." }
        Start-Sleep -Milliseconds 100
    }
    Assert-NoAutomaticSampleData
    $report.ExplicitSamplePreviewExited = $true
    $report.Actions += "Verified work-area dock bounds, vertical layout, toggle scroll position, unapplied draft retention, and explicit preview exit"

    $report.Stage = "All twenty-one tools open without public-testing paywalls"
    Invoke-Button "All tools"
    Save-OwnedScreenshot "all-tools"
    $toolCases = @(
        @{ Module = "Home"; Name = "Notchling"; Type = [System.Windows.Automation.ControlType]::Text },
        @{ Module = "Media"; Name = "Nothing playing"; Type = [System.Windows.Automation.ControlType]::Text },
        @{ Module = "Revenue"; Name = "Connect a payment provider"; Type = [System.Windows.Automation.ControlType]::Text },
        @{ Module = "Analytics"; Name = "Your website"; Type = [System.Windows.Automation.ControlType]::Text },
        @{ Module = "Coding"; Name = "Connect your local usage logs"; Type = [System.Windows.Automation.ControlType]::Text },
        @{ Module = "Calendar"; Name = "Previous month"; Type = [System.Windows.Automation.ControlType]::Button },
        @{ Module = "Weather"; Name = "Weather"; Type = [System.Windows.Automation.ControlType]::Text },
        @{ Module = "Focus"; Name = "Start / pause Pomodoro"; Type = [System.Windows.Automation.ControlType]::Button },
        @{ Module = "Shelf"; Name = "Choose files"; Type = [System.Windows.Automation.ControlType]::Button },
        @{ Module = "Clipboard"; Name = "Enable clipboard history"; Type = [System.Windows.Automation.ControlType]::Button },
        @{ Module = "Servers"; Name = "Refresh ports"; Type = [System.Windows.Automation.ControlType]::Button },
        @{ Module = "System"; Name = "CPU"; Type = [System.Windows.Automation.ControlType]::Text },
        @{ Module = "ScreenTime"; Name = "Screen time"; Type = [System.Windows.Automation.ControlType]::Text },
        @{ Module = "Notes"; Name = "Save note"; Type = [System.Windows.Automation.ControlType]::Button },
        @{ Module = "Scratchpad"; Name = "Scratchpad"; Type = [System.Windows.Automation.ControlType]::Edit },
        @{ Module = "Files"; Name = "Documents"; Type = [System.Windows.Automation.ControlType]::Button },
        @{ Module = "Links"; Name = "Add"; Type = [System.Windows.Automation.ControlType]::Button },
        @{ Module = "Emoji"; Name = "Open Windows emoji picker"; Type = [System.Windows.Automation.ControlType]::Button },
        @{ Module = "Sounds"; Name = "A quiet backdrop for focused work."; Type = [System.Windows.Automation.ControlType]::Text },
        @{ Module = "Convert"; Name = "Length"; Type = [System.Windows.Automation.ControlType]::Text },
        @{ Module = "Awake"; Name = "Keep awake"; Type = [System.Windows.Automation.ControlType]::Button }
    )
    foreach ($case in $toolCases) {
        Open-CatalogTool $case.Module
        Wait-ModuleControl $case.Name $case.Type | Out-Null
        Assert-HorizontalBounds
        if ($case.Module -eq "Home") {
            # Exercise the real shared viewport before leaving for the short Media
            # view; the next catalog navigation must restore its own top edge.
            $homeScroll = (Find-AutomationId "ModuleScroll" ([System.Windows.Automation.ScrollPattern]::Pattern)).GetCurrentPattern([System.Windows.Automation.ScrollPattern]::Pattern)
            if ($homeScroll.Current.VerticallyScrollable) {
                $homeScroll.SetScrollPercent([System.Windows.Automation.ScrollPattern]::NoScroll, 100)
                Start-Sleep -Milliseconds 100
            }
        }
        $report.ToolsOpened += $case.Module
    }
    $report.FeaturedNavigationStartsAtTop = $true
    $report.Actions += "Verified twenty-one public-testing tools open into their actual content without a subscription"

    $report.Stage = "Public-testing native Awake power request releases cleanly"
    Invoke-Button "Keep awake"
    Wait-Control "Stop keeping awake" ([System.Windows.Automation.ControlType]::Button) $true ([System.Windows.Automation.InvokePattern]::Pattern) | Out-Null
    if (Find-Control "Notchling error" $null $false) { throw "The native Awake power request failed on the disposable Windows desktop." }
    Invoke-Button "Stop keeping awake"
    Wait-Control "Keep awake" ([System.Windows.Automation.ControlType]::Button) | Out-Null
    $report.AwakeRoundTrip = $true
    $report.Actions += "Verified native Awake request enables and releases; no physical sleep-duration claim"

    $report.Stage = "Public-testing note editing persistence and deletion"
    Open-CatalogTool "Notes"
    $noteTitle = Wait-ModuleControl "Note title" ([System.Windows.Automation.ControlType]::Edit) $true ([System.Windows.Automation.ValuePattern]::Pattern)
    $noteTitleText = "Notchling disposable note " + [guid]::NewGuid().ToString("N")
    $noteTitle.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($noteTitleText)
    $noteBody = Wait-ModuleControl "Note body" ([System.Windows.Automation.ControlType]::Edit) $true ([System.Windows.Automation.ValuePattern]::Pattern)
    $noteBodyText = "A public-testing notebook round trip."
    $noteBody.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($noteBodyText)
    Wait-ModuleControl "Save note" ([System.Windows.Automation.ControlType]::Button) $true ([System.Windows.Automation.InvokePattern]::Pattern) | Out-Null
    Invoke-Button "Save note"
    $noteWait = [Diagnostics.Stopwatch]::StartNew()
    do {
        Assert-Budget
        $workspace = Read-Workspace
        if ($workspace -and @($workspace.Notes | Where-Object { $_.Title -eq $noteTitleText -and $_.Text -eq $noteBodyText }).Count -eq 1) { break }
        if ($noteWait.Elapsed.TotalSeconds -ge 4) { throw "The public-testing note did not persist in the disposable workspace." }
        Start-Sleep -Milliseconds 100
    } while ($true)
    Invoke-Button "Home"
    Open-CatalogTool "Notes"
    Wait-ModuleControl $noteTitleText ([System.Windows.Automation.ControlType]::Button) $true ([System.Windows.Automation.InvokePattern]::Pattern) | Out-Null
    Invoke-Button $noteTitleText
    $noteBody = Wait-ModuleControl "Note body" ([System.Windows.Automation.ControlType]::Edit) $true ([System.Windows.Automation.ValuePattern]::Pattern)
    if ($noteBody.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value -ne $noteBodyText) {
        throw "The public-testing note body was lost after navigation."
    }
    Wait-ModuleControl "Delete" ([System.Windows.Automation.ControlType]::Button) $true ([System.Windows.Automation.InvokePattern]::Pattern) | Out-Null
    Invoke-Button "Delete"
    $noteWait.Restart()
    do {
        Assert-Budget
        $workspace = Read-Workspace
        if ($workspace -and @($workspace.Notes | Where-Object { $_.Title -eq $noteTitleText }).Count -eq 0) { break }
        if ($noteWait.Elapsed.TotalSeconds -ge 4) { throw "Deleting the disposable test note did not persist." }
        Start-Sleep -Milliseconds 100
    } while ($true)
    $report.NotesRoundTrip = $true
    $report.Actions += "Verified unlocked note creation, durable save, navigation, body recovery and deletion"

    $report.Stage = "Genuine no-player media state"
    Invoke-Button "Media"
    Wait-Control "Nothing playing" ([System.Windows.Automation.ControlType]::Text) | Out-Null
    foreach ($name in @("Previous", "Play", "Next")) {
        $control = Wait-Control $name ([System.Windows.Automation.ControlType]::Button) $false
        if ($control.Current.IsEnabled) { throw "No-player media control incorrectly accepts commands: $name" }
    }
    $report.MediaNoPlayerControlsDisabled = $true
    Save-OwnedScreenshot "media"
    $volume = Wait-ModuleControl "System volume" ([System.Windows.Automation.ControlType]::Slider) $false ([System.Windows.Automation.RangeValuePattern]::Pattern)
    $volumeValue = $volume.GetCurrentPattern([System.Windows.Automation.RangeValuePattern]::Pattern).Current
    if ($volume.Current.IsEnabled) {
        if ($volumeValue.Minimum -ne 0 -or $volumeValue.Maximum -ne 1 -or $volumeValue.Value -lt 0 -or $volumeValue.Value -gt 1) {
            throw "The native audio endpoint volume exposed an invalid scalar."
        }
        $report.NativeVolumeStatus = "Native audio endpoint available; valid scalar observed without changing volume."
    } else {
        $report.NativeVolumeStatus = "No native audio endpoint on this hosted desktop; volume is correctly disabled."
    }
    $report.Actions += "Verified genuine no-player media state"

    $report.Stage = "Pomodoro start pause and reset"
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
    Invoke-Button "Reset Pomodoro"
    $report.FocusAfterReset = Wait-FocusClock $report.FocusInitial
    Save-OwnedScreenshot "focus"
    $report.Actions += "Verified Pomodoro start, pause, and reset"

    $report.Stage = "Public-testing scratchpad editing persistence and navigation"
    Invoke-Button "Home"
    Open-CatalogTool "Scratchpad"
    $editor = Wait-Control "Scratchpad" ([System.Windows.Automation.ControlType]::Edit) $true ([System.Windows.Automation.ValuePattern]::Pattern)
    $value = $editor.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern)
    if ($value.Current.IsReadOnly) { throw "The public-testing scratchpad is read-only." }
    $scratchpadText = "Notchling disposable cloud smoke " + [guid]::NewGuid().ToString("N")
    $value.SetValue($scratchpadText)
    Wait-ScratchpadSave $scratchpadText
    $report.ScratchpadSaved = $true
    Invoke-Button "Home"
    Open-CatalogTool "Scratchpad"
    $editor = Wait-Control "Scratchpad" ([System.Windows.Automation.ControlType]::Edit) $true ([System.Windows.Automation.ValuePattern]::Pattern)
    $value = $editor.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern)
    if ($value.Current.Value -ne $scratchpadText) { throw "The scratchpad lost its text after navigating away and back." }
    $report.ScratchpadRoundTrip = $true
    $value.SetValue("")
    Wait-ScratchpadSave ""
    $report.ScratchpadCleared = $true
    $report.Actions += "Verified scratchpad edit, durable save, navigation, and cleanup"
    $report.Stage = "Completed all UI interaction regressions"
    $report.Succeeded = $true
} catch {
    $failure = $_
    $report.ScriptStackTrace = $_.ScriptStackTrace
    $report.Error = "Stage [$($report.Stage)], operation [$($report.Operation)]: $($_.Exception.Message). Script stack: $($report.ScriptStackTrace). Completed actions: $($report.Actions -join '; '). Rejected named providers: $($report.PatternRejections -join '; ')"
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
