# Native orchestration regression checks

This portable executable compiles the production clipboard, Windows media-session, and ambient-sound classes against explicit API doubles. It checks their C# ordering, cancellation, bounded work, registration retries, player switching, control capabilities, and optional-component fallback behavior without requiring a Windows desktop.

Run `dotnet run --project tests/Notch.Native.Tests -c Release`.

The public-testing refinement adds track/source/album metadata, playback-state labels, long-duration and non-1× timing, same-session transient metadata recovery, late track/artwork isolation, bounded thumbnail byte reads and source-logo fallbacks. The current suite has 33 grouped regression cases. It exercises production orchestration with explicit platform doubles; it does not produce a real player session or render an icon.

These checks do not validate Windows COM, clipboard-owner behavior, MediaPlayer codecs, audio endpoints, shell integration, or rendering. The Windows 10 and Windows 11 runtime matrix remains a separate release requirement.
