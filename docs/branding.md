# Notchling identity

**Product name:** Notchling

**Companion mark:** Pixel Dragon

**Tagline:** Your desktop’s little companion.

Notchling is a native desktop companion for music, focus, capture, and everyday controls. Its identity combines a compact black dragon with expressive mint eyes and a warm, pale icon tile. The approved artwork is **A — Pixel Dragon** from the selected concept sheet.

## Name and artwork

Write **Notchling** as one word, with an initial capital. Use **Notchling Free** and **Notchling Premium** for the product tiers. Pixel Dragon names the mascot; it is not a separate product or subscription.

The app icon is [Notchling.png](../src/Notch.Windows/Assets/Notchling.png). Its Windows counterpart is [Notchling.ico](../src/Notch.Windows/Assets/Notchling.ico). The icon belongs on the executable, window, taskbar, tray, installer, README, compact launcher, and Settings identity card. Preserve the mascot’s proportions and eye placement. Keep decorative artwork away from editable content and essential controls.

The icon’s light tile gives the dark silhouette a clear boundary. Small application controls use the same mascot artwork with accessible labels on their parent controls; the image itself does not add a duplicate screen-reader announcement.

The 512 × 512 RGBA master isolates the approved tile from the concept sheet, preserves the character’s proportions, and has transparent outer corners. The Windows ICO includes 16, 20, 24, 32, 40, 48, 64, 96, 128, and 256 px frames. The window and notification area select native sizes for the current display scale and refresh when DPI changes. Explorer restart recovery keeps the current tray icon.

To regenerate the ICO from the approved PNG, run `python3 scripts/export-app-icon.py` with Pillow installed in the development environment. Pillow is not an application dependency. Review shell-size previews against light and dark backgrounds before replacing the master; do not substitute a different mascot from the concept sheet.

## Product and distribution names

| Surface | Identity |
| --- | --- |
| App/window and Windows product metadata | Notchling |
| Windows application files | `Notchling.Windows.exe`, `Notchling.Windows.dll` |
| Start menu and installed-app listing | Notchling |
| Evaluation artifact | `notchling-windows-x64-unpackaged` |
| Signed installer | `Notchling-<version>-windows-x64-setup.exe` |
| Stable update manifest | `notchling-update.json` |
| Pricing | Free; Premium at US$2/month after commercial activation |

## Continuity for existing installations

The displayed brand is separate from stored identity. Existing data remains under `%LOCALAPPDATA%\Notch`, and the installer retains `%LOCALAPPDATA%\Programs\Notch` and its established AppId. Credential Locker resource names, subscription-cache keys, device identity, entitlement issuer/audience, and application mutexes remain compatible. The existing-instance lookup recognizes an older running window too.

The repository URL and `Notch.*` source/project namespaces remain stable. They do not determine the product name shown to a customer. Previous audit text and named CI snapshots retain their historical context.

The rebrand does not activate billing, publish a signed release, change the subscription price, or establish native Windows qualification. Those requirements remain in [release readiness](release-readiness.md) and [validation notes](validation-notes.md).
