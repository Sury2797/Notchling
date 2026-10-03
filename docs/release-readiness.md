# Windows release readiness

This checklist is the acceptance plan for the native Windows application. The cloud host is Linux: it can validate the portable core and perform limited static checks, but cannot run WinUI XAML compilation or verify Windows shell behavior. CI definitions are not evidence of a green Windows build. Record the Windows build URL, commit, OS build, GPU, monitor configuration and test result when executing these checks.

## Build and deployment

- [ ] Windows x64 Release build and self-contained unpackaged publish succeed for this commit.
- [ ] A clean Windows 11 machine opens the extracted folder without a developer SDK.
- [ ] All published dependencies travel with the folder; startup does not depend on the build machine's package cache.
- [ ] Single-instance/startup behavior, tray menu, close-to-tray and explicit Quit behave consistently.
- [ ] A signed installer, updates, rollback, upgrade and uninstallation are validated before public distribution. CI currently produces an unsigned review ZIP.
- [ ] Restore versions, dependency notices, license and third-party attribution are reviewed.

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
- [ ] Weather attribution is visible. A paid release uses an appropriate Open-Meteo commercial agreement and licensed endpoint; the current free development endpoint is not sufficient.
- [ ] Credential save/remove and disconnect are exercised through the Windows vault; secret values are never rendered back or included in logs.
- [ ] Sounds, emoji, conversions and awake controls respond correctly; explicitly quitting releases sleep inhibition and native resources.

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

Use `./scripts/measure-windows.ps1 -ProcessName Notch.Windows -Seconds 60` for a basic CPU, memory and handle sample. Take a separate sample while repeatedly using active controls and animations. This script does not measure rendered frame pacing or replace a Windows performance trace.

## Release record

| Evidence | Result |
| --- | --- |
| Linux portable core | Run the package-free executable and record the executed/passed/failed counts |
| Windows portable core | Windows CI result required |
| Windows WinUI build | Windows CI result required |
| Native Windows launch and interaction | Manual matrix above required |
| Real provider accounts | Opt-in connection validation required |
| Linux native UI | Planned later; unavailable in this implementation |
| Billing and subscriptions | Deferred; Free/Pro concept only |

Unchecked items are remaining work, not passing results. A source-level implementation, generated image or static C# compile cannot stand in for interactive native QA.
