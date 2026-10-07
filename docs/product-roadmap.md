# Product and platform roadmap

Notchling is a Windows-first native desktop notch inspired by Dynamic Island, built around expandable controls, live activities, quick access, and local utility. The product model is a lightweight Free edition and **Premium at US$2 per month**. Linux desktop support follows the Windows release.

Debug exposes the broader development catalog; Release enforces the feature split and validates configured signed subscriptions. The repository does not activate a production payment service. See [pricing and subscription policy](pricing.md) for the exact boundaries.

## Current foundation

| Area | Current state |
| --- | --- |
| Native Windows application | C#/WinUI 3 with Windows App SDK; tray, global shortcut, native overlay placement and local tools implemented |
| Portable core | Timers, state machine, local storage, conversions and provider/import adapters; checks run on Windows and Linux |
| Connected data | Read-only Stripe reporting, configured HTTPS analytics, Open-Meteo development weather and explicit calendar/coding imports |
| Build and packaging | Windows x64 Release build and self-contained unpackaged publish verified by [CI](https://github.com/Sury2797/Notchling/actions/runs/37098620398) |
| Interactive release QA | Windows launch, accessibility, monitor behavior, live service checks and performance measurement remain required |
| Subscriptions | Free/Premium product policy documented; Release enforcement and configurable secure service implemented; production activation and live sandbox validation pending |
| Linux desktop | Planned; the core is portable, while the UI and operating-system services are Windows-specific |

## Windows release sequence

### 1. Finish the daily-use experience

Validate media, focus, notes, shelf, clipboard and system controls on Windows. Test empty and offline states alongside configured accounts. Refine typography, layout, keyboard focus and dock navigation before adding more integration breadth.

Validate panel-size transitions and measure input response, frame pacing, idle CPU, memory and handle growth. Preserve reduced motion and avoid waking hidden tools to redraw unchanged data.

### 2. Complete service and privacy validation

Exercise supported media players, missing audio devices, clipboard opt-in and disable, corrupt workspace recovery, calendar time zones, sleep/resume and monitor changes. Verify provider credentials can be removed and that disconnected dashboards stop requesting data.

A commercially distributed app needs an appropriate weather service arrangement and licensed endpoint. Release weather requires an authenticated commercially licensed proxy; development endpoints are not silently used for paid distribution. Unsupported revenue providers, provider OAuth and calendar account synchronization are separate integration work.

### 3. Validate and activate Free and Premium

Ship the basic Free experience without an application account for its local features. Configure and verify the implemented US$2 monthly Premium purchase flow, signed subscription state and Release feature enforcement. Define cancellation, payment failure, offline validation and account recovery before taking payment.

Upgrading must preserve local data. A subscription ending must return access to Free without silently deleting Premium-created notes or file references. Validate the implemented recovery/export path as part of downgrade handling. The in-app read-only Stripe dashboard remains separate from the system used to bill Notchling customers.

### 4. Prepare paid distribution

Configure the signed installer/update pipeline and execute clean-machine installation, upgrade, interruption and uninstallation checks. Complete customer-facing subscription disclosures, support and applicable license/terms review. The current CI ZIP is an unsigned development artifact.

Do not label a build commercially ready until the [release checklist](release-readiness.md) is complete. Build success and test counts are evidence for specific checks, not substitutes for native usability or subscription validation.

## Linux after the Windows release

Reuse `Notch.Core` and its provider contracts while replacing the presentation layer, media, audio, clipboard, tray, secret storage and monitor services. WinUI is Windows-only.

Evaluate X11 and Wayland independently. Global shortcuts, always-on-top behavior, overlay placement and tray support vary by compositor. A passing Linux core suite establishes portable logic; it does not establish Linux desktop support.

## Scope discipline

The launch priority is reliable local utility, then supported opt-in connections. No cloud synchronization, annual plan, lifetime plan or additional device allowance is included in the current product policy. New integrations and commercial terms require an explicit product decision rather than being implied by a toolbar entry.
