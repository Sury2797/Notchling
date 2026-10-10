# Notchling native Windows qualification record

**Status: not executed.** This template is not a passing report. Save completed records under `docs/qualification/<version>-<os-build>.md` and attach traces/screenshots that contain no personal data.

| Artifact/machine | Recorded value |
| --- | --- |
| App version / commit | Pending |
| Signed installer SHA-256 / workflow URL | Pending |
| Setup EXE size / download ZIP size / extracted application size | Pending |
| Installed .NET runtime / Windows App SDK runtime package versions | Pending |
| OS edition, version and build | Pending |
| Native OS architecture / app architecture / measured process architecture | Pending |
| CPU / GPU / driver / RAM | Pending |
| Monitors / resolutions / refresh rates / DPI | Pending |
| Text scaling / theme / high contrast | Pending |
| Examiner / date | Pending |

Run every row on Windows 10 22H2 and separately on each Windows 11 release and architecture selected for support. The v0.4.9 targets are native x64, x86 and ARM64 apps; a passing x86 app on an x64 cloud host does not fill a 32-bit Windows 10 result. Windows 11 has no x86 edition. Record native ARM64 results separately from emulation and keep unsupported configurations out of the download promise.

| Area | Steps | Expected result | Result / evidence |
| --- | --- | --- | --- |
| Missing prerequisites | Clean standard-user Windows machine; no .NET 10/Windows App SDK 1.8 runtimes; run Setup with Internet | Setup detects missing runtimes, downloads/installs official installers, handles required UAC, and launches one usable app/tray instance without developer tools | Pending |
| Prerequisite interruption | Missing runtimes; no Internet; cancel download/install; deny UAC; rerun Setup | Useful failure/retry state; no broken Notchling upgrade; installed shared runtimes remain independently managed | Pending |
| Existing prerequisites | Compatible app-architecture .NET and app/native Windows App Runtime package plan installed; run Setup, launch, quit, relaunch | No repeated runtime download; correct process architecture; one usable app/tray instance; app payload has no bundled runtime files | Pending |
| Advanced app-only folder | Extract optional app-only artifact with prerequisites installed; repeat on a machine without them | Launches with shared runtimes; missing-runtime state is identifiable when Setup is bypassed | Pending |
| Branding | Inspect app/taskbar/tray, Start menu, installer and Settings at small/high-DPI sizes | Notchling name and Pixel Dragon icon; readable small icon; no stale displayed product name | Pending |
| Installer presentation | Open welcome, accept terms, navigate privacy and back; enlarge text/DPI; cancel before installing | Complete formatted documents without Markdown syntax; approved artwork, readable headings and aligned reachable actions; cancellation installs nothing | Pending |
| Upgrade | Create notes/scratchpad/reminders; install newer signed candidate | Save/quit prompt; data and credentials preserved | Pending |
| Interruption | Cancel setup; interrupt download; wrong hash and untrusted certificate | Existing app remains usable; update refused; no data loss | Pending |
| Recovery | Launch compatible previous signed version; restore exported workspace | Documented recovery works with no hand-edited JSON | Pending |
| Uninstall | Quit, uninstall, inspect installation folder and app-data folder | Application files removed; data/vault retained as disclosed | Pending |
| Media | Browser and desktop player; Unicode/long/absent title, artist and album; player switches; missing/changing artwork; pause, 2× and seek; no audio endpoint; unplug/replug | Correct session metadata and source identity; artwork/logo fallback; no late old track; accurate timing and disabled unsupported controls | Pending |
| Browser source choice | Chrome/Edge/Firefox with unidentified website; choose YouTube; update artwork/timeline; change track; replace native session with identical track text | Browser identity is truthful by default; selected logo stays only for its current track/session; compact source and expanded artwork remain distinct | Pending |
| Windows N | Test without Media Feature Pack, then with it | Optional media/sounds degrade; local tools still function | Pending |
| Workspace | Save each tool; exceed limit; corrupt JSON; export/restore; final save fails | No truncation or replacement; visible save/recovery action | Pending |
| Shelf files and images | Add a chosen file; drag onto compact notch; paste a bitmap; opt into copies; cancel/fail a write; open/reveal item; restart; remove item | Reference/capture ownership is clear; stored items reopen; originals remain intact; partial writes are cleaned, completed copies are disclosed and retained, Remove keeps files; URL-only images are not downloaded | Pending |
| Timers | Multiple simultaneous reminders; suspend beyond deadline; restart | Activities deliver and acknowledge without lost reminders | Pending |
| Desktop shell | Top/bottom/side taskbars where supported; auto-hide; fullscreen; Explorer restart | Reachable overlay; no obstructed essential shell controls | Pending |
| Display | 100/125/150/175/200%; secondary left/above; monitor unplug | Sharp text, aligned hit areas, visible controls | Pending |
| Input | Repeated hover/leave, body-to-dock gap, transparent flanks, Settings leave, six-second typing lease, pin, hotkey collision, edits, dialogs, 50 rapid switches | No stuck expansion, focus theft, editor loss or accidental collapse; pinned and active protected interactions remain usable | Pending |
| Accessibility | Narrator; keyboard only; high contrast; 125/150% text scaling | Useful names/states; visible focus; no lost content | Pending |
| Contextual help | Hover, click and keyboard-open Home/Settings guide and selected control explanations; resize, scroll, Escape, switch panels and leave the notch | Local preview/full text stays readable inside display bounds; explicit keyboard dismissal; active help remains usable without stuck expansion or leaked popup | Pending |
| Icons and responsive layout | Stock fonts; Weather, source brands and every catalog control; narrow/text-scaled Revenue/Analytics/Coding and Settings; contrast theme change | No missing-glyph boxes; readable consistent icons; cards and actions reflow without clipped content or fixed dark contrast overrides | Pending |
| Motion and feedback | Rapid opening reversal; reduced motion toggled mid-transition; Windows animations off; hydration feedback followed by another tool | Current geometry retained; immediate reduced-motion settlement; text does not scale; ordinary feedback expires after eight seconds and stays scoped to its tool | Pending |
| Evaluation updates | Check official releases, unavailable network, Cancel, opt-in daily checks, preview, duplicate checks while editing | Read-only discovery; no unsigned installer download/execution; bounded operation; default automatic checks off; quiet one-per-version session notice with no editor takeover | Pending |
| Signed updates | Trusted signed release; select matching architecture; progress/Cancel; wrong hash, stale version, wrong architecture, untrusted signer and save failure | Explicit install only; size/hash/publisher verification before Setup; no execution on rejection; local changes preserved | Pending |
| Testing access | Release without account/purchase; open all 21 tools and direct controls; visit unconfigured dashboards | All tools available; setup states truthful; checkout paused; no fabricated Premium proof | Pending |
| Future billing | Explicit Freemium phase; signed entitlement; offline expiry; cancellation/refund; duplicate webhooks | Correct access; safe downgrade/export; no desktop secrets | Pending |
| Providers | Missing account, expired credential, timeout, malformed result, disconnect | Truthful state; no stale account/range values or leaked secrets | Pending |
| Performance | Cold/warm launch, 60/120/144 Hz trace, idle sample, 100 switches and 30-min soak | Record real values against release-readiness budgets | Pending |

After passing, record remaining defects and sign-off explicitly. A workflow run, code review or mock service cannot fill an interactive result cell.
