# Product and platform roadmap

Notchling is a Windows-first native desktop notch inspired by Dynamic Island, built around expandable controls, live activities, quick access, and local utility. The current v0.3.3 candidate opens **all tools free to everyone for public testing**. A lightweight Free edition and **Premium at US$2 per month** remain the later commercial plan. Linux desktop support follows the Windows release.

Release and Debug both enable the complete public-testing catalog; checkout is paused. Actual paid subscriptions still require signed verification, separate from testing access. The repository does not activate a production payment service. The previous verified v0.2.11 download retains its older access policy until the new candidate passes qualification. See [pricing and subscription policy](pricing.md).

## Current foundation

| Area | Current state |
| --- | --- |
| Native Windows application | C#/WinUI 3 with Windows App SDK; tray, global shortcut, native overlay placement and local tools implemented |
| Portable core | Timers, state machine, local storage, conversions and provider/import adapters; checks run on Windows and Linux |
| Connected data | Read-only Stripe reporting, configured HTTPS analytics and explicit calendar/coding imports; licensed weather backend still unconfigured |
| Build and packaging | Single Windows x64 Setup EXE with automatic shared prerequisites; last qualified v0.2.11 delivery in [CI](https://github.com/Sury2797/Notchling/actions/runs/37735689610); v0.3.0 qualification pending |
| Interactive release QA | Windows launch, accessibility, monitor behavior, live service checks and performance measurement remain required |
| Access and subscriptions | Entire catalog free during public testing; checkout paused; future Free/Premium gates and signed entitlement validation retained; production activation and live sandbox validation pending |
| Linux desktop | Planned; the core is portable, while the UI and operating-system services are Windows-specific |

## Windows release sequence

### 1. Finish the daily-use experience

Validate media, focus, notes, shelf, clipboard and system controls on Windows. Test empty and offline states alongside configured accounts. Refine typography, layout, keyboard focus and dock navigation before adding more integration breadth.

Validate panel-size transitions and measure input response, frame pacing, idle CPU, memory and handle growth. Preserve reduced motion and avoid waking hidden tools to redraw unchanged data.

### 2. Complete service and privacy validation

Exercise supported media players, missing audio devices, clipboard opt-in and disable, corrupt workspace recovery, calendar time zones, sleep/resume and monitor changes. Verify provider credentials can be removed and that disconnected dashboards stop requesting data.

A commercially distributed app needs an appropriate weather service arrangement and licensed endpoint. Release weather requires an authenticated commercially licensed proxy; development endpoints are not silently used for paid distribution. Unsupported revenue providers, provider OAuth and calendar account synchronization are separate integration work.

### 3. Test the full catalog, then decide commercial activation

Ship the entire supported catalog without an application account for local testing. Check real media, audio, clipboard, notes, import and Awake workflows while collecting modest-hardware layout and motion evidence. Keep disconnected providers explicit rather than treating an unlocked panel as a connected service.

When the owner decides to restore the commercial split, configure and verify the implemented US$2 monthly Premium purchase flow, signed subscription state and commercial Release gates. Define cancellation, payment failure, offline validation and account recovery before taking payment. Public testing does not automatically enroll anyone in that plan.

Upgrading must preserve local data. A subscription ending must return access to Free without silently deleting Premium-created notes or file references. Validate the implemented recovery/export path as part of downgrade handling. The in-app read-only Stripe dashboard remains separate from the system used to bill Notchling customers.

### 4. Prepare paid distribution

Configure the signed installer/update pipeline and execute clean-machine installation, upgrade, interruption and uninstallation checks. Complete customer-facing subscription disclosures, support and applicable license/terms review. The current public installer is an unsigned evaluation EXE; CI also retains temporary installer and app-folder artifacts.

Do not label a build commercially ready until the [release checklist](release-readiness.md) is complete. Build success and test counts are evidence for specific checks, not substitutes for native usability or subscription validation.

## Linux after the Windows release

Reuse `Notch.Core` and its provider contracts while replacing the presentation layer, media, audio, clipboard, tray, secret storage and monitor services. WinUI is Windows-only.

Evaluate X11 and Wayland independently. Global shortcuts, always-on-top behavior, overlay placement and tray support vary by compositor. A passing Linux core suite establishes portable logic; it does not establish Linux desktop support.

## Scope discipline

The launch priority is reliable local utility, then supported opt-in connections. No cloud synchronization, annual plan, lifetime plan or additional device allowance is included in the current product policy. New integrations and commercial terms require an explicit product decision rather than being implied by a toolbar entry.
