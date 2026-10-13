# Windows ARM support

Notchling builds separate x64, x86 and ARM64 apps. The installer uses the app architecture for .NET, the Windows App Runtime Framework and DDLM; Main and Singleton use the native Windows architecture. On an emulated x86 app, the native Framework is also required. These decisions come from Windows `IsWow64Process2`, rather than process environment variables.

## Windows 10 and Windows 11

| Device | App to choose | Setup helper | Qualification scope |
| --- | --- | --- | --- |
| Windows 10 22H2 x64 | x64 | Native x64 Windows PowerShell | Installed x64 cloud checks; consumer Windows 10 retesting remains open |
| Windows 10 22H2 32-bit x86 | x86 | Native x86 Windows PowerShell | x86 app runs in cloud on x64; a real 32-bit Windows 10 device remains open |
| Windows 10 22H2 ARM64 | ARM64 | Existing x86 Windows PowerShell/.NET Framework helper | Compatibility path implemented; real Windows 10 ARM hardware remains unqualified |
| Windows 11 x64 | x64 | Native x64 Windows PowerShell | Installed x64 cloud checks |
| Windows 11 ARM64 | ARM64 | Native ARM64 Windows PowerShell | Native ARM64 cloud checks |

Windows 11 has no 32-bit OS edition. The x86 app can run through supported Windows emulation; prefer the app matching the native device. ARM32 apps and operating systems are outside this product's build targets. Notchling requires Windows 10 build 19045 or Windows 11; this does not claim support for every earlier Windows 10 release.

## Windows 10 ARM setup correction

The earlier prerequisite guard demanded a native ARM64 Windows PowerShell process even on Windows 10 ARM. That could reject the existing x86 Windows PowerShell/.NET Framework helper before preparing the app. Setup now explicitly selects the x86 helper on Windows 10 ARM, and permits that one process exception for builds 19045 through 21999. This does not change the selected app to x86: a native ARM64 app still receives ARM64 .NET and ARM64 runtime packages.

Windows 11 ARM continues to require its native ARM64 helper. x64 Windows still rejects an x86 prerequisite helper, wrong-device installers still fail, and the Windows minimum is unchanged. Fixture checks cover these boundaries and ensure that the permitted x86 helper cannot alter ARM64 package selection. The compatibility fixture is not proof of an installation on real Windows 10 ARM hardware.

## Official platform evidence

Checked 13 October 2026:

- Microsoft's [.NET on Windows installation guide](https://github.com/dotnet/docs/blob/main/docs/core/install/windows.md) lists .NET 10 ARM64 along with x64 and x86 for Windows 10 LTSC/Enterprise, and documents ARM64 installation in `C:\Program Files\dotnet`. The current table excludes consumer Windows 10 22H2 because of its servicing lifecycle; it does not establish an ARM-specific binary incompatibility. Notchling's consumer Windows 10 support must be validated separately.
- Microsoft's [.NET Framework release notes](https://github.com/dotnet/docs/blob/main/docs/framework/whats-new/index.md) state: “.NET Framework 4.8.1 adds native Arm64 support to the .NET Framework family.” An AnyCPU `net48` Setup helper can use the existing x86 Framework host without assuming a native ARM64 Framework host is present on Windows 10.
- Microsoft's [Windows App SDK support table](https://github.com/MicrosoftDocs/windows-dev-docs/blob/docs/hub/apps/windows-app-sdk/support.md) lists Windows App SDK 1.8 with Windows 10 22H2 and Windows 11. The exact pinned ARM64 Framework MSIX, `Microsoft.WindowsAppSDK.Runtime` 1.8.260921001, declares `Windows.Universal` minimum version `10.0.17763.0` and processor architecture `arm64`; it does not declare a Windows 11-only ARM64 minimum.
- Microsoft's [deployment architecture guide](https://github.com/MicrosoftDocs/windows-dev-docs/blob/docs/hub/apps/windows-app-sdk/deployment-architecture.md) explains the Framework, Main, Singleton and DDLM packages, and the app-architecture-specific DDLM.

These contracts justify preserving native ARM64 builds for Windows 10 rather than inventing a Windows 11-only app restriction. They do not replace actual installation, live-provider, display-scaling and interaction tests. Download badges must only advertise assets that exist and have passed their release gates.
