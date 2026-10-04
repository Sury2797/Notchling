# Modules and connections

Notch has three presentation states: a collapsed strip, an expanded selected tool and a temporary live activity. The separate dock changes tools. Pinning keeps a panel open; dismissing an activity restores the previous panel. Activities are deduplicated and use a bounded queue.

The catalog below describes the implemented development build and the planned commercial split. **Release enforces Free/Premium access; billing is available only after secure publisher-service configuration. Debug builds visibly enable development tools.** Native behavior and external connections still require the [release checks](release-readiness.md).

## Tool catalog

| Tool | Data and behavior | Planned edition and implementation limits |
| --- | --- | --- |
| Home | Summary of connected tools and local state | Available navigation summary in both plans; extended dashboard data requires Premium. Empty states precede optional labeled demo data |
| Media | Windows media sessions, playback, artwork, timeline and volume | Basic play/pause/previous/next in Free; full panel in Premium. The player must expose a system media session; seeking and transport availability depend on that session |
| Revenue | Read-only Stripe captured successful charges after refunds; daily totals and recent payments | Premium. Requires a restricted Stripe credential. Totals are payment revenue, not subscription MRR; mixed currencies are not summed. Polar, Dodo and AdSense are unsupported |
| Analytics | Configured HTTPS endpoint using a normalized JSON response | Premium. Optional bearer credential and the adapter schema below; no built-in website tracking or provider OAuth |
| Coding | Import local Claude or Codex session JSONL usage records | Premium. User-selected file only. Repeated Claude messages and cumulative Codex counters avoid double-counting; no inferred subscription quotas |
| Calendar | Imported iCalendar events, recognized meeting links and local reminders | Premium. Single events, DAILY/WEEKLY rules, exclusions and OS time zones. Unsupported recurrence fails explicitly; no Google/Outlook OAuth or background account sync |
| Weather | City geocoding, current conditions, hourly and daily forecasts | Premium. Explicit city, no GPS lookup; short-lived cache. Release requires the authenticated licensed weather proxy and attribution; there is no silent public-endpoint fallback |
| Focus | Pomodoro, countdown, stopwatch/laps and hydration timers | One Pomodoro in Free; expanded controls in Premium. Deadlines account for missed UI ticks; restart and sleep/resume behavior need Windows QA |
| Shelf | Saved file references | Premium. Files remain in their original locations; moved/deleted-file recovery needs validation |
| Clipboard | Text history | Premium. Off by default; memory-only plain text, capped at 50 items and 100,000 characters per item; disabling clears captured history |
| Servers | Listening local TCP ports | Premium. Read-only system information; does not stop servers or execute commands |
| System | CPU, memory, battery and volume | Premium. Battery can be unavailable; endpoint changes and missing audio devices need Windows QA |
| Screen time | Active desktop time since the app launched | Premium. Excludes idle periods over 60 seconds and caps long sampling gaps; not a historical per-application usage database |
| Notes | Local notes | Premium. Atomic UTF-8 JSON storage; no cloud sync |
| Scratchpad | Single local scratch text | Free and Premium. No cloud sync |
| Files | Saved local file shortcuts | Premium. Existing paths; no remote file service |
| Links | Saved web links | Premium. Opens URLs through the OS; no remote link index |
| Emoji | Native emoji picker and copy action | Premium. Windows input/clipboard behavior needs interactive QA |
| Sounds | Local ambient sound controls | Premium. Locally generated sound; no streaming service |
| Convert | Length, mass, temperature and decimal/binary data conversion | Premium. Rejects mixed categories, nonfinite input, overflow and temperatures below absolute zero |
| Awake | Prevent idle sleep while explicitly active | Premium. Does not defeat explicit user sleep; cleanup and resume behavior need Windows QA |
| Settings | Preferences, privacy, connections and demo mode | Privacy, reduced motion and basic preferences in both editions. Secrets use the OS vault; configured service login, signed plan state and billing portal; unavailable setup stays explicit |

The catalog contains Home plus 20 tool panels. Settings handles configuration; All tools is a navigation index. The Free surface is intentionally limited to the compact notch, basic transport, one Pomodoro and one Scratchpad. See [pricing](pricing.md) for subscription policy.

## Connection behavior

Connections are optional. A missing account produces an unconfigured state, and a failed request produces an error rather than an unlabeled demo result. Imports operate on files the user chooses. Provider credentials belong in Windows Credential Locker, separately from local workspace JSON.

Disconnecting a service must remove its saved credential and invalidate in-flight results. Never include credential values in setup scripts, examples, test output or screenshots. The Revenue adapter's Stripe credential is separate from the Notch subscription service.

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

The [provider data contracts](../src/Notch.Core/Providers/README.md) define Stripe units and reporting limits, analytics validation, coding-import semantics, supported calendar recurrence and weather licensing requirements.

## Network destinations

Core tests use fixtures and do not contact these services. Optional live connections require these HTTPS destinations:

| Purpose | Destination |
| --- | --- |
| Stripe reporting | `api.stripe.com` |
| Development weather geocoding | `geocoding-api.open-meteo.com` |
| Development weather forecast | `api.open-meteo.com` |
| Analytics | The explicitly configured HTTPS endpoint |
| SDK bootstrap | `builds.dotnet.microsoft.com` and the official artifact origin in release metadata |
| Native dependency restore | NuGet endpoints, including `api.nuget.org` |

Production weather uses the configured owner-operated HTTPS service and licensed external endpoints. Subscription requests use that same explicitly configured service; hosted payment/management links are accepted only at Stripe checkout/billing HTTPS hosts. See [billing setup](billing.md).
