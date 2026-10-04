using Notch.Core.Providers;

namespace Notch.Core.Tests;

internal static class CalendarCases
{
    private static readonly DateTimeOffset October = new(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);
    private static string Calendar(params string[] events) => "BEGIN:VCALENDAR\r\nVERSION:2.0\r\n" + string.Join("\r\n", events) + "\r\nEND:VCALENDAR";
    private static string Event(string fields) => "BEGIN:VEVENT\r\n" + fields.Replace("\n", "\r\n", StringComparison.Ordinal) + "\r\nEND:VEVENT";

    public static void Register(TestSuite suite)
    {
        suite.Add("Calendar unfolds and unescapes text without taking nested alarm fields", () =>
        {
            var text = Calendar(Event("DTSTART:20261003T090000Z\nDTEND:20261003T100000Z\nSUMMARY:Design\\, work and\n review\\nNext step\nDESCRIPTION:Join https://meet.google.com/abc-defg-hij\nBEGIN:VALARM\nSUMMARY:Alarm title\nEND:VALARM"));
            var result = IcsCalendar.Parse(text, October, October.AddMonths(1));
            Check.Equal(1, result.Count);
            Check.Equal("Design, work andreview\nNext step", result[0].Title);
            Check.Equal("https://meet.google.com/abc-defg-hij", result[0].MeetingUrl);
            Check.Equal(TimeSpan.FromHours(1), result[0].End - result[0].Start);
        });
        suite.Add("Calendar expands weekly BYDAY recurrence and applies exclusions", () =>
        {
            var text = Calendar(Event("DTSTART:20261005T090000Z\nDURATION:PT30M\nSUMMARY:Standup\nRRULE:FREQ=WEEKLY;BYDAY=MO,WE;COUNT=5\nEXDATE:20261007T090000Z"));
            var result = IcsCalendar.Parse(text, October, October.AddMonths(1));
            Check.Equal(4, result.Count);
            Check.Equal(5, result[0].Start.Day);
            Check.Equal(12, result[1].Start.Day);
            Check.Equal(19, result[3].Start.Day);
        });
        suite.Add("Daily calendar recurrence keeps wall-clock time across daylight saving", () =>
        {
            var from = new DateTimeOffset(2026, 3, 6, 0, 0, 0, TimeSpan.Zero);
            var text = Calendar(Event("DTSTART;TZID=America/New_York:20260307T090000\nDURATION:PT30M\nSUMMARY:Morning\nRRULE:FREQ=DAILY;COUNT=3"));
            var result = IcsCalendar.Parse(text, from, from.AddDays(5));
            Check.Equal(3, result.Count);
            Check.Equal(9, result[0].Start.Hour);
            Check.Equal(9, result[1].Start.Hour);
            Check.Equal(TimeSpan.FromHours(-5), result[0].Start.Offset);
            Check.Equal(TimeSpan.FromHours(-4), result[1].Start.Offset);
            Check.Equal(TimeSpan.FromHours(23), result[1].Start - result[0].Start);
        });
        suite.Add("Calendar skips nonexistent DST times and resolves repeated times to first occurrence", () =>
        {
            var spring = Calendar(Event("DTSTART;TZID=America/New_York:20260307T023000\nDURATION:PT30M\nRRULE:FREQ=DAILY;COUNT=2"));
            var springFrom = new DateTimeOffset(2026, 3, 6, 0, 0, 0, TimeSpan.Zero);
            var springResult = IcsCalendar.Parse(spring, springFrom, springFrom.AddDays(6));
            Check.Equal(2, springResult.Count);
            Check.Equal(9, springResult[1].Start.Day);
            var autumn = Calendar(Event("DTSTART;TZID=America/New_York:20261101T013000\nDURATION:PT30M"));
            var autumnFrom = new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);
            Check.Equal(TimeSpan.FromHours(-4), IcsCalendar.Parse(autumn, autumnFrom, autumnFrom.AddDays(2))[0].Start.Offset);
        });
        suite.Add("Calendar excludes cancelled events and respects an exclusive range end", () =>
        {
            var text = Calendar(
                Event("DTSTART:20261003T100000Z\nSUMMARY:Later"),
                Event("DTSTART:20261003T090000Z\nSUMMARY:Cancelled\nSTATUS:CANCELLED"),
                Event("DTSTART;VALUE=DATE:20261002\nDTEND;VALUE=DATE:20261004\nSUMMARY:All day"));
            var until = new DateTimeOffset(2026, 10, 3, 10, 0, 0, TimeSpan.Zero);
            var result = IcsCalendar.Parse(text, October, until);
            Check.Equal(1, result.Count);
            Check.Equal("All day", result[0].Title);
            Check.Equal(TimeSpan.FromDays(2), result[0].End - result[0].Start);
        });
        suite.Add("Unsupported or malformed calendars fail explicitly", () =>
        {
            Check.Throws<NotSupportedException>(() => IcsCalendar.Parse(Calendar(Event("DTSTART:20261003T090000Z\nRRULE:FREQ=MONTHLY")), October, October.AddMonths(1)));
            Check.Throws<InvalidDataException>(() => IcsCalendar.Parse("BEGIN:VCALENDAR\nBEGIN:VEVENT\nDTSTART:20261003T090000Z\nEND:VCALENDAR", October, October.AddMonths(1)));
            Check.Throws<InvalidDataException>(() => IcsCalendar.Parse(Calendar(Event("DTSTART:20261003T090000Z\nDTEND:20261002T090000Z")), October, October.AddMonths(1)));
            Check.Throws<InvalidDataException>(() => IcsCalendar.Parse(Calendar(Event("DTSTART:20261003T090000Z\nRRULE:FREQ=DAILY;COUNT=3;UNTIL=20261010T090000Z")), October, October.AddMonths(1)));
        });
        suite.Add("Calendar meeting links cannot disguise an unrelated domain", () =>
        {
            var text = Calendar(Event("DTSTART:20261003T090000Z\nURL:https://meet.google.com.attacker.test/fake"));
            Check.True(IcsCalendar.Parse(text, October, October.AddMonths(1))[0].MeetingUrl is null);
        });
        suite.Add("Calendar nominal-day durations differ from elapsed 24 hours across DST", () =>
        {
            var from = new DateTimeOffset(2026, 3, 6, 0, 0, 0, TimeSpan.Zero);
            var nominal = Calendar(Event("DTSTART;TZID=America/New_York:20260307T120000\nDURATION:P1D"));
            var elapsed = Calendar(Event("DTSTART;TZID=America/New_York:20260307T120000\nDURATION:PT24H"));
            var nominalEvent = IcsCalendar.Parse(nominal, from, from.AddDays(5))[0];
            var elapsedEvent = IcsCalendar.Parse(elapsed, from, from.AddDays(5))[0];
            Check.Equal(TimeSpan.FromHours(23), nominalEvent.End - nominalEvent.Start);
            Check.Equal(TimeSpan.FromHours(24), elapsedEvent.End - elapsedEvent.Start);
            Check.Equal(12, nominalEvent.End.Hour);
            Check.Equal(13, elapsedEvent.End.Hour);
        });
        suite.Add("Calendar recognizes Windows zone names and jumps near old unbounded recurrence", () =>
        {
            var zone = Calendar(Event("DTSTART;TZID=Eastern Standard Time:20261003T090000\nDURATION:PT30M"));
            Check.Equal(TimeSpan.FromHours(-4), IcsCalendar.Parse(zone, October, October.AddMonths(1))[0].Start.Offset);
            var ancient = Calendar(Event("DTSTART:19000101T090000Z\nRRULE:FREQ=DAILY\nSUMMARY:Daily"));
            Check.Equal(2, IcsCalendar.Parse(ancient, October, October.AddDays(2)).Count);
        });
        suite.Add("All-day calendar dates remain the same in positive and negative offsets", () =>
        {
            var text = Calendar(Event("DTSTART;VALUE=DATE:20261003\nDTEND;VALUE=DATE:20261005\nSUMMARY:Holiday"));
            var zones = new[]
            {
                TimeZoneInfo.CreateCustomTimeZone("Test/+14", TimeSpan.FromHours(14), "+14", "+14"),
                TimeZoneInfo.CreateCustomTimeZone("Test/-7", TimeSpan.FromHours(-7), "-7", "-7")
            };
            foreach (var zone in zones)
            {
                var item = IcsCalendar.Parse(text, October.AddDays(-1), October.AddDays(10), zone)[0];
                Check.True(item.IsAllDay);
                Check.False(item.IsFloating);
                Check.Equal(new DateOnly(2026, 10, 3), DateOnly.FromDateTime(item.Start.DateTime));
                Check.True(CalendarDay.Overlaps(item, new DateOnly(2026, 10, 3), zones[0]));
                Check.True(CalendarDay.Overlaps(item, new DateOnly(2026, 10, 4), zones[1]));
                Check.False(CalendarDay.Overlaps(item, new DateOnly(2026, 10, 2), zone));
                Check.False(CalendarDay.Overlaps(item, new DateOnly(2026, 10, 5), zone));
                Check.Equal("All day", CalendarDay.TimeLabel(item, new DateOnly(2026, 10, 3), zone));
            }
        });
        suite.Add("Floating appointments resolve against the explicit calendar zone", () =>
        {
            var zone = TimeZoneInfo.CreateCustomTimeZone("Test/+0530", TimeSpan.FromMinutes(330), "+0530", "+0530");
            var text = Calendar(
                Event("DTSTART:20261003T090000\nDTEND:20261003T100000\nSUMMARY:Floating"),
                Event("DTSTART:20261003T090000Z\nSUMMARY:UTC"),
                Event("DTSTART;TZID=America/New_York:20261003T090000\nSUMMARY:Named zone"));
            var result = IcsCalendar.Parse(text, October, October.AddMonths(1), zone);
            var floating = result.Single(item => item.Title == "Floating");
            Check.True(floating.IsFloating);
            Check.Equal(9, floating.Start.Hour);
            Check.Equal(TimeSpan.FromMinutes(330), floating.Start.Offset);
            Check.Equal(TimeSpan.FromHours(1), floating.End - floating.Start);
            Check.Equal(zone.Id, floating.TimeZoneId);
            Check.False(result.Single(item => item.Title == "UTC").IsFloating);
            Check.Equal(TimeSpan.Zero, result.Single(item => item.Title == "UTC").Start.Offset);
            Check.Equal(TimeSpan.FromHours(-4), result.Single(item => item.Title == "Named zone").Start.Offset);
        });
        suite.Add("Calendar day selection includes overnight continuations and excludes exclusive endpoints", () =>
        {
            var zone = TimeZoneInfo.CreateCustomTimeZone("Test/-7", TimeSpan.FromHours(-7), "-7", "-7");
            var text = Calendar(
                Event("DTSTART:20261003T230000\nDTEND:20261004T020000\nSUMMARY:Overnight"),
                Event("DTSTART:20261003T230000\nDTEND:20261004T000000\nSUMMARY:Ends at midnight"),
                Event("DTSTART:20261004T000000\nSUMMARY:Point appointment"));
            var result = IcsCalendar.Parse(text, October, October.AddMonths(1), zone);
            var continuation = CalendarDay.EventsForDay(result, new DateOnly(2026, 10, 4), zone);
            Check.Equal(2, continuation.Count);
            Check.True(continuation.Any(item => item.Title == "Overnight"));
            Check.True(continuation.Any(item => item.Title == "Point appointment"));
            Check.False(continuation.Any(item => item.Title == "Ends at midnight"));
            Check.True(CalendarDay.TimeLabel(result.Single(item => item.Title == "Overnight"), new DateOnly(2026, 10, 4), zone, System.Globalization.CultureInfo.InvariantCulture).StartsWith("00:00", StringComparison.Ordinal));
        });
        suite.Add("All-day default duration and explicit date ends preserve civil days across DST", () =>
        {
            var zone = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
            var from = new DateTimeOffset(2026, 3, 6, 0, 0, 0, TimeSpan.Zero);
            var text = Calendar(
                Event("DTSTART;VALUE=DATE:20260308\nRRULE:FREQ=DAILY;COUNT=2\nSUMMARY:One day"),
                Event("DTSTART;VALUE=DATE:20260307\nDTEND;VALUE=DATE:20260309\nRRULE:FREQ=DAILY;COUNT=2\nSUMMARY:Two days"));
            var result = IcsCalendar.Parse(text, from, from.AddDays(7), zone);
            var oneDay = result.Where(item => item.Title == "One day").ToArray();
            Check.Equal(TimeSpan.FromHours(23), oneDay[0].End - oneDay[0].Start);
            Check.Equal(TimeSpan.FromHours(24), oneDay[1].End - oneDay[1].Start);
            foreach (var item in result.Where(item => item.Title == "Two days"))
                Check.Equal(2, (item.End.Date - item.Start.Date).Days);
            Check.False(CalendarDay.Overlaps(oneDay[0], new DateOnly(2026, 3, 9), zone));
        });
        suite.Add("Floating daily recurrence keeps local time when the calendar zone changes offset", () =>
        {
            var zone = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
            var from = new DateTimeOffset(2026, 3, 6, 0, 0, 0, TimeSpan.Zero);
            var text = Calendar(Event("DTSTART:20260307T090000\nDURATION:PT30M\nRRULE:FREQ=DAILY;COUNT=3"));
            var result = IcsCalendar.Parse(text, from, from.AddDays(5), zone);
            Check.Equal(3, result.Count);
            Check.True(result.All(item => item.IsFloating && item.Start.Hour == 9));
            Check.Equal(TimeSpan.FromHours(23), result[1].Start - result[0].Start);
        });
        suite.Add("Calendar ranges track loaded days and support future re-expansion", () =>
        {
            var text = Calendar(Event("DTSTART:20270403T090000Z\nSUMMARY:Six months ahead"));
            var initial = CalendarRange.ForMonth(new DateTime(2026, 10, 3), TimeZoneInfo.Utc);
            Check.True(initial.ContainsMonth(new DateTime(2026, 10, 1)));
            Check.True(initial.ContainsDate(new DateTime(2026, 11, 30)));
            Check.False(initial.ContainsDate(new DateTime(2026, 12, 1)));
            Check.False(initial.ContainsMonth(new DateTime(2027, 4, 1)));
            Check.Equal(0, IcsCalendar.Parse(text, initial.From, initial.Until, initial.Zone).Count);
            var future = CalendarRange.ForMonth(new DateTime(2027, 4, 1), TimeZoneInfo.Utc);
            Check.Equal(1, IcsCalendar.Parse(text, future.From, future.Until, future.Zone).Count);
        });
        suite.Add("Calendar day boundaries use actual 23-hour and 25-hour days", () =>
        {
            var zone = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
            var spring = CalendarDay.StartOfDay(new DateOnly(2026, 3, 8), zone);
            var springEnd = CalendarDay.StartOfDay(new DateOnly(2026, 3, 9), zone);
            var autumn = CalendarDay.StartOfDay(new DateOnly(2026, 11, 1), zone);
            var autumnEnd = CalendarDay.StartOfDay(new DateOnly(2026, 11, 2), zone);
            Check.Equal(TimeSpan.FromHours(23), springEnd - spring);
            Check.Equal(TimeSpan.FromHours(25), autumnEnd - autumn);
        });
        suite.Add("All-day dates survive a midnight daylight-saving gap", () =>
        {
            var zone = TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");
            var from = new DateTimeOffset(2018, 11, 2, 0, 0, 0, TimeSpan.Zero);
            var text = Calendar(Event("DTSTART;VALUE=DATE:20181104\nSUMMARY:Holiday"));
            var item = IcsCalendar.Parse(text, from, from.AddDays(5), zone)[0];
            Check.Equal(new DateOnly(2018, 11, 4), DateOnly.FromDateTime(item.Start.DateTime));
            Check.Equal(new DateOnly(2018, 11, 5), DateOnly.FromDateTime(item.End.DateTime));
            Check.True(CalendarDay.Overlaps(item, new DateOnly(2018, 11, 4), zone));
            Check.Equal(1, CalendarDay.StartOfDay(new DateOnly(2018, 11, 4), zone).Hour);
        });
        suite.Add("All-day recurrence excludes dates without losing their date-only semantics", () =>
        {
            var zone = TimeZoneInfo.CreateCustomTimeZone("Test/+14", TimeSpan.FromHours(14), "+14", "+14");
            var text = Calendar(Event("DTSTART;VALUE=DATE:20261003\nRRULE:FREQ=DAILY;COUNT=3\nEXDATE;VALUE=DATE:20261004"));
            var result = IcsCalendar.Parse(text, October, October.AddDays(10), zone);
            Check.Equal(2, result.Count);
            Check.Equal(3, result[0].Start.Day);
            Check.Equal(5, result[1].Start.Day);
            Check.True(result.All(item => item.IsAllDay));
        });
    }
}
