# Reference and design decisions

The supplied uploads are identical copies of one 30-second, 1280 × 720 NotchPop demonstration. Both have SHA-256 `62fc3d637c3ffef4ae36dc3ef6072da93e729c87c5edb3e2af3a0d1e69c007ba`. The clip contains no audio track. Review used the entire timeline, full-resolution tool frames and 100 ms samples of the activity-to-dashboard transition. A second distinct reference has not been supplied.

The Threads pages and the inferred `https://notchpop.com/` website returned proxy HTTP 403 in this environment. No website page review or inspection of an unseen second video is claimed.

## What the video establishes

| Approximate time | Observed interface |
| --- | --- |
| 5 s | Compact, top-attached black notch with artwork and an equalizer indicator. |
| 6–9 s | A sale notification, then an agent request with Dismiss and Terminal actions. |
| 10–11 s | Home: six cards for servers, visitors, CPU/RAM/battery, screen time, revenue and weather. |
| 13 s | Media: artwork, metadata, scrubber, transport, volume and output label. |
| 14–15 s | Revenue: Today/7D/30D, provider tabs, total, chart and recent payments. |
| 16 s | Analytics: realtime users, page views, new users, activity chart and page rankings. |
| 17 s | Coding: provider tabs, token usage, quotas and an annual heatmap. |
| 19 s | Calendar: month grid, selected day, connection card and reminders. |
| 20 s | Weather: current conditions, hourly and seven-day forecasts. |
| 22 s | Focus: Pomodoro, three countdown presets, stopwatch and hydration. |
| 24 s | Inventory of Home plus 20 tools; detailed screens for the remaining utilities are not shown. |

The panel is attached to the top of the display, with rounded lower corners. Its dimensions change by tool. A detached charcoal capsule immediately below it contains icon-only tool navigation; a smaller separate capsule contains settings and pin. Selected icons have a gray capsule highlight. Panels use almost-black backgrounds, charcoal cards, white primary text, muted gray supporting text and restrained green or orange data accents.

The cloud wallpaper, macOS menu bar, blue explanatory captions and ending download advertisement belong to the presentation. They are not copied into the application surface. The reference supports hover navigation but does not prove a particular hover delay, easing curve, refresh frequency, startup footprint or integration reliability.

## Decisions for the Windows product

The application uses WinUI 3, C#/.NET 10, the Windows App SDK and native Windows APIs. The core library has no UI dependency. There is one desktop process, no embedded browser, no always-running local HTTP server and no application account requirement for local tools.

The native overlay region excludes the gaps around its detached toolbar so those gaps do not intercept desktop clicks. It anchors to the full display's top edge, including displays with a top taskbar; this behavior must be checked before shipping support for that configuration. Monitor dimensions are clamped in logical pixels; overflowing tool content can scroll rather than become unreachable at high scaling.

Opening on hover waits 180 ms, tool switching waits 100 ms and leaving waits 700 ms. These are design defaults, not timings measured from the video. Clicking and keyboard navigation remain available. Editors and open dialogs are protected from passive hover navigation. Explicit opening via the global shortcut activates the window; passive hover does not.

Content transitions use compositor opacity and scale animations, respecting both the application's reduced-motion setting and the Windows animation preference. **The outer native window currently resizes immediately. Reference-style spring morphing of the shell is not complete.** It needs Windows implementation, visual review and frame-time measurement before a fluidity claim.

## Priorities and limits

The product should first prove that media, focus, notes, shelf, clipboard and system controls are pleasant and reliable. The 21 dashboard/tool entries are not evidence that 21 equally deep features are needed for launch. Local convenience and low interruption are stronger reasons to return than a large menu.

External services are opt-in. Real payment revenue must not be presented as subscription MRR. Coding imports expose measured local usage, not inferred provider quotas. Calendar import does not imply Outlook/Google OAuth or full synchronization. Live activities currently cover local focus, hydration and reminders; sale webhooks and agent approval bridges are additional work. An agent alert must never silently approve or execute a command.

The Free/Pro split is a product concept. Billing, entitlements, account infrastructure, signing, commercial weather licensing and Linux UI implementation are separate release work. WinUI is Windows-only; Linux can share the portable domain and provider library but needs its own native presentation and OS services.

Windows build, XAML compilation, rendered layout, input behavior and performance measurements remain necessary. Linux core tests and C# API checks cannot establish that the Windows interface is production-ready.
