# Notchling Windows release readiness

This checklist records **v0.3.4 public-testing release** acceptance and the remaining work for the later planned US$2/month Premium subscription. Source [`8ad94ce`](https://github.com/Sury2797/Notchling/commit/8ad94cefcbd6417a24312edd4c761d0de2fa28d5) passed all jobs in [CI run 37944357692](https://github.com/Sury2797/Notchling/actions/runs/37944357692), including installed-app qualification on both hosted Windows desktops and publication of the unsigned evaluation EXE. All 21 catalog panels are available free for public testing; checkout is paused. The direct public download's Windows PE header, exact size, and published SHA-256 were verified. Historical baselines remain in [validation notes](validation-notes.md). Consumer Windows 10/11 hardware qualification and stable commercial-release work remain open.

Record the revision, build URL, OS build, GPU, monitor configuration and observed result for each subsequent validation. A Linux host can test portable logic and inspect source; full WinUI builds and native interaction require Windows.

## Build and deployment

- [x] Historical baseline `fafa2cc`: Windows x64 Release build and **framework-dependent** publish passed; this was not a self-contained package.
- [x] Evaluation CI passes for exact source `8ad94ce`, with 345 regression checks per Windows/Linux host and installed-app qualification on `windows-latest` and `windows-2022`. A signed production candidate remains a separate gate.
- [x] One v0.3.4 evaluation setup EXE builds and is the normal download; the advanced app-only folder remains secondary.
- [x] Released Setup EXE is 8,923,610 bytes (8.51 MiB); extracted app is 40,784,258 bytes (38.89 MiB). Final Setup downloaded zero prerequisite bytes on both prepared runners; missing-runtime estimates remain separate.
- [x] The published payload omits bundled runtimes and declares shared .NET 10 and Windows App SDK 1.8 requirements.
- [ ] Clean Windows 10 22H2 x64 and Windows 11 x64 machines launch with the documented shared x64 runtimes installed, without a developer SDK.
- [ ] Setup detects existing shared runtimes and downloads/installs missing official prerequisites on both OS targets; test Internet failure, cancellation, denied UAC, and rerunning Setup.
- [x] Installed v0.3.4 includes required assemblies, bootstrapper, and nonempty compiled-XAML PRI; it launches using the verified shared runtimes.
- [x] Both hosted Windows desktops pass actual setup, visible window/icon, bounded responsiveness, 21-tool access/navigation, real `SendInput` pointer hover/leave, finite typing lease, compact credential alignment, eight connection states, retained drafts, disposable-data controls, reopening, and uninstall.
- [x] The public v0.3.4 EXE was downloaded without authentication; its Windows PE header, 8,923,610-byte size, and published SHA-256 matched before default links changed.
- [ ] Single-instance/startup behavior, tray menu, close-to-tray and explicit Quit behave consistently.
- [ ] App/taskbar/tray icons, installer, Start menu shortcuts, window titles and Settings use Notchling and the Pixel Dragon assets; small/high-DPI icons remain readable.
- [ ] A signed installer, updates, rollback, upgrade and uninstallation are validated before a stable commercial release. Authorized public testing uses the explicitly unsigned evaluation setup; the manual signed-candidate workflow requires publisher credentials.
- [ ] Restore versions, source/distribution permissions, third-party notices and service attribution are reviewed for the release package.

## Interaction and display matrix

| Area | Exact cases | Acceptance |
| --- | --- | --- |
| DPI | 100%, 125%, 150%, 175%, 200%; 1080p and 4K | Text remains sharp; panel and dock boundaries align; no clipped labels or hit targets |
| Mixed monitors | Primary 100%, secondary 150%/200%; secondary to left and above primary; disconnected selected monitor | Overlay centers at the selected monitor's top edge, repositions correctly and remains reachable |
| Taskbar/work area | Bottom/top/side taskbar where supported; auto-hide on/off | The notch and dock remain within the appropriate display and do not obscure shell actions unexpectedly |
| Native placement | Collapse/expand each panel; maximized/fullscreen application; resolution changes | Top-center anchor stays stable and no stale transparent rectangle intercepts unrelated desktop clicks |
| Pointer | Hover strip, enter dock, cross panel/dock gap, leave panel, rapid module changes | No accidental collapse while operating the dock; pin and unpin states match behavior |
| Keyboard | Open global shortcut, Tab/Shift+Tab every control, Enter/Space, Escape; shortcut collision with another app | Focus is visible, controls are reachable, closure is predictable and registration failures are shown |
| Accessibility | Narrator, high contrast, Windows text scale at 125%/150% | Controls expose useful names/roles/states; important information survives disabled decoration |
| Motion | Normal animation and explicit reduced motion; Windows animation setting disabled | Panels settle smoothly; reduced motion removes unnecessary transitions while preserving state feedback |
| Tray | Left/right click, Open, Settings, Quit, taskbar Explorer restart | Exactly one usable icon; menu actions work; icon and event handlers are removed on exit |
| Resume | Lock/unlock, suspend for longer than a running focus timer, hibernate/resume | Timer completes once, media/audio sessions refresh, display placement is repaired and awake state stays intentional |
| Lifetime | 50 rapid panel changes; pin then quit; repeated open/close; second app instance | No stranded overlay, leaked tray icon or growing event subscription count |

## Modules and real services

- [x] Hosted v0.3.4 qualification navigates all 21 unlocked panels, verifies eight unconfigured connection states, and exercises the selected local controls and disposable-data workflows without inventing provider data.
- [ ] Home and each of the 20 tool panels have a useful connected/local state and a correct empty or unavailable state.
- [ ] No configured accounts, disabled demo mode and no Internet produce truthful empty/error states.
- [ ] Explicit demo mode labels illustrative figures and never implies a connection.
- [ ] Media controls work with at least a browser player and one desktop player; unsupported seeking disables the control.
- [ ] Volume updates the active audio endpoint; endpoint unplug/replug and muted/zero-volume states remain consistent.
- [ ] Battery absence on a desktop shows unavailable status; listening ports remain a read-only list.
- [ ] Clipboard is disabled on first launch; explicit enable, clear and disable work using synthetic test text.
- [ ] Notes/scratchpad/links/file references survive restart. Missing and moved files explain recovery. Corrupted saved data reports recovery instead of silently overwriting user material.
- [ ] Calendar imports include UTC, named time zones, DST transitions, date-only events, folded lines, recurring weekly events and exclusions. Unsupported recurrence fails clearly.
- [ ] Stripe testing uses a restricted credential, refunded payments, pagination and multiple currencies. Display labels say captured payment totals, not MRR.
- [ ] Analytics refuses insecure/credential-bearing URLs, validates malformed schemas and displays connection failures.
- [ ] Claude/Codex imports handle cumulative/repeated usage records without double-counting. No file is scanned without an explicit user choice.
- [ ] Weather city changes refresh the correct location; offline and unknown city states are visible.
- [ ] Weather attribution is visible. Release uses the configured licensed proxy, and the publisher has an appropriate commercial service agreement; unconfigured weather remains unavailable.
- [ ] Credential save/remove and disconnect are exercised through the Windows vault; secret values are never rendered back or included in logs.
- [ ] Sounds, emoji, conversions and awake controls respond correctly; explicitly quitting releases sleep inhibition and native resources.

## Subscription and commercial release

The current v0.3.4 release opens all supported tools free to everyone for public testing in Release and Debug; checkout is paused. Hosted qualification passed full-catalog access/navigation and unconfigured connection states. Actual Premium proofs remain strict, and a future commercial phase may restore the basic Free/US$2 monthly Premium split after an owner decision, secure service configuration and native/live sandbox evidence. The following subscription checks apply before that commercial activation; see [pricing](pricing.md).

- [x] Public-testing Release exposes every supported catalog tool without an owner-only or paid gate; no fake paid entitlement is created and checkout remains paused. Native access assertions and public-testing/future-commercial regression fixtures passed for the released source.
- [ ] Before commercial activation, Free exposes basic play/pause/previous/next, one Pomodoro and one Scratchpad; verified Premium exposes the supported extended catalog.
- [ ] Privacy controls, keyboard access, reduced motion and local data-integrity protections remain available in both editions.
- [ ] Checkout clearly displays the monthly price, recurring billing, applicable taxes and cancellation terms before payment.
- [ ] Purchase and renewal state are verified by a secure service; billing secrets do not travel in the desktop binary.
- [ ] Duplicate, delayed and out-of-order billing events do not produce inconsistent entitlements.
- [ ] Cancellation stops future renewal and preserves access through the confirmed paid period, subject to a disclosed refund policy.
- [ ] Expiry returns access to Free without silently deleting local notes or file references; recovery/export is available.
- [ ] Payment failure, refunds, offline validation, account recovery and device rules have explicit, tested behavior.
- [ ] Weather uses an appropriate commercial service agreement and licensed endpoint.
- [ ] Customer support, refund policy, subscription disclosures and applicable terms are ready before payment is enabled.

## Measured responsiveness and efficiency

These are proposed acceptance targets, not achieved measurements. Report the actual machine, sample duration, foreground panel and process counts alongside results.

| Measurement | Initial target and method |
| --- | --- |
| Frame pacing | No visible stall during module changes; inspect frame timing on 60 Hz and 120/144 Hz displays with Windows profiling tools. A 60 Hz frame has 16.7 ms; a 120 Hz frame has 8.3 ms |
| Input response | Record high-frame-rate interaction or ETW trace; target visual acknowledgement within the next frame where practical |
| Idle CPU | Sample a collapsed, settled app for 60 seconds; initial target below 0.5% CPU normalized across logical processors |
| Memory | Record working set and private bytes after startup, after visiting every panel and after a 30-minute idle period; investigate continuing growth |
| Handles | Record handle count before and after 100 panel changes and 20 audio/media-session changes; expect a plateau rather than monotonic growth |
| Network | Verify no rapid background provider polling when disconnected or idle; cache weather and bound provider response sizes/pagination |

Use `./scripts/measure-windows.ps1 -ProcessName Notchling.Windows -Seconds 60` for a basic CPU, memory and handle sample. Take a separate sample while repeatedly using active controls and animations. This script does not measure rendered frame pacing or replace a Windows performance trace.

## Release record

| Evidence | Result |
| --- | --- |
| Linux and Windows regression suites | 345 checks per host passed on `8ad94ce`: 128 Core, 76 Commerce, 74 Debug view-model, 24 Release view-model, 33 native API doubles, and 10 Python release cases; source/XAML projections passed |
| Windows WinUI build and publish | Framework-dependent build, compiled-XAML publication, and package audit passed on both hosted Windows desktops |
| Single installer and shared prerequisites | Both hosts passed real prerequisite integration/recovery fixtures, final setup/reopening/uninstall, and package measurement. Final Setup reused verified runtimes with zero additional download bytes; fixtures retained installed SDKs. Bare consumer OS, denied UAC, and offline cases remain open |
| Native Windows launch and interaction | Both hosts passed 21-tool/public-testing UI assertions, real injected hover, finite editing lease, compact settings, connections, drafts and selected local controls. Full consumer-OS matrix remains required |
| Public download | Direct unsigned v0.3.4 EXE verified without sign-in: 8,923,610 bytes; Windows PE header and published SHA-256 matched |
| Real provider accounts | Opt-in connection validation required |
| Linux native UI | Planned later; unavailable in this implementation |
| Billing and subscriptions | Release gates and configurable billing service implemented; production activation/live sandbox validation pending |

Unchecked items are remaining work, not passing results. Source-level checks and successful compilation cannot stand in for interactive native QA.
