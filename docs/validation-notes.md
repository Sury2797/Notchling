# Validation evidence

Historical baseline recorded on **3 October 2026**; remediation checks recorded on **4 October 2026** (Asia/Kolkata). The verified baseline is commit [`131b597`](https://github.com/SuryaK999/Notch-win-linux/commit/131b597f6e2e8cdb084d633db8cd7836ad698a68). Later changes require their own checks; this record does not certify a future release.

## Verified baseline

| Check | Evidence and result |
| --- | --- |
| Linux core build and tests | 58 cases executed; 58 passed; 0 failed |
| Windows core build and tests | Passed in hosted CI |
| Windows x64 native Release build | Passed in hosted CI |
| Self-contained unpackaged Windows publish | Passed in hosted CI; development artifact is unsigned |
| Simulated view-model behavior | 31 temporary linked-source scenarios passed with simulated Windows services and dispatchers |
| Native Windows launch and interactive QA | Not yet recorded |
| Windows performance measurements | Not yet recorded |
| Subscription billing and enforcement | Not implemented |
| Linux desktop UI | Not implemented |

The Windows and Linux jobs are recorded in [GitHub Actions run 37098620398](https://github.com/SuryaK999/Notch-win-linux/actions/runs/37098620398). The repository's CI badge reports the current workflow status; this table records the named baseline.

## Core test coverage

The package-free test executable runs with .NET SDK 10.0.100. Its 58 cases cover:

- Preference normalization; countdown expiry after missed UI ticks; pause/resume; stopwatch laps; bounded, deduplicated live activities; activity dismissal and pin behavior.
- Atomic UTF-8 JSON storage, concurrent reads/writes, queued cancellation, corrupt and oversize data preservation, Unicode serialization and path-traversal rejection.
- Length, mass, temperature and decimal/binary data conversions, including invalid categories, overflow and absolute-zero checks.
- Stripe captured payments, refunds, pagination, currency precision and errors; weather time zones, caching and schema validation; HTTPS analytics contracts.
- Claude repeated usage messages and Codex cumulative counters, without double-counting.
- Calendar folding/escaping, excluded and cancelled events, weekly recurrence, Windows/IANA time zones, DST transitions, nominal-day versus elapsed-hour duration and unsupported input.

Reads and writes share an asynchronous storage gate. The concurrent-storage case and queued-reader cancellation regression verify that readers cannot interfere with Windows atomic file replacement and that cancellation leaves storage usable.

Provider tests use synthetic inputs and mocked HTTP. They verify parsing and reporting behavior without production credentials; they do not establish live-account connectivity.

## Additional source and lifecycle checks

Native App, MainWindow, Views, ViewModels, Services and Interop C# compiled against Windows SDK projections and the pinned Windows App SDK in temporary cross-target harnesses with no errors. XAML property setters, event signatures, resource references and XML syntax were checked. Generated initialization stubs excluded Windows-only XAML tasks, so these checks did not render the UI. Hosted Windows CI subsequently completed the real XAML build and publish.

The historical 31 simulated scenarios have now been preserved in [Notch.ViewModel.Tests](../tests/Notch.ViewModel.Tests/README.md) with injected temporary storage, checked-in doubles and new final-save/retry, over-limit rejection and simultaneous-reminder cases. Debug tests expose the development suite; a separate Release fixture verifies Free defaults/direct-command guards. [Notch.Native.SourceChecks](../tests/Notch.Native.SourceChecks/README.md) regenerates member/event projections from the current XAML rather than relying on temporary generated files. Neither suite executes native WinUI.

The CI YAML, Python SDK bootstrap and Bash core-check wrapper also passed syntax checks. The optional bootstrap verifies the SDK archive against the SHA-512 value in Microsoft's official release metadata before extraction.

## Remaining validation

- Native launch and complete tool interactions on Windows, including keyboard accessibility, Narrator, DPI, mixed monitors, tray lifetime and sleep/resume.
- Measured input response, rendered frame pacing, idle CPU, memory and long-running resource behavior.
- Perceived native panel motion, interruption and rendered frame pacing at 60/120/144 Hz; source behavior alone is not a measurement.
- Live supported-provider checks using explicit test credentials, including network failure and disconnect behavior.
- Commercial weather configuration, signed packaging, update handling and clean-machine installation/upgrade/uninstallation.
- Live sandbox subscription purchase, renewal, cancellation, refund/dispute, expiry and recovery for the configured service; Release enforcement is covered separately by synthetic fixtures.

A full Windows project build on Linux restores dependencies but cannot execute the Windows-only XAML compiler. Use Windows for the native build. Portable core success is independent of the Linux desktop UI, which remains future work.

The [release matrix](release-readiness.md) defines concrete acceptance cases. Passing tests and a green build establish specific technical checks; they do not establish commercial release readiness.

## Reproduce repository checks

Install the SDK selected by `global.json`, then run from the repository root.

**Linux or another Bash host — portable core:**

```bash
./scripts/check-core.sh
```

The optional verified bootstrap can install an SDK locally without changing the system installation:

```bash
python3 scripts/install-dotnet.py --install-dir .tools/dotnet --cache-dir .tools/downloads
NOTCH_DOTNET="$PWD/.tools/dotnet/dotnet" ./scripts/check-core.sh
```

**Windows PowerShell — core and native build:**

```powershell
./scripts/check-core.ps1
dotnet build src/Notch.Windows/Notch.Windows.csproj --configuration Release -p:Platform=x64
```

The custom runner exits with failure when a case fails, times out or no cases are registered. The wrappers invoke `dotnet run`, because the test executable deliberately has no external test-runner dependency.

## Remediation verification — 4 October 2026

| Check | Recorded result / limit |
| --- | --- |
| Portable core | 92 Release cases passed, including monotonic timing, civil calendar semantics and aggregate workspace limits |
| Commerce | 58 Release checks passed; billing/test projects built with zero warnings/errors; synthetic Stripe HTTP, OTP, refund/dispute and signed-proof coverage |
| Linked native service scenarios | 10 passed using Windows API doubles; real Windows services were not executed |
| Checked-in Debug view-model scenarios | 36 executed, 36 passed, 0 failed on the Linux cloud host with simulated native services |
| Release Free-plan fixture | One executed, one passed; unconfigured Release remained Free and direct paid commands were gated |
| XAML source generator | Five source XAML documents parsed and regenerated; full linked native C#/WinUI member projections compiled with zero C# errors. Windows UI was not executed. |
| Publisher notice/SBOM fixtures | Five passed: exact text/hash preservation, build-only scope, missing dependency/runtime-notice refusal, and installer-engine license/provenance inclusion |
| Actual Windows release / installer / update | Not executed; publisher signing credentials and native Windows machines unavailable |

Run these checks from a clean checkout:

```bash
dotnet run --project tests/Notch.Commerce.Tests/Notch.Commerce.Tests.csproj --configuration Release
dotnet run --project tests/Notch.ViewModel.Tests/Notch.ViewModel.Tests.csproj --configuration Debug
dotnet run --project tests/Notch.ViewModel.Tests/Notch.ViewModel.Tests.csproj --configuration Release
dotnet run --project tests/Notch.Native.Tests/Notch.Native.Tests.csproj --configuration Release
python3 scripts/check-native-source.py
python3 -m unittest discover -s tests/release -v
```

The linked native-service tests use platform doubles and the source projection check does not run `InitializeComponent`. Windows CI performs the real XAML build; each consumer OS still needs [native qualification](native-qualification.md). No current CI result for the uncommitted remediation is implied by the historical green run.
