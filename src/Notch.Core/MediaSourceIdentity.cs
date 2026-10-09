namespace Notch.Core;

public enum MediaSourceBrand
{
    Automatic, Unknown, YouTube, YouTubeMusic, Spotify, Chrome, Edge, Firefox, Vlc, AppleMusic, MediaPlayer
}

/// <summary>An explicit, temporary provider choice, bounded to the exact active track and native session.</summary>
public sealed record MediaSourceSelection(MediaSourceBrand Brand, string Source, string Title, string Artist,
    string? AlbumTitle, long SessionRevision);

public sealed record MediaSourcePresentation(MediaSourceBrand Brand, string Label, bool IsBrowser,
    bool IsUserSelected, string Tooltip);

/// <summary>
/// Resolves identities supplied by Windows. A browser session does not expose its tab URL;
/// never infer a website from a title, artist, thumbnail, or a generic Chrome/Edge session.
/// </summary>
public static class MediaSourceIdentity
{
    public static MediaSourcePresentation Resolve(MediaSnapshot? media, MediaSourceSelection? selection = null)
    {
        if (media is null) return new(MediaSourceBrand.Unknown, "No active player", false, false, "No active Windows media session.");
        var nativeBrand = NativeBrand(media);
        var browser = BrowserBrand(media.Source) is MediaSourceBrand.Chrome or MediaSourceBrand.Edge or MediaSourceBrand.Firefox;
        if (CanSelectProvider(media) && selection is not null && Matches(media, selection) && Selectable(selection.Brand))
        {
            var label = Label(selection.Brand);
            return new(selection.Brand, label, browser, true, $"{label} — selected for this track. Windows identifies the browser; this provider choice is yours.");
        }
        var sourceLabel = nativeBrand == MediaSourceBrand.Unknown
            ? MediaPresentation.SourceLabel(media.Source, media.SourceDisplayName) : Label(nativeBrand);
        var genericBrowser = IsBrowserBrand(nativeBrand);
        return new(nativeBrand, sourceLabel, browser || genericBrowser, false, genericBrowser
            ? $"{sourceLabel} — Windows identifies the browser, not its website. Choose a provider for this track if needed."
            : $"{sourceLabel} — identity supplied by Windows media controls.");
    }

    public static bool CanSelectProvider(MediaSnapshot? media) => media is not null && IsBrowserBrand(NativeBrand(media));

    public static MediaSourceSelection? SelectForTrack(MediaSnapshot? media, MediaSourceBrand brand) =>
        media is not null && CanSelectProvider(media) && Selectable(brand)
            ? new(brand, media.Source, media.Title, media.Artist, media.AlbumTitle, media.SessionRevision) : null;

    public static bool Matches(MediaSnapshot? media, MediaSourceSelection? selection) => media is not null && selection is not null
        && media.SessionRevision == selection.SessionRevision && media.Source == selection.Source
        && media.Title == selection.Title && media.Artist == selection.Artist && media.AlbumTitle == selection.AlbumTitle;

    public static string Label(MediaSourceBrand brand) => brand switch
    {
        MediaSourceBrand.YouTube => "YouTube", MediaSourceBrand.YouTubeMusic => "YouTube Music",
        MediaSourceBrand.Spotify => "Spotify", MediaSourceBrand.Chrome => "Google Chrome",
        MediaSourceBrand.Edge => "Microsoft Edge", MediaSourceBrand.Firefox => "Firefox",
        MediaSourceBrand.Vlc => "VLC", MediaSourceBrand.AppleMusic => "Apple Music",
        MediaSourceBrand.MediaPlayer => "Media Player", MediaSourceBrand.Automatic => "Automatic",
        _ => "Connected player"
    };

    private static bool Selectable(MediaSourceBrand brand) => brand is MediaSourceBrand.YouTube or MediaSourceBrand.YouTubeMusic
        or MediaSourceBrand.Spotify or MediaSourceBrand.Vlc or MediaSourceBrand.AppleMusic or MediaSourceBrand.MediaPlayer;

    private static bool IsBrowserBrand(MediaSourceBrand brand) => brand is MediaSourceBrand.Chrome or MediaSourceBrand.Edge or MediaSourceBrand.Firefox;

