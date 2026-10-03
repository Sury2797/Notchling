# Validation evidence

Recorded for the initial native implementation and efficiency follow-up on 3 October 2026. Local checks use Linux x86-64; hosted Windows results are recorded separately. Re-run these checks for later changes; this document does not establish the status of a future commit.

## Completed locally

- Official .NET SDK 10.0.100 was downloaded over verified HTTPS and matched the SHA-512 hash in Microsoft's release metadata before extraction into `/workspace/.cache/dotnet`.
- The portable core and package-free test executable compiled and ran with that SDK. **57 tests executed; 57 passed; 0 failed.**
- Cases cover preference normalization, countdown expiry after missed UI ticks, pause/resume, stopwatch laps, bounded/deduplicated live activities, pin behavior, concurrent atomic JSON writes, cancellation, Unicode storage, traversal rejection and conversions.
- In-memory provider fixtures verify Stripe net captured charges/refunds/pagination/currency precision/errors, weather time zones/caching/schema checks, HTTPS analytics contracts, Claude repeated messages and Codex cumulative counters.
- Calendar fixtures verify folding/escaping, excluded/cancelled events, weekly recurrence, named Windows/IANA zones, DST gaps and repeated times, nominal-day versus elapsed-hour durations, old recurrence queries and unsupported input.
- Native App, MainWindow, Views, ViewModels, Services and Interop C# files compiled against actual Windows SDK projections and the pinned Windows App SDK in temporary Linux cross-target harnesses with zero errors. XAML element property setters, event signatures, resource references and XML syntax were also checked. The UI harness uses generated field/initialization stubs and excludes Windows-only XAML build tasks. An unused-field warning belongs to those temporary stubs. These checks did not execute Windows services or render the interface.
- Seven added regression cases verify that inactive activity dismissal cannot undo navigation, queued activities cannot revive after forced collapse, oversize and escaped-Unicode saves preserve the previous readable file, and a failed serializer leaves later writes usable.
- **31 temporary linked-source view-model scenarios passed** with simulated dispatchers, timers and Windows services. They exercise corrupt-data preservation, optional startup failure, clipboard notifications, delayed reminders, demo isolation, stale refreshes, startup input guards, accepted settings persistence, port-scan view gating and unchanged lists, stale scan results/errors, worker-thread enumeration, stopwatch notifications and shutdown ordering. These harness files are outside the checkout at `/workspace/.cache/viewmodel-check`; they are not Windows runtime checks.
- The CI workflow parses as YAML; the optional SDK bootstrap parses as Python; the Bash core-check wrapper passes shell syntax checking.

Tests use synthetic inputs and mocked HTTP. They do not require production credentials or assert that live provider accounts are connected.

## Hosted Windows build

The initial pushed commit, `19a24dbc17a94c04b4f42ed913fdcda9b56855ca`, triggered [workflow run 37097521066](https://github.com/SuryaK999/Notch-win-linux/actions/runs/37097521066). Its public job pages confirm:

- **Windows x64 native build and self-contained publish succeeded**, producing the `notch-windows-x64-unpackaged` artifact (98.6 MB).
- **Linux core checks succeeded**.
- **Windows core checks failed** during test execution. The assertion is being investigated; no test has been skipped or weakened.

GitHub API requests return `Forbidden`, while public GitHub Actions HTML pages are accessible. Detailed logs require sign-in. The test runner now emits escaped GitHub error annotations so failures can be diagnosed from the public run summary. A successful native build does not establish a successful overall workflow or interactive Windows behavior.

## Still required

A complete `dotnet build src/Notch.Windows/Notch.Windows.csproj --configuration Release -p:Platform=x64` was attempted on Linux. Restore succeeded and the core compiled, but the Windows-only `XamlCompiler.exe` failed with `Exec format error`; its expected `output.json` was not produced. This remains a local host-platform blocker; full builds run in Windows CI.

- Resolve the Windows core-test failure and obtain a green overall workflow for the final revision.
- Native application launch, complete module interactions, DPI/monitor behavior, keyboard/accessibility checks, tray lifetime and sleep/resume behavior on Windows.
- Measured rendered responsiveness, idle CPU, memory use and long-running resource behavior.
- Reference-style outer-shell morph animations. Current content transitions animate, but native window resizing is immediate.
- Live provider integration using explicitly configured test credentials. The live Open-Meteo request was not validated in this environment.
- Commercial weather endpoint configuration, signed distribution and update handling before a paid public release.
- Billing/account/entitlement work and a Linux desktop UI remain future phases.

The [release matrix](release-readiness.md) lists concrete cases. Static C# checks, portable tests and reference images are separate evidence from interactive Windows validation.

## Reproduce the local checks

```bash
python scripts/install-dotnet.py --install-dir /workspace/.cache/dotnet --cache-dir /workspace/.cache/dotnet-downloads
DOTNET_CLI_HOME=/workspace/.cache/dotnet-home \
NUGET_PACKAGES=/workspace/.cache/nuget \
DOTNET_NOLOGO=1 DOTNET_CLI_TELEMETRY_OPTOUT=1 \
/workspace/.cache/dotnet/dotnet run --project tests/Notch.Core.Tests/Notch.Core.Tests.csproj --configuration Release
```

The custom runner exits with failure when a case fails, times out, or no cases are registered. It is invoked with `dotnet run`, not `dotnet test`, because the repository deliberately avoids external test-runner dependencies.

The tested SDK installation and core-check wrapper, plus SDK activation/start instructions, were saved as `install_script` and `start_skill` in the cloud configuration draft. The repeatability run completed with 57 passing tests. Saving did not publish a snapshot or establish a fresh-task restoration result.
