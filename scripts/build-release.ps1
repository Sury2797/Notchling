[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version,
    [string]$OutputDirectory = 'artifacts/release',
    [string]$Iscc = 'C:\Program Files (x86)\Inno Setup 6\ISCC.exe',
    [ValidatePattern('^https://[A-Za-z0-9.-]+(?::[0-9]+)?(?:/[A-Za-z0-9._~/-]*)?$')][string]$TimestampUrl = 'https://timestamp.digicert.com'
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if (-not $IsWindows) { throw 'A signed Windows release must be built on Windows.' }
$root = Split-Path -Parent $PSScriptRoot
$publishRoot = Join-Path $root ('artifacts/publish-' + [Guid]::NewGuid().ToString('N'))
$release = [IO.Path]::GetFullPath((Join-Path $root $OutputDirectory))
$temporaryPfx = Join-Path ([IO.Path]::GetTempPath()) ('notchling-signing-' + [Guid]::NewGuid().ToString('N') + '.pfx')
$certificate = $null
$previousCertificates = @(Get-ChildItem Cert:\CurrentUser\My | Select-Object -ExpandProperty Thumbprint)
$allowedTimestamp = [Uri]$TimestampUrl
if ($allowedTimestamp.Scheme -ne 'https') { throw 'Timestamp service must use HTTPS.' }
if (-not $env:NOTCH_SIGNING_PFX_BASE64 -or -not $env:NOTCH_SIGNING_PFX_PASSWORD) {
    throw 'Signing is required. Configure NOTCH_SIGNING_PFX_BASE64 and NOTCH_SIGNING_PFX_PASSWORD; unsigned public releases are refused.'
}
if (-not (Test-Path $Iscc)) { throw 'Install Inno Setup 6 and pass -Iscc with its compiler path.' }
$sdkRoot = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin'
$signtool = Get-ChildItem $sdkRoot -Filter signtool.exe -Recurse | Where-Object { $_.Directory.Name -eq 'x64' } | Sort-Object FullName -Descending | Select-Object -First 1 -ExpandProperty FullName
if (-not $signtool) { throw 'Windows SDK x64 signtool.exe is required.' }

function Invoke-Checked([string]$File, [string[]]$Arguments) {
    & $File @Arguments
    if ($LASTEXITCODE -ne 0) { throw "$File failed with exit code $LASTEXITCODE." }
}
function Assert-Signed([string]$Path, [string]$Thumbprint) {
    $signature = Get-AuthenticodeSignature -LiteralPath $Path
    if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Thumbprint -ne $Thumbprint) {
        throw "Release signature verification failed for $Path."
    }
}

