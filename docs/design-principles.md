# Notchling design principles

Notchling is a native desktop notch inspired by Dynamic Island: useful controls and live activities near the top of the display, available in a moment and compact when they are not needed. Windows is the primary platform, with a native Linux interface planned later. The interface and service boundaries are designed around native desktop behavior.

## A small surface with a clear purpose

The collapsed notch presents a short status at the display's top edge. Expanding reveals one selected tool. A separate compact dock provides navigation, while settings and pinning have their own controls. Temporary live activities draw attention to a completed timer or reminder, then return to the previous state.

Panels use near-black surfaces, charcoal cards, white primary text and muted supporting text. Color carries meaning: playback, progress, a warning or a selected state. Content determines the panel's size; every tool should not have to fill a large dashboard.

The Pixel Dragon gives Notchling a recognizable app icon: a compact dark dragon with expressive eyes. Use the approved assets for the application, installer, tray and documentation; preserve a readable silhouette at small sizes. The icon supports product identity, while the interface centers on controls, content, and live activities. Decorative motion should not compete with those tasks.

Home summarizes a workspace. Media exposes playback. Focus keeps a deadline visible. Notes and Scratchpad hold local text. Connected dashboards expose a specific dataset. Each panel must be useful on its own, with truthful empty, busy, unavailable and failed states.

## Native input and desktop behavior

- Opening by global shortcut activates the window. Passive hover opening does not steal focus from another application.
- The dock remains usable while moving between its detached controls and the panel. Native window regions exclude the empty gaps so unrelated desktop clicks can pass through.
- Focused editors and open dialogs are protected from passive navigation and collapse.
- Pinning is explicit. Keyboard navigation, visible focus, useful accessible names and reduced motion belong to both product tiers.
- Display scaling and small monitors must preserve access to controls. Content may scroll instead of being clipped outside the display.

Current implementation defaults are 180 ms to open on hover, 100 ms to switch tools and 700 ms before passive closing. These timings remain subject to usability testing. The overlay anchors to the selected display's top edge; top taskbars and mixed-monitor layouts still need interactive validation.

## Motion should explain state

Transitions should make an opening panel, a change of tool or a completed activity easy to follow. Animating decoration continuously adds work without helping the user. Content currently uses compositor opacity and scale transitions and respects the application reduced-motion setting and the Windows animation preference.

Native panel transitions are designed to interrupt smoothly and respect reduced motion. Their perceived quality and rendered frame pacing remain native qualification work. Performance claims must come from measurements on Windows hardware, including high-refresh-rate displays.

## Efficiency through boundaries

The application uses WinUI 3, C#/.NET and the Windows App SDK in one desktop process. The portable core has no UI dependency. Local tools do not need an embedded browser, a background HTTP server or an application account.

Views are created when needed. Native media updates are event-driven. System data is sampled for relevant visible tools; TCP scans run away from the UI thread. Weather responses are cached, external requests are cancellable and stale results are discarded. A closed or disconnected dashboard should not create unnecessary provider traffic.

The full module catalog is a capability inventory, not a reason to make every tool run continuously. Media, focus, notes, shelf, clipboard and system controls are the first interaction priorities. Connected tools should remain optional.

## Trustworthy data and quiet defaults

Local notes and scratch text stay on the device. Provider credentials use Windows Credential Locker. Clipboard capture is opt-in, memory-only, bounded and cleared when disabled. Imports read files chosen by the user rather than searching personal directories automatically.

Payment totals are not subscription MRR. Imported coding counters are not account quotas. Calendar import is not account synchronization. Unsupported providers and recurrence rules fail explicitly rather than displaying invented results. Illustrative data is available only through labeled demo mode.

The v0.3.0 public-testing phase opens every supported tool to everyone in Release and Debug, while paid Premium status still requires a valid signed proof. Checkout is paused. A later Free/Premium split shares the same accessibility, privacy and data-integrity standards; see [pricing](pricing.md). Commercial billing requires a further owner decision, configuration and validation.

## Platform discipline

Windows 10 22H2 x64 and supported Windows 11 x64 releases are equal product targets. Native launch, display behavior, input, accessibility and resource use must pass the [Windows release matrix](release-readiness.md). A green build establishes compilation and packaging, not completed interactive QA.

Linux can reuse the portable core and provider contracts, but needs a separate native interface and operating-system services. X11 and Wayland behavior must be evaluated independently. The Windows executable is not a Linux desktop release.
