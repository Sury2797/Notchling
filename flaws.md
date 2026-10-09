# Notchling — engineering and product audit

**Date:** 3 October 2026

**Status:** Original audit retained below, with subsequent device reports and repairs. Latest update: **9 October 2026**. The v0.3.0 public-testing candidate unlocks the full catalog and refines hover, media, settings and connection health. Its Windows qualification is pending; the last verified published installer remains v0.2.11 until replacement qualification succeeds.

**Scope:** Native WinUI desktop app, portable core, persistence, integrations, commercial readiness, documentation, and equal Windows 10 / Windows 11 release requirements.

The billing client/service, release tooling, recovery flows, contrast resources and Windows 10/11 support contract are implemented. At the owner's request, all tool access is now free for public testing; purchasing is paused and actual paid-entitlement verification remains unchanged. The application is **not yet a qualified paid public release**: production accounts, domain, commercial weather access and signing setup are not ready. Native Windows 10/11 interaction and performance evidence remains required.

The original static audit covered the working tree based on commit [131b597](https://github.com/Sury2797/Notchling/commit/131b597f6e2e8cdb084d633db8cd7836ad698a68). Original line references below describe that historical snapshot; they have moved after repair. The status table here supersedes the original proposed actions. Prior documentation and licensing edits were preserved and completed alongside these repairs.


## Implementation status — 4 October 2026

“Implemented” describes checked-in code and available automated evidence. It does not certify a Windows desktop scenario that was never exercised. Production integration is prepared and disabled by default; no live purchases, deployment, email delivery, or signing certificate has been fabricated.

| Finding | Current resolution | Remaining requirement |
| --- | --- | --- |
| F01 | Stripe billing service, email restore, verified webhook processing, device-bound RSA entitlements, offline expiry, cancellation portal, sign-out fencing, and Release feature enforcement implemented. | Domain, Stripe/SMTP configuration, signing keys, customer policies, and real-account acceptance. Live keys additionally require explicit completed-release approval. |
| F02 | Release weather uses an authenticated licensed proxy; development endpoints cannot be enabled in Release. | Obtain commercial provider access and deploy the configured proxy. |
| F03 | Durable-save preflight, dirty state, generation-aware preferences, retry/export/discard shutdown, and input freeze implemented. | Windows disk/permission failure acceptance. |
| F04 | Text/title validation rejects oversized content before mutation; complete drafts can be exported. Cached scalar byte budgeting avoids serializing the whole notebook on each keystroke. | Native editor boundary/paste checks. |
| F05 | Existing whitespace/empty note edits flush; processed drafts are removed individually, preserving failures. | Native edit/quit scenario. |
| F06 | Activities keep queued reminders, receive full visible durations, defer during interaction, acknowledge actual presentation, and support cancellation/history. | Resume and notification behavior on both operating systems. |
| F07 | All-day/floating/zoned calendar semantics and interval-overlap rendering implemented. | Native calendar visual checks. |
| F08 | Forms retain drafts; Settings toggles update current preferences without reconstructing editors; activities defer during editing/dialogs/manipulation. | Focus/IME/dialog checks on Windows. |
| F09 | One guarded native placement path, bounded retry, and layout-only display notifications implemented. | Display hot-plug/driver testing. |
| F10 | Equal Windows 10 22H2 x64 and supported Windows 11 x64 release targets documented; shared API surface retained. | Independent consumer-OS runtime qualification. Hosted Windows build is not that evidence. |
| F11 | Work-area positioning, stable display identity, offsets, and fullscreen suppression implemented. | Taskbar/auto-hide/fullscreen native checks. |
| F12 | Per-user signed installer, stable-channel manifest, bounded/hash/signature-verified in-app updater, upgrade/uninstall safeguards, notice inventory/SBOM and signing workflow implemented. | Publisher certificate, complete release notice inventory, real installer/update qualification, and stable publication. |
| F13 | Source license, product terms, privacy, support and proposed refund policy documented; live release defaults disabled. | Publisher legal/contact details, owner policy approval and applicable consumer disclosures. |
| F14 | Notebook export/validated restore, previous-state backups, preserved corrupt-file recovery, save state, deletion undo and Free recovery access implemented. | Native picker/recovery workflows. |
| F15 | System contrast resources, mutable code brushes, pin toggle state, live-region activity text, larger controls and text-aware layouts implemented. | Narrator, contrast and 200% text validation on both OS versions. |
| F16 | Revenue results bind to requested range; mismatched cached totals are not rendered. | Live Stripe reporting checks. |
| F17 | Analytics shows snapshot/stale/unconnected status with a timestamp and refresh contract. | Live configured endpoint checks. |
| F18 | Entire provider response has a deadline; safe authentication/rate-limit/timeout guidance retained. | Optional real-provider fault checks. |
| F19 | Unsupported revenue providers disabled and explained; analytics bearer requirement consistent. | No unsupported provider is advertised as connected. |
| F20 | Calendar month navigation re-expands the selected source outside its loaded range. | Native rapid-navigation check. |
| F21 | Monotonic durations and explicit measured suspend/resume accounting implemented. | Native suspend/hibernate checks. |
| F22 | Media capabilities, visible monotonic timeline projection, playback rate and manipulation-aware sliders implemented. | Browser/desktop players and changed audio endpoints. |
| F23 | Explicit hidden state and deterministic tray Open implemented. | Tray/display-change native checks. |
| F24 | Pending ambient requests cancel on Stop/replacement/disposal; unavailable media is recoverable. | N-edition/audio playback checks. |
| F25 | Clipboard capture is ordered/bounded; Clear and Disable fence pending reads. | Real clipboard owners and bursts. |
| F26 | Media/clipboard/host errors surface safely, with explicit retry; vault initialization is lazy and subscription failures default to Free. | Native service failure/recovery checks. |
| F27 | Weather displays city-local hours/dates and retains timezone/DST metadata. | Licensed forecast account check. |
| F28 | Guaranteed Windows fonts and optional multimedia fallbacks implemented. | Clean Windows 10/11/N-edition checks. |
| F29 | Interruptible 180 ms native shell resizing, reduced motion, content transitions and adaptive layouts implemented. | Measured frame pacing, input response, resource use and modest-hardware profiling. |
| F30 | Core, linked native-service, view-model, commerce, notice and native-source harnesses are in the repository and CI. | Actual consumer-OS UI qualification remains separate. |

The smaller defects are also repaired: redirected Downloads uses Known Folders; confirmation activities have explicit destinations or no Open action; README setup is portable; Windows 11-first wording is removed; weather credit has its own row; clipped-gap exits arm the normal collapse delay.

## Verification of the repair

| Check | Evidence |
| --- | --- |
| Portable core | 92 Release cases passed, including storage budgets, queue cancellation, wall-clock changes and calendar DST semantics. |
| Billing service/client | Release build passed with zero warnings/errors; 58 commerce checks passed, including actual Stripe HTTP mapping, refunds/disputes, OTP recovery, signed proofs and logout races. |
| Linked native orchestration | 10 cases passed using explicit Windows API doubles; these do not execute the native desktop. |
| View-model | 36 Debug behavioral scenarios passed, plus one Release Free-enforcement fixture, including hydration-setting enforcement and corrupt-file recovery. |
| Notice generation | Five fixtures passed, including missing-license/dependency refusal and actual installer-engine terms. |
| Native source / Windows CI | Linked-source compilation passed; [run 37180992149](https://github.com/Sury2797/Notchling/actions/runs/37180992149) also passed the real WinUI XAML build, self-contained publish, notice bundle and artifact upload for source commit `7e88a82`. Both platform regression jobs passed. |
| Production configuration | Missing defaults refuse live billing/weather. No production credentials or signing certificate supplied. |

See [validation notes](docs/validation-notes.md), [Windows support](docs/windows-support.md), [native qualification](docs/native-qualification.md), [billing setup](docs/billing.md), and [release delivery](docs/release-delivery.md). Every unchecked native scenario below remains a release requirement.

## Installed-app findings and repairs — 4 October 2026 UTC

Actual installation and UI Automation uncovered failures beyond the original source audit. These repairs passed on [`fafa2cc`](https://github.com/Sury2797/Notchling/commit/fafa2ccac2c34e6b464a1254d637164df59b50ee) in [Windows cloud run 37203173532](https://github.com/Sury2797/Notchling/actions/runs/37203173532):

| Observed defect | Repair and evidence |
| --- | --- |
| Installed app could not locate its main-window XAML | Enabled the SDK resource-publishing pipeline; require a nonempty `resources.pri`; installed native window now opens |
| Unavailable `HighContrastChanged` notification aborted startup | Guard each optional notification independently and retain contrast/text-size queries through Windows fallbacks; normal startup succeeds |
| Opening a panel crashed when pointer capture was absent | Treat missing pointer-capture collections as empty in media/volume interaction checks; Free panel interactions now pass |
| Accessibility toggling could change the pin appearance without changing its behavior | Bind pin updates to checked/unchecked state; UI Automation verifies the saved pin preference |

The setup EXE is **8,875,854 bytes** and its application payload is **40,648,773 bytes**, without bundled .NET or Windows App Runtime files. Setup's missing Windows App Runtime transfer was **106,879,800 bytes**; .NET was already present. Both missing runtimes require roughly **147 MB total** first-install downloads at current versions, including the app and estimated .NET transfer. A small app installer does not eliminate those shared dependencies.

Installed launch, native icon/message response, Free media empty state, Pomodoro start/pause/reset, scratchpad durable save/navigation/clear, and uninstall passed on the hosted Windows desktop. Full Windows 10/11 hardware qualification, real media/providers, signed releases, and production billing remain open. Exact measurements and limits are in [validation notes](docs/validation-notes.md).

## Windows 10 installer report and reliability repairs — 7 October 2026

The owner's Windows 10 Pro 22H2 machine (build **19045.7725**) reproduced an evaluation **0.2.0** setup failure. Its log confirms .NET 10.0.12 was present, Microsoft's 106,879,800-byte runtime download passed Authenticode verification, and the native Windows App Runtime installer returned **0x8007007E** (module not found). The required 1.8 framework/Main/DDLM registrations were missing or below the SDK minimum; the newer shared Singleton was healthy. This was an installation failure, not evidence that the notch's managed UI had launched and crashed. The older cloud result did not cover this device configuration.

The **0.2.5** evaluation release repairs the following confirmed issues:

| Finding | Repair and coverage |
| --- | --- |
| Native runtime installer cannot load a module | Targeted recovery reads the already verified Microsoft's four x64 MSIX resources as data, checks every manifest before deployment, and lets Windows verify the package signatures. Framework dependencies install first; healthy newer Singleton registrations are reused. Other installer errors do not trigger this fallback. |
| Setup hides the failing component and shows a blank console | Capture installer output; record component, hexadecimal error and phase; explain cancelled permission/restarts and genuine Windows package-deployment failures. |
| Windows PowerShell can lose the native installer exit code | Retain the process handle before waiting, refresh the exit state, and reject missing exit evidence; the cloud integration actually runs Microsoft’s .NET installer. |
| Setup does not launch the app by default | The normal post-install Open action is selected; silent installs still do not launch it. |
| Private `DOTNET_ROOT` overrides the shared runtime verified by Setup | Published apphost uses global runtime discovery; installed-app CI supplies intentionally invalid overrides. |
| Startup/Explorer/fullscreen recovery leaves the app unreachable | Explicit first launch and existing-instance activation, optional theme/icon fallbacks, tray-registration retries, native fatal diagnostics, and save recovery before closing without a tray. |
| Native service failures remove working controls or escape shutdown callbacks | Preserve valid media without timelines, select an active player when the current session is absent, isolate optional audio metadata, retry clipboard enrollment, and contain background telemetry/power/cleanup exceptions. |
| Core presentation and persistence failures | Normalize imported notebook fields, reset deletion undo and obsolete calendar caches, publish media independently of telemetry, retry native integrations independently, and keep timers/local services usable while optional billing is delayed. |

Local verification passed **228 checks**: 92 core, 58 commerce, 47 Debug view-model, two Release Free, 19 native-service doubles, and 10 Python release cases. Actual native source/XAML projections compiled with zero warnings/errors. Windows PowerShell 5.1 prerequisite fixtures, actual Microsoft runtime integration/recovery, native build/publish, and installed-app controls/reopening/uninstall also passed on both `windows-latest` and `windows-2022` in [run 37608970728](https://github.com/Sury2797/Notchling/actions/runs/37608970728). The published installer is 8,881,476 bytes. Exact results are in the [validation record](docs/validation-notes.md). No result here claims every possible crash is eliminated or substitutes for running the repaired installer on the reporting laptop. See [troubleshooting](docs/troubleshooting.md).

## Live UI report and refinement — 7 October 2026

The supplied PDF shows evaluation **0.2.5** running on the reporting Windows laptop. The screenshots confirm sparse Free layouts, persistent sample-mode labeling/music, Settings horizontal overflow, an overcrowded dock and a Premium-access message styled as an error. The reported lag, sticking and unexpected closing require the forthcoming live video to distinguish collapse/fullscreen suppression from a process exit. No video was attached with this PDF.

The **0.2.9** evaluation release addresses these findings:

| Finding | Repair |
| --- | --- |
| Generic Free screens and disruptive media updates | Persistent responsive Home cards and focus dial; shared native artwork/playback controls; accessible empty, paused and completed states. Updates retain focus and pointer state. Free seeking/volume entitlement restrictions remain. |
| Sample track appears as current music after restart | Preview is explicit and session-only. Legacy saved demo mode resets without deleting notebook content. A visible Exit preview action restores real media and clears sample metrics; samples cannot pretend to play audio. |
| Settings clipped and difficult to use | Bounded vertical scrolling, wrapping/adaptive rows, grouped cards, collapsed optional connection/placement/preview/support sections, and clear provider/plan requirements. Toggling preferences updates controls in place; drafts and scroll position survive. |
| Too many dock controls and misleading Premium errors | Free dock contains Home, Media, Focus, Scratchpad and All tools; advanced tools remain discoverable with requirements. Access restrictions are informational, while genuine failures keep diagnostic errors. Native clipping matches actual dock widths. |
| Panel collapses while using controls or crossing the dock gap | Delayed collapse checks native cursor bounds and edit/dialog state. Active Settings stays expanded. Maximized windows are excluded from fullscreen suppression. |
| Excess resize/refresh work and jitter | Identical geometry does not restart animations, enumerate monitors or rebuild native regions. Unchanged timer notifications and irrelevant Free listener scans are suppressed; inactive media projection timers stop. |
| Placement exceeds a small or scaled display | Pixel-clamped work-area geometry handles fractional DPI, mixed monitor coordinates and extreme saved offsets; an unusably short work area omits the dock. |
| Awake toggle/shutdown can wait on a worker indefinitely | Native disposable Windows power-request handles replace blocking worker handshakes and joins. Windows API failure closes partial requests cleanly. Actual Premium Awake hardware qualification remains open. |

Local checks passed **246 scenarios**: 103 core (including 11 geometry cases), 58 commerce, 53 Debug view-model, three Release Free, 19 native-service doubles, and 10 release cases. Native source/XAML projections compiled with zero warnings/errors. The expanded Windows UI check exercises real pointer movements, dock bounds, active Settings retention, horizontal containment, preference scrolling/drafts and preview exit, in addition to playback empty state, Pomodoro and scratchpad. All jobs passed in [run 37669754772](https://github.com/Sury2797/Notchling/actions/runs/37669754772), including actual installation, the expanded UI assertions, reopening and uninstall on both Windows hosts. The public 8,889,407-byte EXE download and release checksum were verified. The repository links and exact trusted update URLs now use GitHub's confirmed `Sury2797/Notchling` location. The live video arrived after this qualification; the separate review below records its observed behavior. Hardware qualification and native Premium Awake testing remain open.

## Live recording review — 8 October 2026

The supplied `notchling-video.mp4` is a 62.33-second handheld recording of the reporting laptop. Settings explicitly identifies **0.2.5** around 10 and 50 seconds; the release page at the end also shows that version. This is evidence of the older build's behavior, rather than a failed v0.2.9 qualification.

| Recording time | Observed behavior | Resolution / remaining evidence |
| --- | --- | --- |
| 0–8 s | Sparse, oversized Free Focus/Media/Home screens; dock contains many unavailable tools. | v0.2.9 has compact responsive Free views and a five-tool Free dock. |
| 10–25 s | Hovering locked tools returns to Settings and leaves a red Premium message. | v0.2.9 removes those tools from the Free dock, labels their requirements in All tools and uses informational plan guidance. |
| 30–48 s | Settings has horizontal overflow; changing switches sends the scrolled view back to the top. | v0.2.9 bounds content width and updates switches in place; cloud UI checks cover containment, scroll retention and number drafts. |
| 44–61 s | Sample mode is enabled; the compact title subsequently shows “Golden Hour Drive” without a preview prefix. | v0.2.9 makes samples session-only, labels compact preview and supplies Exit preview. A sample session cannot send real transport commands. |
| 50–52 s | The signed updater's limitation appears as a red application error on the unsigned evaluation. | New source repair checks installed publisher eligibility first, supplies visible manual-update guidance and keeps the signed updater's verification unchanged. Qualification is recorded separately below. |
| 53–61 s | Opening Release page collapses the panel; hovering reopens Settings; the compact strip remains visible afterward. | This sequence shows collapse/reopen, not a demonstrated process crash. v0.2.9 guards active Settings and the dock gap; ordinary unpinned hover panels still collapse when left. |

The new **0.2.11** update flow uses an in-place, polite accessibility status in Settings. An unsigned evaluation does not contact the stable manifest, download an installer or trigger a notebook save when checking updates; it directs the user to **Release page** instead. Signed builds retain bounded HTTPS downloads, exact asset allowlists, SHA-256 checks and trusted matching publisher verification. Actual update failures still surface diagnostics.

Local validation of the follow-up passed **55 Debug and five Release view-model cases**; native source/XAML projections compiled with zero warnings/errors. All **250 checks per platform** and both installed Windows jobs passed for [`7d5226e`](https://github.com/Sury2797/Notchling/commit/7d5226efcb28d858458e20db74fe13beb8cf0af0) in [run 37735689610](https://github.com/Sury2797/Notchling/actions/runs/37735689610). Native UI Automation verified the visible update live region, no error banner, retained Settings viewport, no new download, the earlier dock/preview/Free controls, existing-instance reopening and uninstall. The [public v0.2.11 Setup EXE](https://github.com/Sury2797/Notchling/releases/download/notchling-evaluation-0.2.11/Notchling-0.2.11-windows-x64-evaluation-setup.exe) is **8,890,277 bytes**; its Windows PE header and published SHA-256 were verified. The initial v0.2.10 candidate was withheld because the Windows PowerShell 5.1 test helper inherited an incompatible PowerShell 7 security-module path. Importing its own security manifest explicitly fixed the qualification failure without weakening functional assertions.

The recording does not establish a fatal exception, process exit or measured freeze/input latency. Audio was not transcribed. Consumer Windows 10/11 hardware, real media/audio, Premium Awake and sustained animation/performance qualification remain open; a phone recording cannot substitute for those measurements.

## Current laptop screenshots and public-testing refinement — 9 October 2026

The owner supplied new screenshots and reported sticky hover, uneven layouts, missing media artwork/source detail and inaccessible extended tools. The media screenshot shows a paused Chrome training session with a title and timeline but a generic music tile; that alone does not establish whether the player supplied album artwork. The Settings screenshot directly confirms oversized Save/Remove actions, poor alignment against header-bearing credential inputs and crowded endpoint placement. The app version is not visible in these screenshots; the preceding conversation distributed v0.2.11. The reporting device is Windows 10 Pro 22H2, build 19045.7725.

The owner explicitly changed product policy: **unlock every supported tool for everyone to test**, with no administrator-only or paid gate. The v0.3.0 source implements that phase separately from purchased Premium status. No signed subscription proof is fabricated, and the future US$2/month commercial policy remains dormant.

| Finding | Candidate source repair | Remaining verification |
| --- | --- | --- |
| Unpinned island remains open after leaving | Shape-aware interaction boundaries include the body, actual dock and crossing corridor, rather than the entire transparent native rectangle. Recent editing has a bounded lease; stale focus and Settings selection no longer pin the island indefinitely. | Actual laptop hover/crossing/leave behavior, keyboard editing and native popups on Windows 10/11. |
| Shell changes feel uneven or sticky | Resize advances on rendering frames, retains its current geometry during reversal, caches placement inputs and separates final XAML layout from frame-by-frame native boundary updates. Reduced motion remains respected. | 60/120/144 Hz frame pacing, integrated-GPU behavior, rapid reversals and sustained interaction; no measured “silky” guarantee yet. |
| Credential actions are oversized and misaligned | Dedicated compact form controls; labels outside input/action rows; expanding fields with natural-width Save/Remove actions; narrow/text-scaled rows stack without horizontal overflow. Settings action groups, card corners and spacing are consistent. | Native input/action bounds, keyboard order, text scaling and real screenshots at the reporting laptop's DPI. |
| Settings updates disrupt edits or placement | Connection and subscription status update retained text in place; endpoint, number and credential drafts remain intact. Reset placement restores the primary display and default top-center offsets. | Live vault actions, mixed-DPI monitor selection, picker interactions and retained scroll/drafts after updates. |
| Player metadata/artwork can appear generic or stale | Native media presentation is being refined around the selected session's actual title, artist, source, artwork and command capabilities. Player/source identity can supply a fallback when album art is unavailable; unsupported commands remain disabled. | Real Chrome/browser video, Spotify or another desktop player, track/session switches, unavailable metadata/artwork, long titles, non-1× playback and audio endpoint removal. |
| Tool access prevents testing extended functions | All 21 catalog panels and extended controls are unlocked in Release and Debug during public testing. Settings labels the phase honestly, and checkout stays paused. Optional configured service sign-in is retained for authenticated providers. | Native access/navigation and disposable-data exercises; commercial-phase regression fixtures must continue to enforce real signed access when that phase is selected. |
| Saved credentials look like connected services without proof | Settings checks per-source health explicitly. Missing credentials/endpoints/imports show setup guidance; actual requests and validation determine success/failure. Errors distinguish authentication, permissions, unsupported data, timeout and retry conditions without exposing secrets. | Approved Stripe test account, a compatible analytics endpoint, selected ICS/JSONL imports and real network-failure recovery. |
| Unlocked Weather suggests an operational bundled service | Weather tool access is open, but the licensed backend and authenticated session are still required. Current configuration has no deployed service; the panel explains setup instead of inventing a forecast or falling back to an unsuitable endpoint. | Owner obtains provider agreement and deploys/configures the authenticated proxy, then verifies attribution, quotas and actual forecasts. |

**Verification status:** candidate source and local regression work are in progress. Windows CI must qualify the actual v0.3.2 installer, public-testing UI interactions, hover collapse, compact credential alignment, connection states, draft retention, native build and packaging before the README download links change. Earlier passing runs do not certify this candidate. The validation notes will record its immutable revision, checks and artifact measurements after qualification.

Public testing removes the product paywall, not Windows permissions, provider accounts, native command limitations or absent integrations. Polar, Dodo and AdSense remain unsupported; Google/Outlook calendar account synchronization and built-in analytics OAuth remain future work. Consumer Windows 10/11 hardware, live provider behavior, Narrator/high contrast and measured motion/resource use remain separate acceptance work.

## Original audit findings

The remainder preserves the original problem descriptions and acceptance intent for traceability, including the former **Notch** product name. Statements about absent functionality describe the pre-repair snapshot; use the implementation table above for current status.

## How to read the findings

| Priority | Meaning |
| --- | --- |
| P0 | Blocks the relevant paid offering: do not enable sales or include the affected service until resolved. |
| P1 | Resolve before a paid beta: data integrity, reliability, accessibility, compatibility, or release delivery. |
| P2 | Resolve before a broad premium launch, or explicitly narrow the supported behavior. |
| P3 | Lower-impact usability and documentation follow-up. |

**Confirmed** means the failure path follows from the inspected source; it does not mean the Windows UI was used to reproduce it. **Missing capability** means an intended feature or release process is absent. **Validation gap** means there is insufficient evidence to claim the behavior works. **Conditional risk** identifies a plausible failure requiring a particular device, OS configuration, or interaction to reproduce.

The findings are grouped for actionability. This is a static audit, not a claim that every possible runtime error has been discovered.

## Recommended product and architecture decisions

Keep **C#/.NET and WinUI 3** for the Windows product, with the portable core kept independent of WinUI. The reviewed APIs do not expose an obvious Windows 11-only dependency that forces a different UI stack for Windows 10. The optional Windows 11 corner attribute already tolerates an unsupported result on Windows 10. The manifest's Windows 10 compatibility identifier also covers Windows 11.

Keep one desktop process, native services behind interfaces, cancellable background I/O, and composition-based visual transitions. Improve ownership of draft state, error reporting, notification delivery, and save completion. Split large view/view-model responsibilities as those areas are repaired. A wholesale rewrite in C++ or Rust, microservices for local utilities, and an extra browser UI are unnecessary for the current problems.

The strongest initial paid experience is reliable media, focus, notes, scratchpad, shelf, and carefully bounded clipboard history. Connected dashboards should join the paid promise only when setup, failure handling, accuracy, and service terms are ready. Linux can retain the shared core now and receive its own native frontend later; WinUI itself does not provide a Linux desktop UI.

Keep the requested **very light Free edition and Premium at US$2/month**. Accessibility, privacy controls, and access to recover the user's own data should remain available in both. Define payment fees, tax handling, weather costs, hosting, and support costs before promising bundled services at that price. There is no measured contribution-margin model in the current pricing documentation; that is a business decision to complete, not evidence that US$2 cannot work.

## P0 — paid-offering blockers

### F01 — Subscriptions and Free/Premium enforcement do not exist

**Missing capability.** [Pricing:35](docs/pricing.md#L35) lists upgrade, renewal, cancellation, downgrade, offline validation, and purchase recovery as future work. [Settings:333](src/Notch.Windows/Views/UtilityToolsView.xaml.cs#L333) displays plan information only. Current development tools remain accessible without an entitlement.

**Impact:** a price label cannot sell, restore, cancel, or securely validate Premium. The Revenue tool's Stripe reader is separate from billing Notch customers.

**Proposed action:** choose a billing provider and a small server-side entitlement service; define account/device rules, verified purchase events, bounded offline access, payment failures, cancellation, refunds, and recovery. Keep provider secrets out of the desktop binary. Avoid treating a local preference as purchase verification.

**Acceptance after approval:** purchase and restore on both OS versions; duplicate/delayed/out-of-order payment events; renewal failure; offline expiry; cancellation at period end; refund; reinstall; downgrade without deleting or trapping user data.

### F02 — Weather has no commercial service configuration

**Missing commercial dependency.** [WeatherClient:21](src/Notch.Core/Providers/WeatherClient.cs#L21) and [WeatherClient:31](src/Notch.Core/Providers/WeatherClient.cs#L31) hardcode the development geocoding and forecast endpoints. [Third-party notices:54](THIRD_PARTY_NOTICES.md#L54) records that commercial delivery requires an appropriate arrangement.

**Impact:** attribution alone does not establish permission to use the hosted development service in this commercial product.

**Proposed action:** obtain suitable commercial terms and configure the supported endpoint and credential strategy, or keep weather out of the paid distribution until that is ready. Do not ship a shared privileged API key inside the executable.

**Acceptance after approval:** review the actual service agreement, verify requests use the licensed configuration, confirm attribution, and set usage/cost limits appropriate to US$2/month.

## P1 — data integrity and core reliability

### F03 — Quit silently ignores final save failures

**Confirmed.** [MainViewModel:624](src/Notch.Windows/ViewModels/MainViewModel.cs#L624) catches pending and final persistence errors without reporting failure. [MainWindow:190](src/Notch.Windows/MainWindow.xaml.cs#L190) flushes note drafts into memory, then exits after disposal regardless of durable-save success.

**Scenario:** edit a note or scratchpad and quit while storage is full, access is denied, or the workspace exceeds the storage limit. Reopening can restore older data despite an apparently normal exit. Atomic replacement protects the previous file; it does not guarantee that the newest edit reached disk.

**Proposed action:** separate save completion from resource teardown; maintain an unsaved state; offer retry, export, or explicit discard before closing after a write failure. Validate the aggregate storage budget: individually permitted notes can collectively exceed the store's 10 MB limit.

**Acceptance after approval:** inject final-write and pending-write failures; verify the user can recover the full draft, the prior file remains intact, and successful retry persists the exact content.

### F04 — Long note and scratchpad content is silently truncated

**Confirmed.** [MainViewModel:73](src/Notch.Windows/ViewModels/MainViewModel.cs#L73), [MainViewModel:457](src/Notch.Windows/ViewModels/MainViewModel.cs#L457), and [MainViewModel:463](src/Notch.Windows/ViewModels/MainViewModel.cs#L463) slice content to 500,000 characters. [UtilityToolsView:57](src/Notch.Windows/Views/UtilityToolsView.xaml.cs#L57) creates editors without a matching input limit or visible validation.

**Scenario:** paste a longer document. Scratchpad can show the complete input while the model holds only a prefix; saving a note accepts the shortened content and clears its draft.

**Proposed action:** disclose and enforce limits before accepting edits, or reject oversized saves while preserving the full draft for export. Apply the same principle to title limits.

**Acceptance after approval:** boundary-length, over-limit, and Unicode input must either round-trip exactly or show an explicit recoverable validation error.

### F05 — Clearing an existing note is lost during quit-time draft flushing

**Confirmed.** [UtilityToolsView:364](src/Notch.Windows/Views/UtilityToolsView.xaml.cs#L364) flushes only drafts with non-whitespace bodies, then clears all drafts. [MainViewModel:459](src/Notch.Windows/ViewModels/MainViewModel.cs#L459) permits empty content when updating an existing note.

**Scenario:** erase an existing note's body, then quit without pressing Save. The empty edit is discarded and the previous body returns on restart. Explicit Save and quit-time saving have different semantics.

**Proposed action:** distinguish an untouched empty new note from an intentional empty update to an existing note; track dirty state independently of whether the body contains text.

**Acceptance after approval:** clear an existing note, change its title, and quit; the intended changes must survive. An unused blank new-note editor should not create junk notes.

### F06 — Simultaneous reminders can expire without being shown

**Confirmed.** [MainViewModel:347](src/Notch.Windows/ViewModels/MainViewModel.cs#L347) marks reminders delivered when queuing them. Every activity receives an eight-second lifetime at [MainViewModel:358](src/Notch.Windows/ViewModels/MainViewModel.cs#L358). [OverlayStateMachine:42](src/Notch.Core/OverlayStateMachine.cs#L42) discards queued items whose creation-based deadline has passed.

**Scenario:** two reminders become due in the same tick. The first uses the display period; the second expires in the queue. Its ID is already marked delivered, so it does not reappear during that session. Opening or collapsing the shell also clears the activity queue.

**Proposed action:** separate event expiry from visible presentation duration, acknowledge delivery after presentation, and preserve outstanding reminders in a recoverable list or notification history.

**Acceptance after approval:** multiple simultaneous reminders, reminders behind other activities, opening a tool during delivery, and resume after several missed deadlines must not silently drop reminders.

### F07 — Calendar dates can shift, and continuation days disappear

**Confirmed.** [IcsCalendar:288](src/Notch.Core/Providers/IcsCalendar.cs#L288) converts date-only and floating date-times to UTC. [Models:15](src/Notch.Core/Models.cs#L15) does not preserve an all-day flag. [FeaturedToolsView:377](src/Notch.Windows/Views/FeaturedToolsView.xaml.cs#L377) groups by the local start date only and renders clock times.

**Scenario:** an all-day October 3 event appears on October 2 at 17:00 on a UTC−7 machine. A floating 09:00 appointment shifts with the local offset. An overnight or multiday event is absent from its continuation days.

**Proposed action:** preserve date-only, floating, and zoned semantics; resolve floating appointments against an explicit calendar/user zone; render all-day events appropriately; use interval overlap when selecting events for a day.

**Acceptance after approval:** date-only events in positive/negative offsets, floating appointments, overnight spans, exclusive all-day end dates, and DST boundaries.

### F08 — Activities and settings changes can discard unfinished form input

**Confirmed control flow; native focus effects need validation.** [OverlayStateMachine:35](src/Notch.Core/OverlayStateMachine.cs#L35) switches immediately to Activity. [MainWindow:124](src/Notch.Windows/MainWindow.xaml.cs#L124) detaches the tool view; focus/dialog protection at [MainWindow:159](src/Notch.Windows/MainWindow.xaml.cs#L159) does not protect this path. [UtilityToolsView:29](src/Notch.Windows/Views/UtilityToolsView.xaml.cs#L29) rebuilds controls when reloaded. Separately, any Preferences change rebuilds the utility view at [UtilityToolsView:34](src/Notch.Windows/Views/UtilityToolsView.xaml.cs#L34).

**Scenarios:** a reminder arrives while entering a new Link; or a user types a credential, endpoint, or interval, then changes a settings toggle. Controls are reconstructed and view-local unsaved values can disappear. Notes have a draft dictionary, but these forms do not.

**Proposed action:** keep form state outside disposable controls, update settings incrementally, and defer intrusive activities during text entry, dialogs, and pointer manipulation.

**Acceptance after approval:** trigger every activity while editing each form and while a picker/dialog is open; preserve input, selection, focus, and the intended notification.

### F09 — Display-change recovery repeats a failing resize outside its guard

**Confirmed unhandled failure path; hardware reproduction pending.** [OverlayHost:248](src/Notch.Windows/Interop/OverlayHost.cs#L248) catches a positioning exception, then raises DisplayChanged at line 255. Its subscriber at [MainWindow:44](src/Notch.Windows/MainWindow.xaml.cs#L44) calls RenderShell, which repeats ResizeAndPlace outside that catch at [MainWindow:114](src/Notch.Windows/MainWindow.xaml.cs#L114).

**Impact:** a transient display reconfiguration failure can escape on the UI dispatcher instead of being recovered.

**Proposed action:** use one guarded geometry update path, publish successful geometry, and retry transient failures with a bound. Preserve a usable last-known position or primary-monitor fallback.

**Acceptance after approval:** simulated positioning failures plus repeated monitor disconnect/reconnect, DPI changes, sleep/resume, and display-driver reconfiguration on both Windows versions.

## P1 — Windows and release readiness

### F10 — Complete Windows 10 and Windows 11 support is unverified

**Validation gap and outdated target documentation.** [Project:4](src/Notch.Windows/Notch.Windows.csproj#L4) targets API floor 19041 and currently builds x64. [CI:31](.github/workflows/build.yml#L31) uses a hosted Windows runner to build/publish; it does not establish consumer desktop behavior. [Validation notes:14](docs/validation-notes.md#L14) records no native launch evidence. [README:147](README.md#L147) still prioritizes Windows 11.

**Proposed action:** make both operating systems equal release gates. Proposed starting baseline: Windows 10 22H2/build 19045 x64 and the supported Windows 11 x64 releases selected for launch. Confirm exact editions/builds before publishing the support promise. API floor 19041 does not certify every Windows 10 release; x86 and native ARM64 are not configured targets.

**Acceptance after approval:** qualify the same release artifact independently against every lane in the matrix below. Update all support claims to distinguish intended support from verified support.

### F11 — The top-edge overlay conflicts with a top-positioned taskbar

**Confirmed placement conflict; visual interaction needs Windows verification.** [OverlayHost:87](src/Notch.Windows/Interop/OverlayHost.cs#L87) uses full monitor bounds and [OverlayHost:102](src/Notch.Windows/Interop/OverlayHost.cs#L102) positions a topmost window at the monitor's top edge. The work area is not used.

**Scenario:** Windows 10 permits a normal top-positioned taskbar. Notch and the taskbar occupy the same area. Fullscreen applications and other app bars also have no explicit coexistence policy.

**Proposed action:** define taskbar-aware placement, an optional user offset, and fullscreen/presentation suppression. Preserve the visual concept while respecting occupied screen space.

**Acceptance after approval:** supported taskbar edges, auto-hide, fullscreen video/games, presentation mode, and shell restart. No obstructed essential shell controls or unwanted activation.

### F12 — Distribution, updates, and release notices are incomplete

**Missing release capability.** [Workflow:39](.github/workflows/build.yml#L39) creates an unpackaged publish folder and a temporary ZIP artifact, with 14-day retention. There is no completed signed installer/release channel or update, rollback, upgrade, and uninstall process. [Third-party notices:5](THIRD_PARTY_NOTICES.md#L5) is explicitly an inventory, not the complete binary notices bundle; [Project:25](src/Notch.Windows/Notch.Windows.csproj#L25) has no explicit project-license/notices packaging step.

**Proposed action:** choose a signed distribution format supported on both operating systems; provide stable downloads, integrity verification, safe updates, recovery, and a predictable uninstall/data-retention policy. Inventory the actual shipped binaries and preserve their required notices. This review does not assert that every vendor notice is absent from existing publish output.

**Acceptance after approval:** clean standard-user installation without developer tools; interrupted update; failed update recovery; upgrade preserving data; uninstall; release artifact inspection and applicable notices included.

### F13 — Source licensing does not complete customer product terms

**Missing release policy / owner decision.** [LICENSE:25](LICENSE#L25) delegates official app usage to separate product terms and an end-user agreement. [Pricing:33](docs/pricing.md#L33) and [Pricing:46](docs/pricing.md#L46) leave tax presentation, refunds, support, and subscription disclosures unresolved. The custom source-available draft remains a choice to confirm.

**Proposed action:** confirm the intended source-license model and finalize consumer usage, privacy, cancellation, refund, and support policies before charging. Validate rights for distributed dependencies and any external assets. A public repository, a subscription price, and an open-source license are separate choices; subscription revenue does not itself determine the source license.

**Acceptance after approval:** users can understand rights, recurring charges, cancellation, refunds, data handling, and support before purchase; the shipped terms match the approved business policy.

### F14 — Data recovery and downgrade access are manual or absent

**Missing capability.** [MainViewModel:93](src/Notch.Windows/ViewModels/MainViewModel.cs#L93) asks users to manually back up/repair/rename workspace JSON. [README:129](README.md#L129) describes manual folder backup. [UtilityToolsView:133](src/Notch.Windows/Views/UtilityToolsView.xaml.cs#L133) deletes notes without undo. [Pricing:42](docs/pricing.md#L42) promises a downgrade recovery/export path that does not exist yet.

**Proposed action:** add usable export/import, actionable corrupt-file recovery, visible save status, and an undo or recovery mechanism for deletion. Preserve access to recover existing Premium data after expiry.

**Acceptance after approval:** recover from corrupt JSON, accidental deletion, interrupted saves, reinstall, and expired entitlement without needing to hand-edit application files.

### F15 — Accessibility needs a complete custom-surface pass

**Missing theme behavior and validation gap.** [NotchTheme:5](src/Notch.Windows/Styles/NotchTheme.xaml#L5) defines fixed colors, and [MainWindow:5](src/Notch.Windows/MainWindow.xaml#L5) uses explicit dark surfaces. There is no explicit high-contrast palette path. Activity text at [MainWindow:30](src/Notch.Windows/MainWindow.xaml#L30) has no live-region announcement; the pin at [MainWindow:52](src/Notch.Windows/MainWindow.xaml#L52) is a Button without an exposed toggle state.

**Impact:** ordinary native control defaults do not establish that these custom surfaces work with contrast themes, Narrator, keyboard focus, or enlarged text. Some compact text and targets also require evaluation.

**Proposed action:** use system-aware contrast resources, meaningful automation states and announcements, consistent focus restoration, and layouts that tolerate text scaling. Preserve reduced-motion support in both plans.

**Acceptance after approval:** keyboard-only use, Narrator, both OS contrast-theme systems, 200% text scaling, reduced motion, and touch where supported. Record actual failures rather than assuming every hard-coded color is automatically unreadable.

## P2 — correctness, trust, and interaction polish

### F16 — Revenue values can belong to the wrong selected range

**Confirmed.** [FeaturedToolsView:613](src/Notch.Windows/Views/FeaturedToolsView.xaml.cs#L613) changes the selected range before fetching. [FeaturedToolsView:240](src/Notch.Windows/Views/FeaturedToolsView.xaml.cs#L240) matches cached data only by provider; [Models:17](src/Notch.Core/Models.cs#L17) does not retain reporting boundaries.

**Scenario:** load 30 days, select Today, then encounter a failed request. Today remains selected while the previous 30-day total/chart is shown.

**Proposed action / acceptance:** attach range boundaries to results; render only matching data or retain its true label. Exercise rapid range changes and failed refreshes. Continue describing this as captured payments after refunds; it is not subscription MRR.

### F17 — Cached analytics is labeled LIVE without a freshness contract

**Confirmed.** [FeaturedToolsView:277](src/Notch.Windows/Views/FeaturedToolsView.xaml.cs#L277) labels any real snapshot LIVE. [MainViewModel:389](src/Notch.Windows/ViewModels/MainViewModel.cs#L389) refreshes analytics explicitly; periodic refresh at line 349 covers local tools only.

**Proposed action / acceptance:** show timestamped, stale, offline, and loading states, or define bounded refresh behavior appropriate to the service. An old snapshot after a failed refresh or long idle period must not imply current live measurements.

### F18 — Provider requests can hang on bodies and hide actionable errors

**Confirmed.** [ProviderHttp:10](src/Notch.Core/Providers/ProviderHttp.cs#L10) uses ResponseHeadersRead. Its subsequent body-read loop at line 28 has no complete-request deadline beyond the supplied cancellation token. The HttpClient timeout can expire its responsibility after headers. [MainViewModel:196](src/Notch.Windows/ViewModels/MainViewModel.cs#L196) swallows all cancellation exceptions, while line 200 replaces credential and rate-limit errors with generic connection advice.

**Proposed action / acceptance:** use a linked deadline covering headers and body; distinguish user/superseded cancellation from timeout; preserve safe structured 401/403/429 guidance. Exercise a server that returns headers then stalls, invalid credentials, rate limits, malformed data, and user cancellation.

### F19 — Integration setup advertises unsupported or inconsistent paths

**Confirmed UI/documentation mismatch.** [FeaturedToolsView:238](src/Notch.Windows/Views/FeaturedToolsView.xaml.cs#L238) exposes revenue providers with awaiting-connection/setup instructions, but [Settings:329](src/Notch.Windows/Views/UtilityToolsView.xaml.cs#L329) only offers Stripe and analytics credentials; Polar, Dodo, and AdSense adapters are unsupported. [Connections:14](docs/modules-and-connections.md#L14) calls the analytics bearer credential optional, but [AnalyticsClient:15](src/Notch.Core/Providers/AnalyticsClient.cs#L15) requires it.

**Proposed action / acceptance:** clearly label or remove unavailable setup routes; choose one documented analytics authentication contract. Every visible Connect action should lead to a working supported flow or an explicit unavailable explanation. Keep future integrations out of current paid-feature claims.

### F20 — Calendar navigation extends beyond the imported window silently

**Confirmed.** [MainViewModel:428](src/Notch.Windows/ViewModels/MainViewModel.cs#L428) imports one month before through three months after the current date. [FeaturedToolsView:624](src/Notch.Windows/Views/FeaturedToolsView.xaml.cs#L624) navigates indefinitely without loading another range; [FeaturedToolsView:382](src/Notch.Windows/Views/FeaturedToolsView.xaml.cs#L382) calls empty results “No events on this day.”

**Proposed action / acceptance:** retain loaded-range metadata and re-expand from the selected source on navigation, or label unloaded dates. A valid appointment six months ahead must not look absent just because it lies outside the initial import window.

### F21 — Timer and stopwatch durations depend on the wall clock

**Confirmed.** [FocusSession:10](src/Notch.Core/FocusSession.cs#L10) and [FocusSession:44](src/Notch.Core/FocusSession.cs#L44) calculate elapsed duration using UTC clock differences.

**Scenario:** move the clock backward five minutes during a running timer; its remaining duration increases. A forward adjustment can finish it early. Stopwatch totals jump too.

**Proposed action / acceptance:** use an injectable monotonic elapsed-time source and an explicit suspend/resume policy. Cover forward/backward clock adjustments independently from intentional elapsed sleep time.

### F22 — Media progress and controls need interaction-aware state

**Confirmed gaps; affected players and exact drag behavior need Windows reproduction.** [WindowsMediaService:174](src/Notch.Windows/Services/WindowsMediaService.cs#L174) updates the timeline during event-driven refresh, while [MainViewModel:233](src/Notch.Windows/ViewModels/MainViewModel.cs#L233) republishes the cached snapshot. [FeaturedToolsView:200](src/Notch.Windows/Views/FeaturedToolsView.xaml.cs#L200) enables transport based on session existence; lines 202–208 protect slider updates only while a debounce task exists.

**Impact:** players with sparse timeline events can show frozen progress; unsupported controls appear actionable; holding a slider still after the debounce completes permits a refresh to overwrite it. The Media volume control can be enabled even when the audio endpoint is unavailable.

**Proposed action / acceptance:** project visible playback using timestamp and playback rate; synchronize on session events; stop projection when hidden/paused. Expose native command capabilities and audio availability. Preserve pointer capture/focus during manipulation and reconcile on release. Verify browser/desktop players, non-1× playback, missing endpoints, long-held thumbs, seeks, and session switches.

### F23 — Hide and tray Open have inconsistent semantics

**Confirmed.** [OverlayHost:111](src/Notch.Windows/Interop/OverlayHost.cs#L111) hides only the HWND. Every [RenderShell:139](src/Notch.Windows/MainWindow.xaml.cs#L139) shows it again. [TrayService:121](src/Notch.Windows/Interop/TrayService.cs#L121) labels an item Open Notch, but invokes Toggle at line 137; [MainWindow:153](src/Notch.Windows/MainWindow.xaml.cs#L153) collapses an already-expanded model.

**Proposed action / acceptance:** represent explicit hidden state and define when notifications may override it. Give Open a deterministic show/expand action. Hide followed by display/settings changes should follow the documented policy; Open from hidden-expanded state should open the full panel.

### F24 — Stop does not cancel pending ambient-sound playback

**Confirmed asynchronous race.** [AmbientSound:18](src/Notch.Windows/Views/AmbientSound.cs#L18) awaits generation/loading before Play; Stop at line 24 only pauses the current player.

**Scenario:** press Play, then Stop while the first file is being prepared. The earlier task can finish and start sound after Stop. Rapid White/Brown selections can complete out of order.

**Proposed action / acceptance:** invalidate pending work with a playback generation/cancellation token; only the latest request may assign/play. Exercise slow initialization, immediate Stop, alternating sounds, and quit during loading.

### F25 — Fast clipboard changes can drop history entries

**Confirmed under delayed reads.** [ClipboardService:70](src/Notch.Windows/Services/ClipboardService.cs#L70) increments a shared generation on every change; line 79 discards any capture that completes after another change.

**Proposed action / acceptance:** separate enable/disable/clear lifetime invalidation from per-entry sequencing. Capture bounded entries in order without reintroducing discarded sensitive material after Clear or Disable. Delayed clipboard owners and rapid consecutive copies should preserve supported entries or clearly disclose capture limitations.

### F26 — Native service failures have no visible health path

**Confirmed.** [WindowsMediaService:138](src/Notch.Windows/Services/WindowsMediaService.cs#L138) and [ClipboardService:88](src/Notch.Windows/Services/ClipboardService.cs#L88) raise Error events. [MainViewModel:102](src/Notch.Windows/ViewModels/MainViewModel.cs#L102) subscribes only to Changed events; [Contracts:3](src/Notch.Core/Contracts.cs#L3) does not expose media errors.

**Proposed action / acceptance:** expose recoverable service health through contracts, safely dispatch state, and offer retry where useful. A failed media refresh should not be indistinguishable from “nothing playing”; clipboard capture failure should be visible without logging clipboard contents or credentials.

### F27 — Remote-city weather uses the computer's time zone for labels

**Confirmed rendering mismatch.** [WeatherClient:46](src/Notch.Core/Providers/WeatherClient.cs#L46) preserves forecast-location offsets, but [FeaturedToolsView:441](src/Notch.Windows/Views/FeaturedToolsView.xaml.cs#L441) converts hourly labels to LocalDateTime. Line 453 compares forecast dates against the computer's Today.

**Proposed action / acceptance:** display city-local hours and dates, or explicitly label a chosen alternative zone. Retain sufficient zone metadata for DST and the city-local current date. Check a remote city across midnight and a DST change.

### F28 — Clean-machine fonts and optional media components need fallbacks

**Confirmed font dependency; conditional media risk.** [NotchTheme:42](src/Notch.Windows/Styles/NotchTheme.xaml#L42) uses Segoe UI Variable, which is not standard on Windows 10. [FeaturedToolsView:107](src/Notch.Windows/Views/FeaturedToolsView.xaml#L107) uses Cascadia Mono, which is not guaranteed on a clean installation. Implicit substitution may alter sizing/alignment. [AmbientSound:10](src/Notch.Windows/Views/AmbientSound.cs#L10) constructs MediaPlayer without a local unavailable-state fallback, and playback failures are not surfaced.

**Proposed action / acceptance:** use available explicit font fallbacks or licensed bundled fonts. Scope and test Windows N editions with/without Media Feature Pack; optional sound failure must leave other tools usable. Compare clean Windows 10/11 typography rather than relying on a developer machine's fonts.

### F29 — Premium motion and efficiency claims are not measured

**Implementation gap and validation gap.** [MainWindow:114](src/Notch.Windows/MainWindow.xaml.cs#L114) changes native size immediately; [MainWindow:141](src/Notch.Windows/MainWindow.xaml.cs#L141) animates content only. [Validation notes:15](docs/validation-notes.md#L15) records no Windows performance measurements. Fixed/minimum content sizing in [FeaturedToolsView](src/Notch.Windows/Views/FeaturedToolsView.xaml) also needs small-screen and text-scaling review.

**Proposed action:** define a coherent shell transition, cancellation on rapid interaction, reduced-motion behavior, and adaptive content sizing. Profile before optimizing. Favor incremental control updates and bounded background work over rebuilding entire visual trees or adding perpetual animations.

**Acceptance after approval:** use the measurement targets in [release readiness:67](docs/release-readiness.md#L67); record frame pacing at 60 and 120/144 Hz, input response, cold/warm launch, idle CPU, memory, handles, and a long interaction session on both OS versions. Include a modest integrated-GPU machine. Proposed budgets are not achieved results.

### F30 — Important supplemental validation is not reproducible from the repo

**Validation gap.** [Validation notes:40](docs/validation-notes.md#L40) describes 31 temporary simulated view-model scenarios outside the repository. They supply historical evidence but cannot be rerun by a contributor from this checkout. Core tests and build CI do not cover the native interaction findings above.

**Proposed action / acceptance:** preserve meaningful regression cases for the approved fixes in maintained repository tests, and add a documented native qualification procedure with recorded results. Avoid tests that merely repeat implementation details. A clean checkout should be sufficient to reproduce the supported automated checks.

## P3 — smaller defects and presentation follow-up

| Finding | Evidence | Proposed action |
| --- | --- | --- |
| Downloads assumes the default user-profile location. A redirected/moved Downloads folder is ignored. | [UtilityToolsView:183](src/Notch.Windows/Views/UtilityToolsView.xaml.cs#L183) | Resolve the Windows Known Folder rather than concatenate a path. |
| Open on an emoji-copy confirmation navigates to Focus. | [UtilityToolsView:267](src/Notch.Windows/Views/UtilityToolsView.xaml.cs#L267), [MainWindow:183](src/Notch.Windows/MainWindow.xaml.cs#L183) | Give activities explicit destinations; omit Open for confirmations with no useful destination. |
| Public README setup contains this particular cloud workspace's assumptions. | [README:193](README.md#L193) | Publish portable contributor instructions and move environment-specific setup into an appropriate development note. |
| Windows 11-first wording is repeated beyond the support table. | [Design principles:47](docs/design-principles.md#L47), [release checklist:11](docs/release-readiness.md#L11) | Align product, installation, and QA documentation with equal Windows 10/11 release gates after the baseline is decided. |

Additional visual checks are **unverified risks**, not confirmed rendering failures: the weather credit and forecast occupy the same grid row ([XAML:99](src/Notch.Windows/Views/FeaturedToolsView.xaml#L99)); transparent clipped gaps may interact with pointer-exit handling ([MainWindow:168](src/Notch.Windows/MainWindow.xaml.cs#L168)); monitor indexes may select a different secondary monitor after topology changes ([OverlayHost:87](src/Notch.Windows/Interop/OverlayHost.cs#L87)). Inspect these on Windows before choosing repairs.

## Required Windows 10 / Windows 11 qualification matrix

Both OS columns are release requirements. They are currently **unverified**, not failed test runs. Proposed x64 baseline and edition scope still need a product decision; “complete support” should refer to a published, qualified matrix rather than every historical Windows configuration.

| Lane | Required coverage on each operating system | Windows 10 | Windows 11 |
| --- | --- | --- | --- |
| Clean deployment | Standard user, no SDK/Visual Studio, complete release package, first/second launch, upgrade, recovery, uninstall | Unverified | Unverified |
| Displays and shell | 1080p/4K, 100–200% DPI, mixed-DPI monitors, negative coordinates, disconnect/reconnect, supported taskbar edges/auto-hide, Explorer restart | Unverified | Unverified |
| Coexistence | Fullscreen apps, games/video, presentation, task switch, focus and topmost behavior, hide/open consistency | Unverified | Unverified |
| Input/accessibility | Keyboard-only, shortcut collision, IME/non-English layout, Narrator, contrast themes, 200% text scaling, animations off, touch if supported | Unverified | Unverified |
| Media/audio | Browser and desktop players, no session, unsupported commands, non-1× playback, seek, endpoint removal/switch, muted/no output | Unverified | Unverified |
| Local tools | Notes/scratchpad restart and failure recovery, simultaneous reminders, shelf/redirected folders, clipboard enable/clear/disable, vault save/read/remove, pickers, Awake | Unverified | Unverified |
| Lifecycle | Lock/unlock, suspend/hibernate/resume, timer expiry during sleep, network loss/recovery, repeated view changes, explicit quit, second instance | Unverified | Unverified |
| Clean fonts/components | Stock fonts, missing optional media components, N-edition policy and unavailable states | Unverified | Unverified |
| Connections | Supported provider accounts with approved test credentials; expiry, invalid credentials, timeout, rate limit, stale results, calendar/weather zones | Unverified | Unverified |
| Efficiency/motion | Frame timing, input response, launch time, CPU/memory/handles, rapid reversal, modest hardware, prolonged session | Unverified | Unverified |
| Commerce | Verified purchase/restore, offline access, renewal/cancellation/refund, expiry, data-preserving downgrade | Not implemented | Not implemented |

Each record should name the commit and artifact hash, OS edition/build, architecture, hardware/GPU, display settings, scenario, observed result, and remaining failures. Native screenshots/recordings and traces are useful evidence; design mockups cannot establish runtime correctness or performance.

## Evidence already available

The historical [CI run 37098620398](https://github.com/Sury2797/Notchling/actions/runs/37098620398) passed the baseline Windows native build/publish and Windows/Linux core checks. The existing [validation record](docs/validation-notes.md) reports 58 core cases and 31 temporary simulated view-model scenarios. These results were not rerun during this audit and do not certify the pending working-tree changes, native desktop behavior, live integrations, or subscription readiness.

The existing separation of portable core and native services, atomic storage replacement, bounded provider responses, opt-in memory-only clipboard capture, explicit demo labels, credential-vault use, and reduced-motion checks are useful foundations. They should be preserved while repairing the specific failure paths. No full rewrite is justified by the evidence collected here.

## Suggested order after the owner decides

| Phase | Work | Completion gate |
| --- | --- | --- |
| 1 — Protect work | F03–F09 and F14; preserve form drafts and fix loss of reminders/data | Targeted regression cases and recoverable failure paths |
| 2 — Make Windows dependable | F10–F11, F15, F21–F26, F28; complete native matrix | Independent Windows 10 and Windows 11 results for the same artifact |
| 3 — Establish daily product quality | F16–F20, F27, F29–F30 and smaller UX defects | Accurate states, usable setup, measured responsiveness and polished interaction |
| 4 — Prepare commercial delivery | F01–F02, F12–F13; finalize service costs and customer policies | Signed stable delivery, verified subscription lifecycle, permitted services, data-preserving downgrade |

The owner subsequently authorized repairs. The source work above is implemented; the remaining release work is production configuration, publisher-policy completion, and recorded Windows 10/11 native qualification. Signing and live sales remain disabled until those prerequisites are met.
