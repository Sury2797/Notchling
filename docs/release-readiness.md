# Notchling Windows release readiness

This checklist defines acceptance for a public Windows release and the planned US$2/month Premium subscription. The current framework-dependent evaluation installer passed build, package auditing, actual installation, native launch, Free controls, and uninstall for [`fafa2cc`](https://github.com/SuryaK999/Notch-win-linux/commit/fafa2ccac2c34e6b464a1254d637164df59b50ee) in [CI run 37203173532](https://github.com/SuryaK999/Notch-win-linux/actions/runs/37203173532). Consumer Windows 10/11 native qualification and commercial-release work remain open. Earlier bundled-runtime results remain historical evidence in [validation notes](validation-notes.md).

Record the revision, build URL, OS build, GPU, monitor configuration and observed result for each subsequent validation. A Linux host can test portable logic and inspect source; full WinUI builds and native interaction require Windows.

## Build and deployment

- [x] Windows x64 Release build and self-contained unpackaged publish succeeded for the baseline commit linked above.
- [x] Evaluation CI succeeds for exact source revision `fafa2cc`; a signed production candidate remains a separate gate.
- [x] One evaluation setup EXE builds and is the normal download; the advanced app-only folder remains secondary.
- [x] Setup EXE, extracted application, and actual Windows App Runtime transfer sizes recorded; missing-.NET transfer is explicitly an estimate.
- [x] Runtime payload is absent; the published runtime configuration uses installed .NET 10 and Windows App SDK 1.8 packages.
- [ ] Clean Windows 10 22H2 x64 and Windows 11 x64 machines launch with the documented shared x64 runtimes installed, without a developer SDK.
- [ ] Setup detects existing shared runtimes and downloads/installs missing official prerequisites on both OS targets; test Internet failure, cancellation, denied UAC, and rerunning Setup.
- [x] Installed app includes required assemblies, bootstrapper, and nonempty compiled-XAML PRI; it launches using the shared runtimes prepared by Setup.
- [x] Hosted Windows desktop: actual evaluation setup, visible window/icon, bounded responsiveness, Free pin/media/Pomodoro/scratchpad UI interaction, and silent uninstall pass.
- [ ] Single-instance/startup behavior, tray menu, close-to-tray and explicit Quit behave consistently.
- [ ] App/taskbar/tray icons, installer, Start menu shortcuts, window titles and Settings use Notchling and the Pixel Dragon assets; small/high-DPI icons remain readable.
- [ ] A signed installer, updates, rollback, upgrade and uninstallation are validated before public distribution. Build CI produces an explicitly unsigned evaluation setup; the manual signed-candidate workflow requires publisher credentials.
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

Release enforces basic Free access and Premium at US$2 per month through signed subscription proofs. Checkout is disabled unless the owner configures the secure service; native and live sandbox evidence are required. See [pricing](pricing.md) for the planned feature boundaries and subscription lifecycle.

- [ ] Free exposes the compact notch, basic play/pause/previous/next, one Pomodoro and one Scratchpad; Premium exposes the full supported catalog.
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
| Linux portable core | 58 historical baseline cases; new revision requires fresh results |
| Windows portable core | Baseline CI green |
| Windows WinUI build and publish | Current framework-dependent build and compiled-XAML publication passed on `fafa2cc` |
| Single installer and shared prerequisites | Windows cloud setup passed; missing Windows App Runtime downloaded and installed, .NET reused; sizes recorded. Bare Windows 10/11, missing-.NET, UAC, and offline cases remain open |
| Native Windows launch and interaction | Hosted window and Free UI checks passed; full consumer-OS matrix above remains required |
| Real provider accounts | Opt-in connection validation required |
| Linux native UI | Planned later; unavailable in this implementation |
| Billing and subscriptions | Release gates and configurable billing service implemented; production activation/live sandbox validation pending |

Unchecked items are remaining work, not passing results. Source-level checks and successful compilation cannot stand in for interactive native QA.
