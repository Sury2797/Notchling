param(
    [string]$ProcessName = "Notchling.Windows",
    [ValidateRange(10, 300)][int]$Seconds = 30
)
$ErrorActionPreference = "Stop"
$target = Get-Process -Name $ProcessName | Select-Object -First 1
if (-not $target) { throw "Start the native application before measuring." }
$logicalProcessors = [Environment]::ProcessorCount
$cpuBefore = $target.TotalProcessorTime.TotalSeconds
$started = [Diagnostics.Stopwatch]::StartNew()
$memorySamples = [Collections.Generic.List[double]]::new()
$privateSamples = [Collections.Generic.List[double]]::new()
$handles = [Collections.Generic.List[int]]::new()
for ($sample = 0; $sample -lt $Seconds; $sample++) {
    Start-Sleep -Seconds 1
    $target.Refresh()
    if ($target.HasExited) { throw "Application exited during measurement." }
    $memorySamples.Add($target.WorkingSet64 / 1MB)
    $privateSamples.Add($target.PrivateMemorySize64 / 1MB)
    $handles.Add($target.HandleCount)
}
$started.Stop()
$target.Refresh()
[pscustomobject]@{
    ProcessName = $ProcessName
    ProcessId = $target.Id
    DurationSeconds = [Math]::Round($started.Elapsed.TotalSeconds, 2)
    CpuPercentAllCores = [Math]::Round(100 * ($target.TotalProcessorTime.TotalSeconds - $cpuBefore) / $started.Elapsed.TotalSeconds / $logicalProcessors, 3)
    AverageWorkingSetMiB = [Math]::Round(($memorySamples | Measure-Object -Average).Average, 2)
    PeakWorkingSetMiB = [Math]::Round(($memorySamples | Measure-Object -Maximum).Maximum, 2)
    AveragePrivateMiB = [Math]::Round(($privateSamples | Measure-Object -Average).Average, 2)
    LastHandleCount = $handles[$handles.Count - 1]
} | Format-List
