# Notchling identity

**Product name:** Notchling

**App icon:** Pixel Dragon

**Tagline:** A dynamic island for your desktop.

Notchling is a native desktop notch inspired by Dynamic Island: a compact top-edge surface that expands into media, focus, capture, and everyday controls, with live activities for timers and reminders. Windows is the first platform; a native Linux application is planned. The product’s visual identity uses a compact black dragon with expressive mint eyes and a warm, pale icon tile. The approved artwork is **A — Pixel Dragon** from the selected concept sheet.

## Name and artwork

Write **Notchling** as one word, with an initial capital. Use **Notchling Free** and **Notchling Premium** for the product tiers. Pixel Dragon names the app artwork. Product copy should lead with the desktop notch, expandable controls, and live activities; the artwork does not imply pet-care features or a virtual-pet product.

The app icon is [Notchling.png](../src/Notch.Windows/Assets/Notchling.png). Its Windows counterpart is [Notchling.ico](../src/Notch.Windows/Assets/Notchling.ico). The icon belongs on the executable, window, taskbar, tray, installer, README, compact launcher, and Settings identity card. Preserve the dragon’s proportions and eye placement. Keep decorative artwork away from editable content and essential controls.

The icon’s light tile gives the dark silhouette a clear boundary. Small application controls use the same app artwork with accessible labels on their parent controls; the image itself does not add a duplicate screen-reader announcement. In v0.4.10 source, the compact strip shows Pixel Dragon when idle and the selected player/source logo during playback. Track thumbnails belong in the expanded Media panel. User-selected browser-provider labels remain explicit and expire with the track/session; they never replace the application icon in Windows or the installer.

The 512 × 512 RGBA master isolates the approved tile from the concept sheet, preserves the character’s proportions, and has transparent outer corners. The Windows ICO includes 16, 20, 24, 32, 40, 48, 64, 96, 128, and 256 px frames. The window and notification area select native sizes for the current display scale and refresh when DPI changes. Explorer restart recovery keeps the current tray icon.

To regenerate the ICO from the approved PNG, run `python3 scripts/export-app-icon.py` with Pillow installed in the development environment. Pillow is not an application dependency. Review shell-size previews against light and dark backgrounds before replacing the master; do not substitute different artwork from the concept sheet.

## Product and distribution names

| Surface | Identity |
| --- | --- |
| App/window and Windows product metadata | Notchling |
| Windows application files | `Notchling.Windows.exe`, `Notchling.Windows.dll` |
| Start menu and installed-app listing | Notchling |
| Normal evaluation artifact | `notchling-windows-<arch>-installer` |
| Evaluation installer | `Notchling-<version>-windows-<arch>-evaluation-setup.exe` |
| Optional application-folder artifact | `notchling-windows-<arch>-app-only` |
| Signed installer | `Notchling-<version>-windows-<arch>-setup.exe` |
| Stable update manifest | `notchling-update.json` |
| Pricing | Free; Premium at US$2/month after commercial activation |

Lead ordinary download instructions with the single setup EXE. Setup handles the shared-runtime check and installation; do not send customers through developer build steps or manual DLL copying. App-only describes the application payload without bundled .NET or Windows App SDK runtimes. It does not eliminate shared runtime dependencies or the initial prerequisite downloads documented in [Windows support](windows-support.md). Historical CI records retain the previous artifact names and distribution mode.

The published v0.3.4 download is x64. Current source configures `<arch>` as `x64`, `x86` or `arm64`; new public assets must pass their own qualification before becoming recommended downloads. Architecture is package metadata, not a different product name.

## Continuity for existing installations

The displayed brand is separate from stored identity. Existing data remains under `%LOCALAPPDATA%\Notch`, and the installer retains `%LOCALAPPDATA%\Programs\Notch` and its established AppId. Credential Locker resource names, subscription-cache keys, device identity, entitlement issuer/audience, and application mutexes remain compatible. The existing-instance lookup recognizes an older running window too.

The repository uses the product name: [Sury2797/Notchling](https://github.com/Sury2797/Notchling). GitHub redirects the previous repository URL after the rename. The `Notch.*` source/project namespaces remain compatible and do not determine the displayed product name. Previous audit text and named CI snapshots retain their historical context.

The rebrand does not activate billing, publish a signed release, change the subscription price, or establish native Windows qualification. Those requirements remain in [release readiness](release-readiness.md) and [validation notes](validation-notes.md).
