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
$restartRequired = $false

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

function Test-WindowsAppRuntimePackages([object[]]$Packages) {
    $frameworkReady = $false; $mainReady = $false; $singletonReady = $false; $ddlmReady = $false
    foreach ($package in $Packages) {
        Write-SetupLog "Observed runtime package: $($package.Name); family=$($package.PackageFamilyName); version=$($package.Version); architecture=$($package.Architecture); status=$($package.Status)."
        $version = $null
        if (-not [Version]::TryParse([string]$package.Version, [ref]$version) -or
            [string]$package.Architecture -ne 'X64' -or $version -lt $minimumWindowsAppRuntimeVersion -or
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

function Test-WindowsAppRuntime {
    # Get-AppxPackage without -AllUsers deliberately checks the account that
    # will run the app. Machine-wide staging alone cannot bootstrap WinUI.
    return Test-WindowsAppRuntimePackages @(Get-AppxPackage -Name '*Win*AppRuntime*' -ErrorAction Stop)
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
    # Use Windows' configured proxy, including integrated authentication, for
    # managed networks. Publisher/hash validation still applies to every file.
    $handler.DefaultProxyCredentials = [Net.CredentialCache]::DefaultCredentials
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

function Get-DotNetRuntimeInstallerFromMetadata([object]$Metadata) {
    if ([string]$Metadata.'channel-version' -ne '10.0' -or [string]$Metadata.'latest-runtime' -notmatch '^10\.0\.\d+$') {
        throw 'Microsoft returned unsupported .NET runtime metadata.'
    }
    $release = @($Metadata.releases | Where-Object { $_.runtime.version -eq $Metadata.'latest-runtime' })
    if ($release.Count -ne 1) { throw 'Microsoft runtime release metadata did not identify one stable release.' }
    $file = @($release[0].runtime.files | Where-Object { $_.rid -eq 'win-x64' -and $_.name -eq 'dotnet-runtime-win-x64.exe' })
    if ($file.Count -ne 1 -or [string]$file[0].hash -notmatch '^[0-9a-fA-F]{128}$') {
        throw 'Microsoft runtime release metadata did not include a verified x64 installer.'
    }
    Assert-MicrosoftDownloadUri ([Uri]$file[0].url)
    return $file[0]
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
    $component = [IO.Path]::GetFileName($Installer)
    if ($RequiresElevation) {
        $principal = [Security.Principal.WindowsPrincipal]::new([Security.Principal.WindowsIdentity]::GetCurrent())
        if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
            Write-SetupLog 'Requesting Windows permission to install the shared .NET runtime.'
            $start.Verb = 'RunAs'
        }
    }
    if (-not $start.ContainsKey('Verb')) {
        # The Windows App Runtime installer is a console program. Capture its
        # deployment details instead of showing a blank transient console.
        $start.WindowStyle = 'Hidden'
        $logDirectory = Split-Path -Parent $LogPath
        $start.RedirectStandardOutput = Join-Path $logDirectory ($component + '.stdout.log')
        $start.RedirectStandardError = Join-Path $logDirectory ($component + '.stderr.log')
    }
    Write-SetupLog "Starting verified Microsoft installer: $component."
    try { $process = Start-Process @start }
    catch {
        $cause = $_.Exception
        while ($cause) {
            if ($cause -is [ComponentModel.Win32Exception] -and $cause.NativeErrorCode -eq 1223) {
                throw 'Windows administrator permission was cancelled. Run Notchling Setup again and approve the Microsoft runtime installer, or ask your administrator to install it.'
            }
            $cause = $cause.InnerException
        }
        throw
    }
    try {
        if (-not $process.WaitForExit(600000)) {
            throw 'A shared Windows component installer is still running. Wait for it to finish, then retry Notchling Setup.'
        }
        $code = $process.ExitCode
        $hex = '0x{0:X8}' -f ([long]$code -band 4294967295L)
        Write-SetupLog "Microsoft prerequisite installer exit code: $code. Component: $component; Windows error: $hex."
        foreach ($outputKey in @('RedirectStandardOutput', 'RedirectStandardError')) {
            if ($start.ContainsKey($outputKey) -and (Test-Path -LiteralPath $start[$outputKey])) {
                foreach ($line in (Get-Content -LiteralPath $start[$outputKey] -Tail 20)) {
                    if ($line) { Write-SetupLog "$component $outputKey`: $line" }
                }
            }
        }
        if ($code -eq 3010 -or $code -eq 1641) { $script:restartRequired = $true }
        elseif ($code -ne 0) {
            if ($hex -eq '0x8007007E' -or $code -eq 126) {
                $failure = [InvalidOperationException]::new("$component could not load a required Windows module ($hex, code $code). The installer output is saved beside the Setup log. Windows components may be missing or damaged; this is not an internet failure.")
                $failure.Data['InstallerExitCode'] = $code
                throw $failure
            }
            throw "$component failed (code $code, $hex). See the Setup logs for details; managed PCs may require administrator help."
        }
    }
    finally { $process.Dispose() }
}

function Get-WindowsAppRuntimePackageIdentity([string]$Package, [string]$ExpectedName) {
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [IO.Compression.ZipFile]::OpenRead($Package)
    $reader = $null; $stream = $null
    try {
        $manifest = $archive.GetEntry('AppxManifest.xml')
        if (-not $manifest -or $manifest.Length -gt 1048576 -or -not $archive.GetEntry('AppxSignature.p7x')) {
            throw 'A Windows App Runtime package did not contain its signed manifest.'
        }
        $settings = [Xml.XmlReaderSettings]::new()
        $settings.DtdProcessing = [Xml.DtdProcessing]::Prohibit
        $settings.XmlResolver = $null
        $stream = $manifest.Open()
        $reader = [Xml.XmlReader]::Create($stream, $settings)
        $document = [Xml.XmlDocument]::new()
        $document.XmlResolver = $null
        $document.Load($reader)
        $identity = $document.SelectSingleNode('/*[local-name()="Package"]/*[local-name()="Identity"]')
        if (-not $identity) { throw 'A Windows App Runtime package did not contain its identity.' }
        $version = $null
        if (-not [Version]::TryParse($identity.GetAttribute('Version'), [ref]$version) -or
            $version -lt $minimumWindowsAppRuntimeVersion -or $version.Major -ne 8000 -or
            $identity.GetAttribute('ProcessorArchitecture') -ne 'x64' -or
            $identity.GetAttribute('Publisher') -ne 'CN=Microsoft Corporation, O=Microsoft Corporation, L=Redmond, S=Washington, C=US') {
            throw 'A Windows App Runtime package did not match the required Microsoft x64 runtime identity.'
        }
        if ($ExpectedName -eq 'DDLM') { $ExpectedName = "Microsoft.WinAppRuntime.DDLM.$version-x6" }
        if ($identity.GetAttribute('Name') -ne $ExpectedName) {
            throw "A Windows App Runtime package did not match the required component $ExpectedName."
        }
        return [PSCustomObject]@{ Name = $identity.GetAttribute('Name'); Version = $version; Path = $Package }
    }
    finally {
        if ($reader) { $reader.Dispose() }
        if ($stream) { $stream.Dispose() }
        $archive.Dispose()
    }
}

function Expand-WindowsAppRuntimePackages([string]$Installer, [string]$Destination) {
    # The publisher has already been verified. Reading its PACKAGE resources
    # as data avoids executing the native installer's failing loader/licensing
    # path. These names come from WindowsAppSDK's installer .rc definitions.
    if (-not ('Notchling.Setup.RuntimeResources' -as [type])) {
        Add-Type -TypeDefinition @'
using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
namespace Notchling.Setup {
    public static class RuntimeResources {
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr LoadLibraryEx(string file, IntPtr reserved, uint flags);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr FindResource(IntPtr module, string name, string type);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern uint SizeofResource(IntPtr module, IntPtr resource);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr LoadResource(IntPtr module, IntPtr resource);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr LockResource(IntPtr resource);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool FreeLibrary(IntPtr module);
        public static string[] Extract(string installer, string directory) {
            string[] names = { "MSIX_FWPACKAGE_X64", "MSIX_MAINPACKAGE_X64", "MSIX_SINGLETONPACKAGE_X64", "MSIX_DDLMPACKAGE_X64" };
            string[] paths = new string[names.Length];
            // LOAD_LIBRARY_AS_DATAFILE | LOAD_LIBRARY_AS_IMAGE_RESOURCE:
            // no import resolution, DllMain, or application code execution.
            IntPtr module = LoadLibraryEx(installer, IntPtr.Zero, 0x2 | 0x20);
            if (module == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not read the verified Microsoft installer as resource data.");
            try {
                long total = 0;
                for (int index = 0; index < names.Length; index++) {
                    IntPtr resource = FindResource(module, names[index], "PACKAGE");
                    if (resource == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error(), "The verified installer did not contain " + names[index] + ".");
                    uint size = SizeofResource(module, resource);
                    total += size;
                    if (size < 4 || size > 167772160 || total > 268435456) throw new InvalidDataException("A Microsoft runtime resource exceeded its size limit.");
                    IntPtr loaded = LoadResource(module, resource);
                    if (loaded == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not load the Microsoft runtime resource.");
                    IntPtr data = LockResource(loaded);
                    if (data == IntPtr.Zero || Marshal.ReadInt32(data) != 0x04034B50) throw new InvalidDataException("A Microsoft runtime resource was not an MSIX archive.");
                    paths[index] = Path.Combine(directory, names[index] + ".msix");
                    using (FileStream output = new FileStream(paths[index], FileMode.CreateNew, FileAccess.Write, FileShare.None)) {
                        byte[] buffer = new byte[65536];
                        for (long offset = 0; offset < size; offset += buffer.Length) {
                            int count = (int)Math.Min(buffer.Length, size - offset);
                            Marshal.Copy(new IntPtr(data.ToInt64() + offset), buffer, 0, count);
                            output.Write(buffer, 0, count);
                        }
                    }
                }
                return paths;
            }
            finally { FreeLibrary(module); }
        }
    }
}
'@
    }
    New-Item -ItemType Directory -Path $Destination | Out-Null
    return [Notchling.Setup.RuntimeResources]::Extract($Installer, $Destination)
}

function Install-WindowsAppRuntimePackages([string]$Installer, [string]$WorkDirectory) {
    # Recheck the signature at the recovery boundary; a caller cannot feed an
    # arbitrary EXE into native resource extraction or package installation.
    Assert-MicrosoftSignature $Installer
    $files = @(Expand-WindowsAppRuntimePackages $Installer (Join-Path $WorkDirectory 'runtime-recovery'))
    $expected = @('Microsoft.WindowsAppRuntime.1.8', 'MicrosoftCorporationII.WinAppRuntime.Main.1.8',
        'MicrosoftCorporationII.WinAppRuntime.Singleton', 'DDLM')
    if ($files.Count -ne $expected.Count) { throw 'The verified installer did not provide all four x64 runtime packages.' }
    $packages = @()
    for ($index = 0; $index -lt $files.Count; $index++) {
        $packages += Get-WindowsAppRuntimePackageIdentity $files[$index] $expected[$index]
    }
    # Validate every identity before deploying anything. Windows validates the
    # package signatures itself; do not use AllowUnsigned or force app shutdown.
    foreach ($package in $packages) {
        $alreadyReady = @(Get-AppxPackage -Name $package.Name -ErrorAction Stop | Where-Object {
            $_.PackageFamilyName -eq ($package.Name + '_8wekyb3d8bbwe') -and
            [string]$_.Architecture -eq 'X64' -and [string]$_.Status -eq 'Ok' -and
            [Version]$_.Version -ge $package.Version
        })
        if ($alreadyReady.Count -gt 0) {
            # Singleton is shared by newer Windows App Runtime lines. A newer
            # healthy registration must be reused rather than downgraded.
            Write-SetupLog "Reusing an equal or newer healthy Microsoft x64 package for the current account: $($package.Name); version=$($alreadyReady[0].Version)."
            continue
        }
        Write-SetupLog "Registering verified Microsoft x64 package for the current account: $($package.Name); version=$($package.Version)."
        try { Add-AppxPackage -Path $package.Path -ErrorAction Stop }
        catch {
            Write-SetupLog ("Microsoft runtime package registration failed: " + ($_ | Out-String).Trim())
            throw "Windows could not register $($package.Name). $($_.Exception.Message) This may require repair of Windows AppX deployment components by your administrator; retrying a download alone will not repair them."
        }
    }
    Write-SetupLog 'Verified-resource recovery registered all four Microsoft Windows App Runtime x64 packages.'
}

function Invoke-WindowsAppRuntimeInstaller([string]$Installer, [string]$WorkDirectory) {
    try { Invoke-RuntimeInstaller $Installer @('--quiet') }
    catch {
        $cause = $_.Exception
        $missingModule = $false
        while ($cause) {
            if ($cause.Data.Contains('InstallerExitCode') -and
                [int]$cause.Data['InstallerExitCode'] -in @(126, -2147024770)) { $missingModule = $true; break }
            $cause = $cause.InnerException
        }
        if (-not $missingModule) { throw }
        Write-SetupLog 'The native Windows App Runtime installer returned 0x8007007E. Recovering with its verified signed MSIX resources for the current user.'
        Install-WindowsAppRuntimePackages $Installer $WorkDirectory
    }
}

function Invoke-NotchlingPrerequisites {
    $workDirectory = $null
    $script:restartRequired = $false
    $exitCode = 1
    $phase = 'checking your Windows version and installed components'
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
        Import-Module (Join-Path $PSHOME 'Modules\Appx\Appx.psd1') -Force -ErrorAction Stop
        Add-Type -AssemblyName System.Net.Http
        [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
        $dotNetReady = Test-DotNetRuntime
        $appRuntimeReady = Test-WindowsAppRuntime
        if (-not $dotNetReady -or -not $appRuntimeReady) {
            $workDirectory = Join-Path ([IO.Path]::GetTempPath()) ('Notchling.Prerequisites-' + [Guid]::NewGuid().ToString('N'))
            New-Item -ItemType Directory -Path $workDirectory | Out-Null
        }

        if (-not $dotNetReady) {
            $phase = 'downloading and verifying the Microsoft .NET 10 runtime'
            Write-SetupLog 'Downloading the current stable .NET 10 runtime from Microsoft.'
            $metadataPath = Join-Path $workDirectory 'dotnet-releases.json'
            Save-MicrosoftDownload ([Uri]'https://builds.dotnet.microsoft.com/dotnet/release-metadata/10.0/releases.json') $metadataPath 2097152 45
            $metadata = [IO.File]::ReadAllText($metadataPath) | ConvertFrom-Json
            $file = Get-DotNetRuntimeInstallerFromMetadata $metadata
            $dotNetInstaller = Join-Path $workDirectory 'dotnet-runtime-win-x64.exe'
            Save-MicrosoftDownload ([Uri]$file.url) $dotNetInstaller 536870912 300
            if ((Get-FileHash -LiteralPath $dotNetInstaller -Algorithm SHA512).Hash -ne $file.hash) {
                throw 'The shared .NET runtime download failed integrity verification. Setup did not run it.'
            }
            Write-SetupLog 'Verified the .NET runtime SHA-512 against official Microsoft release metadata.'
            Assert-MicrosoftSignature $dotNetInstaller
            $runtimeLog = Join-Path $logDirectory 'dotnet-runtime-install.log'
            $phase = 'installing the Microsoft .NET 10 runtime'
            Invoke-RuntimeInstaller $dotNetInstaller @('/install', '/quiet', '/norestart', '/log', ('"' + $runtimeLog + '"')) -RequiresElevation
            $dotNetReady = Test-DotNetRuntime
        }

        if (-not $appRuntimeReady) {
            $phase = 'downloading and verifying Microsoft Windows App Runtime 1.8'
            Write-SetupLog 'Downloading Windows App Runtime 1.8 from Microsoft.'
            $appRuntimeInstaller = Join-Path $workDirectory 'windowsappruntimeinstall-x64.exe'
            Save-MicrosoftDownload ([Uri]'https://aka.ms/windowsappsdk/1.8/1.8.260921001/windowsappruntimeinstall-x64.exe') $appRuntimeInstaller 536870912 300
            Assert-MicrosoftSignature $appRuntimeInstaller
            Write-SetupLog "Verified Windows App Runtime installer version: $((Get-Item -LiteralPath $appRuntimeInstaller).VersionInfo.ProductVersion)."
            # Do not force elevation: registration must target the installing user.
            $phase = 'registering Microsoft Windows App Runtime 1.8 for your Windows account'
            Invoke-WindowsAppRuntimeInstaller $appRuntimeInstaller $workDirectory
            $appRuntimeReady = Test-WindowsAppRuntime
        }

        $phase = 'verifying that the installed Microsoft components are ready'
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
        $message = "Setup failed while $phase. " + $_.Exception.Message +
            " If a download failed, check your internet connection or network policy, then retry Setup. Details: $LogPath"
        try { Write-SetupLog $message } catch { Write-Host $message }
        try { Write-SetupResult $message } catch { Write-Host 'Setup could not save its diagnostic message.' }
    }
    finally {
        if ($workDirectory -and (Test-Path -LiteralPath $workDirectory)) {
            Remove-Item -LiteralPath $workDirectory -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
    return $exitCode
}

# Dot-sourcing exposes the same production functions to the PS5.1 regression
# fixture without starting installers or downloading any prerequisite.
if ($MyInvocation.InvocationName -eq '.') { return }
exit (Invoke-NotchlingPrerequisites)
