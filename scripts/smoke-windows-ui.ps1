# Windows PowerShell 5.1 supplies the built-in .NET Framework UI Automation client.
# The caller bounds this helper to 45 seconds and owns the target app's lifecycle.
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
    if ($clock.Elapsed.TotalSeconds -gt 35) { throw "Free-tier UI interaction exceeded its 35-second operation budget." }
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
        if ($pinWait.Elapsed.TotalSeconds -ge 3) { throw "The expanded notch could not be pinned for interaction." }
        Start-Sleep -Milliseconds 100
    }
    $report.Actions += "Pinned expanded notch"
    Wait-Control "Notchling Free" ([System.Windows.Automation.ControlType]::Text) | Out-Null

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
    $report.ElapsedSeconds = [Math]::Round($clock.Elapsed.TotalSeconds, 2)
    $reportFullPath = [IO.Path]::GetFullPath($ReportPath)
    [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($reportFullPath)) | Out-Null
    $report | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $reportFullPath -Encoding utf8
}
if ($failure) { Write-Error -Message $report.Error -ErrorAction Continue; exit 1 }
exit 0
