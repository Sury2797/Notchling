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
$publish = Join-Path $root ('artifacts/publish-' + [Guid]::NewGuid().ToString('N'))
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
    New-Item -ItemType Directory -Path $release -Force | Out-Null
    Invoke-Checked 'dotnet' @('run', '--project', (Join-Path $root 'tests/Notch.Core.Tests/Notch.Core.Tests.csproj'), '--configuration', 'Release')
    Invoke-Checked 'dotnet' @('run', '--project', (Join-Path $root 'tests/Notch.Commerce.Tests/Notch.Commerce.Tests.csproj'), '--configuration', 'Release')
    Invoke-Checked 'dotnet' @('run', '--project', (Join-Path $root 'tests/Notch.ViewModel.Tests/Notch.ViewModel.Tests.csproj'), '--configuration', 'Debug')
    Invoke-Checked 'dotnet' @('run', '--project', (Join-Path $root 'tests/Notch.ViewModel.Tests/Notch.ViewModel.Tests.csproj'), '--configuration', 'Release')
    Invoke-Checked 'dotnet' @('publish', (Join-Path $root 'src/Notch.Windows/Notch.Windows.csproj'), '--configuration', 'Release', '--runtime', 'win-x64', '--self-contained', 'false', '-p:Platform=x64', '-p:WindowsPackageType=None', '-p:WindowsAppSDKSelfContained=false', '-p:PublishSingleFile=false', "-p:Version=$Version", "-p:FileVersion=$Version.0", "-p:AssemblyVersion=$Version.0", '--output', $publish)
    # Sign only project-owned binaries; preserve publisher signatures on dependencies.
    foreach ($file in @('Notchling.Windows.exe', 'Notchling.Windows.dll', 'Notch.Core.dll')) {
        $path = Join-Path $publish $file
        Invoke-Checked $signtool @('sign', '/s', 'My', '/sha1', $certificate.Thumbprint, '/fd', 'SHA256', '/tr', $TimestampUrl, '/td', 'SHA256', $path)
        Assert-Signed $path $certificate.Thumbprint
    }
    $installerLicense = Join-Path (Split-Path -Parent $Iscc) 'license.txt'
    $installerVersion = (Get-Item -LiteralPath $Iscc).VersionInfo.FileVersion
    if (-not (Test-Path $installerLicense)) { throw 'The installed Inno Setup publisher license.txt must be included in release notices.' }
    Invoke-Checked 'python' @((Join-Path $root 'scripts/bundle-notices.py'), '--publish', $publish, '--assets', (Join-Path $root 'src/Notch.Windows/obj/project.assets.json'), '--strict', '--installer-license', $installerLicense, '--installer-version', $installerVersion)
    Invoke-Checked 'python' @((Join-Path $root 'scripts/report-package-size.py'), '--publish', $publish, '--require-app-only', '--output', (Join-Path $release 'package-size.json'))
    $signCommand = '"' + $signtool + '" sign /s My /sha1 ' + $certificate.Thumbprint + ' /fd SHA256 /tr ' + $TimestampUrl + ' /td SHA256 $f'
    Invoke-Checked $Iscc @("/DAppVersion=$Version", "/DPublishDirectory=$publish", "/DReleaseDirectory=$release", "/Snotch=$signCommand", (Join-Path $root 'packaging/windows/Notch.iss'))
    $installer = Join-Path $release "Notchling-$Version-windows-x64-setup.exe"
    Assert-Signed $installer $certificate.Thumbprint
    Invoke-Checked 'python' @((Join-Path $root 'scripts/report-package-size.py'), '--publish', $publish, '--require-app-only', '--installer', $installer, '--output', (Join-Path $release 'package-size.json'))
    $publicKeyPin = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($certificate.GetPublicKey())).ToLowerInvariant()
    $manifest = [ordered]@{
        schemaVersion = 1
        version = $Version
        minimumWindowsBuild = 19045
        architecture = 'x64'
        installerUrl = "https://github.com/SuryaK999/Notchling/releases/download/v$Version/Notchling-$Version-windows-x64-setup.exe"
        sha256 = (Get-FileHash -LiteralPath $installer -Algorithm SHA256).Hash.ToLowerInvariant()
        signerPublicKeySha256 = $publicKeyPin
        sizeBytes = (Get-Item $installer).Length
    }
    $manifest | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $release 'notchling-update.json') -Encoding utf8NoBOM
    # Checksums cover installer + manifest. Update helpers also verify
    # Authenticode against the already-trusted installed publisher before launch.
    Remove-Item -LiteralPath (Join-Path $release 'SHA256SUMS') -Force -ErrorAction SilentlyContinue
    foreach ($file in @($installer, (Join-Path $release 'notchling-update.json'))) {
        $hash = (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.ToLowerInvariant()
        "$hash  $([IO.Path]::GetFileName($file))" | Add-Content -LiteralPath (Join-Path $release 'SHA256SUMS') -Encoding utf8NoBOM
    }
    Copy-Item (Join-Path $publish 'sbom.spdx.json') $release
    Copy-Item (Join-Path $publish 'publish-inventory.json') $release
    Write-Output "Signed release prepared in $release. Publisher public-key pin: $publicKeyPin"
}
finally {
    Remove-Item -LiteralPath $publish -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $temporaryPfx -Force -ErrorAction SilentlyContinue
    if ($certificate -and $certificate.Thumbprint -notin $previousCertificates) { Remove-Item -LiteralPath "Cert:\CurrentUser\My\$($certificate.Thumbprint)" -Force -ErrorAction SilentlyContinue }
}
