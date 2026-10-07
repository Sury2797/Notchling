# Execute with Windows PowerShell 5.1, the same host used by Setup.
# Unit mode never downloads or installs a runtime. Integration is restricted to
# disposable GitHub runners and exercises the production missing-.NET branch
# without deleting the SDK/runtime that the rest of CI needs.
[CmdletBinding()]
param(
    [ValidateSet('Unit', 'Integration', 'RuntimeFallback')][string]$Mode = 'Unit',
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '..\artifacts\prerequisite-tests')
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if ($PSVersionTable.PSEdition -ne 'Desktop' -or $PSVersionTable.PSVersion.Major -ne 5) {
    throw 'Run prerequisite fixtures with 64-bit Windows PowerShell 5.1, not PowerShell 7.'
}
if (-not [Environment]::Is64BitProcess) { throw 'The prerequisite fixture must use the x64 Windows PowerShell host.' }
if ($Mode -ne 'Unit' -and ($env:GITHUB_ACTIONS -ne 'true' -or -not $env:RUNNER_TEMP)) {
    throw 'The real installer integration fixture is permitted only on a disposable GitHub Actions runner.'
}

New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$OutputDirectory = (Resolve-Path -LiteralPath $OutputDirectory).Path
$fixtureLog = Join-Path $OutputDirectory ($Mode.ToLowerInvariant() + '-prerequisites.log')
$fixtureResult = Join-Path $OutputDirectory ($Mode.ToLowerInvariant() + '-result.txt')
if (Test-Path -LiteralPath $fixtureLog) { Remove-Item -LiteralPath $fixtureLog -Force }
. (Join-Path $PSScriptRoot '..\packaging\windows\install-prerequisites.ps1') -LogPath $fixtureLog -ResultPath $fixtureResult

if ($Mode -ne 'Unit') {
    $script:actualDotNetTest = (Get-Item Function:Test-DotNetRuntime).ScriptBlock
    $script:actualAppRuntimeTest = (Get-Item Function:Test-WindowsAppRuntime).ScriptBlock
    $script:actualRuntimeInstaller = (Get-Item Function:Invoke-RuntimeInstaller).ScriptBlock
    $baselineRuntimePresent = Test-DotNetRuntime
    $script:dotNetDetectionCalls = 0
    $script:appRuntimeDetectionCalls = 0
    $script:injectedMissingModule = $false
    if ($Mode -eq 'Integration') {
        function Test-DotNetRuntime {
            $script:dotNetDetectionCalls++
            if ($script:dotNetDetectionCalls -eq 1) { return $false }
            return & $script:actualDotNetTest
        }
    }
    else {
        function Test-WindowsAppRuntime {
            $script:appRuntimeDetectionCalls++
            if ($script:appRuntimeDetectionCalls -eq 1) { return $false }
            return & $script:actualAppRuntimeTest
        }
        function Invoke-RuntimeInstaller {
            param([string]$Installer, [string[]]$Arguments, [switch]$RequiresElevation)
            if ([IO.Path]::GetFileName($Installer) -eq 'windowsappruntimeinstall-x64.exe') {
                $script:injectedMissingModule = $true
                $failure = [InvalidOperationException]::new('FIXTURE: native runtime installer returned the reported 0x8007007E.')
                $failure.Data['InstallerExitCode'] = -2147024770
                throw $failure
            }
            & $script:actualRuntimeInstaller $Installer $Arguments -RequiresElevation:$RequiresElevation
        }
    }
    try {
        if ($Mode -eq 'Integration') {
            Write-SetupLog 'INTEGRATION FIXTURE: forcing the initial .NET detection to missing; the existing SDK/runtime remains installed.'
        }
        else {
            Write-SetupLog 'RUNTIME RECOVERY FIXTURE: forcing the initial app-runtime detection to missing and the native installer result to 0x8007007E. Official download, signatures, resource extraction and current-user package deployment remain real.'
        }
        $resultCode = Invoke-NotchlingPrerequisites
        if ($resultCode -ne 0) { throw "The real prerequisite integration returned $resultCode. See $fixtureLog." }
        $lines = @(Get-Content -LiteralPath $fixtureLog)
        $requirements = @('Verified a trusted Microsoft Authenticode signature\.', 'All prerequisites verified for the current user\.')
        if ($Mode -eq 'Integration') {
            $requirements += @('Downloading the current stable \.NET 10 runtime from Microsoft\.',
                'Verified the \.NET runtime SHA-512 against official Microsoft release metadata\.',
                'Microsoft prerequisite installer exit code: 0\.')
        }
        else {
            $requirements += @('Downloading Windows App Runtime 1\.8 from Microsoft\.',
                'Recovering with its verified signed MSIX resources for the current user\.',
                'Verified-resource recovery registered all four Microsoft Windows App Runtime x64 packages\.')
        }
        foreach ($required in $requirements) {
            if (-not ($lines -match $required)) { throw "The integration log did not prove the required step: $required" }
        }
        if ($Mode -eq 'Integration' -and $script:dotNetDetectionCalls -lt 2) { throw 'The integration did not recheck the real runtime after installation.' }
        if ($Mode -eq 'RuntimeFallback' -and (-not $script:injectedMissingModule -or $script:appRuntimeDetectionCalls -lt 2)) {
            throw 'The runtime recovery integration did not inject the native error and recheck the real user registrations.'
        }
        $scope = if ($Mode -eq 'Integration') { 'Actual Microsoft installer execution through the production missing-runtime branch; initial detection forced false. This is not bare Windows 10/11 qualification.' } else { 'Real official download, Microsoft signature verification, resource extraction, manifest validation and package registration after an injected native 0x8007007E. Existing healthy registrations can be reused; this is not bare Windows 10/11 qualification.' }
        $report = [ordered]@{
            mode = $Mode
            host = 'Windows PowerShell 5.1 x64'
            baselineDotNetRuntimePresent = $baselineRuntimePresent
            forcedMissingRuntimeBranch = ($Mode -eq 'Integration')
            retainedExistingSdk = $true
            officialMetadataHashSignatureAndInstallerVerified = ($Mode -eq 'Integration')
            forcedNativeMissingModuleError = $script:injectedMissingModule
            verifiedResourceRecovery = ($Mode -eq 'RuntimeFallback')
            realRuntimeVerifiedAfterInstall = $true
            exitCode = $resultCode
            scope = $scope
        }
        $report | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $OutputDirectory ($Mode.ToLowerInvariant() + '.json')) -Encoding UTF8
        Write-Host "PASS: $Mode; verified official runtime path and real post-install detection."
    }
    finally {
        Set-Item Function:Test-DotNetRuntime -Value $script:actualDotNetTest
        Set-Item Function:Test-WindowsAppRuntime -Value $script:actualAppRuntimeTest
        Set-Item Function:Invoke-RuntimeInstaller -Value $script:actualRuntimeInstaller
    }
    exit 0
}

