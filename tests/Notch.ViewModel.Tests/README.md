# Simulated view-model regression tests

Run `dotnet run --project tests/Notch.ViewModel.Tests/Notch.ViewModel.Tests.csproj --configuration Debug` on Windows or Linux. A unique temporary data directory is injected into the actual linked `MainViewModel`; tests never access `%LOCALAPPDATA%\Notch`. Test fixtures and their simulated native services are checked in, and failures or an empty suite return a nonzero exit code.

These scenarios exercise startup recovery, missed reminders, late result isolation, settings persistence, cancellation, native-adapter disposal order, clipboard notifications, telemetry refresh boundaries and stopwatch notifications. Dispatchers, timers, media, audio, credentials and clipboard are simulations. The tests do not execute Windows services or WinUI and do not establish native interaction, performance or Windows 10/11 compatibility.

Exit has a durable-save preflight. Native read-only results may finish while the app is still alive for retry/export; successful disposal then waits for those operations before releasing adapters and suppresses future publication. A failed save must keep the application and its edits available.

Debug is intentional for the full development tool suite. Release entitlement enforcement is a separate product concern and must be checked independently; a simulated full-catalog test is not evidence that paying customers can bypass access controls.