    private static MediaSourceBrand NativeBrand(MediaSnapshot media)
    {
        // Exact OS display names can identify an installed/PWA player. Track text is deliberately ignored.
        var name = MediaPresentation.CleanMetadata(media.SourceDisplayName, 80);
        var namedBrand = DisplayNameBrand(name);
        if (namedBrand != MediaSourceBrand.Unknown && !IsBrowserBrand(namedBrand)) return namedBrand;
        var source = media.Source?.Trim() ?? string.Empty;
        if (Exact(source, "YouTube", "YouTube.exe")) return MediaSourceBrand.YouTube;
        if (Exact(source, "YouTubeMusic", "YouTubeMusic.exe")) return MediaSourceBrand.YouTubeMusic;
        if (Exact(source, "Spotify", "Spotify.exe") || source.StartsWith("SpotifyAB.SpotifyMusic_", StringComparison.OrdinalIgnoreCase)) return MediaSourceBrand.Spotify;
        if (Exact(source, "VLC", "vlc.exe") || source.StartsWith("VideoLAN.VLC_", StringComparison.OrdinalIgnoreCase)) return MediaSourceBrand.Vlc;
        if (source.StartsWith("AppleInc.AppleMusic_", StringComparison.OrdinalIgnoreCase) || Exact(source, "Apple Music", "AppleMusic.exe")) return MediaSourceBrand.AppleMusic;
        if (source.StartsWith("Microsoft.ZuneMusic_", StringComparison.OrdinalIgnoreCase) || Exact(source, "Microsoft.MediaPlayer", "Microsoft.MediaPlayer.exe")) return MediaSourceBrand.MediaPlayer;
        var browser = BrowserBrand(source);
        // A distinct Windows-registered PWA name/logo should not be replaced with
        // the host browser's mark. An unrecognized installed player keeps its OS logo.
        if (browser != MediaSourceBrand.Unknown) return name.Length > 0 && namedBrand == MediaSourceBrand.Unknown
            ? MediaSourceBrand.Unknown : browser;
        return namedBrand;
    }

    private static MediaSourceBrand DisplayNameBrand(string name)
    {
        if (Exact(name, "YouTube")) return MediaSourceBrand.YouTube;
        if (Exact(name, "YouTube Music")) return MediaSourceBrand.YouTubeMusic;
        if (Exact(name, "Spotify")) return MediaSourceBrand.Spotify;
        if (Exact(name, "VLC", "VLC media player")) return MediaSourceBrand.Vlc;
        if (Exact(name, "Apple Music")) return MediaSourceBrand.AppleMusic;
        if (Exact(name, "Media Player", "Windows Media Player")) return MediaSourceBrand.MediaPlayer;
        if (Exact(name, "Google Chrome", "Chrome")) return MediaSourceBrand.Chrome;
        if (Exact(name, "Microsoft Edge")) return MediaSourceBrand.Edge;
        if (Exact(name, "Firefox", "Mozilla Firefox")) return MediaSourceBrand.Firefox;
        return MediaSourceBrand.Unknown;
    }

    private static MediaSourceBrand BrowserBrand(string? source)
    {
        var id = source?.Trim() ?? string.Empty;
        if (Exact(id, "Chrome", "chrome.exe") || id.StartsWith("Chrome.", StringComparison.OrdinalIgnoreCase)) return MediaSourceBrand.Chrome;
        if (Exact(id, "MSEdge", "msedge.exe") || id.StartsWith("MSEdge.", StringComparison.OrdinalIgnoreCase)
            || id.StartsWith("Microsoft.MicrosoftEdge_", StringComparison.OrdinalIgnoreCase)) return MediaSourceBrand.Edge;
        if (Exact(id, "Firefox", "firefox.exe") || id.StartsWith("Mozilla.Firefox_", StringComparison.OrdinalIgnoreCase)) return MediaSourceBrand.Firefox;
        return MediaSourceBrand.Unknown;
    }

    private static bool Exact(string value, params string[] choices) => choices.Any(choice => value.Equals(choice, StringComparison.OrdinalIgnoreCase));
}
