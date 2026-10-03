namespace Notch.Core;

public sealed record UnitDefinition(string Symbol, string Category, double Factor);
public static class UnitConverter
{
    public static IReadOnlyList<UnitDefinition> Units { get; } = [
        new("m", "Length", 1), new("km", "Length", 1000), new("cm", "Length", 0.01), new("mm", "Length", 0.001), new("in", "Length", 0.0254), new("ft", "Length", 0.3048), new("mi", "Length", 1609.344),
        new("kg", "Mass", 1), new("g", "Mass", 0.001), new("lb", "Mass", 0.45359237), new("oz", "Mass", 0.028349523125),
        new("°C", "Temperature", 1), new("°F", "Temperature", 1), new("K", "Temperature", 1),
        new("B", "Data", 1), new("KB", "Data", 1000), new("MB", "Data", 1000000), new("GB", "Data", 1000000000), new("KiB", "Data", 1024), new("MiB", "Data", 1048576), new("GiB", "Data", 1073741824),
    ];
    public static double Convert(double value, string from, string to)
    {
        if (!double.IsFinite(value)) throw new ArgumentOutOfRangeException(nameof(value));
        var source = Units.FirstOrDefault(unit => unit.Symbol == from) ?? throw new ArgumentException("Unknown input unit.", nameof(from));
        var target = Units.FirstOrDefault(unit => unit.Symbol == to) ?? throw new ArgumentException("Unknown output unit.", nameof(to));
        if (source.Category != target.Category) throw new ArgumentException("Choose units from the same category.");
        if (source.Category == "Temperature")
        {
            var kelvin = from switch { "°C" => value + 273.15, "°F" => (value - 32) * 5 / 9 + 273.15, _ => value };
            if (kelvin < -1e-10) throw new ArgumentOutOfRangeException(nameof(value), "Temperature cannot be below absolute zero.");
            return to switch { "°C" => kelvin - 273.15, "°F" => (kelvin - 273.15) * 9 / 5 + 32, _ => kelvin };
        }
        var result = value * source.Factor / target.Factor;
        if (!double.IsFinite(result)) throw new OverflowException("The result exceeds the supported numeric range.");
        return result;
    }
}
