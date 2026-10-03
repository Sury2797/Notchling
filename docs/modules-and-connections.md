# Modules and connections

The notch has three states: a collapsed strip, an expanded selected module, and a temporary live activity. The separate dock changes modules. Pinning keeps a panel open; dismissing an activity restores the previous panel. Short-lived activities are deduplicated and queued with a bound, so a burst cannot grow an unlimited queue.

These are source-level implementation notes, not a claim that every Windows interaction has been exercised. Windows service and UI behavior must pass the release matrix.

| Module | Data and behavior | Requirements and limits |
| --- | --- | --- |
| Home | Summary of connected modules and local state | Empty states precede optional demo data |
| Media | Windows media sessions, playback controls, artwork and timeline | A player must expose a system media session; seek and transport availability depend on that session |
| Revenue | Read-only Stripe captured successful charges, less refunds, daily totals and recent payments | Save a read-only Stripe credential; totals are payment revenue, not a calculation of MRR. Different currencies are not summed. Polar, Dodo and AdSense connections remain unsupported |
| Analytics | Configured HTTPS endpoint using a normalized JSON response | Save an optional bearer credential and implement the adapter schema below; no built-in website tracking or provider OAuth |
| Coding | Import local Claude or Codex session JSONL records | Choose a file explicitly; no recursive home-directory scan. Claude repeated message IDs and Codex cumulative counters avoid double-counting |
| Calendar | Imported iCalendar events and recognized meeting links; local reminders | Choose an `.ics` file. Supports single events and DAILY/WEEKLY rules, exclusions and OS time-zone rules. Unsupported recurrence errors explicitly. No Google/Outlook OAuth or background account sync |
| Weather | City geocoding, current weather, hourly and daily forecasts | Open-Meteo HTTPS access; city is explicit rather than GPS location. Forecasts are cached briefly. Current endpoint is for noncommercial development; paid distribution requires licensed endpoint configuration and attribution |
| Focus | Pomodoro, countdown, stopwatch/laps and hydration timers | Wall-clock deadlines account for long UI tick gaps; restart persistence and sleep/resume UX still require Windows QA |
| Shelf | Saved file references | Files remain in their original locations; verify behavior when files are moved or deleted |
| Clipboard | Text history | Capture is off by default; newly captured plain text is kept in memory, capped at 50 items/100,000 characters per item, and cleared on disable |
| Servers | Listening local ports | Reports local system information; does not stop servers or execute arbitrary commands |
| System | CPU, memory, battery and volume | Battery can be unavailable; audio endpoint changes and missing devices require Windows QA |
| Screen time | Active desktop time since the app launched | Excludes idle periods over 60 seconds and caps long sampling gaps; not a complete per-application historical usage database |
| Notes | Local notes | Atomic UTF-8 JSON storage; no cloud sync |
| Scratchpad | Local scratch text | No cloud sync |
| Files | Saved file shortcuts | Existing local paths; no remote file service |
| Links | Saved web links | URLs are opened using the OS; no remote link index |
| Emoji | Emoji picker and copy action | Clipboard interaction needs Windows QA |
| Sounds | Local background sound controls | Does not stream music or claim a music-service subscription |
| Convert | Length, mass, temperature and decimal/binary data conversions | Rejects mixed categories, nonfinite input, overflow and temperatures below absolute zero |
| Awake | Prevent idle sleep while explicitly active | Does not defeat an explicit user sleep action; cleanup on quit/resume requires Windows QA |
| Settings | Preferences, privacy, connections and demo mode | Stores secrets in the OS vault; Free/Pro is conceptual until billing is implemented |

The inventory is Home plus 20 tool panels: 21 dashboard surfaces. Settings handles configuration, and All tools is the navigation index.

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

Counts must be nonnegative. The series and page list are bounded. A site label is display text, not an instruction to crawl a website. The saved `analytics` vault entry is sent as a bearer credential to the configured endpoint; credentials embedded in URLs are rejected.

The [provider contract reference](../src/Notch.Core/Providers/README.md) records the exact Stripe units/reporting limits, analytics validation, coding import semantics, calendar subset and weather licensing/attribution requirements.

## Network destinations

Core tests do not contact these services. Optional live connections require the following HTTPS origins:

| Use | Destination |
| --- | --- |
| Stripe reporting | `api.stripe.com` |
| Weather geocoding | `geocoding-api.open-meteo.com` |
| Weather forecast | `api.open-meteo.com` |
| Analytics | The user-configured HTTPS endpoint |
| .NET bootstrap | `builds.dotnet.microsoft.com` and the official artifact origin in its release metadata |
| Native dependency restore | NuGet endpoints, including `api.nuget.org` |

No credential values belong in setup scripts, examples, test results or screenshots. Disconnecting a provider should remove its saved vault entry. Connection failures and unsupported providers must remain visible instead of falling back to unlabeled invented figures.
