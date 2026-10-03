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
    }
}
