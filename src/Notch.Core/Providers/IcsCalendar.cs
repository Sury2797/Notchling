using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Notch.Core.Providers;

/// <summary>
/// Reads a bounded subset of iCalendar: single events and daily/weekly recurrence.
/// Floating date-times are resolved in the caller's calendar zone (the local zone
/// by default). Date-only values retain their calendar dates and exclusive end.
/// Named zones use the operating system's historical rules, including daylight saving.
/// Unsupported recurrence features fail explicitly instead of losing appointments.
/// </summary>
public static class IcsCalendar
{
    private const int MaximumCharacters = 4 * 1024 * 1024;
    private const int MaximumLineCharacters = 64 * 1024;
    private const int MaximumLines = 100_000;
    private const int MaximumOccurrences = 2_000;
    private static readonly Regex Links = new(@"https?://[^\s<>""']+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    private static readonly Regex DurationPattern = new(@"^P(?:(?<weeks>\d+)W|(?:(?<days>\d+)D)?(?:T(?:(?<hours>\d+)H)?(?:(?<minutes>\d+)M)?(?:(?<seconds>\d+)S)?)?)$", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    private static readonly string[] MeetingHosts = ["zoom.us", "zoom.com", "zoomgov.com", "meet.google.com", "teams.microsoft.com", "teams.live.com", "teams.cloud.microsoft", "webex.com", "whereby.com", "meet.jit.si", "meet.goto.com", "gotomeet.me", "gotomeeting.com", "bluejeans.com", "chime.aws"];
    private static readonly string[] EventProperties = ["DTSTART", "DTEND", "DURATION", "SUMMARY", "URL", "LOCATION", "DESCRIPTION", "RRULE", "EXDATE", "RDATE", "EXRULE", "RECURRENCE-ID", "STATUS"];

    /// <summary>Returns events overlapping the half-open interval [from, until), sorted by start time.</summary>
    /// <exception cref="InvalidDataException">Malformed input, invalid dates, or a resource limit was exceeded.</exception>
    /// <exception cref="NotSupportedException">A recurrence feature outside the supported subset was encountered.</exception>
    public static IReadOnlyList<CalendarEvent> Parse(string text, DateTimeOffset from, DateTimeOffset until, TimeZoneInfo? calendarZone = null)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (until < from) throw new ArgumentException("The calendar range must end at or after its start.", nameof(until));
        if (text.Length > MaximumCharacters) throw Invalid("The calendar exceeds the 4 MiB text limit.");

        calendarZone ??= TimeZoneInfo.Local;
        var events = ReadEvents(text);
        var result = new List<CalendarEvent>();
        var evaluated = 0;
        foreach (var properties in events)
            Expand(properties, from, until, calendarZone, result, ref evaluated);
        return result.OrderBy(item => item.Start).ThenBy(item => item.Title, StringComparer.Ordinal).ToArray();
    }

    private static List<Dictionary<string, List<Property>>> ReadEvents(string text)
    {
        var events = new List<Dictionary<string, List<Property>>>();
        var stack = new Stack<string>();
        Dictionary<string, List<Property>>? current = null;
        var eventDepth = 0;
        var sawCalendar = false;
        foreach (var line in Unfold(text))
        {
            if (line.Length == 0) continue;
            var property = ReadProperty(line);
            if (property.Name == "BEGIN")
            {
                var component = property.Value.ToUpperInvariant();
                if (stack.Count >= 16) throw Invalid("Calendar components are nested too deeply.");
                if (stack.Count == 0 && component != "VCALENDAR")
                    throw Invalid("The selected file must contain a VCALENDAR calendar.");
                if (component == "VCALENDAR")
                {
                    if (stack.Count != 0 || sawCalendar) throw Invalid("The selected file contains an invalid calendar container.");
                    sawCalendar = true;
                }
                stack.Push(component);
                if (component == "VEVENT")
                {
                    if (current is not null) throw Invalid("VEVENT components cannot be nested.");
                    if (events.Count >= MaximumOccurrences) throw Invalid("The calendar exceeds the 2,000 event limit.");
                    current = new(StringComparer.OrdinalIgnoreCase);
                    eventDepth = stack.Count;
                }
                continue;
            }
            if (property.Name == "END")
            {
                if (stack.Count == 0 || !string.Equals(stack.Peek(), property.Value, StringComparison.OrdinalIgnoreCase))
                    throw Invalid("Calendar components have mismatched BEGIN and END lines.");
                if (stack.Peek() == "VEVENT")
                {
                    if (current is null) throw Invalid("The calendar contains an unmatched VEVENT.");
                    events.Add(current);
                    current = null;
                }
                stack.Pop();
                continue;
            }
            // Alarm properties belong to VALARM, not to the containing appointment.
            if (current is null || stack.Count != eventDepth || !EventProperties.Contains(property.Name, StringComparer.OrdinalIgnoreCase)) continue;
            if (!current.TryGetValue(property.Name, out var values)) current[property.Name] = values = [];
            values.Add(property);
        }
        if (stack.Count != 0 || current is not null) throw Invalid("The calendar has an unterminated component.");
        if (!sawCalendar) throw Invalid("The selected file does not contain a VCALENDAR calendar.");
        return events;
    }