try {
    [IO.File]::WriteAllBytes($temporaryPfx, [Convert]::FromBase64String($env:NOTCH_SIGNING_PFX_BASE64))
    $password = ConvertTo-SecureString $env:NOTCH_SIGNING_PFX_PASSWORD -AsPlainText -Force
    $importedCertificates = @(Import-PfxCertificate -FilePath $temporaryPfx -CertStoreLocation Cert:\CurrentUser\My -Password $password)
    $certificate = $importedCertificates | Where-Object HasPrivateKey | Select-Object -First 1
    if (-not $certificate.HasPrivateKey -or $certificate.NotAfter -le (Get-Date)) { throw 'A current certificate with a private key is required.' }
    if (-not ($certificate.EnhancedKeyUsageList.ObjectId -contains '1.3.6.1.5.5.7.3.3')) { throw 'The certificate must permit code signing.' }
    if ((Test-Path -LiteralPath $release) -and @(Get-ChildItem -LiteralPath $release -Force).Count -gt 0) { throw 'The signed release output directory must be empty; choose a fresh path to avoid stale release assets.' }
    New-Item -ItemType Directory -Path $release -Force | Out-Null
    Invoke-Checked 'dotnet' @('run', '--project', (Join-Path $root 'tests/Notch.Core.Tests/Notch.Core.Tests.csproj'), '--configuration', 'Release')
    Invoke-Checked 'dotnet' @('run', '--project', (Join-Path $root 'tests/Notch.Commerce.Tests/Notch.Commerce.Tests.csproj'), '--configuration', 'Release')
    Invoke-Checked 'dotnet' @('run', '--project', (Join-Path $root 'tests/Notch.ViewModel.Tests/Notch.ViewModel.Tests.csproj'), '--configuration', 'Debug')
    Invoke-Checked 'dotnet' @('run', '--project', (Join-Path $root 'tests/Notch.ViewModel.Tests/Notch.ViewModel.Tests.csproj'), '--configuration', 'Release')
    Invoke-Checked 'dotnet' @('run', '--project', (Join-Path $root 'tests/Notch.Native.Tests/Notch.Native.Tests.csproj'), '--configuration', 'Release')
    Invoke-Checked 'python' @('-m', 'unittest', 'discover', '-s', (Join-Path $root 'tests/release'), '-v')
    $setupInteropOutput = Join-Path $publishRoot 'setup-interop'
    Invoke-Checked 'dotnet' @('build', (Join-Path $root 'packaging/windows/native-interop/Notchling.Setup.Interop.csproj'), '--configuration', 'Release', '--output', $setupInteropOutput)
    $setupInterop = Join-Path $setupInteropOutput 'Notchling.Setup.Interop.dll'
    Invoke-Checked $signtool @('sign', '/s', 'My', '/sha1', $certificate.Thumbprint, '/fd', 'SHA256', '/tr', $TimestampUrl, '/td', 'SHA256', $setupInterop)
    Assert-Signed $setupInterop $certificate.Thumbprint
    $installerLicense = Join-Path (Split-Path -Parent $Iscc) 'license.txt'
    $installerVersion = (Get-Item -LiteralPath $Iscc).VersionInfo.FileVersion
    if (-not (Test-Path $installerLicense)) { throw 'The installed Inno Setup publisher license.txt must be included in release notices.' }
    $signCommand = '"' + $signtool + '" sign /s My /sha1 ' + $certificate.Thumbprint + ' /fd SHA256 /tr ' + $TimestampUrl + ' /td SHA256 $f'
    $publicKeyPin = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($certificate.GetPublicKey())).ToLowerInvariant()
    $updateAssets = @()
    foreach ($architecture in @('x64', 'x86', 'arm64')) {
        $platform = if ($architecture -eq 'arm64') { 'ARM64' } else { $architecture }
        $publish = Join-Path $publishRoot $architecture
        Invoke-Checked 'dotnet' @('publish', (Join-Path $root 'src/Notch.Windows/Notch.Windows.csproj'), '--configuration', 'Release', '--runtime', "win-$architecture", '--self-contained', 'false', "-p:Platform=$platform", '-p:WindowsPackageType=None', '-p:WindowsAppSDKSelfContained=false', '-p:PublishSingleFile=false', "-p:Version=$Version", "-p:FileVersion=$Version.0", "-p:AssemblyVersion=$Version.0", '--output', $publish)
        # Sign only project-owned binaries; preserve publisher signatures on dependencies.
        foreach ($file in @('Notchling.Windows.exe', 'Notchling.Windows.dll', 'Notch.Core.dll')) {
            $path = Join-Path $publish $file
            Invoke-Checked $signtool @('sign', '/s', 'My', '/sha1', $certificate.Thumbprint, '/fd', 'SHA256', '/tr', $TimestampUrl, '/td', 'SHA256', $path)
            Assert-Signed $path $certificate.Thumbprint
        }
        Invoke-Checked 'python' @((Join-Path $root 'scripts/bundle-notices.py'), '--publish', $publish, '--assets', (Join-Path $root 'src/Notch.Windows/obj/project.assets.json'), '--strict', '--installer-license', $installerLicense, '--installer-version', $installerVersion)
        $sizeReport = Join-Path $release "package-size-$architecture.json"
        Invoke-Checked 'python' @((Join-Path $root 'scripts/report-package-size.py'), '--publish', $publish, '--architecture', $architecture, '--require-app-only', '--output', $sizeReport)
        Invoke-Checked $Iscc @("/DAppArchitecture=$architecture", "/DAppVersion=$Version", "/DPublishDirectory=$publish", "/DReleaseDirectory=$release", "/DNativeInteropPath=$setupInterop", "/Snotch=$signCommand", (Join-Path $root 'packaging/windows/Notch.iss'))
        $installer = Join-Path $release "Notchling-$Version-windows-$architecture-setup.exe"
        Assert-Signed $installer $certificate.Thumbprint
        Invoke-Checked 'python' @((Join-Path $root 'scripts/report-package-size.py'), '--publish', $publish, '--architecture', $architecture, '--require-app-only', '--installer', $installer, '--output', $sizeReport)
        $updateAssets += [ordered]@{
            architecture = $architecture
            installerUrl = "https://github.com/Sury2797/Notchling/releases/download/v$Version/Notchling-$Version-windows-$architecture-setup.exe"
            sha256 = (Get-FileHash -LiteralPath $installer -Algorithm SHA256).Hash.ToLowerInvariant()
            sizeBytes = (Get-Item $installer).Length
        }
        Copy-Item (Join-Path $publish 'sbom.spdx.json') (Join-Path $release "sbom-$architecture.spdx.json")
        Copy-Item (Join-Path $publish 'publish-inventory.json') (Join-Path $release "publish-inventory-$architecture.json")
    }
    $manifest = [ordered]@{
        schemaVersion = 2
        version = $Version
        minimumWindowsBuild = 19045
        signerPublicKeySha256 = $publicKeyPin
        assets = $updateAssets
    }
    $manifest | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $release 'notchling-update.json') -Encoding utf8NoBOM
    # Hash every installer and accompanying report, including the architecture-bound
    # manifest. Download/launch still requires the installed publisher's signature.
    $checksums = Join-Path $release 'SHA256SUMS'
    Remove-Item -LiteralPath $checksums -Force -ErrorAction SilentlyContinue
    foreach ($file in (Get-ChildItem -LiteralPath $release -File | Sort-Object Name)) {
        $hash = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        "$hash  $($file.Name)" | Add-Content -LiteralPath $checksums -Encoding utf8NoBOM
    }
    Write-Output "Signed release prepared in $release. Publisher public-key pin: $publicKeyPin"
}
finally {
    Remove-Item -LiteralPath $publishRoot -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $temporaryPfx -Force -ErrorAction SilentlyContinue
    if ($certificate -and $certificate.Thumbprint -notin $previousCertificates) { Remove-Item -LiteralPath "Cert:\CurrentUser\My\$($certificate.Thumbprint)" -Force -ErrorAction SilentlyContinue }
}
