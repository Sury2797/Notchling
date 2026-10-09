using Notch.Core;

namespace Notch.Core.Tests;

internal static class MediaSourceIdentityCases
{
    private static MediaSnapshot Track(string source = "Chrome", string title = "A track", string artist = "An artist") =>
        new(title, artist, null, true, TimeSpan.Zero, TimeSpan.FromMinutes(3), source, true, AlbumTitle: "An album", SessionRevision: 9);

    public static void Register(TestSuite suite)
    {
        suite.Add("A YouTube title or video thumbnail never relabels a generic browser as YouTube", () =>
        {
            var media = Track(title: "YouTube Shorts • Spotify • music.youtube.com", artist: "YouTube") with { ArtworkPath = "/cache/youtube.png" };
            var result = MediaSourceIdentity.Resolve(media);
            Check.Equal(MediaSourceBrand.Chrome, result.Brand);
            Check.True(result.IsBrowser && !result.IsUserSelected && MediaSourceIdentity.CanSelectProvider(media));
            Check.True(result.Tooltip.Contains("not its website", StringComparison.Ordinal));
        });
        suite.Add("Windows-provided installed YouTube and YouTube Music identities get their exact provider marks", () =>
        {
            Check.Equal(MediaSourceBrand.YouTube, MediaSourceIdentity.Resolve(Track() with { SourceDisplayName = "YouTube" }).Brand);
            Check.Equal(MediaSourceBrand.YouTubeMusic, MediaSourceIdentity.Resolve(Track() with { SourceDisplayName = "YouTube Music" }).Brand);
            Check.Equal(MediaSourceBrand.YouTube, MediaSourceIdentity.Resolve(Track("YouTube")).Brand);
            Check.Equal(MediaSourceBrand.Spotify, MediaSourceIdentity.Resolve(Track() with { SourceDisplayName = "Spotify" }).Brand);
            Check.False(MediaSourceIdentity.CanSelectProvider(Track() with { SourceDisplayName = "YouTube Music" }));
        });
        suite.Add("Known browser and native player identities map to clean source marks", () =>
        {
            var pairs = new (string Source, MediaSourceBrand Brand)[]
            {
                ("Chrome", MediaSourceBrand.Chrome), ("Chrome.Profile.1", MediaSourceBrand.Chrome),
                ("MSEdge", MediaSourceBrand.Edge), ("msedge.exe", MediaSourceBrand.Edge),
                ("firefox.exe", MediaSourceBrand.Firefox), ("SpotifyAB.SpotifyMusic_abc!App", MediaSourceBrand.Spotify),
                ("VLC", MediaSourceBrand.Vlc), ("AppleInc.AppleMusic_abc!App", MediaSourceBrand.AppleMusic),
                ("Microsoft.ZuneMusic_abc!App", MediaSourceBrand.MediaPlayer), ("YouTubeMusic", MediaSourceBrand.YouTubeMusic)
            };
            foreach (var pair in pairs) Check.Equal(pair.Brand, MediaSourceIdentity.Resolve(Track(pair.Source)).Brand);
        });
        suite.Add("Unknown app IDs retain their genuine OS label instead of substring provider guessing", () =>
        {
            foreach (var source in new[] { "fakechrome.app", "mySpotify.exe", "malicious.youtube", "YouTube-title.exe", "firefox-helper" })
                Check.Equal(MediaSourceBrand.Unknown, MediaSourceIdentity.Resolve(Track(source)).Brand);
            var result = MediaSourceIdentity.Resolve(Track("opaque.app") with { SourceDisplayName = "\t My Player \n" });
            Check.Equal("My Player", result.Label);
            Check.False(result.IsBrowser);
            var pwa = Track() with { SourceDisplayName = "Pocket Casts", SourceIconPath = "/cache/pocket-casts.png" };
            var pwaResult = MediaSourceIdentity.Resolve(pwa);
            Check.Equal(MediaSourceBrand.Unknown, pwaResult.Brand);
            Check.Equal("Pocket Casts", pwaResult.Label);
            Check.True(pwaResult.IsBrowser && !MediaSourceIdentity.CanSelectProvider(pwa));
        });
        suite.Add("Explicit YouTube browser choice is labelled as selected for the current track", () =>
        {
            var media = Track(); var selection = MediaSourceIdentity.SelectForTrack(media, MediaSourceBrand.YouTube);
            Check.True(selection is not null);
            var result = MediaSourceIdentity.Resolve(media, selection);
            Check.Equal(MediaSourceBrand.YouTube, result.Brand);
            Check.True(result.IsBrowser && result.IsUserSelected);
            Check.True(result.Tooltip.Contains("selected for this track", StringComparison.Ordinal));
            Check.True(MediaSourceIdentity.CanSelectProvider(media));
        });
        suite.Add("A temporary browser source choice survives position playback and artwork-only updates", () =>
        {
            var media = Track(); var selection = MediaSourceIdentity.SelectForTrack(media, MediaSourceBrand.YouTubeMusic);
            var updated = media with { Position = TimeSpan.FromMinutes(1), IsPlaying = false, ArtworkPath = "/cache/cover2.png", SourceIconPath = "/cache/browser2.png" };
            Check.True(MediaSourceIdentity.Matches(updated, selection));
            Check.Equal(MediaSourceBrand.YouTubeMusic, MediaSourceIdentity.Resolve(updated, selection).Brand);
        });
        suite.Add("Title artist album source and native session changes each clear a selected browser provider", () =>
        {
            var media = Track(); var selection = MediaSourceIdentity.SelectForTrack(media, MediaSourceBrand.YouTube);
            var changed = new[]
            {
                media with { Title = "Next track" }, media with { Artist = "Another artist" },
                media with { AlbumTitle = "Another album" }, media with { Source = "MSEdge" },
                media with { SessionRevision = media.SessionRevision + 1 }
            };
            foreach (var next in changed)
            {
                Check.False(MediaSourceIdentity.Matches(next, selection));
                Check.False(MediaSourceIdentity.Resolve(next, selection).IsUserSelected);
            }
        });
        suite.Add("A new browser session cannot inherit a provider choice even with identical title and source", () =>
        {
            var media = Track(); var selection = MediaSourceIdentity.SelectForTrack(media, MediaSourceBrand.Spotify);
            var replacement = media with { SessionRevision = 50 };
            Check.Equal(MediaSourceBrand.Chrome, MediaSourceIdentity.Resolve(replacement, selection).Brand);
        });
        suite.Add("Automatic invalid and native-player choices cannot override genuine source identity", () =>
        {
            Check.True(MediaSourceIdentity.SelectForTrack(Track(), MediaSourceBrand.Automatic) is null);
            Check.True(MediaSourceIdentity.SelectForTrack(Track(), (MediaSourceBrand)999) is null);
            Check.True(MediaSourceIdentity.SelectForTrack(Track("Spotify"), MediaSourceBrand.YouTube) is null);
            Check.True(MediaSourceIdentity.SelectForTrack(null, MediaSourceBrand.YouTube) is null);
            var native = Track("Spotify"); var injected = new MediaSourceSelection(MediaSourceBrand.YouTube, native.Source, native.Title, native.Artist, native.AlbumTitle, native.SessionRevision);
            Check.Equal(MediaSourceBrand.Spotify, MediaSourceIdentity.Resolve(native, injected).Brand);
        });
        suite.Add("Source availability changes cannot turn a stale browser choice into a claim of provider extraction", () =>
        {
            var media = Track(); var selection = MediaSourceIdentity.SelectForTrack(media, MediaSourceBrand.YouTube);
            var labelledPwa = media with { SourceDisplayName = "YouTube Music" };
            var result = MediaSourceIdentity.Resolve(labelledPwa, selection);
            Check.Equal(MediaSourceBrand.YouTubeMusic, result.Brand);
            Check.False(result.IsUserSelected);
            Check.False(MediaSourceIdentity.Resolve(null, selection).IsUserSelected);
        });
    }
}
