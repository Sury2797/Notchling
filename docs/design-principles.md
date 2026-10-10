# Notchling design principles

Notchling is a native desktop notch inspired by Dynamic Island: useful controls and live activities near the top of the display, available in a moment and compact when they are not needed. Windows is the primary platform, with a native Linux interface planned later. The interface and service boundaries are designed around native desktop behavior.

## A small surface with a clear purpose

The collapsed notch presents a short status at the display's top edge. Expanding reveals one selected tool. A separate compact dock provides navigation, while settings and pinning have their own controls. Temporary live activities draw attention to a completed timer or reminder, then return to the previous state.

Panels use near-black surfaces, charcoal cards, white primary text and muted supporting text. Color carries meaning: playback, progress, a warning or a selected state. Content determines the panel's size; every tool should not have to fill a large dashboard.

The v0.4.3 source uses 49 bundled vector control icons with one stroke vocabulary. Source logos and album artwork have distinct roles: compact Media shows the source, expanded Media shows the track artwork. Browser identities remain truthful; any manually chosen provider is identified as a choice for the current track. Card feedback uses a restrained surface change, and the catalog explains a tool's purpose without repeating testing-access labels on every tile.

The Pixel Dragon gives Notchling a recognizable app icon: a compact dark dragon with expressive eyes. Use the approved assets for the application, installer, tray and documentation; preserve a readable silhouette at small sizes. The icon supports product identity, while the interface centers on controls, content, and live activities. Decorative motion should not compete with those tasks.

Home summarizes a workspace. Media exposes playback. Focus keeps a deadline visible. Notes and Scratchpad hold local text. Connected dashboards expose a specific dataset. Each panel must be useful on its own, with truthful empty, busy, unavailable and failed states.

Contextual help belongs beside ambiguous controls, rather than on every visible label. A small question-mark button offers a short hover preview and a full local explanation on explicit activation. Its target is 28 DIP even though the symbol is 14 DIP. Explanations wrap, scroll and support keyboard dismissal. Home and Settings share a quick guide without interrupting ordinary use or forcing a first-launch tour.

Setup is part of the product experience. The candidate uses the approved artwork, native scalable pages and fully formatted application terms/privacy generated from the canonical documents. Styling must preserve every policy paragraph, link and table description; it cannot shorten obligations or imply production billing/support is operational.

## Native input and desktop behavior

- Opening by global shortcut activates the window. Passive hover opening does not steal focus from another application.
- The dock remains usable while moving between its detached controls and the panel. Native window regions exclude the empty gaps so unrelated desktop clicks can pass through.
- Focused editors and open dialogs are protected from passive navigation and collapse.
- Pinning is explicit. Keyboard navigation, visible focus, useful accessible names and reduced motion belong to both product tiers.
- Display scaling and small monitors must preserve access to controls. Content may scroll instead of being clipped outside the display.

Current implementation defaults are 180 ms to open on hover, 100 ms to switch tools and 700 ms before passive closing. These timings remain subject to usability testing. The overlay anchors to the selected display's top edge; top taskbars and mixed-monitor layouts still need interactive validation.

## Motion should explain state

Transitions should make an opening panel, a change of tool or a completed activity easy to follow. Animating decoration continuously adds work without helping the user. The v0.4.3 source uses compositor opacity and small translations, keeping text and vector icons at their final scale. It respects the application reduced-motion setting and the Windows animation preference, including changes during an active transition.

Native panel transitions retain their current geometry on reversal, use 110–180 ms according to the remaining distance, and settle immediately with reduced motion. Their perceived quality and rendered frame pacing remain native qualification work. Performance claims must come from measurements on Windows hardware, including high-refresh-rate displays.

## Efficiency through boundaries

The application uses WinUI 3, C#/.NET and the Windows App SDK in one desktop process. The portable core has no UI dependency. Local tools do not need an embedded browser, a background HTTP server or an application account.

Views are created when needed. Native media updates are event-driven. System data is sampled for relevant visible tools; TCP scans run away from the UI thread. Weather responses are cached, external requests are cancellable and stale results are discarded. A closed or disconnected dashboard should not create unnecessary provider traffic.

The full module catalog is a capability inventory, not a reason to make every tool run continuously. Media, focus, notes, shelf, clipboard and system controls are the first interaction priorities. Connected tools should remain optional.

## Trustworthy data and quiet defaults

Local notes and scratch text stay on the device. Provider credentials use Windows Credential Locker. Clipboard capture is opt-in, memory-only, bounded and cleared when disabled. Imports read files chosen by the user rather than searching personal directories automatically.

Payment totals are not subscription MRR. Imported coding counters are not account quotas. Calendar import is not account synchronization. Unsupported providers and recurrence rules fail explicitly rather than displaying invented results. Illustrative data is available only through labeled demo mode.

The current public-testing phase opens every supported tool to everyone in Release and Debug, while paid Premium status still requires a valid signed proof. Checkout is paused. A later Free/Premium split shares the same accessibility, privacy and data-integrity standards; see [pricing](pricing.md). Commercial billing requires a further owner decision, configuration and validation.

## Platform discipline

Windows 10 22H2 x64 and supported Windows 11 x64 releases are equal product targets. Native launch, display behavior, input, accessibility and resource use must pass the [Windows release matrix](release-readiness.md). A green build establishes compilation and packaging, not completed interactive QA.

The v0.4.3 source adds native x86 and ARM64 packaging. Qualification must identify the app architecture and the host architecture separately: an x86 app on an x64 runner is not a 32-bit Windows 10 result. Windows 11 has no x86 OS edition. Native ARM64 checks need an ARM64 host; consumer hardware, mixed-DPI and animation results remain separate from runner tests.

Linux can reuse the portable core and provider contracts, but needs a separate native interface and operating-system services. X11 and Wayland behavior must be evaluated independently. The Windows executable is not a Linux desktop release.
