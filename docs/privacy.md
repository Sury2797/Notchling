# Notchling privacy and data handling

**Development privacy notice — 13 October 2026.** Contact the project maintainer through [GitHub Issues](https://github.com/Sury2797/Notchling/issues). Before operating a paid service, publish the actual controller identity, private contact, hosting/subprocessor details, retention schedule and applicable regional rights. Unconfigured services make no billing requests.

## Data on your device

| Data | Location / behavior |
| --- | --- |
| Notes, scratchpad, reminders, links and shelf references | Plaintext JSON in `%LOCALAPPDATA%\Notch`; bounded local saves |
| Shelf file/image captures in v0.4.20 source | Files in `%LOCALAPPDATA%\Notch\shelf-captures`; explicit drops, Paste image or opt-in file copies only. Up to 50 MiB per copy, 250 MiB and 100 saved files in total; no image-URL download |
| Provider tokens | Windows Credential Locker, separate from workspace JSON |
| Clipboard | Off by default; when enabled, up to 50 text entries in memory; clears on disable or exit |
| Calendar and Coding imports | Files you choose explicitly |
| AI usage in v0.4.20 | Read-only supported CLI JSONL data folders while the panel is visible; memory-only usage metadata, no credentials or network requests |
| Update result | Bounded local status in `%LOCALAPPDATA%\Notchling\Updates\latest-result.json`; installer diagnostics in that folder; no automatic upload |
| System / media data | Windows APIs for current local display; optional services may be unavailable |
| Media source choice | A user-selected provider label stays in memory for the active track/session. No browser URL, browsing history, tab scan or remote favicon lookup |
| Notification history | Session-only, capped at 50 notices; Settings shows the latest ten. Update notices do not upload workspace data |
| Subscription entitlement | Locally cached signed entitlement/credential state for the configured billing service; contains no payment-card details |
| Startup diagnostics | A local, bounded log in `%LOCALAPPDATA%\Notchling\Diagnostics\startup.log` records initialization errors; it is not uploaded automatically |

Notchling does not provide a cloud workspace synchronization service. There is no app analytics/tracking SDK in this checkout. Plaintext files and export backups can contain personal information; secure the device and store backups carefully.

The data folder keeps its existing `Notch` name for compatibility. The branding change does not relocate local files or replace existing Windows vault identities.

In the v0.4.20 candidate, local files and folders added to Shelf remain references by default. **Save file copies** deliberately creates an independent local file; folders remain references and their contents are not copied. Bitmap images and virtual files supplied by another app are saved locally because they may have no persistent original path. **Paste image** reads an image only when you request it; it does not enable clipboard-history capture. Removing a Shelf item removes its workspace entry and keeps the original or saved copy. **Reveal saved copies** opens the retained capture folder so you can manage or delete those files yourself. Successfully completed copies can remain there after a later operation/save failure. Notebook JSON exports contain paths, not the captured file contents; back up captured files separately. Upgrade and uninstall retain this workspace folder. These new native capture paths remain subject to release qualification.

## Local AI usage

Opening **AI usage** in v0.4.20 discovers supported local Codex and Claude Code logs. Codex uses `CODEX_HOME/sessions` and `CODEX_HOME/archived_sessions`, or the corresponding `.codex` folders in your profile. Claude Code uses `CLAUDE_CONFIG_DIR/projects`, or `.claude/projects`. This is a limited discovery of those data folders, not a scan of your home directory. Credentials, browser history and account databases are never opened; reparse points and UNC roots are skipped.

JSONL transcript lines may contain prompts or responses. The reader briefly parses those lines to find recognized usage metadata; snapshots and logs do not retain conversation text. Its metadata cache remains in memory and is not persisted or uploaded. Background reads run only while the expanded tool is visible, refresh every 45 seconds and stop when hidden, collapsed or in preview. File, byte, directory and line limits bound each read; partial or stale coverage is labelled. See [the adapter limits](../src/Notch.Core/AiUsage/README.md).

Codex percentages and resets are the last provider-reported local observations, not an authenticated live account query. They become stale after five minutes or a recorded reset. Claude Code transcripts supply measured token usage but not subscription allowance. Unsupported providers, missing records and unknown quotas remain unavailable rather than being displayed as zero. No account password or API token is required for this local tool.

## Optional network requests

Connected tools send requests only for their configured function. Weather sends the selected city/coordinates to the configured service. Revenue uses the selected account's read-only credential to obtain payment reports. Analytics contacts the HTTPS endpoint you configure. Providers can receive your IP address and account/request metadata under their own policies.

If the owner configures commercial billing, the application contacts that HTTPS service for checkout, portal access and signed entitlement validation. The billing provider processes payment information in its hosted checkout; the desktop application never embeds Stripe secret keys or webhook-signing secrets. Exact server logs, storage locations, processors and retention must be disclosed before that service launches.

Public testing unlocks the catalog without a subscription and pauses checkout. It does not enable hidden provider requests or upload workspace material. Optional sign-in to a configured service can still be required for the licensed weather proxy; current configuration has no deployed backend. **Check connections** explicitly inspects supported services using the credentials/imports you configured, reports each source independently and does not display credential values.

Notchling can check official GitHub release metadata, including evaluation prereleases. An explicit check sends the normal HTTPS request metadata and the app's update-client user agent; it does not send notes, clipboard text, provider credentials or a device identifier. GitHub can receive your IP address and normal network metadata under its own policy. Available update behavior depends on your installed version; release-specific features and evidence are recorded in [Validation](validation-notes.md).

**Check for updates daily** is off by default. When enabled, the source checks at most once per 24 hours while Notchling is running, using a monotonic interval within that session; restarting starts a new interval. There is no separate update daemon. Preview skips real release checks. Checks show availability without downloading or opening an installer. The optional fallback reads the repository's public release feed and exact release-asset listing when the API is unavailable; fallback discovery alone cannot authorize installation.

The v0.4.20 **Update and restart** flow is explicit. For an unsigned public-test installation, it requires your acceptance of the unsigned update, a canonical release/asset identity, GitHub-provided SHA-256 and size, and a pinned source commit. Metadata is checked again before handoff and installation. This verifies official release provenance and integrity; it does not provide publisher signing. A signed installation cannot downgrade to this unsigned channel.

Signed releases use the official stable manifest and verify installer size, SHA-256, Windows-trusted Authenticode signature and the installed publisher key. Downloads use official GitHub release hosts. Progress and Cancel are available. After a successful save, the app closes normally, the handoff installs without automatically rebooting Windows, and Notchling reopens. Failure records a local result and attempts to reopen the previous app. No production signing certificate is configured yet, and this candidate's real update handoff still needs Windows qualification.

## Your controls

Disable/clear clipboard history, remove provider credentials, disconnect optional services, export the workspace and choose demo mode in Settings. An expired Premium subscription preserves local files and recovery/export. Upgrade and uninstall retain workspace files and vault credentials; remove secrets intentionally in Settings before uninstall when desired.

Uninstalling the app does not cancel a subscription. Cancel future renewal in the authenticated billing portal. Account deletion, server-side data access/removal and billing-record retention must be offered by the configured service under its published policy; a local file deletion cannot remove a provider's records.

## Support and diagnostics

Startup diagnostics record exception messages and stack traces to help diagnose a window that fails to open. The log is limited to 256 KiB and can be deleted after quitting the app. Setup keeps separate prerequisite-installation logs in `%LOCALAPPDATA%\Notchling\Setup\Logs`.

Public issue reports are optional. Remove notes, tokens, full file paths, payment/customer identifiers and other personal content before sharing screenshots or logs. Never post credentials or card information. A private commercial support contact is a launch requirement; no fabricated email or controller identity is supplied by this repository.
