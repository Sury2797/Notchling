# Third-party software and service notices

The [Notchling license](LICENSE) applies to the project's original code and assets. Third-party software, runtime components and service data retain their respective licenses and terms. The Notchling license does not replace those terms or grant rights to third-party trademarks.

This document records the dependency versions and publisher license files found in the restored Windows project. It is the maintained dependency summary. The release/build pipeline also copies exact publisher license/notice text into `ThirdPartyNotices/` and generates `publish-inventory.json` plus `sbom.spdx.json` from the release restore graph and published files. A restored dependency or build tool is not necessarily included in the published application.

## Windows application packages

The following packages are recorded in `src/Notch.Windows/obj/project.assets.json`. Microsoft is the listed publisher. Links identify the official NuGet package and exact version; filenames identify the authoritative documents inside that package.

| Package | Version | Publisher license and accompanying notices |
| --- | --- | --- |
| [Microsoft.WindowsAppSDK](https://www.nuget.org/packages/Microsoft.WindowsAppSDK/1.8.260921001) | 1.8.260921001 | Microsoft Windows App SDK Software License Terms, `license.txt`; `NOTICE.txt` |
| [Microsoft.WindowsAppSDK.AI](https://www.nuget.org/packages/Microsoft.WindowsAppSDK.AI/1.8.79) | 1.8.79 | Microsoft Windows App SDK Software License Terms, `license.txt` |
| [Microsoft.WindowsAppSDK.Base](https://www.nuget.org/packages/Microsoft.WindowsAppSDK.Base/1.8.251216001) | 1.8.251216001 | Microsoft Windows App SDK Software License Terms, `license.txt`; `NOTICE.txt` |
| [Microsoft.WindowsAppSDK.DWrite](https://www.nuget.org/packages/Microsoft.WindowsAppSDK.DWrite/1.8.25122902) | 1.8.25122902 | Microsoft Windows App SDK Software License Terms, `license.txt` |
| [Microsoft.WindowsAppSDK.Foundation](https://www.nuget.org/packages/Microsoft.WindowsAppSDK.Foundation/1.8.260803002) | 1.8.260803002 | Microsoft Windows App SDK Software License Terms, `license.txt` |
| [Microsoft.WindowsAppSDK.InteractiveExperiences](https://www.nuget.org/packages/Microsoft.WindowsAppSDK.InteractiveExperiences/1.8.260708001) | 1.8.260708001 | Microsoft Windows App SDK Software License Terms, `license.txt` |
| [Microsoft.WindowsAppSDK.ML](https://www.nuget.org/packages/Microsoft.WindowsAppSDK.ML/1.8.2223) | 1.8.2223 | Microsoft Windows App SDK / Windows Machine Learning Software License Terms, `license.txt`; `ThirdPartyNotices.txt` |
| [Microsoft.WindowsAppSDK.Runtime](https://www.nuget.org/packages/Microsoft.WindowsAppSDK.Runtime/1.8.260921001) | 1.8.260921001 | Microsoft Windows App SDK Software License Terms, `license.txt`; `NOTICE.txt` |
| [Microsoft.WindowsAppSDK.Widgets](https://www.nuget.org/packages/Microsoft.WindowsAppSDK.Widgets/1.8.251231004) | 1.8.251231004 | Microsoft Windows App SDK Software License Terms, `license.txt` |
| [Microsoft.WindowsAppSDK.WinUI](https://www.nuget.org/packages/Microsoft.WindowsAppSDK.WinUI/1.8.260803003) | 1.8.260803003 | Microsoft Windows App SDK Software License Terms, `license.txt`; `NOTICE.txt` |
| [Microsoft.Web.WebView2](https://www.nuget.org/packages/Microsoft.Web.WebView2/1.0.3179.45) | 1.0.3179.45 | BSD-style three-clause terms in `LICENSE.txt`; `NOTICE.txt` |
| [Microsoft.Windows.SDK.BuildTools](https://www.nuget.org/packages/Microsoft.Windows.SDK.BuildTools/10.0.26100.4654) | 10.0.26100.4654 | Package metadata points to the [Microsoft Windows SDK license](https://aka.ms/WinSDKLicenseURL) |
| [Microsoft.Windows.SDK.BuildTools.MSIX](https://www.nuget.org/packages/Microsoft.Windows.SDK.BuildTools.MSIX/1.7.20250829.1) | 1.7.20250829.1 | Microsoft Windows SDK Software License Terms, `sdk_license.txt`; `NOTICE.txt` |
| [System.Numerics.Tensors](https://www.nuget.org/packages/System.Numerics.Tensors/9.0.0) | 9.0.0 | MIT, `LICENSE.TXT`; `THIRD-PARTY-NOTICES.TXT` |

**The Windows App SDK NuGet bundles are governed by the Microsoft terms shipped with those packages.** An upstream repository's open-source license does not describe every binary or dependency in the bundle. Distribution must follow the package terms and retain applicable third-party notices. WebView2's entry above describes this SDK package; it does not establish license rights for a separately installed or distributed WebView2 Runtime.

To locate a package's documents after restore, use the NuGet package root reported by `dotnet nuget locals global-packages --list`, followed by `<lowercase-package-id>/<version>/`. For example, the Windows App SDK terms are at `microsoft.windowsappsdk/1.8.260921001/license.txt` beneath that root.

## .NET SDK, runtime and Windows reference packs

The repository pins .NET SDK **10.0.100**. The SDK installation includes `LICENSE.txt` and `ThirdPartyNotices.txt`. The .NET SDK and runtime have an [MIT license](https://github.com/dotnet/runtime/blob/v10.0.0/LICENSE.TXT), with additional notices for included third-party materials. Preserve the applicable files shipped with the actual runtime being distributed; the top-level MIT license alone is not a complete notice set.

The current restore also records these SDK-managed downloads:

| Pack | Version | Publisher license documents |
| --- | --- | --- |
| [Microsoft.NETCore.App.Runtime.win-x64](https://www.nuget.org/packages/Microsoft.NETCore.App.Runtime.win-x64/10.0.0) | 10.0.0 | MIT, `LICENSE.TXT`; `THIRD-PARTY-NOTICES.TXT` |
| [Microsoft.NETCore.App.Host.win-x64](https://www.nuget.org/packages/Microsoft.NETCore.App.Host.win-x64/10.0.0) | 10.0.0 | MIT, `LICENSE.TXT`; `THIRD-PARTY-NOTICES.TXT` |
| [Microsoft.NETCore.App.Crossgen2.linux-x64](https://www.nuget.org/packages/Microsoft.NETCore.App.Crossgen2.linux-x64/10.0.0) | 10.0.0 | MIT, `LICENSE.TXT`; `THIRD-PARTY-NOTICES.TXT` |
| [Microsoft.AspNetCore.App.Runtime.win-x64](https://www.nuget.org/packages/Microsoft.AspNetCore.App.Runtime.win-x64/10.0.0) | 10.0.0 | MIT, `LICENSE.txt`; `THIRD-PARTY-NOTICES.TXT` |
| [Microsoft.WindowsDesktop.App.Ref](https://www.nuget.org/packages/Microsoft.WindowsDesktop.App.Ref/10.0.0) | 10.0.0 | MIT, `LICENSE` |
| [Microsoft.WindowsDesktop.App.Runtime.win-x64](https://www.nuget.org/packages/Microsoft.WindowsDesktop.App.Runtime.win-x64/10.0.0) | 10.0.0 | MIT, `LICENSE` |
| [Microsoft.Windows.SDK.NET.Ref](https://www.nuget.org/packages/Microsoft.Windows.SDK.NET.Ref/10.0.19041.57) | 10.0.19041.57 | Package metadata points to the [Microsoft Windows SDK license](https://aka.ms/WinSDKLicenseURL) |

This restore inventory includes build and reference packs. The final publish output determines which runtime components and corresponding notices must travel with a release. A newer permitted SDK can resolve different runtime packs; regenerate the inventory from that release's restore and publish output.

## Weather data and external services

Weather uses [Open-Meteo](https://open-meteo.com/) forecast and geocoding services. Display Open-Meteo attribution beside weather data and retain attribution required by the service's data terms. Consult the [forecast service repository](https://github.com/open-meteo/open-meteo) and [geocoding service repository](https://github.com/open-meteo/geocoding-api) for their published licenses, terms and attribution requirements.

Public free weather endpoints are for **noncommercial development**. Release uses an authenticated owner-operated proxy and refuses unconfigured paid weather access; commercial distribution requires an appropriate service agreement and licensed external endpoints. This repository does not supply that agreement or claim commercial API rights. The service's source-code license, data attribution requirements and hosted API access terms are separate matters.

Optional Stripe reporting and user-configured analytics access remain subject to the relevant service and account agreements. Naming an integration does not imply endorsement or transfer rights to a provider's branding, data or service.

## Distribution requirements

A self-contained Notchling release includes third-party runtime files. Before distribution, inventory the actual published files and carry the complete applicable publisher licenses, copyright notices and third-party notice documents in the release package. Preserve any required notices already included by the publisher; do not replace them with this summary.

The signed-installer path also records the actual Inno Setup compiler version and copies its installed publisher license.

The checked-in [notice collector](scripts/bundle-notices.py) provides a release-specific SPDX 2.3 file/dependency inventory and publisher notice bundle. Runtime candidates are mapped by published binary basename, with ambiguous matches clearly labeled; missing restored packages or candidate-runtime notice files stop strict signed-release preparation. A reviewed binary-to-license attribution and distribution permission assessment remain required. Generated inventory is evidence about actual files and documents, not a legal conclusion. See [release delivery](docs/release-delivery.md) and [release readiness](docs/release-readiness.md).
