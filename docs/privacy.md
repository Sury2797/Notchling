# Notchling privacy and data handling

**Development privacy notice — 4 October 2026.** Contact the project maintainer through [GitHub Issues](https://github.com/Sury2797/Notchling/issues). Before operating a paid service, publish the actual controller identity, private contact, hosting/subprocessor details, retention schedule and applicable regional rights. Unconfigured services make no billing requests.

## Data on your device

| Data | Location / behavior |
| --- | --- |
| Notes, scratchpad, reminders, links and shelf references | Plaintext JSON in `%LOCALAPPDATA%\Notch`; bounded local saves |
| Provider tokens | Windows Credential Locker, separate from workspace JSON |
| Clipboard | Off by default; when enabled, up to 50 text entries in memory; clears on disable or exit |
| Calendar and coding imports | Files you choose explicitly; no automatic home-directory scan |
| System / media data | Windows APIs for current local display; optional services may be unavailable |
| Subscription entitlement | Locally cached signed entitlement/credential state for the configured billing service; contains no payment-card details |
| Startup diagnostics | A local, bounded log in `%LOCALAPPDATA%\Notchling\Diagnostics\startup.log` records initialization errors; it is not uploaded automatically |

Notchling does not provide a cloud workspace synchronization service. There is no app analytics/tracking SDK in this checkout. Plaintext files and export backups can contain personal information; secure the device and store backups carefully.

The data folder keeps its existing `Notch` name for compatibility. The branding change does not relocate local files or replace existing Windows vault identities.

## Optional network requests

Connected tools send requests only for their configured function. Weather sends the selected city/coordinates to the configured service. Revenue uses the selected account's read-only credential to obtain payment reports. Analytics contacts the HTTPS endpoint you configure. Providers can receive your IP address and account/request metadata under their own policies.

If the owner configures commercial billing, the application contacts that HTTPS service for checkout, portal access and signed entitlement validation. The billing provider processes payment information in its hosted checkout; the desktop application never embeds Stripe secret keys or webhook-signing secrets. Exact server logs, storage locations, processors and retention must be disclosed before that service launches.

Update checking is explicit. The signed-update helper contacts GitHub for a public release manifest and installer. It verifies the hash, trusted Authenticode signature and publisher key before opening Setup. GitHub's own network privacy policy applies. There is no mandatory unattended update daemon.

## Your controls

Disable/clear clipboard history, remove provider credentials, disconnect optional services, export the workspace and choose demo mode in Settings. An expired Premium subscription preserves local files and recovery/export. Upgrade and uninstall retain workspace files and vault credentials; remove secrets intentionally in Settings before uninstall when desired.

Uninstalling the app does not cancel a subscription. Cancel future renewal in the authenticated billing portal. Account deletion, server-side data access/removal and billing-record retention must be offered by the configured service under its published policy; a local file deletion cannot remove a provider's records.

## Support and diagnostics

Startup diagnostics record exception messages and stack traces to help diagnose a window that fails to open. The log is limited to 256 KiB and can be deleted after quitting the app. Setup keeps separate prerequisite-installation logs in `%LOCALAPPDATA%\Notchling\Setup\Logs`.

Public issue reports are optional. Remove notes, tokens, full file paths, payment/customer identifiers and other personal content before sharing screenshots or logs. Never post credentials or card information. A private commercial support contact is a launch requirement; no fabricated email or controller identity is supplied by this repository.
