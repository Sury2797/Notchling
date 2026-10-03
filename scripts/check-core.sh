#!/usr/bin/env bash
set -euo pipefail
repository_root="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
dotnet_binary="${NOTCH_DOTNET:-dotnet}"
cd -- "$repository_root"
"$dotnet_binary" build "$repository_root/src/Notch.Core/Notch.Core.csproj" --configuration Release
"$dotnet_binary" run --project "$repository_root/tests/Notch.Core.Tests/Notch.Core.Tests.csproj" --configuration Release
