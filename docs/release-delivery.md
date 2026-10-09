# Notchling signed Windows delivery

The release path targets **Windows 10 22H2/build 19045 x64 and supported Windows 11 x64 releases equally**. Ordinary build CI produces one unsigned evaluation setup EXE as the normal user download, plus an optional app-only folder artifact for advanced evaluation. GitHub Actions wraps each artifact in a download ZIP; extract the installer artifact once and run Setup. The manual [release workflow](../.github/workflows/release.yml) prepares a signed installer, publisher notice bundle, SPDX inventory, checksums and a GitHub **draft** release. Production signing credentials, a commercial domain, and consumer Windows 10/11 qualification remain external release requirements.

**Current public-testing release: v0.3.4.** [Download the Windows x64 Setup EXE](https://github.com/Sury2797/Notchling/releases/download/notchling-evaluation-0.3.4/Notchling-0.3.4-windows-x64-evaluation-setup.exe) directly, without GitHub sign-in or ZIP extraction. Source [`8ad94ce`](https://github.com/Sury2797/Notchling/commit/8ad94cefcbd6417a24312edd4c761d0de2fa28d5) passed all jobs in [run 37944357692](https://github.com/Sury2797/Notchling/actions/runs/37944357692), including the two-host installed-app checks and publication. All 21 catalog panels and extended controls are free for public testing in Release and Debug; checkout is paused, and actual paid-entitlement validation remains separate. The installer is unsigned. Consumer Windows 10/11 hardware and stable commercial acceptance remain separate requirements.

## Shared runtime requirements

Setup detects the [Windows x64 .NET 10 Runtime](https://dotnet.microsoft.com/en-us/download/dotnet/10.0) and [Windows x64 Windows App SDK 1.8 runtime](https://learn.microsoft.com/windows/apps/windows-app-sdk/downloads). It reuses compatible installed runtimes, or downloads the official installers and installs the missing prerequisites. Internet access is needed when a runtime is missing; the .NET installer may request administrator approval. The pinned Windows App SDK package is `1.8.260921001`; its required runtime package version is `8000.994.2142.0` or newer in the 1.8 line. Developer SDKs are unnecessary for end users.

The setup EXE contains the application payload without bundling .NET or Windows App SDK runtimes. First-time runtime downloads are additional to the installer’s own download size. Later app installation reuses compatible shared runtimes. Framework-dependent publishing (`SelfContained=false`, `WindowsAppSDKSelfContained=false`) removes repeated runtime copies from the app; it does not remove the runtime dependency.

The optional app-only folder contains the launcher, app assemblies, assets, and dependency notices. Keep them together. This advanced path requires the shared runtimes to be installed already; it does not perform Setup’s prerequisite installation.

The current qualification workflow records setup EXE and extracted application sizes, rejects bundled runtime files, and verifies the installed app on both hosted Windows desktops. Separate prerequisite fixtures exercise Microsoft downloads/installers and signed-resource recovery, including a forced missing-.NET detection branch while retaining the runner's installed SDKs. These real integration fixtures passed for v0.3.4; they are not bare consumer Windows images. Final Setup reused the verified runtimes on both prepared runners and downloaded **zero additional prerequisite bytes**. Denied UAC, cancellation, offline errors, and consumer Windows 10/11 clean-machine acceptance still require [native qualification](native-qualification.md). Production signing remains mandatory for stable commercial releases and automatic updates.

### Current package measurements — v0.3.4, 9 October 2026 UTC

| Component | Measured bytes | Scope |
| --- | ---: | --- |
| Published evaluation setup EXE | 8,923,610 | 8.51 MiB; direct public download verified |
| Extracted application files | 40,784,258 | 38.89 MiB; shared runtimes excluded |
| Final Setup prerequisite download | 0 on both hosts | Compatible verified runtimes were already installed by the qualification fixtures |

The public EXE was downloaded without authentication; its `MZ`/Windows PE header and exact size were verified. Its SHA-256 matched the published release: `13e4cd031568aa166bb2aa5e002f7887cd76b2c57c3d6a48cbb1abedbfd7bc3a`.

Zero prerequisite download in final cloud Setup does not mean a clean PC needs no runtime downloads. The dated estimates below remain useful for planning roughly **147 MB** total when both runtimes are missing; compatible preinstalled runtimes avoid those extra transfers.

### Historical package measurements — v0.2.0, 4 October 2026 UTC

The following measurements belong to passing [CI run 37203173532](https://github.com/Sury2797/Notchling/actions/runs/37203173532), source revision [`fafa2cc`](https://github.com/Sury2797/Notchling/commit/fafa2ccac2c34e6b464a1254d637164df59b50ee). They are not v0.3.4 measurements:

| Component | Actual or estimated bytes | Scope |
| --- | ---: | --- |
| Evaluation setup EXE | 8,875,854 | Measured application installer; 8.46 MiB |
| Extracted application files | 40,648,773 | Measured installed payload; 38.77 MiB |
| Missing Windows App Runtime download | 106,879,800 | Measured Setup prerequisite transfer; 101.93 MiB |
| Missing .NET 10.0.12 x64 Runtime download | 30,663,104 | Official HTTP `Content-Length` estimate; 29.24 MiB |
| .NET release metadata | Approximately 1 MiB | Additional estimate when .NET is missing |

That historical cloud setup reused .NET 10.0.11 already installed through the SDK; it did not exercise the missing-.NET path. Its actual evaluation setup, missing Windows App Runtime installation, installed-app launch and basic Free UI interaction, and uninstall passed on the hosted Windows Server runner.

Those recorded runtime versions imply roughly **147 MB** of first-install downloads on a machine missing both shared runtimes, including the app installer and estimated .NET metadata. The current v0.3.4 app installer alone is about **8.9 MB**; compatible preinstalled runtimes avoid the additional transfers. Later prerequisite releases can change the amount. Shared runtime disk usage is separate from the extracted application size.

## Installed Windows cloud smoke

The Windows build matrix compiles evaluation setup, installs it into a disposable directory, and launches the **installed** `Notchling.Windows.exe` on `windows-latest` and `windows-2022`. Setup's prerequisite log shows runtime reuse or downloads and verifies the required Windows App Runtime 1.8 packages for the installing account, accepting a compatible newer shared Singleton. Reports and logs are uploaded in `notchling-windows-cloud-test-results` and its `-windows-2022` counterpart, including package sizes, prerequisite integration/recovery results, setup logs, and native/UI smoke JSON. Native captures are best-effort and explicitly report unavailable or uniform output; they are not a functional pass criterion. Results are tied to their source revisions in [validation notes](validation-notes.md).

The [historical v0.2.0 run](https://github.com/Sury2797/Notchling/actions/runs/37203173532#artifacts) records `Notchling-0.2.0-windows-x64-evaluation-setup.exe`. CI artifacts require sign-in and have 14-day retention; a historical run link does not promise its artifacts remain downloadable.

The [historical v0.2.0 release download](https://github.com/Sury2797/Notchling/releases/download/notchling-evaluation-0.2.0/Notchling-0.2.0-windows-x64-evaluation-setup.exe) identifies the measured baseline, rather than the recommended current download. Public release assets provide the setup EXE directly, without sign-in, ZIP extraction, or CI artifact expiry. Pushing a `notchling-evaluation-<version>` tag runs the full build/installed-app checks first; only after all jobs pass does CI publish that one setup EXE as an explicitly unsigned GitHub prerelease. The tag must match the project's version. This channel does not publish a stable update manifest or replace signed production releases. Default links change only after the new public EXE and its size, Windows PE header, and published SHA-256 have been verified.

Before installation, `report-package-size.py --require-app-only` requires the app assemblies, bootstrapper files, and nonempty `resources.pri`, and refuses bundled CoreCLR, Windows App Runtime, ONNX, or DirectML payload. The project enables the current SDK PRI pipeline so compiled XAML is published correctly; hand-copying resources from an older SDK layout is not the publishing contract.

The historical v0.2.0 run exposed a visible native window with an icon and responded to all five bounded `WM_NULL` samples. UI Automation opened and pinned its basic Free notch, verified disabled no-player media controls, ran Pomodoro start/pause/reset, and verified scratchpad durable save, navigation round trip, and clearing. The test cleaned up its owned process and the installed uninstaller returned success.

That dated run's first visible window appeared after **816.2 ms**. Its **5.02-second startup sample** averaged **107.35 MiB working set**, **31.36 MiB private memory**, and **0.700% CPU normalized across all logical processors**. These historical hosted-runner observations are not current candidate values, sustained idle measurements, or rendered-animation results.

The v0.3.4 run passed all 21 public-testing panels and selected direct controls, eight unconfigured connection states, real `SendInput` pointer hover/leave and transparent flanks, finite keyboard-editing lease expiry, compact credential alignment, retained drafts/scroll, featured-panel scroll reset, notes/Awake disposable controls, preview exit, manual-update guidance, reopening, and uninstall on both hosts. The Windows/Linux regression jobs each passed **345 checks**: 128 Core, 76 Commerce, 74 Debug view-model, 24 Release view-model, 33 linked native API doubles, and 10 Python release cases. Source/XAML member projections also compiled successfully.

| v0.3.4 hosted startup observation | `windows-latest` | `windows-2022` |
| --- | ---: | ---: |
| First visible window | 690.6 ms | 1,135 ms |
| Sample duration | 5.06 seconds | 5.01 seconds |
| Average working set | 110.85 MiB | 111.62 MiB |
| Average private memory | 31.74 MiB | 33.83 MiB |
| CPU normalized across all logical processors | 1.39% | 1.56% |

These are short hosted-runner startup samples, not settled idle targets, sustained performance measurements, or animation frame-pacing results.

This is Windows Server cloud qualification, not consumer Windows 10/11 hardware qualification or an animation benchmark. It does not establish live media/provider connectivity, signed upgrade/update trust, denied-UAC behavior, or graceful save/shutdown; bounded process cleanup can terminate the test process. Unlocking Weather does not supply its absent licensed backend. Those limits remain separate acceptance work.

## Configure signing and prepare a candidate

1. Obtain an appropriate trusted publisher code-signing certificate. Store its PFX as `NOTCH_SIGNING_PFX_BASE64` and password as `NOTCH_SIGNING_PFX_PASSWORD` in the GitHub `production` environment. Restrict that environment and avoid exposing secrets to untrusted contributions.
2. On Windows with PowerShell 7, .NET 10 SDK, Windows SDK and Inno Setup 6, run `./scripts/build-release.ps1 -Version 0.3.4`. The script runs checks, publishes framework-dependent x64 without bundled runtimes, signs project-owned binaries and installer/uninstaller, verifies signatures and refuses missing credentials. It preserves third-party publisher signatures.
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
./Update/install-update.ps1 -CurrentVersion 0.3.4 -TrustedPublisherPublicKeySha256 <approved-64-hex-key-pin>
```

Save and quit the app before continuing in Setup. No unsigned installer is executed, and updates are not silently installed in the background. A failed download or verification leaves the running app/data unchanged. End-to-end update installation still needs real Windows evidence.

## Publish gates still outside this cloud workspace

Trusted production signing credentials; final publisher and consumer policies; production billing/email/weather setup; live sandbox lifecycle evidence; distribution/license review; and clean Windows 10/11 native results. The implemented pipeline makes the release reviewable, but these external requirements cannot be fabricated by a code change.
