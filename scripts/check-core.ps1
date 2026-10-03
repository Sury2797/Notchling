param([string]$Dotnet = "dotnet")
$ErrorActionPreference = "Stop"
$repositoryRoot = Split-Path $PSScriptRoot -Parent
& $Dotnet build (Join-Path $repositoryRoot "src/Notch.Core/Notch.Core.csproj") --configuration Release
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
& $Dotnet run --project (Join-Path $repositoryRoot "tests/Notch.Core.Tests/Notch.Core.Tests.csproj") --configuration Release
exit $LASTEXITCODE
