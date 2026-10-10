# Modules and connections

Notchling has three presentation states: a collapsed strip, an expanded selected tool and a temporary live activity. The separate dock changes tools. Pinning keeps a panel open; dismissing an activity restores the previous panel. Activities are deduplicated and use a bounded queue.

The published v0.3.4 evaluation enables the complete catalog for **public testing in Release and Debug, free for everyone**. No owner-only unlock or paid account is required for local tools, and checkout is paused. Provider setup and native capabilities remain real requirements. All 21 panels and eight connection states passed [installed-app checks on both Windows cloud hosts](https://github.com/Sury2797/Notchling/actions/runs/37944357692). Native behavior and external connections still require the [release checks](release-readiness.md).

## Tool catalog

| Tool | Data and behavior | Public-testing access and implementation limits |
| --- | --- | --- |
| Home | Summary of connected tools and local state | Unlocked. Empty states precede optional, explicitly labeled sample data |
| Media | Windows media sessions, playback, artwork, timeline and volume | Full panel unlocked. Metadata comes from the selected player; seeking and transport depend on its exposed capabilities, and volume requires a usable Windows audio endpoint |
| Revenue | Read-only Stripe captured successful charges after refunds; daily totals and recent payments | Unlocked; requires a restricted Stripe credential. Totals are payment revenue, not subscription MRR; mixed currencies are not summed. Polar, Dodo and AdSense are unsupported |
| Analytics | Configured HTTPS endpoint using a normalized JSON response | Unlocked; requires a bearer credential and the adapter schema below. No built-in website tracking or provider OAuth |
| Coding | Import local Claude or Codex session JSONL usage records | Unlocked; user-selected file only. Repeated Claude messages and cumulative Codex counters avoid double-counting; no inferred subscription quotas |
| Calendar | Imported iCalendar events, recognized meeting links and local reminders | Unlocked. Single events, DAILY/WEEKLY rules, exclusions and OS time zones. Unsupported recurrence fails explicitly; no Google/Outlook OAuth or background account sync |
| Weather | City geocoding, current conditions, hourly and daily forecasts | Tool unlocked; real data still requires the authenticated licensed weather proxy, which is not configured yet. Explicit city, no GPS lookup; attribution required and no silent public-endpoint fallback |
| Focus | Pomodoro, countdown, stopwatch/laps and hydration timers | All controls unlocked. Deadlines account for missed UI ticks; restart and sleep/resume behavior need Windows QA |
| Shelf | File/folder references; v0.4.10 adds explicit local file/image captures | Unlocked. References are the default; optional file copies and supplied bitmaps/virtual files stay locally. Originals are never moved; native drop/picker/paste/restart behavior awaits candidate qualification |
| Clipboard | Text history | Unlocked; off by default. Memory-only plain text, capped at 50 items and 100,000 characters per item; disabling clears captured history |
| Servers | Listening local TCP ports | Unlocked. Read-only system information; does not stop servers or execute commands |
| System | CPU, memory, battery and volume | Unlocked. Battery can be unavailable; endpoint changes and missing audio devices need Windows QA |
| Screen time | Active desktop time since the app launched | Unlocked. Excludes idle periods over 60 seconds and caps long sampling gaps; not a historical per-application usage database |
| Notes | Local notes | Unlocked. Atomic UTF-8 JSON storage; no cloud sync |
| Scratchpad | Single local scratch text | Unlocked. No cloud sync |
| Files | Saved local file shortcuts | Unlocked. Existing paths; no remote file service |
| Links | Saved web links | Unlocked. Opens URLs through the OS; no remote link index |
| Emoji | Native emoji picker and copy action | Unlocked. Windows input/clipboard behavior needs interactive QA |
| Sounds | Local ambient sound controls | Unlocked. Locally generated sound; no streaming service; optional Windows media components must be available |
| Convert | Length, mass, temperature and decimal/binary data conversion | Unlocked. Rejects mixed categories, nonfinite input, overflow and temperatures below absolute zero |
| Awake | Prevent idle sleep while explicitly active | Unlocked. Does not defeat explicit user sleep; cleanup and resume behavior need Windows QA |
| Settings | Preferences, privacy, connections and sample preview | Available to all testers. Secrets use the OS vault; optional service login remains available when configured; purchase controls stay paused |

The catalog contains Home plus 20 tool panels. Settings handles configuration; All tools is a navigation index. The later commercial model is a light Free edition and US$2/month Premium. Its feature restrictions are inactive during public testing; see [pricing](pricing.md).

The v0.4.10 candidate supplies a local quick guide in Home and Settings and small contextual help controls for source identity, focus modes, reporting semantics, clipboard privacy, screen time, connection fields and placement. Hover gives a short preview; explicit activation opens the full explanation. See [Getting started](getting-started.md). Guidance describes actual capabilities and requirements, rather than treating an unlocked panel as an operational connection.

Shelf in this candidate accepts supported drops onto the compact notch, opens its panel, and offers **Choose files**, **Save file copies** and **Paste image**. Files/folders normally retain their original paths; folders are never recursively copied. Supplied bitmap/virtual-file payloads and opted-in file copies are saved in `%LOCALAPPDATA%\Notch\shelf-captures`, with limits of 50 MiB per file, 250 MiB and 100 capture files, alongside the 100-entry Shelf limit. It never downloads an image URL. Removing a Shelf entry keeps its file/copy; **Reveal saved copies** exposes retained files for management. See the [Shelf guide](getting-started.md#keep-files-and-images-on-shelf) and [Privacy](privacy.md) for ownership, clipboard and backup behavior. Actual native file/image acceptance remains pending.

## Connection behavior

Connections are optional. A missing account produces an unconfigured state, and a failed request produces an error rather than an unlabeled demo result. Imports operate on files the user chooses. Provider credentials belong in Windows Credential Locker, separately from local workspace JSON.

Use **Settings → Provider connections** to save/remove the Stripe reporting key or analytics bearer token and endpoint. Compact action rows align fields and buttons; drafts survive navigation and status updates. **Settings → Connection status → Check connections** inspects the real media/audio services, clipboard opt-in, configured providers and selected imports. Missing setup is not contacted. A successful provider read is distinct from merely saving a key; checks report each source independently without exposing secrets. Exit sample-data preview before checking real connections.

Disconnecting a service must remove its saved credential and invalidate in-flight results. Never include credential values in setup scripts, examples, test output or screenshots. The Revenue adapter's Stripe credential is separate from the Notchling subscription service.

## Media source identity in v0.4.10 source

The compact strip shows a **source logo**, while the expanded player keeps artwork/thumbnail and source identity separate. Bundled logos cover YouTube, YouTube Music, Spotify, Chrome, Edge, Firefox, VLC, Apple Music and Media Player. An unknown registered player can retain its Windows-supplied application icon. Metadata remains the selected Windows session's title, artist and album; a logo does not create a playback integration.

Windows often reports a browser without its website. A generic browser keeps its Chrome/Edge/Firefox identity; the app does not infer YouTube from a title or thumbnail. The Media source menu offers an explicit provider choice for that track. Its tooltip identifies the choice as user-selected, and changing title/artist/album/source or replacing the native session clears it. Timeline or artwork refreshes within the same track preserve it. No browser-history access, tab scanning or network favicon fetch is used.

Responsive Revenue, Analytics and Coding summaries in this candidate adapt their card columns to available width and Windows text scale. Candidate visual and provider behavior still needs its own Windows/device qualification; the published v0.3.4 result above does not certify these new views.

## Analytics adapter schema

The configured HTTPS endpoint returns this normalized shape:

```json
{
  "activeUsers": 3,
  "pageViews": 100,
  "newUsers": 12,
  "timeline": [1, 3, 2],
  "pages": [{ "path": "/", "users": 3 }],
  "updatedAt": "2026-10-03T01:02:00Z"
}
```

Counts must be nonnegative. The series and page list are bounded. A site label is display text, not an instruction to crawl a website. A saved `analytics` vault entry is required and sent as a bearer credential to the configured endpoint; credentials embedded in URLs are rejected. The panel identifies the snapshot timestamp and stale state instead of implying continuous live tracking.

The [provider data contracts](../src/Notch.Core/Providers/README.md) define Stripe units and reporting limits, analytics validation, coding-import semantics, supported calendar recurrence and weather licensing requirements.

## Network destinations

Core tests use fixtures and do not contact these services. Optional live connections require these HTTPS destinations:

| Purpose | Destination |
| --- | --- |
| Stripe reporting | `api.stripe.com` |
| Development weather geocoding | `geocoding-api.open-meteo.com` |
| Development weather forecast | `api.open-meteo.com` |
| Analytics | The explicitly configured HTTPS endpoint |
| Release discovery in v0.4.10 source | Official `api.github.com` release metadata, with the canonical `github.com/Sury2797/Notchling` release feed/asset listing as fallback; explicit action or opt-in daily check |
| Verified signed updates | Canonical GitHub stable manifest and allowed official GitHub release-asset hosts; explicit install only |
| SDK bootstrap | `builds.dotnet.microsoft.com` and the official artifact origin in release metadata |
| Native dependency restore | NuGet endpoints, including `api.nuget.org` |

Weather uses the configured owner-operated HTTPS service and licensed external endpoints; an authenticated service session remains required even while product access is free. That backend is currently absent. Optional service login uses the same explicitly configured origin; purchasing is paused during testing. Future hosted payment/management links are accepted only at Stripe checkout/billing HTTPS hosts. See [billing setup](billing.md).
