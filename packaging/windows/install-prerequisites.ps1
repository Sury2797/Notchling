# Windows PowerShell 5.1: embedded in Setup, never installed into the app folder.
# Runtime packages are shared with other Windows apps instead of being bundled
# in every Notchling download. No Microsoft installer runs until it is verified.
[CmdletBinding()]
param(
    [string]$ResultPath,
    [string]$LogPath = (Join-Path $env:LOCALAPPDATA 'Notchling\Setup\Logs\setup-prerequisites.log')
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$minimumWindowsAppRuntimeVersion = [Version]'8000.994.2142.0'
$windowsAppRuntimeFamily = 'Microsoft.WindowsAppRuntime.1.8_8wekyb3d8bbwe'
$workDirectory = $null
$restartRequired = $false
$exitCode = 1

function Write-SetupLog([string]$Message) {
    $line = '{0:u} {1}' -f [DateTime]::UtcNow, $Message
    Add-Content -LiteralPath $LogPath -Value $line -Encoding UTF8
    Write-Host $line
}

function Write-SetupResult([string]$Message) {
    if ($ResultPath) { [IO.File]::WriteAllText($ResultPath, $Message, [Text.UTF8Encoding]::new($false)) }
}

function Test-DotNetRuntime {
    $locations = @()
    # .NET's documented install-location registry lives in the 32-bit view,
    # including the x64 runtime. Check both views for older/custom installers.
    foreach ($view in @([Microsoft.Win32.RegistryView]::Registry32, [Microsoft.Win32.RegistryView]::Registry64)) {
        $registry = $null; $installed = $null
        try {
            $registry = [Microsoft.Win32.RegistryKey]::OpenBaseKey([Microsoft.Win32.RegistryHive]::LocalMachine, $view)
            $installed = $registry.OpenSubKey('SOFTWARE\dotnet\Setup\InstalledVersions\x64')
            if ($installed) {
                $location = [string]$installed.GetValue('InstallLocation', '')
                if ($location) { $locations += $location }
            }
        }
        finally {
            if ($installed) { $installed.Dispose() }
            if ($registry) { $registry.Dispose() }
        }
    }
    if ($env:ProgramW6432) { $locations += (Join-Path $env:ProgramW6432 'dotnet') }
    elseif ($env:ProgramFiles) { $locations += (Join-Path $env:ProgramFiles 'dotnet') }
    foreach ($location in ($locations | Select-Object -Unique)) {
        $shared = Join-Path $location 'shared\Microsoft.NETCore.App'
        if (-not (Test-Path -LiteralPath (Join-Path $location 'dotnet.exe')) -or -not (Test-Path -LiteralPath $shared)) { continue }
        foreach ($directory in (Get-ChildItem -LiteralPath $shared -Directory)) {
            $version = $null
            if ([Version]::TryParse($directory.Name, [ref]$version) -and $version.Major -eq 10 -and
                (Test-Path -LiteralPath (Join-Path $directory.FullName 'coreclr.dll'))) {
                Write-SetupLog "Shared .NET runtime already present: $version (x64)."
                return $true
            }
        }
    }
    return $false
}

function Test-WindowsAppRuntime {
    $packages = @(Get-AppxPackage -Name '*Win*AppRuntime*' -ErrorAction Stop)
    $frameworkReady = $false; $mainReady = $false; $singletonReady = $false; $ddlmReady = $false
    foreach ($package in $packages) {
        Write-SetupLog "Observed runtime package: $($package.Name); family=$($package.PackageFamilyName); version=$($package.Version); architecture=$($package.Architecture); status=$($package.Status)."
        if ([string]$package.Architecture -ne 'X64' -or [Version]$package.Version -lt $minimumWindowsAppRuntimeVersion -or
            [string]$package.Status -ne 'Ok') { continue }
        if ($package.PackageFamilyName -eq $windowsAppRuntimeFamily) { $frameworkReady = $true }
        elseif ($package.PackageFamilyName -eq 'MicrosoftCorporationII.WinAppRuntime.Main.1.8_8wekyb3d8bbwe') { $mainReady = $true }
        elseif ($package.PackageFamilyName -eq 'MicrosoftCorporationII.WinAppRuntime.Singleton_8wekyb3d8bbwe') { $singletonReady = $true }
        elseif ($package.PackageFamilyName -match '^Microsoft\.WinAppRuntime\.DDLM\.8000\.\d+\.\d+\.\d+-x6_8wekyb3d8bbwe$') { $ddlmReady = $true }
    }
    $ready = $frameworkReady -and $mainReady -and $singletonReady -and $ddlmReady
    Write-SetupLog "Runtime checks: framework=$frameworkReady; main=$mainReady; singleton=$singletonReady; DDLM=$ddlmReady."
    if ($ready) { Write-SetupLog "Windows App Runtime 1.8 framework, Main, Singleton and DDLM are registered for this user (x64, minimum $minimumWindowsAppRuntimeVersion)." }
    return $ready
}

function Assert-MicrosoftDownloadUri([Uri]$Uri) {
    $hosts = @('builds.dotnet.microsoft.com', 'download.visualstudio.microsoft.com',
        'dotnetcli.azureedge.net', 'download.microsoft.com', 'aka.ms')
    if (-not $Uri.IsAbsoluteUri -or $Uri.Scheme -ne 'https' -or -not $Uri.IsDefaultPort -or
        $Uri.UserInfo -or $Uri.Fragment -or $Uri.IdnHost -notin $hosts) {
        throw 'A prerequisite download did not use an approved Microsoft HTTPS address.'
    }
}

function Save-MicrosoftDownload([Uri]$Uri, [string]$Destination, [long]$MaximumBytes, [int]$TimeoutSeconds) {
    $handler = [Net.Http.HttpClientHandler]::new()
    $handler.AllowAutoRedirect = $false
    $client = [Net.Http.HttpClient]::new($handler)
    $client.Timeout = [Threading.Timeout]::InfiniteTimeSpan
    $deadline = [Threading.CancellationTokenSource]::new([TimeSpan]::FromSeconds($TimeoutSeconds))
    $reply = $null; $inputStream = $null; $outputStream = $null
    try {
        for ($redirect = 0; $redirect -le 5; $redirect++) {
            Assert-MicrosoftDownloadUri $Uri
            $reply = $client.GetAsync($Uri, [Net.Http.HttpCompletionOption]::ResponseHeadersRead, $deadline.Token).GetAwaiter().GetResult()
            $status = [int]$reply.StatusCode
            if ($status -ge 300 -and $status -lt 400) {
                if (-not $reply.Headers.Location -or $redirect -eq 5) { throw 'A prerequisite download returned too many redirects.' }
                $Uri = [Uri]::new($Uri, $reply.Headers.Location)
                $reply.Dispose(); $reply = $null
                continue
            }
            $reply.EnsureSuccessStatusCode() | Out-Null
            if ($reply.Content.Headers.ContentLength -and $reply.Content.Headers.ContentLength -gt $MaximumBytes) {
                throw 'A prerequisite download exceeded its size limit.'
            }
            $inputStream = $reply.Content.ReadAsStreamAsync().GetAwaiter().GetResult()
            $outputStream = [IO.File]::Open($Destination, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
            $buffer = [byte[]]::new(65536)
            $total = 0L
            while (($count = $inputStream.ReadAsync($buffer, 0, $buffer.Length, $deadline.Token).GetAwaiter().GetResult()) -gt 0) {
                $total += $count
                if ($total -gt $MaximumBytes) { throw 'A prerequisite download exceeded its size limit.' }
                $outputStream.Write($buffer, 0, $count)
            }
            if ($total -eq 0 -or ($reply.Content.Headers.ContentLength -and $total -ne $reply.Content.Headers.ContentLength)) {
                throw 'A prerequisite download was incomplete.'
            }
            Write-SetupLog "Downloaded $total bytes from $($Uri.IdnHost)."
            return
        }
    }
    finally {
        if ($outputStream) { $outputStream.Dispose() }
        if ($inputStream) { $inputStream.Dispose() }
        if ($reply) { $reply.Dispose() }
        $client.Dispose(); $handler.Dispose(); $deadline.Dispose()
    }
}

function Assert-MicrosoftSignature([string]$Installer) {
    $signature = Get-AuthenticodeSignature -LiteralPath $Installer
    if ($signature.Status -ne 'Valid' -or -not $signature.SignerCertificate -or
        $signature.SignerCertificate.Subject -notmatch '(?:^|,\s*)O=Microsoft Corporation(?:,|$)') {
        throw 'A Microsoft prerequisite installer failed publisher verification. Setup did not run it.'
    }
    Write-SetupLog 'Verified a trusted Microsoft Authenticode signature.'
}

function Invoke-RuntimeInstaller([string]$Installer, [string[]]$Arguments, [switch]$RequiresElevation) {
    $start = @{ FilePath = $Installer; ArgumentList = $Arguments; PassThru = $true }
    if ($RequiresElevation) {
        $principal = [Security.Principal.WindowsPrincipal]::new([Security.Principal.WindowsIdentity]::GetCurrent())
        if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
            Write-SetupLog 'Requesting Windows permission to install the shared .NET runtime.'
            $start.Verb = 'RunAs'
        }
    }
    $process = Start-Process @start
    try {
        if (-not $process.WaitForExit(600000)) {
            throw 'A shared Windows component installer is still running. Wait for it to finish, then retry Notchling Setup.'
        }
        $code = $process.ExitCode
        Write-SetupLog "Microsoft prerequisite installer exit code: $code."
        if ($code -eq 3010 -or $code -eq 1641) { $script:restartRequired = $true }
        elseif ($code -ne 0) { throw "A shared Windows component could not be installed (code $code). Restart Windows and try Setup again." }
    }
    finally { $process.Dispose() }
}

try {
    $logDirectory = Split-Path -Parent $LogPath
    New-Item -ItemType Directory -Path $logDirectory -Force | Out-Null
    Write-SetupLog 'Checking Notchling shared runtime prerequisites.'
    if ([Environment]::OSVersion.Version.Build -lt 19045 -or -not [Environment]::Is64BitOperatingSystem -or
        ($env:PROCESSOR_ARCHITECTURE -ne 'AMD64' -and $env:PROCESSOR_ARCHITEW6432 -ne 'AMD64')) {
        throw 'Notchling requires Windows 10 22H2 or Windows 11 on an x64 PC.'
    }
    if (-not [Environment]::Is64BitProcess) { throw 'Setup must run the 64-bit Windows component check. Restart Setup on your x64 PC.' }
    # A PowerShell 7 parent can pass its module search path into Windows PowerShell.
    # Import the Windows PowerShell security module explicitly so certificate
    # verification never resolves an incompatible PowerShell 7 binary module.
    Import-Module (Join-Path $PSHOME 'Modules\Microsoft.PowerShell.Security\Microsoft.PowerShell.Security.psd1') -Force -ErrorAction Stop
    Add-Type -AssemblyName System.Net.Http
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    $dotNetReady = Test-DotNetRuntime
    $appRuntimeReady = Test-WindowsAppRuntime
    if (-not $dotNetReady -or -not $appRuntimeReady) {
        $workDirectory = Join-Path ([IO.Path]::GetTempPath()) ('Notchling.Prerequisites-' + [Guid]::NewGuid().ToString('N'))
        New-Item -ItemType Directory -Path $workDirectory | Out-Null
    }

    if (-not $dotNetReady) {
        Write-SetupLog 'Downloading the current stable .NET 10 runtime from Microsoft.'
        $metadataPath = Join-Path $workDirectory 'dotnet-releases.json'
        Save-MicrosoftDownload ([Uri]'https://builds.dotnet.microsoft.com/dotnet/release-metadata/10.0/releases.json') $metadataPath 2097152 45
        $metadata = [IO.File]::ReadAllText($metadataPath) | ConvertFrom-Json
        if ([string]$metadata.'channel-version' -ne '10.0' -or [string]$metadata.'latest-runtime' -notmatch '^10\.0\.\d+$') {
            throw 'Microsoft returned unsupported .NET runtime metadata.'
        }
        $release = @($metadata.releases | Where-Object { $_.runtime.version -eq $metadata.'latest-runtime' })
        if ($release.Count -ne 1) { throw 'Microsoft runtime release metadata did not identify one stable release.' }
        $file = @($release[0].runtime.files | Where-Object { $_.rid -eq 'win-x64' -and $_.name -match '^dotnet-runtime-win-x64\.exe$' })
        if ($file.Count -ne 1 -or [string]$file[0].hash -notmatch '^[0-9a-fA-F]{128}$') {
            throw 'Microsoft runtime release metadata did not include a verified x64 installer.'
        }
        $dotNetInstaller = Join-Path $workDirectory 'dotnet-runtime-win-x64.exe'
        Save-MicrosoftDownload ([Uri]$file[0].url) $dotNetInstaller 536870912 300
        if ((Get-FileHash -LiteralPath $dotNetInstaller -Algorithm SHA512).Hash -ne $file[0].hash) {
            throw 'The shared .NET runtime download failed integrity verification. Setup did not run it.'
        }
        Assert-MicrosoftSignature $dotNetInstaller
        $runtimeLog = Join-Path $logDirectory 'dotnet-runtime-install.log'
        Invoke-RuntimeInstaller $dotNetInstaller @('/install', '/quiet', '/norestart', '/log', ('"' + $runtimeLog + '"')) -RequiresElevation
        $dotNetReady = Test-DotNetRuntime
    }

    if (-not $appRuntimeReady) {
        Write-SetupLog 'Downloading Windows App Runtime 1.8 from Microsoft.'
        $appRuntimeInstaller = Join-Path $workDirectory 'windowsappruntimeinstall-x64.exe'
        Save-MicrosoftDownload ([Uri]'https://aka.ms/windowsappsdk/1.8/latest/windowsappruntimeinstall-x64.exe') $appRuntimeInstaller 536870912 300
        Assert-MicrosoftSignature $appRuntimeInstaller
        # Do not force elevation: registration must target the installing user.
        Invoke-RuntimeInstaller $appRuntimeInstaller @('--quiet')
        $appRuntimeReady = Test-WindowsAppRuntime
    }

    if (-not $dotNetReady -or -not $appRuntimeReady) {
        if (-not $restartRequired) { throw 'Shared Windows components were not registered successfully. Restart Windows and retry Setup.' }
        Write-SetupLog 'Windows must restart before the shared components are ready.'
    }
    if ($restartRequired) {
        Write-SetupResult 'Restart Windows to finish preparing shared components, then continue Notchling Setup.'
        $exitCode = 3010
    }
    else {
        Write-SetupResult 'Notchling shared Windows components are ready.'
        Write-SetupLog 'All prerequisites verified for the current user.'
        $exitCode = 0
    }
}
catch {
    $message = 'Setup could not prepare the shared Windows components. ' + $_.Exception.Message +
        " Check your internet connection and retry Setup. Details: $LogPath"
    try { Write-SetupLog $message } catch { Write-Host $message }
    try { Write-SetupResult $message } catch { Write-Host 'Setup could not save its diagnostic message.' }
}
finally {
    if ($workDirectory -and (Test-Path -LiteralPath $workDirectory)) {
        Remove-Item -LiteralPath $workDirectory -Recurse -Force -ErrorAction SilentlyContinue
    }
}
exit $exitCode
