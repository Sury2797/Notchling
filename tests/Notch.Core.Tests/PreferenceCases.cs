using Notch.Core;

namespace Notch.Core.Tests;

internal static class PreferenceCases
{
    public static void Register(TestSuite suite)
    {
        suite.Add("Preferences clamp persisted durations and monitor indexes", () =>
        {
            var low = AppPreferences.Normalize(new() { FocusMinutes = -10, HydrationMinutes = 0, ActiveMonitor = -2 });
            Check.Equal(1, low.FocusMinutes);
            Check.Equal(5, low.HydrationMinutes);
            Check.Equal(0, low.ActiveMonitor);
            var high = AppPreferences.Normalize(new() { FocusMinutes = int.MaxValue, HydrationMinutes = int.MaxValue });
            Check.Equal(180, high.FocusMinutes);
            Check.Equal(180, high.HydrationMinutes);
        });
        suite.Add("Preferences sanitize blank and overlong city names", () =>
        {
            Check.Equal("Bengaluru", AppPreferences.Normalize(new() { WeatherCity = " \t " }).WeatherCity);
            Check.Equal("Pune", AppPreferences.Normalize(new() { WeatherCity = "  Pune  " }).WeatherCity);
            Check.Equal(80, AppPreferences.Normalize(new() { WeatherCity = new string('A', 200) }).WeatherCity.Length);
        });
        suite.Add("Preferences preserve valid explicit privacy and motion choices", () =>
        {
            var input = new AppPreferences
            {
                FocusMinutes = 45, HydrationMinutes = 15, ActiveMonitor = 2,
                CaptureClipboard = false, ReducedMotion = true, Pinned = true,
                DemoMode = false, HoverNavigation = false, WeatherCity = "Berlin",
            };
            Check.Equal(input, AppPreferences.Normalize(input));
            Check.False(AppPreferences.Normalize(input).CaptureClipboard);
        });
    }
}
