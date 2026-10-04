namespace Notch.Core;

/// <summary>The expanded window of a calendar source, including its resolution zone.</summary>
public sealed record CalendarRange(DateTimeOffset From, DateTimeOffset Until, TimeZoneInfo Zone)
{
    public string TimeZoneId => Zone.Id;

    public bool ContainsDate(DateTime date) => ContainsDate(DateOnly.FromDateTime(date));

    public bool ContainsDate(DateOnly date) =>
        From <= CalendarDay.StartOfDay(date, Zone) && Until >= CalendarDay.StartOfDay(date.AddDays(1), Zone);

    public bool ContainsMonth(DateTime month)
    {
        var requested = ForMonth(month, Zone, 0);
        return From <= requested.From && Until >= requested.Until;
    }

    /// <summary>Creates a full-month window with optional preceding/following month buffers.</summary>
    public static CalendarRange ForMonth(DateTime month, TimeZoneInfo? zone = null, int adjacentMonths = 1)
    {
        if (adjacentMonths is < 0 or > 12) throw new ArgumentOutOfRangeException(nameof(adjacentMonths));
        zone ??= TimeZoneInfo.Local;
        var first = new DateTime(month.Year, month.Month, 1);
        var from = first.AddMonths(-adjacentMonths);
        var until = first.AddMonths(adjacentMonths + 1);
        return new(CalendarDay.StartOfDay(DateOnly.FromDateTime(from), zone), CalendarDay.StartOfDay(DateOnly.FromDateTime(until), zone), zone);
    }
}
