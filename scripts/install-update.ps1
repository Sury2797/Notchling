[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidatePattern('^[0-9a-fA-F]{64}$')][string]$TrustedPublisherPublicKeySha256,
    [Parameter(Mandatory)][ValidatePattern('^\d+\.\d+\.\d+$')][string]$CurrentVersion,
    [string]$ManifestUrl = 'https://github.com/Sury2797/Notchling/releases/latest/download/notchling-update.json',
    [ValidateSet('', 'x64', 'x86', 'arm64')][string]$Architecture = ''
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
Add-Type -AssemblyName System.Net.Http
# Trust is supplied by the installed/signed release, never taken from the remote
# manifest. The default GitHub stable channel is the only permitted source.
$manifestUri = [Uri]$ManifestUrl
if ($manifestUri.AbsoluteUri -ne 'https://github.com/Sury2797/Notchling/releases/latest/download/notchling-update.json') {
    throw 'Only the official stable update manifest is accepted.'
}
if ([Environment]::OSVersion.Version.Build -lt 19045) { throw 'Updates require Windows 10 22H2/build 19045 or Windows 11.' }
# Upgrade the installed application architecture; a 32-bit app may run on a
# 64-bit OS. Callers launching a different PowerShell host must pass its app arch.
if (-not $Architecture) {
    $Architecture = switch ($env:PROCESSOR_ARCHITECTURE) { 'AMD64' { 'x64' }; 'x86' { 'x86' }; 'ARM64' { 'arm64' }; default { throw 'Unsupported updater process architecture.' } }
}
function Test-OfficialHttpsReply($Reply) {
    $uri = $Reply.RequestMessage.RequestUri
    return $uri.Scheme -eq 'https' -and $uri.IsDefaultPort -and -not $uri.UserInfo -and
        $uri.Host -in @('github.com', 'api.github.com', 'release-assets.githubusercontent.com', 'objects.githubusercontent.com')
}
$work = Join-Path ([IO.Path]::GetTempPath()) ('Notchling.Update-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $work | Out-Null
try {
    $manifestClient = [Net.Http.HttpClient]::new()
    $manifestDeadline = [Threading.CancellationTokenSource]::new([TimeSpan]::FromSeconds(20))
    $manifestReply = $null; $manifestStream = $null; $manifestBuffer = [IO.MemoryStream]::new()
    try {
        $manifestReply = $manifestClient.GetAsync($manifestUri, [Net.Http.HttpCompletionOption]::ResponseHeadersRead, $manifestDeadline.Token).GetAwaiter().GetResult()
        $manifestReply.EnsureSuccessStatusCode() | Out-Null
        if (-not (Test-OfficialHttpsReply $manifestReply)) { throw 'An untrusted manifest redirect was refused.' }
        $manifestStream = $manifestReply.Content.ReadAsStreamAsync().GetAwaiter().GetResult()
        $readBuffer = [byte[]]::new(4096)
        while (($count = $manifestStream.ReadAsync($readBuffer, 0, $readBuffer.Length, $manifestDeadline.Token).GetAwaiter().GetResult()) -gt 0) {
            if ($manifestBuffer.Length + $count -gt 16384) { throw 'Update manifest exceeds its size limit.' }
            $manifestBuffer.Write($readBuffer, 0, $count)
        }
        $manifest = [Text.Encoding]::UTF8.GetString($manifestBuffer.ToArray()) | ConvertFrom-Json
    }
    finally {
        if ($manifestStream) { $manifestStream.Dispose() }; if ($manifestReply) { $manifestReply.Dispose() }
        $manifestBuffer.Dispose(); $manifestClient.Dispose(); $manifestDeadline.Dispose()
    }
    if ($manifest.version -notmatch '^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$') { throw 'Unsupported update version.' }
    if ($manifest.schemaVersion -eq 1 -and $Architecture -eq 'x64' -and $manifest.architecture -eq $Architecture) { $asset = $manifest }
    elseif ($manifest.schemaVersion -eq 2) {
        $assets = @($manifest.assets)
        $architectures = @($assets | ForEach-Object { $_.architecture })
        if ($assets.Count -lt 1 -or $assets.Count -gt 3 -or @($architectures | Where-Object { $_ -notin @('x64', 'x86', 'arm64') }).Count -gt 0 -or
            @($architectures | Select-Object -Unique).Count -ne $assets.Count) { throw 'Unsupported update manifest architecture list.' }
        $matches = @($assets | Where-Object { $_.architecture -eq $Architecture })
        if ($matches.Count -ne 1) { throw 'This release has no installer for the installed application architecture.' }
        $asset = $matches[0]
    }
    else { throw 'Unsupported update manifest.' }
    if ($manifest.PSObject.Properties.Name -contains 'signerPublicKeySha256' -and
        ($manifest.signerPublicKeySha256 -notmatch '^[0-9a-fA-F]{64}$' -or $manifest.signerPublicKeySha256 -ne $TrustedPublisherPublicKeySha256)) {
        throw 'Manifest publisher does not match the trusted installed release.'
    }
    if ([Version]$manifest.version -le [Version]$CurrentVersion) { Write-Output 'Notchling is current.'; return }
    if ($manifest.minimumWindowsBuild -lt 19045 -or [Environment]::OSVersion.Version.Build -lt $manifest.minimumWindowsBuild) { throw 'This update requires a newer Windows build.' }
    if ($asset.sha256 -notmatch '^[0-9a-fA-F]{64}$' -or $asset.sizeBytes -le 0 -or $asset.sizeBytes -gt 268435456) { throw 'Invalid installer integrity information.' }
    $expectedUrl = "https://github.com/Sury2797/Notchling/releases/download/v$($manifest.version)/Notchling-$($manifest.version)-windows-$Architecture-setup.exe"
    if ($asset.installerUrl -ne $expectedUrl) { throw 'Installer must be an exact official release asset.' }
    $installer = Join-Path $work 'Notchling-setup.exe'
    # Use streaming HttpClient with an enforced upper bound, rather than trusting
    # Content-Length or buffering a large untrusted response in memory.
    $handler = [Net.Http.HttpClientHandler]::new()
    $client = [Net.Http.HttpClient]::new($handler)
    $client.Timeout = [TimeSpan]::FromMinutes(5)
    $installerDeadline = [Threading.CancellationTokenSource]::new([TimeSpan]::FromMinutes(5))
    $reply = $null; $inputStream = $null; $outputStream = $null
    try {
        $reply = $client.GetAsync($expectedUrl, [Net.Http.HttpCompletionOption]::ResponseHeadersRead, $installerDeadline.Token).GetAwaiter().GetResult()
        $reply.EnsureSuccessStatusCode() | Out-Null
        if (-not (Test-OfficialHttpsReply $reply)) { throw 'An untrusted installer redirect was refused.' }
        $inputStream = $reply.Content.ReadAsStreamAsync().GetAwaiter().GetResult()
        $outputStream = [IO.File]::Open($installer, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
        $buffer = [byte[]]::new(65536)
        $total = 0L
        while (($count = $inputStream.ReadAsync($buffer, 0, $buffer.Length, $installerDeadline.Token).GetAwaiter().GetResult()) -gt 0) {
            $total += $count
            if ($total -gt $asset.sizeBytes -or $total -gt 268435456) { throw 'Installer exceeds its declared size.' }
            $outputStream.Write($buffer, 0, $count)
        }
        if ($total -ne $asset.sizeBytes) { throw 'Installer download was incomplete.' }
    }
    finally {
        if ($outputStream) { $outputStream.Dispose() }; if ($inputStream) { $inputStream.Dispose() }
        if ($reply) { $reply.Dispose() }; $client.Dispose(); $handler.Dispose(); $installerDeadline.Dispose()
    }
    $actualHash = (Get-FileHash -LiteralPath $installer -Algorithm SHA256).Hash
    if ($actualHash -ne $asset.sha256) { throw 'Installer SHA-256 verification failed.' }
    $signature = Get-AuthenticodeSignature -LiteralPath $installer
    if ($signature.Status -ne 'Valid' -or -not $signature.SignerCertificate) { throw 'Installer Authenticode signature is invalid or untrusted.' }
    $hasher = [Security.Cryptography.SHA256]::Create()
    try { $publisherKey = [BitConverter]::ToString($hasher.ComputeHash($signature.SignerCertificate.GetPublicKey())).Replace('-', '') }
    finally { $hasher.Dispose() }
    if ($publisherKey -ne $TrustedPublisherPublicKeySha256) { throw 'Installer publisher does not match the trusted installed release.' }
    Write-Output 'Verified update downloaded. Save your work and quit Notchling before continuing in Setup.'
    # Interactive installer retains user control, checks the running-app mutex,
    # uses per-user versioned files, and preserves the existing workspace.
    Start-Process -FilePath $installer -Wait
}
finally { Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue }
