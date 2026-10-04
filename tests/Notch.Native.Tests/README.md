# Native orchestration regression checks

This portable executable compiles the production clipboard and ambient-sound classes against explicit API doubles. It checks their C# ordering, cancellation, bounded work, and optional-component fallback behavior without requiring a Windows desktop.

Run `dotnet run --project tests/Notch.Native.Tests -c Release`.

These checks do not validate Windows COM, clipboard-owner behavior, MediaPlayer codecs, audio endpoints, shell integration, or rendering. The Windows 10 and Windows 11 runtime matrix remains a separate release requirement.
