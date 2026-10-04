# Notchling native Windows qualification record

**Status: not executed.** This template is not a passing report. Save completed records under `docs/qualification/<version>-<os-build>.md` and attach traces/screenshots that contain no personal data.

| Artifact/machine | Recorded value |
| --- | --- |
| App version / commit | Pending |
| Signed installer SHA-256 / workflow URL | Pending |
| Setup EXE size / download ZIP size / extracted application size | Pending |
| Installed .NET runtime / Windows App SDK runtime package versions | Pending |
| OS edition, version and build | Pending |
| CPU / GPU / driver / RAM | Pending |
| Monitors / resolutions / refresh rates / DPI | Pending |
| Text scaling / theme / high contrast | Pending |
| Examiner / date | Pending |

Run every row on Windows 10 22H2 x64 and separately on each Windows 11 release selected for support.

| Area | Steps | Expected result | Result / evidence |
| --- | --- | --- | --- |
| Missing prerequisites | Clean standard-user Windows machine; no .NET 10/Windows App SDK 1.8 runtimes; run Setup with Internet | Setup detects missing runtimes, downloads/installs official installers, handles required UAC, and launches one usable app/tray instance without developer tools | Pending |
| Prerequisite interruption | Missing runtimes; no Internet; cancel download/install; deny UAC; rerun Setup | Useful failure/retry state; no broken Notchling upgrade; installed shared runtimes remain independently managed | Pending |
| Existing prerequisites | Compatible shared x64 runtimes installed; run Setup, launch, quit, relaunch | No repeated runtime download; one usable app/tray instance; app payload has no bundled runtime files | Pending |
| Advanced app-only folder | Extract optional app-only artifact with prerequisites installed; repeat on a machine without them | Launches with shared runtimes; missing-runtime state is identifiable when Setup is bypassed | Pending |
| Branding | Inspect app/taskbar/tray, Start menu, installer and Settings at small/high-DPI sizes | Notchling name and Pixel Dragon icon; readable small icon; no stale displayed product name | Pending |
| Upgrade | Create notes/scratchpad/reminders; install newer signed candidate | Save/quit prompt; data and credentials preserved | Pending |
| Interruption | Cancel setup; interrupt download; wrong hash and untrusted certificate | Existing app remains usable; update refused; no data loss | Pending |
| Recovery | Launch compatible previous signed version; restore exported workspace | Documented recovery works with no hand-edited JSON | Pending |
| Uninstall | Quit, uninstall, inspect installation folder and app-data folder | Application files removed; data/vault retained as disclosed | Pending |
| Media | Browser and desktop player; disabled seeking; no audio endpoint; unplug/replug | Capability-specific controls; useful unavailable states | Pending |
| Windows N | Test without Media Feature Pack, then with it | Optional media/sounds degrade; local tools still function | Pending |
| Workspace | Save each tool; exceed limit; corrupt JSON; export/restore; final save fails | No truncation or replacement; visible save/recovery action | Pending |
| Timers | Multiple simultaneous reminders; suspend beyond deadline; restart | Activities deliver and acknowledge without lost reminders | Pending |
| Desktop shell | Top/bottom/side taskbars where supported; auto-hide; fullscreen; Explorer restart | Reachable overlay; no obstructed essential shell controls | Pending |
| Display | 100/125/150/175/200%; secondary left/above; monitor unplug | Sharp text, aligned hit areas, visible controls | Pending |
| Input | Hover, gaps, pin, hotkey collision, edits, dialogs, 50 rapid switches | No focus theft, editor loss or accidental collapse | Pending |
| Accessibility | Narrator; keyboard only; high contrast; 125/150% text scaling | Useful names/states; visible focus; no lost content | Pending |
| Billing | Free Release; signed entitlement; offline expiry; cancellation/refund; duplicate webhooks | Correct access; safe downgrade/export; no desktop secrets | Pending |
| Providers | Missing account, expired credential, timeout, malformed result, disconnect | Truthful state; no stale account/range values or leaked secrets | Pending |
| Performance | Cold/warm launch, 60/120/144 Hz trace, idle sample, 100 switches and 30-min soak | Record real values against release-readiness budgets | Pending |

After passing, record remaining defects and sign-off explicitly. A workflow run, code review or mock service cannot fill an interactive result cell.
