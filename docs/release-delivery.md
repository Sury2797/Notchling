# Notchling signed Windows delivery

The release path targets **Windows 10 22H2/build 19045 x64 and supported Windows 11 x64 releases equally**. Ordinary build CI produces one unsigned evaluation setup EXE as the normal user download, plus an optional app-only folder artifact for advanced evaluation. GitHub Actions wraps each artifact in a download ZIP; extract the installer artifact once and run Setup. The manual [release workflow](../.github/workflows/release.yml) prepares a signed installer, publisher notice bundle, SPDX inventory, checksums and a GitHub **draft** release. Production signing credentials, a commercial domain, and consumer Windows 10/11 qualification remain external release requirements.

## Shared runtime requirements

Setup detects the [Windows x64 .NET 10 Runtime](https://dotnet.microsoft.com/en-us/download/dotnet/10.0) and [Windows x64 Windows App SDK 1.8 runtime](https://learn.microsoft.com/windows/apps/windows-app-sdk/downloads). It reuses compatible installed runtimes, or downloads the official installers and installs the missing prerequisites. Internet access is needed when a runtime is missing; the .NET installer may request administrator approval. The pinned Windows App SDK package is `1.8.260921001`; its required runtime package version is `8000.994.2142.0` or newer in the 1.8 line. Developer SDKs are unnecessary for end users.

The setup EXE contains the application payload without bundling .NET or Windows App SDK runtimes. First-time runtime downloads are additional to the installer’s own download size. Later app installation reuses compatible shared runtimes. Framework-dependent publishing (`SelfContained=false`, `WindowsAppSDKSelfContained=false`) removes repeated runtime copies from the app; it does not remove the runtime dependency.

The optional app-only folder contains the launcher, app assemblies, assets, and dependency notices. Keep them together. This advanced path requires the shared runtimes to be installed already; it does not perform Setup’s prerequisite installation.

CI records setup EXE and extracted application sizes, rejects bundled runtime files, and verifies the app against installed shared runtimes. It exercises Setup's missing Windows App Runtime path on a hosted Windows Server desktop. The runner already has .NET 10 through `setup-dotnet`, so missing-.NET installation, denied UAC, cancellation, offline errors, and consumer Windows 10/11 clean-machine acceptance still require [native qualification](native-qualification.md). The evaluation setup is explicitly unsigned; production signing remains mandatory for public release and updates.

Package/prerequisite measurements from passing [CI run 37203173532](https://github.com/Sury2797/Notchling/actions/runs/37203173532), source revision [`fafa2cc`](https://github.com/Sury2797/Notchling/commit/fafa2ccac2c34e6b464a1254d637164df59b50ee), on 4 October 2026 UTC:

| Component | Actual or estimated bytes | Scope |
| --- | ---: | --- |
| Evaluation setup EXE | 8,875,854 | Measured application installer; 8.46 MiB |
| Extracted application files | 40,648,773 | Measured installed payload; 38.77 MiB |
| Missing Windows App Runtime download | 106,879,800 | Measured Setup prerequisite transfer; 101.93 MiB |
| Missing .NET 10.0.12 x64 Runtime download | 30,663,104 | Official HTTP `Content-Length` estimate; 29.24 MiB |
| .NET release metadata | Approximately 1 MiB | Additional estimate when .NET is missing |

That cloud setup reused .NET 10.0.11 already installed through the SDK; it did not exercise the missing-.NET path. Actual evaluation setup, missing Windows App Runtime installation, installed-app launch and Free UI interaction, and uninstall passed on the hosted Windows Server runner.

A machine missing both shared runtimes therefore needs roughly **147 MB** of first-install downloads, including the app installer and estimated .NET metadata. The app installer alone is about **8.9 MB**; compatible preinstalled runtimes avoid the additional transfers. Patches and later prerequisite releases can change these amounts. Shared runtime disk usage is separate from the extracted application size.

## Installed Windows cloud smoke

The Windows build job compiles the evaluation setup, installs it into a disposable directory, and launches the **installed** `Notchling.Windows.exe`. Setup's prerequisite log shows runtime reuse or downloads and verifies the x64 framework, Main, Singleton, and DDLM packages for Windows App Runtime 1.8. Reports and logs are uploaded in `notchling-windows-cloud-test-results`, including package sizes, setup/prerequisite logs, and native/UI smoke JSON. Results are tied to their source revisions in [validation notes](validation-notes.md).

The [tested evaluation installer](https://github.com/Sury2797/Notchling/actions/runs/37203173532#artifacts) is available as a GitHub Actions artifact subject to sign-in and the workflow's 14-day retention. Extract its download ZIP once and run `Notchling-0.2.0-windows-x64-evaluation-setup.exe`.

For users, [the evaluation release download](https://github.com/Sury2797/Notchling/releases/download/notchling-evaluation-0.2.0/Notchling-0.2.0-windows-x64-evaluation-setup.exe) provides the setup EXE directly, without sign-in, ZIP extraction, or artifact expiry. Pushing a `notchling-evaluation-<version>` tag runs the full build/installed-app checks first; only after all jobs pass does CI publish that one setup EXE as an explicitly unsigned GitHub prerelease. The tag must match the project's version. This channel does not publish a stable update manifest or replace signed production releases.

Before installation, `report-package-size.py --require-app-only` requires the app assemblies, bootstrapper files, and nonempty `resources.pri`, and refuses bundled CoreCLR, Windows App Runtime, ONNX, or DirectML payload. The project enables the current SDK PRI pipeline so compiled XAML is published correctly; hand-copying resources from an older SDK layout is not the publishing contract.

The installed app exposed a visible native window with an icon and responded to all five bounded `WM_NULL` samples in the passing run. UI Automation opened and pinned the Free notch, verified disabled no-player media controls, ran Pomodoro start/pause/reset, and verified scratchpad durable save, navigation round trip, and clearing. The test cleaned up its owned process and the installed uninstaller returned success.

First visible window appeared after **816.2 ms**. A **5.02-second startup sample** averaged **107.35 MiB working set**, **31.36 MiB private memory**, and **0.700% CPU normalized across all logical processors**. These hosted-runner observations are not sustained idle measurements or rendered-animation results.

This is a Windows Server cloud smoke, not consumer Windows 10/11 hardware qualification or an animation benchmark. It does not test a real media player, signed upgrade/update trust, missing-.NET UAC, or graceful save/shutdown; bounded process cleanup can terminate the test process. Those limits remain separate acceptance work.

## Configure signing and prepare a candidate

1. Obtain an appropriate trusted publisher code-signing certificate. Store its PFX as `NOTCH_SIGNING_PFX_BASE64` and password as `NOTCH_SIGNING_PFX_PASSWORD` in the GitHub `production` environment. Restrict that environment and avoid exposing secrets to untrusted contributions.
2. On Windows with PowerShell 7, .NET 10 SDK, Windows SDK and Inno Setup 6, run `./scripts/build-release.ps1 -Version 0.2.0`. The script runs checks, publishes framework-dependent x64 without bundled runtimes, signs project-owned binaries and installer/uninstaller, verifies signatures and refuses missing credentials. It preserves third-party publisher signatures.
3. `bundle-notices.py --strict` copies the **actual publisher text** from the exact restored dependency/runtime packs. It includes the installed Inno Setup compiler/engine license and provenance too, and produces `ThirdPartyNotices/`, `publish-inventory.json` and `sbom.spdx.json`; missing runtime-candidate documents stop a signed release. The basename mapping is explicitly a candidate mapping and needs distribution review.
4. Complete [native qualification](native-qualification.md) on both OS targets for this exact installer. Complete commercial setup and approved customer policies. Then publish the reviewed draft as a stable GitHub release with its installer, manifest, checksums and inventory. A draft never updates the stable `/releases/latest` channel.

Use version numbers consistently. The release script validates `major.minor.patch`, applies it to assembly/file metadata, names installer assets and writes the update manifest. Do not reuse an already-published version for different bits.

The desktop entry point is `Notchling.Windows.exe`. Evaluation CI uploads `notchling-windows-x64-installer` containing `Notchling-<version>-windows-x64-evaluation-setup.exe`; its optional `notchling-windows-x64-app-only` artifact contains the application folder directly. The signed installer is `Notchling-<version>-windows-x64-setup.exe`, and the signed-candidate workflow uploads `notchling-signed-release-candidate`. Repository paths and `NOTCH_*` configuration/secret names remain stable.

## Installation, upgrade and rollback

Notchling's app installation is per-user and does not require administrator rights; installing a missing shared .NET runtime can require a separate UAC approval. Files live under `%LOCALAPPDATA%\Programs\Notch\app\<version>`. Shortcuts point to that version. Setup refuses to install/uninstall while the application mutex is present, rather than forcibly killing unsaved work. The existing version directory stays available for recovery; failed or cancelled app installation uses Inno Setup rollback. Installed shared runtimes are independently managed and are not removed by app rollback or uninstall.

The compatible installation folder and AppId are deliberately retained under the Notchling brand. Setup uses Notchling for displayed names and shortcuts; retaining the identity avoids a second unrelated installation during an evaluation-build upgrade.

Workspace JSON under `%LOCALAPPDATA%\Notch` and Windows Credential Locker entries are retained on upgrade/uninstall. Uninstall does not cancel billing. An intentional data reset requires an export and explicit user action; the installer has no workspace-deletion directive.

Preserve exports before downgrade. Only revert to a prior signed version with a compatible data schema, as documented in its release notes. Cloud CI includes evaluation setup/uninstall; interruption, stale/duplicate versions, rollback, retained-data behavior, and consumer Windows 10/11 install/uninstall remain separate native qualification cases.

## Verified updates

The stable manifest is `https://github.com/Sury2797/Notchling/releases/latest/download/notchling-update.json`. `install-update.ps1` accepts only that channel and the exact versioned official installer asset. It bounds installer size, verifies SHA-256 and a valid trusted Authenticode signature, and checks the publisher key against a pin already trusted by the installed release. The remote manifest never supplies its own trust anchor.

Settings also exposes an explicit update action through `WindowsUpdateService`. Its trust anchor requires matching valid Windows-trusted signatures on the installed executable and application assembly; a signed `dotnet.exe` alone cannot authorize an unsigned app DLL. Certificate checks run off the UI thread; the service streams bounded downloads, rechecks the hash/signature before opening Setup and refuses unsigned evaluation builds. It does not force the running app to close.

The publisher pin is SHA-256 of the signer certificate's `GetPublicKey()` bytes, not a certificate thumbprint or a string from an untrusted download. `build-release.ps1` prints the pin for the approved certificate. Configure it in the trusted installed release or supply it from an independently verified signed installer; certificate-key rotation needs an approved new trust path.

The helper is compatible with Windows PowerShell and runs an interactive installer after verification:

```powershell
./Update/install-update.ps1 -CurrentVersion 0.2.0 -TrustedPublisherPublicKeySha256 <approved-64-hex-key-pin>
```

Save and quit the app before continuing in Setup. No unsigned installer is executed, and updates are not silently installed in the background. A failed download or verification leaves the running app/data unchanged. End-to-end update installation still needs real Windows evidence.

## Publish gates still outside this cloud workspace

Trusted production signing credentials; final publisher and consumer policies; production billing/email/weather setup; live sandbox lifecycle evidence; distribution/license review; and clean Windows 10/11 native results. The implemented pipeline makes the release reviewable, but these external requirements cannot be fabricated by a code change.
