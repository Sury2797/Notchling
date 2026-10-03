using Notch.Core;

namespace Notch.Core.Tests;

internal static class ConverterCases
{
    public static void Register(TestSuite suite)
    {
        suite.Add("Unit conversion handles real length, mass and decimal/binary data examples", () =>
        {
            Check.Near(30.48, UnitConverter.Convert(1, "ft", "cm"));
            Check.Near(1609.344, UnitConverter.Convert(1, "mi", "m"));
            Check.Near(453.59237, UnitConverter.Convert(1, "lb", "g"));
            Check.Near(1000, UnitConverter.Convert(1, "MB", "KB"));
            Check.Near(1024, UnitConverter.Convert(1, "MiB", "KiB"));
            Check.Near(1.048576, UnitConverter.Convert(1, "MiB", "MB"));
        });
        suite.Add("Temperature conversions use offsets and reject below absolute zero", () =>
        {
            Check.Near(32, UnitConverter.Convert(0, "°C", "°F"));
            Check.Near(100, UnitConverter.Convert(212, "°F", "°C"));
            Check.Near(0, UnitConverter.Convert(-459.67, "°F", "K"));
            Check.Throws<ArgumentOutOfRangeException>(() => UnitConverter.Convert(-274, "°C", "K"));
            Check.Throws<ArgumentOutOfRangeException>(() => UnitConverter.Convert(-1, "K", "°C"));
        });
        suite.Add("Invalid conversion inputs fail rather than fabricate a number", () =>
        {
            Check.Throws<ArgumentException>(() => UnitConverter.Convert(1, "m", "kg"));
            Check.Throws<ArgumentException>(() => UnitConverter.Convert(1, "unknown", "m"));
            Check.Throws<ArgumentOutOfRangeException>(() => UnitConverter.Convert(double.NaN, "m", "km"));
            Check.Throws<ArgumentOutOfRangeException>(() => UnitConverter.Convert(double.PositiveInfinity, "m", "km"));
            Check.Throws<OverflowException>(() => UnitConverter.Convert(double.MaxValue, "km", "mm"));
        });
    }
}