$script:passed = [Collections.Generic.List[string]]::new()
function Assert-Fixture([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
}
function Assert-FixtureThrows([scriptblock]$Action, [string]$MessagePattern) {
    $caught = $false
    try { & $Action | Out-Null }
    catch {
        $caught = $true
        Assert-Fixture ($_.Exception.Message -match $MessagePattern) ("Unexpected failure: " + $_.Exception.Message)
    }
    Assert-Fixture $caught 'An unsafe or failed prerequisite operation was accepted.'
}
function Invoke-Fixture([string]$Name, [scriptblock]$Action) {
    & $Action
    $script:passed.Add($Name)
    Write-Host "PASS: $Name"
}
function New-RuntimePackages {
    foreach ($family in @(
        'Microsoft.WindowsAppRuntime.1.8_8wekyb3d8bbwe',
        'MicrosoftCorporationII.WinAppRuntime.Main.1.8_8wekyb3d8bbwe',
        'MicrosoftCorporationII.WinAppRuntime.Singleton_8wekyb3d8bbwe',
        'Microsoft.WinAppRuntime.DDLM.8000.994.2142.0-x6_8wekyb3d8bbwe'
    )) {
        [PSCustomObject]@{ Name = ($family -split '_')[0]; PackageFamilyName = $family; Version = '8000.994.2142.0'; Architecture = 'X64'; Status = 'Ok' }
    }
}
function New-RuntimeMetadata {
    [PSCustomObject]@{
        'channel-version' = '10.0'
        'latest-runtime' = '10.0.12'
        releases = @([PSCustomObject]@{ runtime = [PSCustomObject]@{
            version = '10.0.12'
            files = @([PSCustomObject]@{
                rid = 'win-x64'; name = 'dotnet-runtime-win-x64.exe'
                hash = ('a' * 128); url = 'https://builds.dotnet.microsoft.com/dotnet/Runtime/10.0.12/dotnet-runtime-10.0.12-win-x64.exe'
            })
        } })
    }
}

Invoke-Fixture 'Approved Microsoft HTTPS download accepted' {
    Assert-MicrosoftDownloadUri ([Uri]'https://builds.dotnet.microsoft.com/dotnet/Runtime/runtime.exe')
}
foreach ($uriText in @(
    'http://download.microsoft.com/runtime.exe',
    'https://download.microsoft.com:444/runtime.exe',
    'https://download.microsoft.com.evil.example/runtime.exe',
    'https://credentials@download.microsoft.com/runtime.exe',
    'https://download.microsoft.com/runtime.exe#fragment',
    '/relative/runtime.exe'
)) {
    Invoke-Fixture ("Reject unsafe download address: $uriText") {
        Assert-FixtureThrows { Assert-MicrosoftDownloadUri ([Uri]$uriText) } 'approved Microsoft HTTPS'
    }
}
Invoke-Fixture 'Stable .NET x64 EXE selected from official metadata shape' {
    $selected = Get-DotNetRuntimeInstallerFromMetadata (New-RuntimeMetadata)
    Assert-Fixture ($selected.rid -eq 'win-x64' -and $selected.name -eq 'dotnet-runtime-win-x64.exe') 'Wrong .NET package selected.'
}
Invoke-Fixture 'Reject prerelease .NET metadata' {
    $metadata = New-RuntimeMetadata
    $metadata.'latest-runtime' = '10.0.13-preview.1'
    Assert-FixtureThrows { Get-DotNetRuntimeInstallerFromMetadata $metadata } 'unsupported .NET runtime metadata'
}
Invoke-Fixture 'Reject a different runtime channel' {
    $metadata = New-RuntimeMetadata
    $metadata.'channel-version' = '11.0'
    Assert-FixtureThrows { Get-DotNetRuntimeInstallerFromMetadata $metadata } 'unsupported .NET runtime metadata'
}
Invoke-Fixture 'Reject duplicate stable releases' {
    $metadata = New-RuntimeMetadata
    $metadata.releases += $metadata.releases[0]
    Assert-FixtureThrows { Get-DotNetRuntimeInstallerFromMetadata $metadata } 'one stable release'
}
Invoke-Fixture 'Reject wrong runtime architecture' {
    $metadata = New-RuntimeMetadata
    $metadata.releases[0].runtime.files[0].rid = 'win-arm64'
    Assert-FixtureThrows { Get-DotNetRuntimeInstallerFromMetadata $metadata } 'verified x64 installer'
}
Invoke-Fixture 'Reject missing SHA-512 metadata' {
    $metadata = New-RuntimeMetadata
    $metadata.releases[0].runtime.files[0].hash = ''
    Assert-FixtureThrows { Get-DotNetRuntimeInstallerFromMetadata $metadata } 'verified x64 installer'
}
Invoke-Fixture 'Reject metadata that redirects selection to another publisher' {
    $metadata = New-RuntimeMetadata
    $metadata.releases[0].runtime.files[0].url = 'https://example.com/runtime.exe'
    Assert-FixtureThrows { Get-DotNetRuntimeInstallerFromMetadata $metadata } 'approved Microsoft HTTPS'
}
Invoke-Fixture 'All four healthy current-user x64 runtime packages accepted' {
    Assert-Fixture (Test-WindowsAppRuntimePackages @(New-RuntimePackages)) 'Complete runtime registration was rejected.'
}
for ($packageIndex = 0; $packageIndex -lt 4; $packageIndex++) {
    Invoke-Fixture ("Incomplete runtime registration rejected: package $packageIndex") {
        $packages = @(New-RuntimePackages)
        $packages[$packageIndex].Status = 'Modified'
        Assert-Fixture (-not (Test-WindowsAppRuntimePackages $packages)) 'Unhealthy package registration was accepted.'
    }
}
Invoke-Fixture 'x86 runtime packages cannot satisfy the x64 app' {
    $packages = @(New-RuntimePackages)
    foreach ($package in $packages) { $package.Architecture = 'X86' }
    Assert-Fixture (-not (Test-WindowsAppRuntimePackages $packages)) 'Wrong architecture was accepted.'
}
Invoke-Fixture 'Older framework cannot satisfy this SDK minimum' {
    $packages = @(New-RuntimePackages)
    $packages[0].Version = '8000.600.0.0'
    Assert-Fixture (-not (Test-WindowsAppRuntimePackages $packages)) 'Older framework was accepted.'
}
Invoke-Fixture 'Malformed package version is treated as missing' {
    $packages = @(New-RuntimePackages)
    $packages[0].Version = 'invalid'
    Assert-Fixture (-not (Test-WindowsAppRuntimePackages $packages)) 'Invalid framework version was accepted.'
}

function Get-AuthenticodeSignature {
    param([string]$LiteralPath)
    return $script:signatureFixture
}
Invoke-Fixture 'Trusted Microsoft publisher accepted' {
    $script:signatureFixture = [PSCustomObject]@{ Status = 'Valid'; SignerCertificate = [PSCustomObject]@{ Subject = 'CN=Microsoft Corporation, O=Microsoft Corporation, C=US' } }
    Assert-MicrosoftSignature 'fixture.exe'
}
Invoke-Fixture 'A different valid publisher cannot install prerequisites' {
    $script:signatureFixture = [PSCustomObject]@{ Status = 'Valid'; SignerCertificate = [PSCustomObject]@{ Subject = 'CN=Other Publisher, O=Other Publisher, C=US' } }
    Assert-FixtureThrows { Assert-MicrosoftSignature 'fixture.exe' } 'publisher verification'
}
Invoke-Fixture 'Invalid Microsoft signature cannot install prerequisites' {
    $script:signatureFixture = [PSCustomObject]@{ Status = 'NotTrusted'; SignerCertificate = [PSCustomObject]@{ Subject = 'CN=Microsoft Corporation, O=Microsoft Corporation, C=US' } }
    Assert-FixtureThrows { Assert-MicrosoftSignature 'fixture.exe' } 'publisher verification'
}

function Start-Process {
    [CmdletBinding()]
    param([string]$FilePath, [string[]]$ArgumentList, [switch]$PassThru, [string]$Verb,
        [string]$WindowStyle, [string]$RedirectStandardOutput, [string]$RedirectStandardError)
    if ($script:cancelPermission) { throw [ComponentModel.Win32Exception]::new(1223) }
    $process = [PSCustomObject]@{ ExitCode = $script:installerExitCode; WaitResult = $script:waitResult; Disposed = $false }
    $process | Add-Member ScriptMethod WaitForExit { param([int]$Milliseconds); return $this.WaitResult }
    $process | Add-Member ScriptMethod Dispose { $this.Disposed = $true }
    $script:lastProcess = $process
    return $process
}
$script:cancelPermission = $false
$script:waitResult = $true
foreach ($runtimeCode in @(0, 3010, 1641)) {
    Invoke-Fixture ("Microsoft installer result handled: $runtimeCode") {
        $script:restartRequired = $false
        $script:installerExitCode = $runtimeCode
        Invoke-RuntimeInstaller 'fixture.exe' @('/quiet')
        Assert-Fixture ($script:restartRequired -eq ($runtimeCode -ne 0)) 'Restart requirement was not propagated.'
        Assert-Fixture $script:lastProcess.Disposed 'Installer process was not disposed.'
    }
}
Invoke-Fixture 'Installer failure includes the Windows error code' {
    $script:installerExitCode = -2147009287
    Assert-FixtureThrows { Invoke-RuntimeInstaller 'fixture.exe' @('/quiet') } '0x80073CF9'
    Assert-Fixture $script:lastProcess.Disposed 'Failed installer process was not disposed.'
}
Invoke-Fixture 'Missing-module failure identifies the component and separate log' {
    $script:installerExitCode = -2147024770
    Assert-FixtureThrows { Invoke-RuntimeInstaller 'windowsappruntimeinstall-x64.exe' @('--quiet') } 'windowsappruntimeinstall-x64\.exe.*required Windows module.*0x8007007E'
    Assert-Fixture $script:lastProcess.Disposed 'Missing-module process handle was not disposed.'
}
Invoke-Fixture 'Cancelled Windows permission has an actionable retry message' {
    $script:cancelPermission = $true
    Assert-FixtureThrows { Invoke-RuntimeInstaller 'fixture.exe' @('/quiet') } 'administrator permission was cancelled'
    $script:cancelPermission = $false
}
Invoke-Fixture 'Installer timeout advises waiting instead of killing a shared installer' {
    $script:waitResult = $false
    Assert-FixtureThrows { Invoke-RuntimeInstaller 'fixture.exe' @('/quiet') } 'still running.*Wait for it to finish'
    Assert-Fixture $script:lastProcess.Disposed 'Timed-out process handle was not disposed.'
    $script:waitResult = $true
}

function New-ManifestArchive {
    param([string]$FileName, [string]$Name, [string]$Version = '8000.994.2142.0',
        [string]$Architecture = 'x64',
        [string]$Publisher = 'CN=Microsoft Corporation, O=Microsoft Corporation, L=Redmond, S=Washington, C=US',
        [bool]$IncludeSignature = $true)
    Add-Type -AssemblyName System.IO.Compression
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $path = Join-Path $OutputDirectory $FileName
    if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path -Force }
    $archive = [IO.Compression.ZipFile]::Open($path, [IO.Compression.ZipArchiveMode]::Create)
    try {
        $manifest = $archive.CreateEntry('AppxManifest.xml')
        $writer = [IO.StreamWriter]::new($manifest.Open())
        try { $writer.Write("<Package xmlns=`"http://schemas.microsoft.com/appx/manifest/foundation/windows10`"><Identity Name=`"$Name`" Publisher=`"$Publisher`" ProcessorArchitecture=`"$Architecture`" Version=`"$Version`" /></Package>") }
        finally { $writer.Dispose() }
        # Unit fixture sentinel only. Actual signature validation is performed
        # by Windows deployment in RuntimeFallback mode, never by this fixture.
        if ($IncludeSignature) { $archive.CreateEntry('AppxSignature.p7x') | Out-Null }
    }
    finally { $archive.Dispose() }
    return $path
}
Invoke-Fixture 'Recovery validates the Microsoft x64 manifest identity' {
    $path = New-ManifestArchive 'identity-valid.msix' 'Microsoft.WindowsAppRuntime.1.8'
    $identity = Get-WindowsAppRuntimePackageIdentity $path 'Microsoft.WindowsAppRuntime.1.8'
    Assert-Fixture ($identity.Version -eq $minimumWindowsAppRuntimeVersion) 'Wrong recovery package version.'
}
Invoke-Fixture 'Recovery rejects a wrong publisher before deployment' {
    $path = New-ManifestArchive 'identity-publisher.msix' 'Microsoft.WindowsAppRuntime.1.8' -Publisher 'CN=Other Publisher'
    Assert-FixtureThrows { Get-WindowsAppRuntimePackageIdentity $path 'Microsoft.WindowsAppRuntime.1.8' } 'Microsoft x64 runtime identity'
}
Invoke-Fixture 'Recovery rejects a wrong architecture before deployment' {
    $path = New-ManifestArchive 'identity-architecture.msix' 'Microsoft.WindowsAppRuntime.1.8' -Architecture 'x86'
    Assert-FixtureThrows { Get-WindowsAppRuntimePackageIdentity $path 'Microsoft.WindowsAppRuntime.1.8' } 'Microsoft x64 runtime identity'
}
Invoke-Fixture 'Recovery rejects an obsolete runtime before deployment' {
    $path = New-ManifestArchive 'identity-obsolete.msix' 'Microsoft.WindowsAppRuntime.1.8' -Version '8000.946.1701.0'
    Assert-FixtureThrows { Get-WindowsAppRuntimePackageIdentity $path 'Microsoft.WindowsAppRuntime.1.8' } 'Microsoft x64 runtime identity'
}
Invoke-Fixture 'Recovery rejects a wrong package component before deployment' {
    $path = New-ManifestArchive 'identity-wrong-component.msix' 'Microsoft.WindowsAppRuntime.1.8'
    Assert-FixtureThrows { Get-WindowsAppRuntimePackageIdentity $path 'MicrosoftCorporationII.WinAppRuntime.Main.1.8' } 'required component'
}
Invoke-Fixture 'Recovery rejects a package without its signature before deployment' {
    $path = New-ManifestArchive 'identity-unsigned.msix' 'Microsoft.WindowsAppRuntime.1.8' -IncludeSignature $false
    Assert-FixtureThrows { Get-WindowsAppRuntimePackageIdentity $path 'Microsoft.WindowsAppRuntime.1.8' } 'signed manifest'
}

$script:recoveryFiles = @(
    (New-ManifestArchive 'recovery-framework.msix' 'Microsoft.WindowsAppRuntime.1.8'),
    (New-ManifestArchive 'recovery-main.msix' 'MicrosoftCorporationII.WinAppRuntime.Main.1.8'),
    (New-ManifestArchive 'recovery-singleton.msix' 'MicrosoftCorporationII.WinAppRuntime.Singleton'),
    (New-ManifestArchive 'recovery-ddlm.msix' 'Microsoft.WinAppRuntime.DDLM.8000.994.2142.0-x6')
)
$script:deploymentCalls = [Collections.Generic.List[string]]::new()
$script:signatureFixture = [PSCustomObject]@{ Status = 'Valid'; SignerCertificate = [PSCustomObject]@{ Subject = 'CN=Microsoft Corporation, O=Microsoft Corporation, C=US' } }
function Expand-WindowsAppRuntimePackages {
    param([string]$Installer, [string]$Destination)
    return $script:recoveryFiles
}
function Get-AppxPackage {
    [CmdletBinding()]
    param([string]$Name)
    if ($Name -eq 'MicrosoftCorporationII.WinAppRuntime.Singleton') {
        [PSCustomObject]@{ Name = $Name; PackageFamilyName = ($Name + '_8wekyb3d8bbwe'); Architecture = 'X64'; Status = 'Ok'; Version = '8002.5.1.0' }
    }
}
function Add-AppxPackage {
    [CmdletBinding()]
    param([string]$Path)
    $script:deploymentCalls.Add([IO.Path]::GetFileName($Path))
}
Invoke-Fixture 'Missing-module recovery deploys framework first and preserves the newer Singleton' {
    $script:deploymentCalls.Clear()
    $script:installerExitCode = -2147024770
    Invoke-WindowsAppRuntimeInstaller 'windowsappruntimeinstall-x64.exe' $OutputDirectory
    Assert-Fixture (($script:deploymentCalls -join ',') -eq 'recovery-framework.msix,recovery-main.msix,recovery-ddlm.msix') 'Recovery downgraded the newer Singleton or deployed dependencies in the wrong order.'
}
Invoke-Fixture 'Recovery is restricted to the reported missing-module result' {
    $script:deploymentCalls.Clear()
    $script:installerExitCode = -2147009287
    Assert-FixtureThrows { Invoke-WindowsAppRuntimeInstaller 'windowsappruntimeinstall-x64.exe' $OutputDirectory } '0x80073CF9'
    Assert-Fixture ($script:deploymentCalls.Count -eq 0) 'An unrelated failure entered resource recovery.'
}
Invoke-Fixture 'All recovery identities are verified before any package is installed' {
    $savedFiles = $script:recoveryFiles
    try {
        $script:recoveryFiles = @($savedFiles[0], $savedFiles[1], $savedFiles[2], (New-ManifestArchive 'recovery-invalid-ddlm.msix' 'Wrong.Package'))
        $script:deploymentCalls.Clear()
        Assert-FixtureThrows { Install-WindowsAppRuntimePackages 'windowsappruntimeinstall-x64.exe' $OutputDirectory } 'required component'
        Assert-Fixture ($script:deploymentCalls.Count -eq 0) 'Recovery installed a package before validating the complete set.'
    }
    finally { $script:recoveryFiles = $savedFiles }
}
Invoke-Fixture 'Recovery refuses a runtime EXE without a trusted Microsoft signature' {
    $script:signatureFixture = [PSCustomObject]@{ Status = 'NotTrusted'; SignerCertificate = $null }
    $script:deploymentCalls.Clear()
    Assert-FixtureThrows { Install-WindowsAppRuntimePackages 'windowsappruntimeinstall-x64.exe' $OutputDirectory } 'publisher verification'
    Assert-Fixture ($script:deploymentCalls.Count -eq 0) 'Recovery deployed resources from an untrusted installer.'
}

# The main error path is tested without touching package registrations or
# network state. The integration invocation above keeps these functions real.
function Test-DotNetRuntime { return $false }
function Test-WindowsAppRuntime { return $true }
function Save-MicrosoftDownload { throw 'Network path blocked in fixture.' }
Invoke-Fixture 'Offline setup returns failure, identifies the phase, and writes diagnostics' {
    $resultCode = Invoke-NotchlingPrerequisites
    Assert-Fixture ($resultCode -eq 1) 'Offline prerequisite setup did not fail.'
    $resultText = [IO.File]::ReadAllText($fixtureResult)
    Assert-Fixture ($resultText -match 'downloading and verifying.*\.NET 10.*Network path blocked') 'Failure did not identify the download phase.'
    Assert-Fixture ($resultText.Contains($fixtureLog)) 'Failure did not identify the diagnostic log.'
}

[ordered]@{ mode = $Mode; host = 'Windows PowerShell 5.1 x64'; passed = $script:passed.Count; tests = @($script:passed); scope = 'Executable fixture cases; runtime/network/process responses are doubled. No installers run in Unit mode.' } |
    ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $OutputDirectory 'unit.json') -Encoding UTF8
Write-Host "PASS: $($script:passed.Count) prerequisite fixture cases."
