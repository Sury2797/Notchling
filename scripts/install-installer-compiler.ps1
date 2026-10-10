# Pin build tooling independently from the application's shared prerequisites.
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if ($env:OS -ne 'Windows_NT' -or $env:GITHUB_ACTIONS -ne 'true') { throw 'Compiler provisioning is restricted to a disposable Windows Actions runner.' }
$compiler = 'C:\Program Files (x86)\Inno Setup 6\ISCC.exe'
$version = '6.7.3'
$expectedHash = '9c73c3bae7ed48d44112a0f48e66742c00090bdb5bef71d9d3c056c66e97b732'
$url = 'https://github.com/jrsoftware/issrc/releases/download/is-6_7_3/innosetup-6.7.3.exe'
$temporary = Join-Path $env:RUNNER_TEMP ('Notchling.Compiler-' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($temporary) | Out-Null
$installer = Join-Path $temporary 'innosetup-6.7.3.exe'
try {
    Invoke-WebRequest -Uri $url -OutFile $installer -TimeoutSec 90
    if ((Get-Item -LiteralPath $installer).Length -ne 10592232 -or
        (Get-FileHash -LiteralPath $installer -Algorithm SHA256).Hash -ne $expectedHash) {
        throw 'The official pinned compiler installer did not match its recorded release size and SHA-256.'
    }
    $signature = Get-AuthenticodeSignature -LiteralPath $installer
    if ($signature.Status -ne 'Valid') { throw "The pinned compiler installer does not have a trusted Authenticode signature: $($signature.Status)." }
    $arguments = @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/SP-', '/ALLUSERS',
        ('/DIR="' + (Split-Path $compiler) + '"'))
    $process = Start-Process -FilePath $installer -ArgumentList $arguments -PassThru
    try {
        if (-not $process.WaitForExit(90000)) {
            $process.Kill($true)
            $null = $process.WaitForExit(5000)
            throw 'The owned compiler installer exceeded its 90-second watchdog.'
        }
        if ($process.ExitCode -ne 0) { throw "The compiler installer failed with exit code $($process.ExitCode)." }
    } finally { $process.Dispose() }
    if (-not (Test-Path -LiteralPath $compiler -PathType Leaf)) { throw 'The pinned compiler was not installed at its configured path.' }
    # Read the executable's own banner. Version resource display strings can
    # include a suffix and should not be cast directly to System.Version.
    $info = [Diagnostics.ProcessStartInfo]::new($compiler)
    $info.UseShellExecute = $false; $info.CreateNoWindow = $true
    $info.RedirectStandardOutput = $true; $info.RedirectStandardError = $true
    $probe = [Diagnostics.Process]::Start($info)
    try {
        $output = $probe.StandardOutput.ReadToEndAsync(); $errors = $probe.StandardError.ReadToEndAsync()
        if (-not $probe.WaitForExit(10000)) {
            $probe.Kill($true)
            $null = $probe.WaitForExit(5000)
            throw 'The installed compiler did not answer its bounded version probe.'
        }
        $banner = $output.GetAwaiter().GetResult() + $errors.GetAwaiter().GetResult()
        if ($banner -notmatch ('(?i)(?:compiler engine version|Inno Setup).*?\b' + [regex]::Escape($version) + '\b')) {
            throw 'The installed compiler did not report the exact pinned version.'
        }
    } finally { $probe.Dispose() }
    Write-Output "::notice title=Pinned installer compiler::Inno Setup $version; official SHA-256 and trusted Authenticode verified."
} catch {
    $message = $_.Exception.Message.Replace('%','%25').Replace("`r",'%0D').Replace("`n",'%0A')
    Write-Output "::error title=Installer compiler provisioning::$message"
    throw
} finally { if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary -Recurse -Force } }
