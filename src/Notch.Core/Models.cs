namespace Notch.Core;

public enum ModuleId { Home, Media, Revenue, Analytics, Coding, Calendar, Weather, Focus, Shelf, Clipboard, Servers, System, ScreenTime, Notes, Scratchpad, Files, Links, Emoji, Sounds, Convert, Awake, Settings, Tools }
public enum OverlayMode { Collapsed, Expanded, Activity }
public enum ActivityKind { Sale, Meeting, Agent, Focus, Information }
public enum RevenueProvider { Stripe, Polar, Dodo, AdSense }
public sealed record ModuleDefinition(ModuleId Id, string Title, string Glyph, double Width, double Height, string Description);
public sealed record MediaSnapshot(string Title, string Artist, string? ArtworkPath, bool IsPlaying, TimeSpan Position, TimeSpan Duration, string Source, bool CanSeek, bool CanPlay = true, bool CanPause = true, bool CanPrevious = true, bool CanNext = true, DateTimeOffset? PositionUpdatedAt = null, double PlaybackRate = 1);
public sealed record SystemSnapshot(double CpuPercent, double MemoryPercent, int? BatteryPercent, double Volume, string OutputDevice, TimeSpan SessionScreenTime);
public sealed record ClipboardItem(Guid Id, string Text, DateTimeOffset CapturedAt);
public sealed record SavedNote(Guid Id, string Title, string Text, DateTimeOffset UpdatedAt);
public sealed record ReminderItem(Guid Id, string Title, DateTimeOffset DueAt, bool Completed);
public sealed record ShelfItem(Guid Id, string Path, DateTimeOffset AddedAt);
public sealed record SavedLink(Guid Id, string Title, string Url);
public sealed record CalendarEvent(string Title, DateTimeOffset Start, DateTimeOffset End, string? MeetingUrl, bool IsAllDay = false, bool IsFloating = false, string? TimeZoneId = null);
public sealed record RevenuePayment(string Id, string Description, decimal Amount, string Currency, DateTimeOffset CreatedAt);
public sealed record RevenueSnapshot(RevenueProvider Provider, decimal Total, string Currency, IReadOnlyList<RevenuePayment> Payments, IReadOnlyList<decimal> DailyAmounts, DateTimeOffset UpdatedAt, bool Complete = true, int RequestedDays = 0, DateTimeOffset? RangeStart = null, DateTimeOffset? RangeEnd = null);
public sealed record WeatherHour(DateTimeOffset Time, double Temperature, int Code);
public sealed record WeatherDay(DateOnly Date, double Maximum, double Minimum, int Code);
public sealed record WeatherSnapshot(string City, double Temperature, double FeelsLike, int Humidity, double Wind, int Code, IReadOnlyList<WeatherHour> Hours, IReadOnlyList<WeatherDay> Days, DateTimeOffset UpdatedAt, string? TimeZoneId = null, TimeSpan? UtcOffset = null);
public sealed record CodingDay(DateOnly Date, long InputTokens, long OutputTokens, int Sessions);
public sealed record CodingSnapshot(string Provider, long InputTokens, long OutputTokens, int Sessions, IReadOnlyList<CodingDay> Days, string SourcePath);
public sealed record AnalyticsSnapshot(string Site, int ActiveUsers, long PageViews, long NewUsers, IReadOnlyList<int> Timeline, IReadOnlyList<KeyValuePair<string, int>> Pages, DateTimeOffset UpdatedAt);
public sealed record LiveActivity(Guid Id, ActivityKind Kind, string Source, string Title, string? Detail, DateTimeOffset CreatedAt, TimeSpan Duration, string? ActionUri = null, ModuleId? Destination = null);

