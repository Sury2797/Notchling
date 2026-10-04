# Notchling signed Windows delivery

The release path targets **Windows 10 22H2/build 19045 x64 and supported Windows 11 x64 releases equally**. Ordinary build CI produces an unsigned evaluation ZIP. The manual [release workflow](../.github/workflows/release.yml) prepares a signed installer, publisher notice bundle, SPDX inventory, checksums and a GitHub **draft** release. No signing credentials, commercial domain or native QA results are supplied by this repository.

## Configure signing and prepare a candidate

1. Obtain an appropriate trusted publisher code-signing certificate. Store its PFX as `NOTCH_SIGNING_PFX_BASE64` and password as `NOTCH_SIGNING_PFX_PASSWORD` in the GitHub `production` environment. Restrict that environment and avoid exposing secrets to untrusted contributions.
2. On Windows with PowerShell 7, .NET 10, Windows SDK and Inno Setup 6, run `./scripts/build-release.ps1 -Version 0.2.0`. The script runs checks, publishes self-contained x64, signs project-owned binaries and installer/uninstaller, verifies signatures and refuses missing credentials. It preserves third-party publisher signatures.
3. `bundle-notices.py --strict` copies the **actual publisher text** from the exact restored dependency/runtime packs. It includes the installed Inno Setup compiler/engine license and provenance too, and produces `ThirdPartyNotices/`, `publish-inventory.json` and `sbom.spdx.json`; missing runtime-candidate documents stop a signed release. The basename mapping is explicitly a candidate mapping and needs distribution review.
4. Complete [native qualification](native-qualification.md) on both OS targets for this exact installer. Complete commercial setup and approved customer policies. Then publish the reviewed draft as a stable GitHub release with its installer, manifest, checksums and inventory. A draft never updates the stable `/releases/latest` channel.

Use version numbers consistently. The release script validates `major.minor.patch`, applies it to assembly/file metadata, names installer assets and writes the update manifest. Do not reuse an already-published version for different bits.

The desktop entry point is `Notchling.Windows.exe`, and the signed installer is `Notchling-<version>-windows-x64-setup.exe`. Evaluation CI uploads `notchling-windows-x64-unpackaged`; the signed-candidate workflow uploads `notchling-signed-release-candidate`. Repository paths and `NOTCH_*` configuration/secret names remain stable.

## Installation, upgrade and rollback

The Inno Setup installer runs per-user without admin rights. Files live under `%LOCALAPPDATA%\Programs\Notch\app\<version>`. Shortcuts point to that version. Setup refuses to install/uninstall while the application mutex is present, rather than forcibly killing unsaved work. The existing version directory stays available for recovery; failed or cancelled installation uses Inno Setup rollback.

The compatible installation folder and AppId are deliberately retained under the Notchling brand. Setup uses Notchling for displayed names and shortcuts; retaining the identity avoids a second unrelated installation during an evaluation-build upgrade.

Workspace JSON under `%LOCALAPPDATA%\Notch` and Windows Credential Locker entries are retained on upgrade/uninstall. Uninstall does not cancel billing. An intentional data reset requires an export and explicit user action; the installer has no workspace-deletion directive.

Preserve exports before downgrade. Only revert to a prior signed version with a compatible data schema, as documented in its release notes. Interruption, stale/duplicate version installation, rollback and uninstall are native qualification cases with **no recorded execution result yet**.

## Verified updates

The stable manifest is `https://github.com/SuryaK999/Notch-win-linux/releases/latest/download/notchling-update.json`. `install-update.ps1` accepts only that channel and the exact versioned official installer asset. It bounds installer size, verifies SHA-256 and a valid trusted Authenticode signature, and checks the publisher key against a pin already trusted by the installed release. The remote manifest never supplies its own trust anchor.

Settings also exposes an explicit update action through `WindowsUpdateService`. Its trust anchor requires matching valid Windows-trusted signatures on the installed executable and application assembly; a signed `dotnet.exe` alone cannot authorize an unsigned app DLL. Certificate checks run off the UI thread; the service streams bounded downloads, rechecks the hash/signature before opening Setup and refuses unsigned evaluation builds. It does not force the running app to close.

The publisher pin is SHA-256 of the signer certificate's `GetPublicKey()` bytes, not a certificate thumbprint or a string from an untrusted download. `build-release.ps1` prints the pin for the approved certificate. Configure it in the trusted installed release or supply it from an independently verified signed installer; certificate-key rotation needs an approved new trust path.

The helper is compatible with Windows PowerShell and runs an interactive installer after verification:

```powershell
./Update/install-update.ps1 -CurrentVersion 0.2.0 -TrustedPublisherPublicKeySha256 <approved-64-hex-key-pin>
```

Save and quit the app before continuing in Setup. No unsigned installer is executed, and updates are not silently installed in the background. A failed download or verification leaves the running app/data unchanged. End-to-end update installation still needs real Windows evidence.

## Publish gates still outside this cloud workspace

Trusted production signing credentials; final publisher and consumer policies; production billing/email/weather setup; live sandbox lifecycle evidence; distribution/license review; and clean Windows 10/11 native results. The implemented pipeline makes the release reviewable, but these external requirements cannot be fabricated by a code change.
