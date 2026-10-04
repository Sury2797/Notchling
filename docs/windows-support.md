# Windows support contract

Windows 10 and Windows 11 are **equal release targets**. The supported desktop architecture is x64.

| System | Release baseline | Requirement |
| --- | --- | --- |
| Windows 10 | 22H2, build 19045, x64 | Fully patched for the machine's servicing channel |
| Windows 11 | Microsoft-supported x64 releases at launch | Record every qualified build in the release evidence |
| Windows N | Same builds, with or without Media Feature Pack | Optional media/sound can report unavailable; local tools must remain usable |
| ARM64 / x86 | No native release currently configured | Do not advertise native support or treat emulation as qualification |

The Windows API target and `TargetPlatformMinVersion` remain `10.0.19041` so code compiles against the common Windows 10-era API surface. The supported product baseline is recorded separately as `MinimumSupportedWindowsBuild=19045`, and the installer refuses older builds. An API floor or successful build is not proof of interactive compatibility. A Windows Server GitHub runner is a build host, not a consumer Windows 10/11 test machine.

The overlay, shortcuts, media controls, fonts, accessibility, installation and resource behavior must pass independently on both systems for the **same signed artifact**. Windows 11-only cosmetic APIs must have harmless Windows 10 fallbacks. Use Segoe UI and a guaranteed monospace font rather than relying on developer-installed fonts. No platform may lose basic controls because a cosmetic feature is unavailable.

## Qualification record

Copy [the native qualification template](native-qualification.md) for each candidate. Record the app version, source commit, installer SHA-256, OS edition/build, GPU/driver, DPI, monitors, text scaling and result. Test Windows 10 separately from Windows 11; do not combine their results into a single passing row.

The cloud execution host is Linux and cannot launch WinUI. Automated source checks and simulated view-model cases are useful evidence with defined limits; neither certifies native launch, accessibility or performance. No native qualification result is currently recorded in this repository.

## Installation and recovery

The release candidate installer is per-user, requires no administrator privileges and installs versioned application files under `%LOCALAPPDATA%\Programs\Notch\app\<version>`. It refuses to continue while Notch is running, so a user can save and quit normally. It never forcibly terminates a process to replace files.

Workspace data stays in `%LOCALAPPDATA%\Notch`; credentials stay in Windows Credential Locker. Upgrade and uninstall retain both. Remove credentials in Settings and export data before an intentional reset. An older installed app directory remains available as a recovery option; restore a workspace backup before reverting if a future release changes its schema. Only use a prior signed installer when its published release notes say the data schema is compatible.

A failed/cancelled setup uses Inno Setup's install rollback. Clean installation, interruption, rollback, repeat upgrade and uninstall are **required tests**, not verified outcomes yet. Native installers and updates must be signed; the release script refuses unsigned output.