public sealed record AppPreferences
{
    public bool Pinned { get; init; }
    public bool ReducedMotion { get; init; }
    public bool HoverNavigation { get; init; } = true;
    public bool CaptureClipboard { get; init; }
    public bool DemoMode { get; init; }
    public int FocusMinutes { get; init; } = 25;
    public int HydrationMinutes { get; init; } = 30;
    public int ActiveMonitor { get; init; }
    public string? MonitorDeviceId { get; init; }
    public double HorizontalOffset { get; init; }
    public double TopOffset { get; init; }
    public bool HideInFullscreen { get; init; } = true;
    public string WeatherCity { get; init; } = "Bengaluru";
    public string? CalendarPath { get; init; }
    public string? CodingPath { get; init; }
    public string? AnalyticsEndpoint { get; init; }
    public string AnalyticsSite { get; init; } = "My workspace";
    public string? AdSenseAccount { get; init; }
    public static AppPreferences Normalize(AppPreferences value) => value with
    {
        FocusMinutes = Math.Clamp(value.FocusMinutes, 1, 180),
        HydrationMinutes = Math.Clamp(value.HydrationMinutes, 5, 180),
        ActiveMonitor = Math.Max(0, value.ActiveMonitor),
        HorizontalOffset = double.IsFinite(value.HorizontalOffset) ? Math.Clamp(value.HorizontalOffset, -1000, 1000) : 0,
        TopOffset = double.IsFinite(value.TopOffset) ? Math.Clamp(value.TopOffset, 0, 1000) : 0,
        WeatherCity = string.IsNullOrWhiteSpace(value.WeatherCity) ? "Bengaluru" : value.WeatherCity.Trim()[..Math.Min(80, value.WeatherCity.Trim().Length)],
    };
}

public static class ModuleCatalog
{
    public static readonly IReadOnlyList<ModuleDefinition> All = [
        new(ModuleId.Home,"Home","\uE80F",760,410,"Your whole day, one hover away."),
        new(ModuleId.Media,"Media","\uE8D6",620,292,"Control what’s playing without switching apps."),
        new(ModuleId.Revenue,"Revenue","\uE8C7",900,410,"Payments and revenue, at a glance."),
        new(ModuleId.Analytics,"Analytics","\uE9D9",900,410,"See who’s on your site right now."),
        new(ModuleId.Coding,"Coding","\uE943",900,410,"Local coding activity, without guesswork."),
        new(ModuleId.Calendar,"Calendar","\uE787",760,410,"Your schedule and reminders, one hover away."),
        new(ModuleId.Weather,"Weather","\uE9BD",760,410,"Hourly and seven-day forecasts, instantly."),
        new(ModuleId.Focus,"Focus","\uE916",790,320,"Pomodoro, countdowns, stopwatch and hydration."),
        new(ModuleId.Shelf,"Shelf","\uE7B8",760,370,"Drop it here. Pick it up later."),
        new(ModuleId.Clipboard,"Clipboard","\uE8C8",760,370,"A little more space for what you copy."),
        new(ModuleId.Servers,"Servers","\uE968",760,370,"Local development ports, in one place."),
        new(ModuleId.System,"System","\uE9D9",760,370,"A clear picture of your system."),
        new(ModuleId.ScreenTime,"Screen time","\uE121",760,370,"Time on your desktop, made visible."),
        new(ModuleId.Notes,"Notes","\uE70B",760,370,"Keep your thoughts close."),
        new(ModuleId.Scratchpad,"Scratchpad","\uE70F",760,370,"A blank space to think."),
        new(ModuleId.Files,"Files","\uE8B7",760,370,"Your frequently used files."),
        new(ModuleId.Links,"Links","\uE71B",760,370,"Useful links, right within reach."),
        new(ModuleId.Emoji,"Emoji","\uE899",620,330,"A little expression."),
        new(ModuleId.Sounds,"Sounds","\uE767",620,330,"Find a gentler background."),
        new(ModuleId.Convert,"Convert","\uE8AB",620,330,"A small, precise conversion tool."),
        new(ModuleId.Awake,"Awake","\uE7F4",620,330,"Keep the display awake when you need it."),
        new(ModuleId.Settings,"Settings","\uE713",790,470,"Make the notch your own."),
        new(ModuleId.Tools,"All tools","\uECA5",900,460,"21 tools. One small notch."),
    ];
    public static ModuleDefinition Get(ModuleId id) => All.First(module => module.Id == id);
    public static readonly ModuleId[] Toolbar = [ModuleId.Home,ModuleId.Media,ModuleId.Revenue,ModuleId.Analytics,ModuleId.Coding,ModuleId.Focus,ModuleId.Calendar,ModuleId.Weather,ModuleId.Shelf,ModuleId.Clipboard,ModuleId.Tools];
}
