# Provider data contracts

These adapters run in the desktop process. They read only data that the user explicitly connects or selects. The caller owns a reused `HttpClient` with a finite timeout, automatic decompression, and **automatic redirects disabled**. Responses are limited to 8 MB. Credentials come from `ISecretVault`; no response bodies, tokens, prompts, or log lines are logged. Authentication and rate-limit errors contain only a generic description and HTTP status. All async reads accept cancellation.

## Revenue

`RevenueClient(HttpClient, ISecretVault).ReadAsync(provider, days, adSenseAccount?, cancellationToken)` supports **Stripe only** in this build. Polar, Dodo, and AdSense fail explicitly with `NotSupportedException`; their tabs do not imply working integrations. `adSenseAccount` is reserved for a future verified adapter.

The vault key is `stripe`. Use a Stripe restricted key with permission to read charges, entered in Settings. The request is `GET https://api.stripe.com/v1/charges?limit=100&created[gte]=...&created[lte]=...`; `starting_after` advances pagination. The range is 1–366 UTC calendar days through the current instant. Only `paid && captured && status == "succeeded"` charges count. Net amount is **`amount_captured - amount_refunded`**, using charge-currency units. Full/partial refunds on these charges are reflected as of refresh; refunds of charges before the selected range are outside this report. Each net amount is attributed to the original charge day. This is a payment report, **not** monthly recurring revenue or a cash-flow statement.

Charges are deduplicated by `id`. Pagination stops after 100 pages / 10,000 charges; a remaining `has_more` sets `RevenueSnapshot.Complete=false`. Never present that result as a complete account total. Multiple currencies cause an explicit error because this snapshot model holds one currency; no foreign exchange rate is invented. An empty result has an empty currency code. Zero-decimal currencies and three-decimal BHD/JOD/KWD/OMR/TND are handled separately. Stripe's ISK and UGX compatibility representation uses hundredths. Unknown currency codes are rejected.

Schema reference: [Stripe's official OpenAPI specification](https://github.com/stripe/openapi/blob/master/openapi/spec3.json), inspected at schema version `2026-09-30.endive`; the request does not override the account's API version. Unit rules: [Stripe currencies](https://docs.stripe.com/currencies). Supported-code reference: [Stripe Go currency definitions](https://github.com/stripe/stripe-go/blob/master/currency.go).

## Weather

`WeatherClient(HttpClient).ReadAsync(city, cancellationToken)` searches a user-entered city at `https://geocoding-api.open-meteo.com/v1/search`, then fetches `https://api.open-meteo.com/v1/forecast`. No GPS permission is requested. Queries use `timezone=auto`, `timeformat=unixtime`, Celsius temperatures, km/h wind, current conditions, hourly forecasts and a seven-day forecast. Named timezones preserve DST where the system supports the returned identifier. Current temperature/apparent temperature/humidity/wind/weather code and aligned hourly/daily arrays are validated. The last city result is cached for five minutes to avoid refresh storms.

This free endpoint is for **noncommercial development**. A paid app needs a commercial arrangement and its licensed endpoint before release; that configuration is not implemented here. Display Open-Meteo attribution beside weather data. References: [forecast service source and terms](https://github.com/open-meteo/open-meteo), [geocoding service and terms](https://github.com/open-meteo/geocoding-api). This environment could inspect the official repositories but live forecast requests were blocked by the remote endpoint; fixtures validate response parsing, not a production API connection.

## Analytics

`AnalyticsClient(HttpClient, ISecretVault).ReadAsync(endpoint, site, cancellationToken)` accepts an explicitly configured absolute HTTPS endpoint with no URL userinfo or fragment. The `site` argument is a display label; the configured URL must identify the intended dataset. No query parameters are automatically added. The vault key is `analytics`; its token is sent as Bearer authorization to that endpoint only. Do not automatically populate the endpoint from imported files.

The endpoint must return the normalized JSON contract below. It is an adapter contract for a user-controlled endpoint; this build does **not** implement Google Analytics or DataFast authentication.

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

All counts must be nonnegative integers. Active users, page users, and timeline entries fit Int32; page views and new users fit Int64. The timeline contains at most 1,440 entries, pages at most 100, and page paths at most 500 characters. `updatedAt` must be a parseable ISO-style timestamp. Metric time windows and timeline intervals are determined by the configured endpoint; the adapter does not invent them.

## Coding usage import

`CodingImporter.ReadAsync(path, cancellationToken)` reads one user-selected UTF-8 JSONL file, up to 50 MB, 500,000 lines, and 2 MB per line. It retains token counts, session IDs and UTC usage days only; message content, prompts, workspace paths inside events and source text are not retained. `CodingSnapshot.SourcePath` is the explicitly selected source path.

Supported records:

- Claude `type: "assistant"` records with `sessionId`, `timestamp`, `message.id` (or record `uuid`) and `message.usage`. Input counts include `input_tokens + cache_creation_input_tokens + cache_read_input_tokens`; output is `output_tokens`. Repeated message IDs within one session use maximum observed counters rather than summing streaming duplicates. Usage is attributed to its first observed message date.
- Codex `type: "session_meta"` records set `payload.id`. `type: "event_msg"`, `payload.type: "token_count"`, and `payload.info.total_token_usage` supply cumulative `input_tokens`/`output_tokens`. Only counter increases count, so duplicate token events do not inflate totals. Cached input and reasoning output are subsets and are not added again. Counter decreases do not create negative or inferred usage. Files without session metadata use their filename as a session identifier.

No account usage limits, model subscription quotas, tools/messages/streak metrics, Cursor or Grok data are inferred. A file with no supported usage fails explicitly. Missing metadata, invalid JSON, invalid UTF-8 and integer overflow fail with sanitized errors.

## Calendar import

`IcsCalendar.Parse(text, from, until)` returns events overlapping the half-open time range, sorted by start. It supports unfolded properties, escaped SUMMARY, date-only/UTC/floating/TZID DTSTART and DTEND, DURATION, cancelled events, known meeting URLs in URL/LOCATION/DESCRIPTION, and daily/weekly RRULE with INTERVAL/COUNT/UNTIL/WKST and weekly BYDAY. EXDATE excludes matching occurrences. All-day DTEND is exclusive. Floating and date-only values are interpreted as UTC; named zones use operating-system historical DST rules, with Windows/IANA identifier conversion where available. The first ambiguous wall time is selected; nonexistent recurrence wall times are skipped.

Input is capped at 4 MB of characters, 100,000 physical lines and 64 KB per unfolded line. Evaluated occurrences and results are bounded at 2,000. RDATE, EXRULE, RECURRENCE-ID overrides, monthly/yearly recurrence, custom VTIMEZONE-only identifiers and unsupported RRULE fields fail explicitly rather than silently omitting appointments. The importer does not synchronize with Google/Microsoft calendar accounts or schedule OS notifications itself.
