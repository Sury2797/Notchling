# Product and platform roadmap

Windows is the first product platform. The supplied reference establishes the structure: dark top-center notch, separate compact dock, native panels and transient live activities. Refinement should come from alignment, typography, useful control states and consistent motion. Each panel has a purpose and a width suited to its content.

## Free and Pro concept

The following is a proposed commercial split for review. No payment flow, account server, entitlement validation or subscription enforcement is present. Pricing should be decided after usability and retention are measured.

| Free | Proposed Pro |
| --- | --- |
| Essential media controls, focus timers and local utilities | Multiple workspace configurations and deeper personalization |
| Local notes, conversions and basic system controls | Advanced connected dashboards and richer history |
| Keyboard navigation, reduced motion and privacy controls | Optional encrypted synchronization and backed-up workspace state |
| Basic calendar import and weather | Expanded calendar/account integrations after OAuth work |

Accessibility, reduced motion, privacy, reliable local storage and responsive basic controls belong in both tiers. Billing integration is a later task with its own account, entitlement, cancellation and recovery design. Read-only Stripe revenue reporting is separate from charging users for this application.

## Windows completion order

1. Compile and launch the WinUI app on a real Windows machine. Resolve native XAML, deployment and service compatibility issues found by the Windows CI job.
2. Validate the full reference-derived module inventory with keyboard and pointer input. Make offline, unconfigured, empty, busy, unsupported and failed states explicit.
3. Measure idle use, active animation responsiveness, memory and handle growth. Tune motion and refresh scheduling using actual results.
4. Verify external connections with user-configured test accounts and representative data. Complete calendar edge cases, media-player differences and monitor/resume checks.
5. Package a signed installer, add update handling and run clean-machine installation/upgrade/uninstallation QA. This repository currently prepares an unsigned unpackaged review artifact.
6. Introduce paid accounts and billing only after the core application passes release QA.

## Linux phase

The portable core and package-free tests run on Linux now. A Linux overlay UI and Linux media, audio, tray, clipboard, secret storage and monitor implementations are future work. Windows APIs must remain behind the existing service contracts so that the core can be reused.

A Linux implementation must be validated separately on X11 and Wayland. Compositor support for global shortcuts, overlay placement, always-on-top windows and trays differs. A successful Linux core build does not constitute Linux desktop support. Do not package the Windows executable as a Linux release or present a browser imitation as a native implementation.
