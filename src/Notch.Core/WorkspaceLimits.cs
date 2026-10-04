using System.Text.Json;

namespace Notch.Core;

public static class WorkspaceLimits
{
    public const int MaximumTextLength = 500_000;
    public const int MaximumTitleLength = 80;
    private static readonly JsonSerializerOptions StorageOptions = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };

    public static void RequireText(string? value, int maximum, string label)
    {
        if ((value?.Length ?? 0) > maximum)
            throw new ArgumentException($"{label} exceeds {maximum:N0} characters. Your full draft is retained; shorten it or export it before saving.");
    }

    public static void RequireStorageBudget<T>(T snapshot)
    {
        if (JsonSerializer.SerializeToUtf8Bytes(snapshot, StorageOptions).LongLength > LocalStore.MaximumBytes)
            throw new InvalidDataException("This edit would exceed the 10 MB notebook limit. Your draft and previous saved data are preserved. Export or shorten the content before saving.");
    }

    public static long Measure<T>(T snapshot) => JsonSerializer.SerializeToUtf8Bytes(snapshot, StorageOptions).LongLength;
    public static void RequireScalarBudget(long bytesExcludingValue, string value)
    {
        // Six JSON bytes per UTF-16 code unit bounds escaping without serializing normal keystrokes.
        if (bytesExcludingValue + (long)value.Length * 6 <= LocalStore.MaximumBytes) return;
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value, StorageOptions).LongLength - 2;
        if (bytesExcludingValue + bytes > LocalStore.MaximumBytes)
            throw new InvalidDataException("This edit would exceed the 10 MB notebook limit. Your full draft is retained for export.");
    }
    public static byte[] SerializeExport<T>(T snapshot) => JsonSerializer.SerializeToUtf8Bytes(snapshot, StorageOptions);
}
