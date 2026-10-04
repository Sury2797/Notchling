using System.Globalization;

namespace Notch.Core;

/// <summary>Calendar-date presentation independent of the device's UTC offset.</summary>
public static class CalendarDay
{
    public static bool Overlaps(CalendarEvent item, DateOnly day, TimeZoneInfo? displayZone = null)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (item.IsAllDay)
            return DateOnly.FromDateTime(item.Start.DateTime) <= day && day < DateOnly.FromDateTime(item.End.DateTime);

        var from = StartOfDay(day, displayZone);
        var until = StartOfDay(day.AddDays(1), displayZone);
        return item.Start < until && (item.End > from || (item.End == item.Start && item.Start >= from));
    }

    public static IReadOnlyList<CalendarEvent> EventsForDay(IEnumerable<CalendarEvent> events, DateOnly day, TimeZoneInfo? displayZone = null)
    {
        ArgumentNullException.ThrowIfNull(events);
        return events.Where(item => Overlaps(item, day, displayZone))
            .OrderByDescending(item => item.IsAllDay).ThenBy(item => item.Start)
            .ThenBy(item => item.Title, StringComparer.Ordinal).ToArray();
    }

    /// <summary>Labels an event's portion of a day, including overnight continuations.</summary>
    public static string TimeLabel(CalendarEvent item, DateOnly day, TimeZoneInfo? displayZone = null, CultureInfo? culture = null)
    {
        if (item.IsAllDay) return "All day";
        displayZone ??= TimeZoneInfo.Local;
        culture ??= CultureInfo.CurrentCulture;
        var from = StartOfDay(day, displayZone);
        var until = StartOfDay(day.AddDays(1), displayZone);
        var start = TimeZoneInfo.ConvertTime(item.Start < from ? from : item.Start, displayZone);
        var end = TimeZoneInfo.ConvertTime(item.End > until ? until : item.End, displayZone);
        if (item.Start == item.End) return start.ToString("t", culture);
        return $"{start.ToString("t", culture)} – {(item.End >= until ? "24:00" : end.ToString("t", culture))}";
    }

    /// <summary>
    /// Resolves the beginning of a civil day. Some zones advance the clock at
    /// midnight; their first valid time is used rather than an invalid offset.
    /// Repeated midnights resolve to their first occurrence.
    /// </summary>
    public static DateTimeOffset StartOfDay(DateOnly day, TimeZoneInfo? zone = null)
    {
        zone ??= TimeZoneInfo.Local;
        var local = day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        while (zone.IsInvalidTime(local)) local = local.AddMinutes(1);
        var offset = zone.IsAmbiguousTime(local) ? zone.GetAmbiguousTimeOffsets(local).Max() : zone.GetUtcOffset(local);
        return new DateTimeOffset(local, offset);
    }
}
