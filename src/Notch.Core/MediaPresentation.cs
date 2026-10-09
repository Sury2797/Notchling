using System.Globalization;
using System.Text;

namespace Notch.Core;

/// <summary>Small presentation rules shared by compact and expanded native media surfaces.</summary>
public static class MediaPresentation
{
    public static string CleanMetadata(string? value, int maximumLength = 512)
    {
        if (string.IsNullOrWhiteSpace(value) || maximumLength <= 0) return string.Empty;
        var text = new StringBuilder(Math.Min(value.Length, maximumLength));
        var space = false;
        foreach (var rune in value.EnumerateRunes())
        {
            if (Rune.IsWhiteSpace(rune) || Rune.GetUnicodeCategory(rune) == UnicodeCategory.Control)
            {
                space = text.Length > 0;
                continue;
            }
            if (text.Length + rune.Utf16SequenceLength + (space ? 1 : 0) > maximumLength) break;
            if (space) text.Append(' ');
            text.Append(rune.ToString());
            space = false;
        }
        return text.ToString();
    }

    // Use an OS display name whenever available. Unknown package IDs are not product names;
    // guessing from a substring could mislabel an unrelated application.
    public static string SourceLabel(string? source, string? displayName = null)
    {
        var name = CleanMetadata(displayName, 80);
        if (name.Length > 0) return name;
        var id = source?.Trim() ?? string.Empty;
        if (id.Equals("Chrome", StringComparison.OrdinalIgnoreCase) || id.StartsWith("Chrome.", StringComparison.OrdinalIgnoreCase)
            || id.Equals("chrome.exe", StringComparison.OrdinalIgnoreCase)) return "Google Chrome";
        if (id.Equals("MSEdge", StringComparison.OrdinalIgnoreCase) || id.StartsWith("MSEdge.", StringComparison.OrdinalIgnoreCase)
            || id.Equals("msedge.exe", StringComparison.OrdinalIgnoreCase) || id.StartsWith("Microsoft.MicrosoftEdge_", StringComparison.OrdinalIgnoreCase)) return "Microsoft Edge";
        if (id.Equals("Spotify", StringComparison.OrdinalIgnoreCase) || id.Equals("Spotify.exe", StringComparison.OrdinalIgnoreCase)
            || id.StartsWith("SpotifyAB.SpotifyMusic_", StringComparison.OrdinalIgnoreCase)) return "Spotify";
        if (id.Equals("Firefox", StringComparison.OrdinalIgnoreCase) || id.Equals("firefox.exe", StringComparison.OrdinalIgnoreCase)
            || id.StartsWith("Mozilla.Firefox_", StringComparison.OrdinalIgnoreCase)) return "Firefox";
        if (id.StartsWith("Microsoft.ZuneMusic_", StringComparison.OrdinalIgnoreCase)) return "Media Player";
        if (id.StartsWith("AppleInc.AppleMusic_", StringComparison.OrdinalIgnoreCase)) return "Apple Music";
        return "Connected player";
    }

    public static string Title(MediaSnapshot? media) => media is null ? "Nothing playing"
        : string.IsNullOrWhiteSpace(media.Title) ? "Untitled media" : media.Title;

    public static string Clock(TimeSpan position)
    {
        position = position < TimeSpan.Zero ? TimeSpan.Zero : position;
        return position.TotalHours >= 1
            ? $"{(long)position.TotalHours}:{position.Minutes:00}:{position.Seconds:00}"
            : $"{(long)position.TotalMinutes}:{position.Seconds:00}";
    }
}
