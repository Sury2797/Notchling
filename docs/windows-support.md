# Windows support contract

Windows 10 and Windows 11 are **equal release targets**. The supported desktop architecture is x64.

| System | Release baseline | Requirement |
| --- | --- | --- |
| Windows 10 | 22H2, build 19045, x64 | Fully patched for the machine's servicing channel |
| Windows 11 | Microsoft-supported x64 releases at launch | Record every qualified build in the release evidence |
| Windows N | Same builds, with or without Media Feature Pack | Optional media/sound can report unavailable; local tools must remain usable |
| ARM64 / x86 | No native release currently configured | Do not advertise native support or treat emulation as qualification |

The Windows API target and `TargetPlatformMinVersion` remain `10.0.19041` so code compiles against the common Windows 10-era API surface. The supported product baseline is recorded separately as `MinimumSupportedWindowsBuild=19045`, and the installer refuses older builds. An API floor or successful build is not proof of interactive compatibility. A Windows Server GitHub runner can exercise installed-app startup and controls, but it is not a consumer Windows 10/11 test machine.

## Runtime prerequisites

The default Windows download is a single setup EXE. Its application payload is framework-dependent: it excludes bundled .NET and Windows App SDK runtimes and uses these shared system prerequisites:

| Prerequisite | Official installer |
| --- | --- |
| .NET 10 Runtime, Windows x64 (`Microsoft.NETCore.App`) | [.NET 10 downloads](https://dotnet.microsoft.com/en-us/download/dotnet/10.0); select **.NET Runtime** |
| Windows App SDK 1.8 runtime, Windows x64 | [Windows App SDK downloads](https://learn.microsoft.com/windows/apps/windows-app-sdk/downloads); current **1.8 runtime installer** |

The application pins Windows App SDK package `1.8.260921001`, requiring runtime package version `8000.994.2142.0` or newer in the 1.8 line. Setup checks these prerequisites and downloads/installs their official installers only when missing. Internet access is needed for those downloads; the .NET installer may request administrator approval. Existing compatible runtimes are reused by subsequent Notchling installs. No .NET SDK, Visual Studio, or development workload is required to use the app.

First-install prerequisite downloads are additional to the app installer size. Record download, cancellation, offline failure, and successful launch separately on Windows 10 and Windows 11. An advanced app-only folder artifact bypasses Setup and requires the shared runtimes already installed; keep that folder's DLLs, assets, and launcher together.

For first-install planning, Microsoft's .NET 10.0.12 Windows x64 Runtime installer reported **30,663,104 bytes (29.24 MiB)** in its official HTTP `Content-Length` on 4 October 2026 UTC. The .NET release metadata is approximately another 1 MiB. This is an external-download estimate, not an observed missing-.NET installation in cloud CI; runtime updates can change the amount.

[CI run 37203173532](https://github.com/Sury2797/Notchling/actions/runs/37203173532) passed for source revision [`fafa2cc`](https://github.com/Sury2797/Notchling/commit/fafa2ccac2c34e6b464a1254d637164df59b50ee) on 4 October 2026 UTC. It recorded **8,875,854 bytes (8.46 MiB)** for the setup EXE and **40,648,773 bytes (38.77 MiB)** for extracted application files. Setup downloaded **106,879,800 bytes (101.93 MiB)** for the missing Windows App Runtime; it reused the runner's installed .NET 10.0.11 runtime. Installed-app launch, Free UI interactions, and uninstall passed on that Windows Server runner.

The small app installer is therefore not the entire first-install transfer on a machine missing both shared runtimes. Allow roughly **147 MB** including that installer, the measured Windows App Runtime download, the estimated .NET download, and its release metadata. Compatible preinstalled runtimes avoid those additional downloads. Neither shared-runtime download is part of Notchling's application payload.

The overlay, shortcuts, media controls, fonts, accessibility, installation and resource behavior must pass independently on both systems for the **same signed artifact**. Windows 11-only cosmetic APIs must have harmless Windows 10 fallbacks. Use Segoe UI and a guaranteed monospace font rather than relying on developer-installed fonts. No platform may lose basic controls because a cosmetic feature is unavailable.

## Qualification record

Copy [the native qualification template](native-qualification.md) for each candidate. Record the app version, source commit, installer SHA-256, OS edition/build, GPU/driver, DPI, monitors, text scaling and result. Test Windows 10 separately from Windows 11; do not combine their results into a single passing row.

The interactive cloud workspace is Linux and cannot launch WinUI. The [Windows CI job](../.github/workflows/build.yml) uses two hosted Windows desktops to install the actual evaluation setup and launch the installed Release app. Its public-testing UI Automation checks a visible native window, its icon and bounded message responsiveness, plus:

- Confirm all 21 catalog tools are unlocked without a purchased entitlement.
- Exercise real pointer hover/leave, transparent dock flanks, Settings dismissal and the finite keyboard editing lease.
- Check compact credential alignment, retained drafts/scroll and eight connection-status rows without provider credentials.
- Navigate every tool, reset featured-panel scroll, save/delete a note and acquire/release the native Awake request.
- Show the genuine no-player media state and require disabled transport controls.
- Start, pause, and reset the Pomodoro timer using its visible clock.
- Edit and save scratchpad text, navigate away and back, and verify clearing the saved text.

The workflow verifies registered Windows App Runtime packages after Setup, records first-window timing and a short CPU/memory sample, then cleans up the owned app process and runs the installed uninstaller. The published payload guard requires the nonempty `resources.pri` produced by the current SDK's compiled-XAML resource pipeline, as well as the app and bootstrapper files; build success alone cannot replace a launch check.

Historical runs established narrower Free UI checks. Current immutable versions, exact completed assertions and package measurements are recorded in [validation notes](validation-notes.md); the v0.3.4 public-testing release passed its own complete two-host run. Hosted-runner first-window, five-second CPU/memory and message samples are startup observations, rather than settled idle or animation benchmarks.

The final Setup reuses runtimes prepared by the preceding integration fixtures. Separate fixtures force initial missing-runtime detection, then download and verify real Microsoft installers, execute the .NET installer, and exercise signed-resource Windows App Runtime recovery with real package registration. They retain the runner's SDKs and are not clean consumer Windows images; denied UAC and offline installation remain device checks. Recorded results, source revisions, and limitations are detailed in [validation notes](validation-notes.md).

This smoke does not qualify Windows 10/11 hardware, Narrator, display scaling, sustained idle usage, animation frame pacing, a real media player, signed upgrades, or graceful save/shutdown. Its bounded cleanup may terminate the owned test process after a normal close request. The separate native qualification matrix remains required for a consumer release.

## Installation and recovery

Notchling's app installation is per-user, requires no administrator privileges, and installs versioned application files under `%LOCALAPPDATA%\Programs\Notch\app\<version>`. Installing a missing shared .NET runtime can require separate administrator approval. Setup refuses to continue while Notchling is running, so a user can save and quit normally. It never forcibly terminates a process to replace files.

Workspace data stays in `%LOCALAPPDATA%\Notch`; credentials stay in Windows Credential Locker. Upgrade and uninstall retain both. Remove credentials in Settings and export data before an intentional reset. An older installed app directory remains available as a recovery option; restore a workspace backup before reverting if a future release changes its schema. Only use a prior signed installer when its published release notes say the data schema is compatible.

Notchling's executable is `Notchling.Windows.exe`. The legacy installation/data directory names, vault identities and installer AppId remain compatible; they do not indicate a second product or require manual migration.

A failed/cancelled app setup uses Inno Setup's install rollback. Shared prerequisites installed beforehand are independently managed Microsoft runtimes and are not removed when Notchling is cancelled or uninstalled. The cloud workflow exercises evaluation setup and uninstall on its Windows Server runner. Consumer Windows 10/11 clean installation, interruption, rollback, repeat upgrade, and retained-data acceptance remain **required native tests**. Evaluation Setup is explicitly unsigned. Production installers and updates must be signed; the release script refuses unsigned production output.
