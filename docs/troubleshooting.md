# Installation and startup help

Use the official **Windows x64 Setup EXE** from [Notchling releases](https://github.com/Sury2797/Notchling/releases). Windows 10 Pro 22H2/build 19045 and Windows 11 are targets; an app-only folder needs shared runtimes already installed. The installer checks prerequisites for the account running Setup. An existing notebook is retained during upgrade; do not delete `%LOCALAPPDATA%\Notch` to troubleshoot installation.

## Windows App Runtime error 0x8007007E

The 0.2.0 installer can fail while preparing Microsoft's Windows App Runtime, even when .NET and a downloaded, verified Microsoft installer are present. Error **0x8007007E** means a module could not be found; it does not identify a particular missing DLL, and retrying an Internet download alone does not fix it.

The [v0.2.5 installer](https://github.com/Sury2797/Notchling/releases/download/notchling-evaluation-0.2.5/Notchling-0.2.5-windows-x64-evaluation-setup.exe) automatically recovers from this native-installer error by reading the same trusted Microsoft EXE's signed MSIX resources without executing its loader. It validates the four x64 package identities and versions, deploys the framework before dependent packages for the installing account, and reuses a newer healthy shared Singleton. No SDK, unsigned-package mode, DLL download site, forced application shutdown, or manual package extraction is needed.

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

An unavailable player produces a disabled media empty state. Connected services and paid access require their configured providers; local Free media controls, Pomodoro, and scratchpad do not require billing or a Notchling account.