    private static IEnumerable<string> Unfold(string text)
    {
        using var reader = new StringReader(text);
        StringBuilder? current = null;
        var count = 0;
        while (reader.ReadLine() is { } line)
        {
            if (++count > MaximumLines) throw Invalid("The calendar exceeds the line limit.");
            if (line.Length > MaximumLineCharacters) throw Invalid("A calendar line exceeds the length limit.");
            if (line.Length > 0 && (line[0] == ' ' || line[0] == '\t'))
            {
                if (current is null) throw Invalid("A folded calendar line has no preceding line.");
                if (current.Length + line.Length - 1 > MaximumLineCharacters) throw Invalid("An unfolded calendar line exceeds the length limit.");
                current.Append(line.AsSpan(1));
            }
            else
            {
                if (current is not null) yield return current.ToString();
                current = new(line.TrimStart('\uFEFF'));
            }
        }
        if (current is not null) yield return current.ToString();
    }

    private static Property ReadProperty(string line)
    {
        var quoted = false;
        var colon = -1;
        for (var index = 0; index < line.Length; index++)
        {
            if (line[index] == '"') quoted = !quoted;
            else if (line[index] == ':' && !quoted) { colon = index; break; }
        }
        if (colon <= 0 || quoted) throw Invalid("A calendar property is missing its value separator.");
        var header = SplitHeader(line[..colon]);
        var parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var parameter in header.Skip(1))
        {
            var equals = parameter.IndexOf('=');
            if (equals <= 0) throw Invalid("A calendar property parameter is malformed.");
            var value = parameter[(equals + 1)..];
            if (value.StartsWith('"') && value.EndsWith('"') && value.Length >= 2) value = value[1..^1];
            if (!parameters.TryAdd(parameter[..equals], value)) throw Invalid("A calendar property has duplicate parameters.");
        }
        return new(header[0].ToUpperInvariant(), parameters, line[(colon + 1)..]);
    }

    private static List<string> SplitHeader(string header)
    {
        var parts = new List<string>();
        var quoted = false;
        var start = 0;
        for (var index = 0; index < header.Length; index++)
        {
            if (header[index] == '"') quoted = !quoted;
            else if (header[index] == ';' && !quoted) { parts.Add(header[start..index]); start = index + 1; }
        }
        parts.Add(header[start..]);
        return parts;
    }

    private static Property? Single(Dictionary<string, List<Property>> properties, string name)
    {
        if (!properties.TryGetValue(name, out var values)) return null;
        if (values.Count != 1) throw Invalid($"An event has more than one {name} property.");
        return values[0];
    }

    private static void Expand(Dictionary<string, List<Property>> properties, DateTimeOffset from, DateTimeOffset until, TimeZoneInfo calendarZone, List<CalendarEvent> output, ref int evaluated)
    {
        foreach (var name in new[] { "RDATE", "EXRULE", "RECURRENCE-ID" })
            if (properties.ContainsKey(name)) throw new NotSupportedException($"Calendar {name} recurrence is not supported.");

        var start = ReadDate(Single(properties, "DTSTART") ?? throw Invalid("An event is missing DTSTART."), calendarZone);
        var startInstant = DateInstant(start, start.Local, false)!.Value;
        var endProperty = Single(properties, "DTEND");
        var durationProperty = Single(properties, "DURATION");
        if (endProperty is not null && durationProperty is not null) throw Invalid("An event cannot contain both DTEND and DURATION.");
        DurationValue duration;
        var endZone = start.Zone;
        if (endProperty is not null)
        {
            var end = ReadDate(endProperty, calendarZone);
            if (end.AllDay != start.AllDay) throw Invalid("DTSTART and DTEND must use the same date value type.");
            // DATE endpoints are calendar dates, not elapsed 24-hour periods.
            // In particular a two-day event spanning DST still occupies two days.
            duration = start.AllDay
                ? new(end.Local - start.Local, (end.Local.Date - start.Local.Date).Days)
                : new(ToInstant(end.Local, end.Zone, false)!.Value - startInstant);
            endZone = end.Zone;
        }
        else duration = durationProperty is not null ? ReadDuration(durationProperty.Value, start.AllDay) : new(start.AllDay ? TimeSpan.FromDays(1) : TimeSpan.Zero, start.AllDay ? 1 : 0);
        if (duration.Span < TimeSpan.Zero || (start.AllDay && duration.Span == TimeSpan.Zero)) throw Invalid("DTEND must follow DTSTART (an all-day end is exclusive).");

        var ruleProperty = Single(properties, "RRULE");
        var rule = ruleProperty is null ? null : ReadRule(ruleProperty.Value, start);
        var exclusions = new HashSet<long>();
        if (properties.TryGetValue("EXDATE", out var excluded))
        {
            foreach (var property in excluded)
                foreach (var value in property.Value.Split(','))
                {
                    if (exclusions.Count >= MaximumOccurrences) throw Invalid("An event exceeds the 2,000 exclusion limit.");
                    var date = ReadDate(property with { Value = value }, start.Zone);
                    if (date.AllDay != start.AllDay) throw Invalid("EXDATE must use the same date value type as DTSTART.");
                    exclusions.Add(DateInstant(date, date.Local, false)!.Value.UtcTicks);
                }
        }
        var title = Unescape(Single(properties, "SUMMARY")?.Value ?? "Untitled event");
        var meetingUrl = FindMeetingUrl(properties);
        if (string.Equals(Single(properties, "STATUS")?.Value, "CANCELLED", StringComparison.OrdinalIgnoreCase)) return;

        CheckBudget(ref evaluated);
        if (rule?.Until is null || startInstant <= rule.Until)
            AddOccurrence(startInstant, duration, endZone, start, title, meetingUrl, exclusions, from, until, output);
        if (rule is null || rule.Count == 1 || until <= startInstant || from == until || (rule.Until is { } ruleEnd && ruleEnd < startInstant)) return;

        var ordinal = 1;
        // Unbounded rules can jump near the requested range. Finite COUNT rules
        // are enumerated from DTSTART so skipped DST-gap instances never consume COUNT.
        // Nominal-day durations can differ from 24 hours across a zone transition.
        var lookBehind = duration.NominalDays == 0 ? duration.Span : TimeSpan.FromTicks(duration.Span.Ticks > TimeSpan.MaxValue.Ticks - TimeSpan.TicksPerDay ? TimeSpan.MaxValue.Ticks : duration.Span.Ticks + TimeSpan.TicksPerDay);
        var earliest = SubtractClamped(from, lookBehind);
        var localEarliest = TimeZoneInfo.ConvertTime(earliest, start.Zone).DateTime;
        if (rule.Frequency == "DAILY")
        {
            var step = rule.Count is null ? Math.Max(1L, (localEarliest.Date - start.Local.Date).Days / rule.Interval - 1L) : 1L;
            while (AddDays(start.Local, step * rule.Interval) is { } candidate)
            {
                var instant = DateInstant(start, candidate, true);
                if (instant is not null)
                {
                    if (instant >= until || (rule.Until is { } last && instant > last)) break;
                    CheckBudget(ref evaluated);
                    ordinal++;
                    if (rule.Count is { } count && ordinal > count) break;
                    AddOccurrence(instant.Value, duration, endZone, start, title, meetingUrl, exclusions, from, until, output);
                    if (rule.Count == ordinal) break;
                }
                else CheckBudget(ref evaluated);
                step++;
            }
        }
        else
        {
            var offset = ((int)start.Local.DayOfWeek - (int)rule.WeekStart + 7) % 7;
            var anchor = start.Local.Date.AddDays(-offset).Add(start.Local.TimeOfDay);
            var week = rule.Count is null ? Math.Max(0L, (localEarliest.Date - anchor.Date).Days / (7L * rule.Interval) - 1L) : 0L;
            var days = rule.Days.OrderBy(day => ((int)day - (int)rule.WeekStart + 7) % 7).ToArray();
            while (AddDays(anchor, week * 7L * rule.Interval) is { } weekStart)
            {
                foreach (var day in days)
                {
                    var dayOffset = ((int)day - (int)rule.WeekStart + 7) % 7;
                    if (AddDays(weekStart, dayOffset) is not { } candidate) return;
                    if (candidate <= start.Local) continue;
                    var instant = DateInstant(start, candidate, true);
                    if (instant is null) { CheckBudget(ref evaluated); continue; }
                    if (instant >= until || (rule.Until is { } last && instant > last)) return;
                    CheckBudget(ref evaluated);
                    ordinal++;
                    if (rule.Count is { } count && ordinal > count) return;
                    AddOccurrence(instant.Value, duration, endZone, start, title, meetingUrl, exclusions, from, until, output);
                    if (rule.Count == ordinal) return;
                }
                week++;
            }
        }
    }

    private static void AddOccurrence(DateTimeOffset start, DurationValue duration, TimeZoneInfo endZone, CalendarDate source, string title, string? meetingUrl, HashSet<long> exclusions, DateTimeOffset from, DateTimeOffset until, List<CalendarEvent> output)
    {
        if (exclusions.Contains(start.UtcTicks) || from == until) return;
        DateTimeOffset end;
        try
        {
            if (duration.NominalDays == 0) end = TimeZoneInfo.ConvertTime(start.Add(duration.Span), endZone);
            else
            {
                var localEnd = AddDays(start.DateTime, duration.NominalDays) ?? throw Invalid("A calendar duration ends outside the supported date range.");
                // A nominal day preserves wall time. If its endpoint falls in a
                // gap, RFC 5545 uses the offset immediately before that gap.
                var dayEnd = source.AllDay
                    ? new DateTimeOffset(localEnd, endZone.GetUtcOffset(localEnd))
                    : endZone.IsInvalidTime(localEnd)
                    ? new DateTimeOffset(localEnd, endZone.GetUtcOffset(localEnd.AddDays(-2)))
                    : ToInstant(localEnd, endZone, false)!.Value;
                end = source.AllDay ? dayEnd : TimeZoneInfo.ConvertTime(dayEnd.Add(duration.Clock), endZone);
            }
        }
        catch (ArgumentOutOfRangeException exception) { throw Invalid("A calendar occurrence ends outside the supported date range.", exception); }
        if (start < until && (end > from || (duration.Span == TimeSpan.Zero && start >= from)))
        {
            if (output.Count >= MaximumOccurrences) throw Invalid("The requested range exceeds the 2,000 occurrence limit.");
            output.Add(new(title, start, end, meetingUrl, source.AllDay, source.Floating, source.Zone.Id));
        }
    }

    private static CalendarDate ReadDate(Property property, TimeZoneInfo? fallbackZone = null)
    {
        property.Parameters.TryGetValue("VALUE", out var valueType);
        property.Parameters.TryGetValue("TZID", out var zoneId);
        if (valueType is not null && !string.Equals(valueType, "DATE", StringComparison.OrdinalIgnoreCase) && !string.Equals(valueType, "DATE-TIME", StringComparison.OrdinalIgnoreCase))
            throw Invalid("Calendar dates must use DATE or DATE-TIME values.");
        var allDay = property.Value.Length == 8;
        if (valueType is not null && string.Equals(valueType, "DATE", StringComparison.OrdinalIgnoreCase) != allDay)
            throw Invalid("A calendar date does not match its VALUE parameter.");
        if (allDay)
        {
            if (zoneId is not null) throw Invalid("Date-only calendar values cannot have a TZID.");
            if (!DateTime.TryParseExact(property.Value, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)) throw Invalid("A calendar date is invalid.");
            return new(DateTime.SpecifyKind(date, DateTimeKind.Unspecified), fallbackZone ?? TimeZoneInfo.Local, true, false);
        }
        var isUtc = property.Value.EndsWith('Z');
        if (isUtc && zoneId is not null) throw Invalid("A UTC calendar date cannot also have a TZID.");
        var value = isUtc ? property.Value[..^1] : property.Value;
        if (!DateTime.TryParseExact(value, "yyyyMMdd'T'HHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var local)) throw Invalid("A calendar date-time is invalid.");
        var zone = isUtc ? TimeZoneInfo.Utc : zoneId is not null ? ResolveZone(zoneId) : fallbackZone ?? TimeZoneInfo.Local;
        return new(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), zone, false, !isUtc && zoneId is null);
    }

    private static TimeZoneInfo ResolveZone(string id)
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
        catch (TimeZoneNotFoundException)
        {
            // .NET usually resolves both identifier families automatically. The
            // explicit conversion also supports hosts with stricter zone lookup.
            if (TimeZoneInfo.TryConvertWindowsIdToIanaId(id, out var iana))
                try { return TimeZoneInfo.FindSystemTimeZoneById(iana); } catch (TimeZoneNotFoundException) { }
            if (TimeZoneInfo.TryConvertIanaIdToWindowsId(id, out var windows))
                try { return TimeZoneInfo.FindSystemTimeZoneById(windows); } catch (TimeZoneNotFoundException) { }
            throw Invalid($"Calendar time zone '{id}' is not available on this system.");
        }
        catch (InvalidTimeZoneException exception) { throw Invalid("A calendar time zone is invalid.", exception); }
    }

    private static DateTimeOffset? ToInstant(DateTime local, TimeZoneInfo zone, bool skipInvalid)
    {
        if (zone.IsInvalidTime(local))
        {
            if (skipInvalid) return null;
            throw Invalid("A calendar date-time falls in a daylight-saving gap.");
        }
        // RFC 5545 resolves repeated wall times to the first occurrence. Choosing
        // the larger offset produces the earlier of the two UTC instants.
        var offset = zone.IsAmbiguousTime(local) ? zone.GetAmbiguousTimeOffsets(local).Max() : zone.GetUtcOffset(local);
        try { return new DateTimeOffset(local, offset); }
        catch (ArgumentException exception) { throw Invalid("A calendar date-time is outside the supported date range.", exception); }
    }

    // DATE values have no time zone under RFC 5545. The offset is only an
    // indexing aid; preserve the actual civil date even in a midnight DST gap.
    private static DateTimeOffset? DateInstant(CalendarDate date, DateTime local, bool skipInvalid) => date.AllDay
        ? new DateTimeOffset(local, date.Zone.GetUtcOffset(local))
        : ToInstant(local, date.Zone, skipInvalid);

    private static Rule ReadRule(string value, CalendarDate start)
    {
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var part in value.Split(';'))
        {
            var separator = part.IndexOf('=');
            if (separator <= 0 || separator == part.Length - 1 || !fields.TryAdd(part[..separator], part[(separator + 1)..])) throw Invalid("An RRULE is malformed or has duplicate fields.");
        }
        foreach (var field in fields.Keys)
            if (field.ToUpperInvariant() is not ("FREQ" or "INTERVAL" or "COUNT" or "UNTIL" or "BYDAY" or "WKST")) throw new NotSupportedException($"RRULE field {field} is not supported.");
        if (!fields.TryGetValue("FREQ", out var frequency)) throw Invalid("An RRULE is missing FREQ.");
        frequency = frequency.ToUpperInvariant();
        if (frequency is not ("DAILY" or "WEEKLY")) throw new NotSupportedException($"RRULE frequency {frequency} is not supported. Only DAILY and WEEKLY are available.");
        var interval = fields.TryGetValue("INTERVAL", out var intervalValue) ? PositiveInteger(intervalValue, "INTERVAL") : 1;
        int? count = fields.TryGetValue("COUNT", out var countValue) ? PositiveInteger(countValue, "COUNT") : null;
        DateTimeOffset? until = null;
        if (fields.TryGetValue("UNTIL", out var untilValue))
        {
            if (count is not null) throw Invalid("An RRULE cannot contain both COUNT and UNTIL.");
            var date = ReadDate(new("UNTIL", new(StringComparer.OrdinalIgnoreCase), untilValue), start.Zone);
            if (date.AllDay != start.AllDay) throw Invalid("RRULE UNTIL must use the same date value type as DTSTART.");
            until = DateInstant(date, date.Local, false);
        }
        var weekStart = fields.TryGetValue("WKST", out var weekStartValue) ? Weekday(weekStartValue) : DayOfWeek.Monday;
        DayOfWeek[] days = [start.Local.DayOfWeek];
        if (fields.TryGetValue("BYDAY", out var byDay))
        {
            if (frequency != "WEEKLY") throw new NotSupportedException("BYDAY is supported only for WEEKLY recurrence.");
            days = byDay.Split(',').Select(Weekday).Distinct().ToArray();
        }
        return new(frequency, interval, count, until, weekStart, days);
    }

    private static int PositiveInteger(string value, string field)
    {
        if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var number) || number <= 0) throw Invalid($"RRULE {field} must be a positive integer.");
        return number;
    }

    private static DayOfWeek Weekday(string value) => value.ToUpperInvariant() switch
    {
        "MO" => DayOfWeek.Monday, "TU" => DayOfWeek.Tuesday, "WE" => DayOfWeek.Wednesday,
        "TH" => DayOfWeek.Thursday, "FR" => DayOfWeek.Friday, "SA" => DayOfWeek.Saturday, "SU" => DayOfWeek.Sunday,
        _ => throw new NotSupportedException($"RRULE weekday '{value}' is not supported; ordinal BYDAY values are unavailable.")
    };

    private static DurationValue ReadDuration(string value, bool allDay)
    {
        if (value.Length > 100) throw Invalid("A calendar duration is too large.");
        var match = DurationPattern.Match(value);
        if (!match.Success || !new[] { "weeks", "days", "hours", "minutes", "seconds" }.Any(name => match.Groups[name].Success) || (value.Contains('T') && !new[] { "hours", "minutes", "seconds" }.Any(name => match.Groups[name].Success)) || (allDay && value.Contains('T')))
            throw Invalid("A calendar duration is invalid for its date value type.");
        try
        {
            long Unit(string name) => match.Groups[name].Success ? long.Parse(match.Groups[name].Value, CultureInfo.InvariantCulture) : 0;
            var days = checked(Unit("weeks") * 7 + Unit("days"));
            var clockTicks = checked(Unit("hours") * TimeSpan.TicksPerHour + Unit("minutes") * TimeSpan.TicksPerMinute + Unit("seconds") * TimeSpan.TicksPerSecond);
            var ticks = checked(days * TimeSpan.TicksPerDay + clockTicks);
            return new(TimeSpan.FromTicks(ticks), days, TimeSpan.FromTicks(clockTicks));
        }
        catch (OverflowException exception) { throw Invalid("A calendar duration is too large.", exception); }
    }

    private static DateTimeOffset SubtractClamped(DateTimeOffset value, TimeSpan duration) => value.UtcTicks < duration.Ticks ? DateTimeOffset.MinValue : new DateTimeOffset(value.UtcTicks - duration.Ticks, TimeSpan.Zero);
    private static DateTime? AddDays(DateTime value, long days) => days < 0 || days > (DateTime.MaxValue.Date - value.Date).Days ? null : value.AddDays(days);
    private static void CheckBudget(ref int evaluated)
    {
        if (++evaluated > MaximumOccurrences) throw Invalid("Calendar recurrence expansion exceeds the 2,000 occurrence limit. Request a smaller range or use a newer calendar export.");
    }

    private static string Unescape(string value)
    {
        var result = new StringBuilder(value.Length);
        for (var index = 0; index < value.Length; index++)
        {
            if (value[index] != '\\' || index + 1 == value.Length) { result.Append(value[index]); continue; }
            var escaped = value[++index];
            result.Append(escaped is 'n' or 'N' ? '\n' : escaped);
        }
        return result.ToString();
    }

    private static string? FindMeetingUrl(Dictionary<string, List<Property>> properties)
    {
        foreach (var name in new[] { "URL", "LOCATION", "DESCRIPTION" })
        {
            if (!properties.TryGetValue(name, out var values)) continue;
            foreach (var property in values)
                foreach (Match match in Links.Matches(Unescape(property.Value)))
                {
                    var candidate = match.Value.TrimEnd('.', ',', ';', ':', '!', '?', ')', ']', '}');
                    if (!Uri.TryCreate(candidate, UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http") || uri.UserInfo.Length != 0 || !uri.IsDefaultPort) continue;
                    var host = uri.IdnHost.TrimEnd('.');
                    if (MeetingHosts.Any(domain => host.Equals(domain, StringComparison.OrdinalIgnoreCase) || host.EndsWith('.' + domain, StringComparison.OrdinalIgnoreCase))) return uri.AbsoluteUri;
                }
        }
        return null;
    }

    private static InvalidDataException Invalid(string message, Exception? exception = null) => new(message, exception);
    private sealed record Property(string Name, Dictionary<string, string> Parameters, string Value);
    private sealed record CalendarDate(DateTime Local, TimeZoneInfo Zone, bool AllDay, bool Floating);
    private sealed record DurationValue(TimeSpan Span, long NominalDays = 0, TimeSpan Clock = default);
    private sealed record Rule(string Frequency, int Interval, int? Count, DateTimeOffset? Until, DayOfWeek WeekStart, DayOfWeek[] Days);
}
