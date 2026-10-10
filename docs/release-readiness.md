# Notchling Windows release readiness

This checklist records **v0.3.4 public-testing release** acceptance and the remaining work for the later planned US$2/month Premium subscription. Source [`8ad94ce`](https://github.com/Sury2797/Notchling/commit/8ad94cefcbd6417a24312edd4c761d0de2fa28d5) passed all jobs in [CI run 37944357692](https://github.com/Sury2797/Notchling/actions/runs/37944357692), including installed-app qualification on both hosted Windows desktops and publication of the unsigned evaluation EXE. All 21 catalog panels are available free for public testing; checkout is paused. The direct public download's Windows PE header, exact size, and published SHA-256 were verified. Historical baselines remain in [validation notes](validation-notes.md). Consumer Windows 10/11 hardware qualification and stable commercial-release work remain open.

Record the revision, build URL, OS build, GPU, monitor configuration and observed result for each subsequent validation. A Linux host can test portable logic and inspect source; full WinUI builds and native interaction require Windows.

## v0.4.4 candidate acceptance

The current source adds vector icons/source logos, responsive reporting layouts, interruptible motion, scoped feedback, read-only evaluation release discovery and native x86/ARM64 delivery paths. **v0.3.4 remains the recommended public download until this candidate passes its own installed-app matrix and each public EXE is verified.** These checks do not inherit a pass from the previous release.

- [x] Preceding v0.4.0 regressions: **407 passed** locally and on both Windows/Linux regression hosts — 141 Core, 76 Commerce, 81 Debug view-model, 31 Release view-model, 65 linked native API-double cases and 13 Python release cases. This candidate's native failures prevented publication.
- [x] Preceding v0.4.3 source `ed50560` passed **432 local cases** and 432 hosted Linux cases — 141 Core, 76 Commerce, 85 Debug view-model, 35 Release view-model, 74 native/service doubles and 21 Python release cases. Native source/XAML projections compiled with zero warnings/errors. Windows Debug/native qualification did not pass.
- [ ] The committed v0.4.4 candidate reruns these checks on both hosted regression jobs after geometry/helper/path/translation repairs and new Shelf/help/installer changes.
- [x] Native source/XAML projections compiled with zero warnings/errors; PowerShell and workflow YAML syntax parsed. These do not execute the Windows installer or WinUI renderer.
- [ ] Actual x64 installed app and prerequisites pass on both hosted x64 Windows jobs.
- [ ] Actual x86 app launches with its mixed app/native runtime-package plan on an x64 Windows host; consumer 32-bit Windows 10 remains a separate record.
- [ ] Native ARM64 app, prerequisites, full existing UI assertions, reopening and uninstall pass on `windows-11-arm`.
- [ ] Branded native installer welcome, full formatted terms/privacy, Next/accept/navigation bounds and cancellation before installation pass on all four hosts.
- [ ] Home's local quick guide opens and dismisses through native UI Automation; contextual explanations wrap, scroll and remain usable with keyboard input, small layouts and text scaling.
- [ ] Shelf's implemented reference/capture repair passes actual file selection, compact drag, bitmap paste, opening, restart persistence and removal. Labels distinguish references from copies; cancellation/failure preserve originals and explain retained complete copies. No URL-only image download is permitted.
- [ ] All four installed-app jobs and both regression jobs pass before three-architecture evaluation publication.
- [ ] Each public x64/x86/ARM64 EXE downloads without authentication and matches its measured size, Windows PE header and published SHA-256.
- [ ] Stock-font icons, contrast changes, source-logo/thumbnail separation, narrow reporting layouts and no-feedback bleed are checked on consumer machines.
- [ ] Generic browser identity/manual per-track choice, real source metadata, rapid session changes and unsupported transport are checked with live players.
- [ ] Normal/reversed transitions and immediate reduced-motion settlement are measured on modest hardware and 60/120/144 Hz displays.
- [ ] Evaluation release checks remain read-only, daily checks default off, preview makes no release check, and update notices preserve the current editor. Trusted signed installer execution remains a later production-signing gate.

All tools remain free for public testing and checkout stays paused throughout this candidate.

The failed [v0.4.0 run](https://github.com/Sury2797/Notchling/actions/runs/37979741879) exposed shared WinUI geometry ownership and an x86 Setup compiler failure. [v0.4.1](https://github.com/Sury2797/Notchling/actions/runs/37980974718) then failed Windows PowerShell 5.1 parameter-path binding before prerequisite fixtures. The [v0.4.2 validation run](https://github.com/Sury2797/Notchling/actions/runs/37981575380) passed final Setup on all four Windows hosts but failed installed launch on an invalid native translation-animation target. These attempts remain unpublished, and the Setup pass does not fill an app-qualification checkbox. The [validation record](validation-notes.md#candidate-qualification-sequence--10-october-2026-utc) preserves exact revisions and stages.

The [v0.4.3 run](https://github.com/Sury2797/Notchling/actions/runs/38029187998) passed native builds/publishes and prior prerequisite fixtures, then failed compiler installation before final Setup compilation. Windows regression also rejected an operating-system-specific exception expectation. v0.4.4 pins/verifies the official Inno Setup 6.7.3 compiler and preserves strict failure assertions while accepting the expected Windows/Linux exception difference. These repairs require a complete new run.

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
