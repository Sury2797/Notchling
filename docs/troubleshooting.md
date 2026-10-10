# Installation and startup help

Use the official **Windows x64 Setup EXE** from [Notchling releases](https://github.com/Sury2797/Notchling/releases). Windows 10 Pro 22H2/build 19045 and Windows 11 are targets; an app-only folder needs shared runtimes already installed. The installer checks prerequisites for the account running Setup. An existing notebook is retained during upgrade; do not delete `%LOCALAPPDATA%\Notch` to troubleshoot installation.

## Windows App Runtime error 0x8007007E

The 0.2.0 installer can fail while preparing Microsoft's Windows App Runtime, even when .NET and a downloaded, verified Microsoft installer are present. Error **0x8007007E** means a module could not be found; it does not identify a particular missing DLL, and retrying an Internet download alone does not fix it.

The [v0.3.4 installer](https://github.com/Sury2797/Notchling/releases/download/notchling-evaluation-0.3.4/Notchling-0.3.4-windows-x64-evaluation-setup.exe) automatically recovers from this native-installer error by reading the same trusted Microsoft EXE's signed MSIX resources without executing its loader. It validates the four x64 package identities and versions, deploys the framework before dependent packages for the installing account, and reuses a newer healthy shared Singleton. No SDK, unsigned-package mode, DLL download site, forced application shutdown, or manual package extraction is needed.

Windows still verifies package signatures and enforces deployment policy. If package registration itself fails, Setup reports that error and stops. A genuinely damaged or policy-blocked Windows AppX service may need an administrator's Windows repair; the app cannot safely bypass those checks.

## Logs that identify the failing stage

Open **Win + R** and paste `%LOCALAPPDATA%\Notchling\Setup\Logs` for installation diagnostics:

- `setup-prerequisites.log`: component checks, download verification, installer result, recovery and current-user package readiness.
- `windowsappruntimeinstall-x64.exe.stdout.log` and `.stderr.log`: native Microsoft's installer output when it ran.
- `dotnet-runtime-install.log`: Microsoft's .NET installer details when that prerequisite was installed.

For an installed app that closes unexpectedly, open `%LOCALAPPDATA%\Notchling\Diagnostics\startup.log`. Startup records the application version, Windows build and runtime; fatal errors show a native dialog with the hexadecimal error and diagnostic path. Optional artwork/theme/integration failures have local diagnostics and should leave the remaining controls usable.

Share the app version, Windows build, action immediately before failure, error code, and relevant final log lines in [GitHub Issues](https://github.com/Sury2797/Notchling/issues). Remove personal paths or content before publishing them; notebook files and provider credentials are not needed to diagnose prerequisite installation.

## Setup succeeds but the notch is hidden

Launch **Notchling** from the Start menu. Launching it again reopens the existing instance. **Ctrl + Shift + Space** or tray **Open Notchling** also opens the panel. Explicit launch/open temporarily overrides fullscreen suppression. If Explorer is rebuilding the notification area, the app retries its tray registration; the notch's context menu remains available for Settings and Quit.

An unavailable player produces a disabled media empty state. The published v0.3.4 public-testing evaluation unlocks all tools without payment; local tools do not require a Notchling account. Connected sources still need their own setup. Use **Settings → Connection status → Check connections** to distinguish unavailable native services, missing provider credentials/imports and real request failures. Use the [qualified v0.3.4 installer](https://github.com/Sury2797/Notchling/releases/tag/notchling-evaluation-0.3.4) to test the unlocked catalog.

## Preview, Settings and unexpected collapse

Use v0.2.9 or newer for the UI repairs. Old saved demo mode now starts with real data. **Settings → Sample-data preview** is an explicit choice for the current session; **Exit preview** returns to live state. Preview never starts audio or unlocks Premium. When no supported Windows media session exists, the player shows **Nothing playing** and disables playback controls.

Settings scrolls vertically; optional connections and placement controls expand inside their own sections. Unapplied number drafts and scroll position are retained during ordinary preference changes. Moving between child controls or through the dock gap keeps the notch open. Actual keyboard editing defers collapse briefly; idle focus in Settings does not hold an unpinned panel open indefinitely. Leaving the interactive surface collapses it after the finite editing lease expires; **Keep expanded**, **Esc**, the tray and **Ctrl + Shift + Space** remain explicit controls.

If the process actually exits, share the final startup-log lines and the action that preceded it. A compact strip that remains visible and reopens is a collapse, rather than a process exit. The supplied v0.2.5 recording shows that distinction; an actual crash needs diagnostic evidence.

## Updates in an evaluation build

Evaluation installers are unsigned. In v0.2.11 and later, **Settings → Updates and troubleshooting → Check for updates** explains manual updates without displaying a signing error. Open **Release page**, download the newest evaluation Setup EXE and run it over the existing installation; your local notebook is retained. Check the app version in Settings after reopening.

The automatic stable updater is reserved for signed releases. It continues to verify the installed publisher, the manifest and installer integrity; evaluation guidance does not bypass those protections.

In the **v0.4.12 source candidate**, **Check for updates** reads official release metadata and offers the compatible release page. The optional **Check for updates daily** switch is off by default. Update availability appears quietly in the compact indicator and Recent notifications; it does not replace your current tool. A failed online check leaves the manual release link available. Cancel stops pending work. Evaluation builds do not download or execute an installer through this action; **Install verified update** becomes available only with trusted signed update eligibility.

## Compact media shows a browser logo

In v0.4.12 source, a source logo appears in the compact strip and the actual cover/video thumbnail stays in expanded Media. If Windows identifies only Chrome, Edge or Firefox, the browser logo is the available automatic identity. Use the Media source menu to select YouTube or another supported provider for that track. The tooltip marks the selection, and a changed track/session clears it. This does not change the Windows player connection or scan browser tabs.

## Choosing an architecture-specific candidate

The published v0.3.4 EXE is x64. The current source configures future x64, x86 and ARM64 installers; check **Settings → System → About → System type** and choose the native architecture after the corresponding release is qualified. A 32-bit x86 app on 64-bit Windows requires its own x86 .NET/runtime packages plus native Windows App Runtime dependencies. Setup checks that mixture. An unrelated installed runtime, a renamed EXE or an extracted app folder does not resolve an architecture mismatch.
