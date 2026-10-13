[CmdletBinding()]
param([Parameter(Mandatory)][string]$RequestPath)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
# A Windows PowerShell child may inherit PowerShell 7's module search path.
# Load the OS verifier from its native installation, never an arbitrary module.
$securityModule = Join-Path $PSHOME 'Modules\Microsoft.PowerShell.Security\Microsoft.PowerShell.Security.psd1'
Import-Module -Name $securityModule -ErrorAction Stop
function Get-FileSha256([string]$Path) {
    $stream = [IO.File]::Open($Path, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
    $hasher = [Security.Cryptography.SHA256]::Create()
    try { return [BitConverter]::ToString($hasher.ComputeHash($stream)).Replace('-', '') }
    finally { $hasher.Dispose(); $stream.Dispose() }
}
Add-Type -AssemblyName System.Net.Http
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
$repository = 'https://github.com/Sury2797/Notchling'
$api = 'https://api.github.com/repos/Sury2797/Notchling'
$resultDirectory = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'Notchling\Updates'
$request = $null; $parentExited = $false; $setupExitCode = $null
function Write-UpdateResult([string]$State, [string]$Message, [Nullable[int]]$ExitCode = $null) {
    New-Item -ItemType Directory -Path $resultDirectory -Force | Out-Null
    $value = @{ schemaVersion = 1; state = $State; message = $Message; checkedAtUtc = [DateTimeOffset]::UtcNow.ToString('o'); installerExitCode = $ExitCode }
    if ($request) { $value.version = $request.Version; $value.architecture = $request.Architecture }
    $temporary = Join-Path $resultDirectory ('result-' + [Guid]::NewGuid().ToString('N') + '.tmp')
    [IO.File]::WriteAllText($temporary, ($value | ConvertTo-Json -Compress), [Text.UTF8Encoding]::new($false))
    Move-Item -LiteralPath $temporary -Destination (Join-Path $resultDirectory 'latest-result.json') -Force
}
function Read-OfficialJson([string]$Url, [int]$Limit = 1048576) {
    $client = [Net.Http.HttpClient]::new()
    $deadline = [Threading.CancellationTokenSource]::new([TimeSpan]::FromSeconds(20))
    $reply = $null; $inputStream = $null; $buffered = [IO.MemoryStream]::new()
    try {
        $client.DefaultRequestHeaders.UserAgent.ParseAdd('Notchling-Updater/1.0')
        $client.DefaultRequestHeaders.Accept.ParseAdd('application/vnd.github+json')
        $reply = $client.GetAsync($Url, [Net.Http.HttpCompletionOption]::ResponseHeadersRead, $deadline.Token).GetAwaiter().GetResult()
        $reply.EnsureSuccessStatusCode() | Out-Null
        $uri = $reply.RequestMessage.RequestUri
        if ($uri.Scheme -ne 'https' -or -not $uri.IsDefaultPort -or $uri.UserInfo -or $uri.Host -ne 'api.github.com') { throw 'An unofficial release metadata response was refused.' }
        if ($reply.Content.Headers.ContentLength -gt $Limit) { throw 'Release metadata exceeds its byte limit.' }
        $inputStream = $reply.Content.ReadAsStreamAsync().GetAwaiter().GetResult(); $buffer = [byte[]]::new(4096)
        while (($count = $inputStream.ReadAsync($buffer, 0, $buffer.Length, $deadline.Token).GetAwaiter().GetResult()) -gt 0) {
            if ($buffered.Length + $count -gt $Limit) { throw 'Release metadata exceeds its byte limit.' }
            $buffered.Write($buffer, 0, $count)
        }
        return [Text.Encoding]::UTF8.GetString($buffered.ToArray()) | ConvertFrom-Json
    }
    finally { if ($inputStream) { $inputStream.Dispose() }; if ($reply) { $reply.Dispose() }; $buffered.Dispose(); $deadline.Dispose(); $client.Dispose() }
}
function Test-PinnedEvaluation {
    $provenance = $request.Provenance
    if (-not $provenance -or $provenance.ReleaseId -le 0 -or $provenance.AssetId -le 0 -or
        $provenance.Tag -cne ('notchling-evaluation-' + $request.Version) -or $provenance.SourceCommit -notmatch '^[0-9a-f]{40}$') { throw 'Evaluation release provenance is invalid.' }
    $release = Read-OfficialJson "$api/releases/$($provenance.ReleaseId)"
    if ($release.id -ne $provenance.ReleaseId -or $release.draft -or -not $release.prerelease -or
        $release.tag_name -cne $provenance.Tag -or $release.html_url -cne "$repository/releases/tag/$($provenance.Tag)") { throw 'Evaluation release identity changed.' }
    $assetName = "Notchling-$($request.Version)-windows-$($request.Architecture)-evaluation-setup.exe"
    $matches = @($release.assets | Where-Object { $_.id -eq $provenance.AssetId -and $_.name -ceq $assetName })
    if ($matches.Count -ne 1) { throw 'The pinned evaluation installer is no longer available.' }
    $asset = $matches[0]
    if ($asset.browser_download_url -cne "$repository/releases/download/$($provenance.Tag)/$assetName" -or
        $asset.state -cne 'uploaded' -or $asset.size -ne $request.SizeBytes -or
        $asset.digest -ine ('sha256:' + $request.Sha256)) { throw 'Evaluation installer integrity metadata changed.' }
    $reference = Read-OfficialJson "$api/git/ref/tags/$($provenance.Tag)" 32768
    if ($reference.ref -cne "refs/tags/$($provenance.Tag)") { throw 'Evaluation source tag identity changed.' }
    $target = $reference.object
    for ($depth = 0; $depth -lt 3; $depth++) {
        if ($target.sha -notmatch '^[0-9a-fA-F]{40}$') { throw 'Evaluation source commit is invalid.' }
        if ($target.type -ceq 'commit') {
            if ($target.sha -ine $provenance.SourceCommit) { throw 'Evaluation source commit changed.' }
            return
        }
        if ($target.type -cne 'tag') { break }
        $annotation = Read-OfficialJson "$api/git/tags/$($target.sha)" 32768; $target = $annotation.object
    }
    throw 'Evaluation source tag did not resolve to the pinned commit.'
}
function Test-Installer {
    $file = Get-Item -LiteralPath $request.InstallerPath
    if ($file.Length -ne $request.SizeBytes -or (Get-FileSha256 $file.FullName) -ine $request.Sha256) { throw 'Prepared installer size or SHA-256 changed.' }
    if ($request.Trust -ceq 'SignedPublisher') {
        if ($request.PublisherKeySha256 -notmatch '^[0-9a-fA-F]{64}$') { throw 'The installed trusted publisher is missing.' }
        $signature = Get-AuthenticodeSignature -LiteralPath $file.FullName
        if ($signature.Status -ne 'Valid' -or -not $signature.SignerCertificate) { throw 'Prepared installer publisher signature is invalid.' }
        $hasher = [Security.Cryptography.SHA256]::Create()
        try { $key = [BitConverter]::ToString($hasher.ComputeHash($signature.SignerCertificate.GetPublicKey())).Replace('-', '') } finally { $hasher.Dispose() }
        if ($key -ine $request.PublisherKeySha256) { throw 'Prepared installer publisher does not match the trusted installed app.' }
    }
    elseif ($request.Trust -ceq 'OfficialUnsignedEvaluation') { Test-PinnedEvaluation }
    else { throw 'The update trust policy is unsupported.' }
}
try {
    $fullRequest = [IO.Path]::GetFullPath($RequestPath)
    $directory = [IO.Path]::GetDirectoryName($fullRequest)
    if ((Get-Item -LiteralPath $fullRequest).Length -gt 16384) { throw 'Update handoff request exceeds its byte limit.' }
    $request = [IO.File]::ReadAllText($fullRequest) | ConvertFrom-Json
    if ($request.SchemaVersion -ne 1 -or $request.Version -notmatch '^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$' -or
        $request.Architecture -notin @('x64', 'x86', 'arm64') -or $request.Sha256 -notmatch '^[0-9a-fA-F]{64}$' -or
        $request.SizeBytes -le 0 -or $request.SizeBytes -gt 268435456 -or $request.Nonce -notmatch '^[0-9a-f]{32}$' -or
        [IO.Path]::GetFullPath($request.InstallerPath) -ine (Join-Path $directory 'Notchling-setup.exe') -or
        [IO.Path]::GetFullPath($request.ReadyPath) -ine (Join-Path $directory ('ready-' + $request.Nonce))) { throw 'The update handoff request is invalid.' }
    $installDirectory = [IO.Path]::GetFullPath($request.InstallDirectory).TrimEnd('\')
    $expected = Join-Path $installDirectory "app\$($request.Version)\Notchling.Windows.exe"
    if ([IO.Path]::GetFullPath($request.ExpectedExecutable) -ine $expected -or
        [IO.Path]::GetFileName($request.CurrentExecutable) -ine 'Notchling.Windows.exe' -or
        -not [IO.Path]::GetFullPath($request.CurrentExecutable).StartsWith($installDirectory + '\app\', [StringComparison]::OrdinalIgnoreCase)) { throw 'The update destination is outside this installation.' }
    $parent = [Diagnostics.Process]::GetProcessById($request.ParentId)
    if ($parent.StartTime.ToUniversalTime().Ticks -ne $request.ParentStartedUtcTicks -or
        $parent.MainModule.FileName -ine $request.CurrentExecutable) { throw 'The running app did not match this update operation.' }
    Test-Installer
    $readyTemporary = Join-Path $directory ('ready-' + $request.Nonce + '.tmp')
    [IO.File]::WriteAllText($readyTemporary, $request.Nonce)
    Move-Item -LiteralPath $readyTemporary -Destination $request.ReadyPath
    Write-UpdateResult 'WaitingForApp' 'Update prepared. Waiting for Notchling to save and close.'
    $waitDeadline = [DateTime]::UtcNow.AddMinutes(2)
    while (-not $parent.WaitForExit(200)) {
        if ((Test-Path -LiteralPath (Join-Path $directory ('cancel-' + $request.Nonce))) -or [DateTime]::UtcNow -ge $waitDeadline) {
            Write-UpdateResult 'Cancelled' 'Notchling stayed open or the update was cancelled. No update was installed; try again when ready.'; exit 2
        }
    }
    if (Test-Path -LiteralPath (Join-Path $directory ('cancel-' + $request.Nonce))) { Write-UpdateResult 'Cancelled' 'The update was cancelled before installation.'; exit 2 }
    $parentExited = $true
    Test-Installer
    Write-UpdateResult 'Installing' 'Installing the prepared Notchling update. Your workspace is preserved.'
    $setupLog = Join-Path $resultDirectory 'setup-update.log'
    # Silent Setup keeps the user out of a second wizard; it preserves existing
    # per-user data and never kills applications or reboots Windows automatically.
    $arguments = '/SP- /VERYSILENT /SUPPRESSMSGBOXES /NORESTART /RESTARTEXITCODE=3010 /DIR="' + $installDirectory + '" /LOG="' + $setupLog + '"'
    $setup = Start-Process -FilePath $request.InstallerPath -ArgumentList $arguments -PassThru -Wait
    $setupExitCode = $setup.ExitCode
    if ($setup.ExitCode -notin @(0, 3010)) { throw "Setup did not complete (code $($setup.ExitCode)). See the local update log." }
    if (-not (Test-Path -LiteralPath $expected -PathType Leaf)) { throw 'Setup finished without the expected new application file.' }
    $version = [Diagnostics.FileVersionInfo]::GetVersionInfo($expected)
    if (($version.FileMajorPart.ToString() + '.' + $version.FileMinorPart + '.' + $version.FileBuildPart) -cne $request.Version) { throw 'The installed application version did not match the prepared update.' }
    Start-Process -FilePath $expected -WorkingDirectory ([IO.Path]::GetDirectoryName($expected)) | Out-Null
    Write-UpdateResult 'Completed' $(if ($setup.ExitCode -eq 3010) { 'Notchling updated and reopened. Windows requests a restart to finish shared component changes.' } else { 'Notchling updated and reopened. Your workspace is preserved.' }) $setup.ExitCode
}
catch {
    Write-UpdateResult 'Failed' $_.Exception.Message $setupExitCode
    if ($parentExited -and $request -and (Test-Path -LiteralPath $request.CurrentExecutable -PathType Leaf)) {
        Start-Process -FilePath $request.CurrentExecutable -WorkingDirectory ([IO.Path]::GetDirectoryName($request.CurrentExecutable)) -ErrorAction SilentlyContinue | Out-Null
    }
    exit 1
}
finally {
    # A startup failure keeps the download for retry; completed/cancelled workers
    # delete only their own unique temp directory, never the user's workspace.
    if ($request -and ($parentExited -or (Test-Path -LiteralPath $request.ReadyPath))) {
        Remove-Item -LiteralPath $request.InstallerPath -Force -ErrorAction SilentlyContinue
        Remove-Item -LiteralPath $request.ReadyPath -Force -ErrorAction SilentlyContinue
        Remove-Item -LiteralPath $fullRequest -Force -ErrorAction SilentlyContinue
    }
}
