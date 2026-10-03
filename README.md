# Notch for Windows

[![Native build and core checks](https://github.com/SuryaK999/Notch-win-linux/actions/workflows/build.yml/badge.svg)](https://github.com/SuryaK999/Notch-win-linux/actions/workflows/build.yml)

A Windows-first desktop notch, built with C#, WinUI 3 and Windows App SDK. A compact strip opens into focused panels for music, work, local utilities and live activities. The visual direction follows the supplied NotchPop reference: a dark top-center surface, a separate icon dock, restrained typography, and panels sized to their content.

The Windows application uses native controls and Windows services. The portable .NET core runs on Windows and Linux; a Linux desktop interface is planned for a later phase. This is an implementation under development, with Windows build and interactive release checks still required. It is not a finished commercial release.

## Develop on Windows

Use Windows 11 x64, a stable .NET 10 SDK, and Visual Studio 2026 or current Visual Studio Build Tools with the Windows SDK and WinUI/C# desktop development tools. Windows 10 build 19041 is the project's minimum API target; Windows 11 is the primary QA platform. The repository pins the .NET 10 SDK in [global.json](global.json) and Windows App SDK 1.8.260921001 in the desktop project. Dependency restore requires access to NuGet. There is no Node, Electron or browser runtime requirement.

From PowerShell in the repository:

```powershell
dotnet restore src/Notch.Windows/Notch.Windows.csproj -p:Platform=x64
dotnet build src/Notch.Windows/Notch.Windows.csproj --configuration Release -p:Platform=x64
dotnet run --project src/Notch.Windows/Notch.Windows.csproj --configuration Release -p:Platform=x64
```

To create an unpackaged folder containing the app and its runtimes:

```powershell
dotnet publish src/Notch.Windows/Notch.Windows.csproj --configuration Release --runtime win-x64 --self-contained true -p:Platform=x64 -p:WindowsPackageType=None -p:WindowsAppSDKSelfContained=true -p:PublishSingleFile=false --output artifacts/notch-windows-x64
```

Keep the entire published folder together. The current CI creates a ZIP artifact for review; it does not sign, upload a store package, or publish a release.

## Validate the shared core

The package-free test executable verifies timer behavior, overlay state transitions, storage, conversions, calendar parsing and provider fixtures. It reports a positive executed-test count and exits nonzero if any case fails. Fixtures use in-memory HTTP responses and synthetic credentials; no live account is required.

```powershell
dotnet run --project tests/Notch.Core.Tests/Notch.Core.Tests.csproj --configuration Release
```

The same command works on Linux. Convenience wrappers are [scripts/check-core.ps1](scripts/check-core.ps1) and [scripts/check-core.sh](scripts/check-core.sh).

For a user-local SDK installation, [scripts/install-dotnet.py](scripts/install-dotnet.py) reads the repository pin, fetches official Microsoft release metadata over HTTPS, and verifies the SDK archive with SHA-512 before extracting it. It needs Python 3.11.8 or newer. This does not install system packages.

```bash
python scripts/install-dotnet.py --install-dir /workspace/.cache/dotnet --cache-dir /workspace/.cache/dotnet-downloads
DOTNET_CLI_HOME=/workspace/.cache/dotnet-home NUGET_PACKAGES=/workspace/.cache/nuget /workspace/.cache/dotnet/dotnet run --project tests/Notch.Core.Tests/Notch.Core.Tests.csproj --configuration Release
```

These paths suit this cloud workspace, whose home directory is read-only. On a regular development machine, use your normal `dotnet` installation. The Windows XAML compiler and interactive Windows services cannot run on this Linux host.

## Features and connections

The tool inventory includes media, revenue, analytics, coding activity, calendar, weather, focus, shelf, clipboard, servers, system, screen time, notes, scratchpad, files, links, emoji, sounds, conversions and awake controls. See [the module and connection guide](docs/modules-and-connections.md) for the implementation boundary and connection requirements.

Notes, links and saved file references are local plaintext app data. Clipboard capture is opt-in, kept in memory and cleared when disabled. Provider credentials use Windows Credential Locker and should be entered through app settings, never committed to the repository. Offline and unconnected modules show their state; demo data must be explicitly enabled.

Free and Pro are a product-tier concept. Billing, account management, licensing and subscription enforcement are deferred. Stripe reporting reads your configured revenue data; it does not create or bill an app subscription.

The current Open-Meteo weather endpoint is for noncommercial development. A paid release needs the appropriate commercial arrangement and licensed endpoint configuration, plus weather attribution.

## Review and release

- [Reference analysis](docs/reference-analysis.md) explains the supplied video's visual and interaction details.
- [Architecture](docs/architecture.md) explains the portable core, native services, storage and refresh boundaries.
- [Module and connection guide](docs/modules-and-connections.md) describes local tools and optional external data.
- [Windows release readiness](docs/release-readiness.md) contains the remaining DPI, monitor, keyboard, accessibility, performance and real-service checks.
- [Validation evidence](docs/validation-notes.md) records 58 passing local core/fixture checks, hosted Windows build evidence and the limits of the Linux static validation.
- [Product and platform roadmap](docs/product-roadmap.md) records Free/Pro and the later Linux phase.

CI is in [.github/workflows/build.yml](.github/workflows/build.yml). Successful portable core tests do not establish a successful Windows XAML build or a fluid desktop experience; those require the Windows job and the manual QA matrix.
