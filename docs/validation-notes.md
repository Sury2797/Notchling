# Validation evidence

Historical baseline recorded on **3 October 2026**; remediation checks recorded on **4 October 2026** (Asia/Kolkata). The verified baseline is commit [`131b597`](https://github.com/Sury2797/Notchling/commit/131b597f6e2e8cdb084d633db8cd7836ad698a68). Later changes require their own checks; this record does not certify a future release.

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

The Windows and Linux jobs are recorded in [GitHub Actions run 37098620398](https://github.com/Sury2797/Notchling/actions/runs/37098620398). The repository's CI badge reports the current workflow status; this table records the named baseline.

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
| Hosted Windows WinUI build/publish | Passed for [`7e88a82`](https://github.com/Sury2797/Notchling/commit/7e88a82139d560d453ec1db999f3f5015c4c4f40) in [run 37180992149](https://github.com/Sury2797/Notchling/actions/runs/37180992149); real XAML build, self-contained publish, release-script syntax, notice bundle and evaluation ZIP upload succeeded |
| Hosted Windows/Linux regression jobs | Both passed for the same source commit, including billing, core, Debug/Release view-model, linked native services, source projections and notice fixtures |
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

The linked native-service tests use platform doubles and the source projection check does not run `InitializeComponent`. Windows CI performs the real XAML build; each consumer OS still needs [native qualification](native-qualification.md). The remediation CI run above verifies the named source commit; the historical green run remains separate. Documentation-only follow-up commits do not change those tested sources.

## Notchling branding verification — 4 October 2026

The approved public name is **Notchling**, with **A — Pixel Dragon** as its selected app icon. Source branding, public window/tray labels, application metadata, installer/update filenames, billing mail/return text, README and product documentation were updated together. Existing workspace, credential, entitlement and installer identities remain compatible.

| Check | Recorded result / limit |
| --- | --- |
| Linked native C# / WinUI projections | Compiled successfully against the Windows SDK; desktop UI not executed |
| View-model regressions | 36 Debug scenarios passed; one Release Free-plan scenario passed |
| Billing/commerce | Updated service built with zero warnings/errors; 58 synthetic commerce checks passed |
| Release notice fixtures | Five passed after the new application filenames were applied |
| Project/XAML syntax | Parsed successfully |
| Pixel Dragon assets | Approved A artwork extracted to a 512 × 512 RGBA master; ten ICO frames from 16–256 px; transparency and small-size previews reviewed against light/dark backgrounds |
| Native icon lifecycle | Explicit window/tray handles, DPI refresh, Explorer recovery, and deterministic cleanup implemented; linked native source compiled; Windows visual qualification pending |
| Renamed application Windows build/publish | Passed the real hosted WinUI Release build and self-contained publish; notices/inventory, ZIP, and artifact upload passed |
| Hosted cross-platform workflow | Passed on the named branding revision, including Linux and Windows regression jobs |

These hosted results are recorded for source commit [`e203c2e`](https://github.com/Sury2797/Notchling/commit/e203c2e99baaeca97ace2bf8482df49a8a02534b) in [GitHub Actions run 37185720671](https://github.com/Sury2797/Notchling/actions/runs/37185720671). The unsigned evaluation artifact is **`notchling-windows-x64-unpackaged`**. Native launch, perceived icon appearance, mixed-monitor interaction, and Windows 10/11 hardware qualification remain unrecorded.

## Installed Windows app verification — 4 October 2026 UTC

Source [`fafa2cc`](https://github.com/Sury2797/Notchling/commit/fafa2ccac2c34e6b464a1254d637164df59b50ee) passed every job in [CI run 37203173532](https://github.com/Sury2797/Notchling/actions/runs/37203173532), including publication of the `notchling-evaluation-0.2.0` prerelease. The Windows job tested the actual evaluation setup and the executable installed by it, rather than launching from the build directory. [Download the tested Setup EXE](https://github.com/Sury2797/Notchling/releases/download/notchling-evaluation-0.2.0/Notchling-0.2.0-windows-x64-evaluation-setup.exe) directly; release downloads require no sign-in or ZIP extraction and do not expire with CI artifacts.

| Check | Actual result / scope |
| --- | --- |
| Windows and Linux regression jobs | Passed; 207 cases per host: 92 core, 58 commerce, 36 Debug view-model, one Release Free fixture, 10 native service doubles, and 10 Python release fixtures. Native source projections also compiled. |
| Native Release build and publish | Real WinUI/XAML build and framework-dependent publish passed; required nonempty `resources.pri` present; no bundled shared runtime files found |
| Setup EXE size | **8,875,854 bytes**, 8.46 MiB / 8.88 MB |
| Published application payload | **40,648,773 bytes**, 38.77 MiB; separate from shared-runtime disk usage and installer bookkeeping |
| Missing Windows App Runtime | Setup downloaded **106,879,800 bytes**, verified Microsoft's installer signature, installed it, and verified all four required x64 runtime registrations |
| .NET prerequisite | Existing .NET **10.0.11** reused; the cloud run did not test missing-.NET installation |
| Native launch | Visible window with expected title and nonzero native icon; first window in **816.2 ms**; five bounded message-responsiveness samples passed |
| Free UI interaction | Opened notch; toggled pin and verified saved preference; genuine no-player media controls disabled; Pomodoro started, paused, and reset; scratchpad edited, durably saved, recovered after navigation, and cleared |
| Short resource sample | **5.02 seconds** immediately after launch: working set **107.35 MiB**, private memory **31.36 MiB**, CPU **0.700%** normalized across logical processors |
| Cleanup and uninstall | Bounded cleanup of the owned test process and actual silent uninstaller completed; graceful save-on-Quit and upgrade/rollback were not tested |
| Public release download | Downloaded the actual published EXE without authentication; Windows PE header, **8,875,854-byte** size, and release SHA-256 matched |

The public installer SHA-256 is `2cde22306bbb0f67f7c18aa1168c1e1068f03f61ddf3bfe0761af6fd6c467ab6`. The separate [main-branch run 37203173413](https://github.com/Sury2797/Notchling/actions/runs/37203173413) also passed the same checks. Installer compression/build metadata can produce different bytes between runs; the checksum above identifies the released tag-run EXE.

The diagnostics artifact, `notchling-windows-cloud-test-results`, contains `package-size.json`, `windows-smoke.json`, `windows-ui-smoke.json`, `installer-smoke.log`, and `setup-prerequisites.log`. The native report records OS/version, window bounds, runtime identities, process measurements, and interaction results. The artifact digest identifies GitHub's downloaded archive; it must not be represented as the enclosed setup EXE's SHA-256.

Real installed-app execution exposed and fixed three failures that successful compilation had not caught: compiled XAML was omitted from publish, an unavailable accessibility notification aborted startup, and a null pointer-capture collection crashed interaction. The package now uses the SDK's PRI publish hook, theme notifications have native accessibility fallbacks, and uncaptured pointers are treated as an empty collection. Pin state also follows toggle changes from accessibility clients instead of relying only on Click.

This hosted Windows desktop result does **not** qualify consumer Windows 10/11 hardware, Narrator, high contrast, mixed DPI, real playback/audio, animation frame pacing, sustained idle performance, production billing, signed updates, or missing-.NET/UAC/offline setup. The five-second sample is a startup observation, not the 60-second settled-idle target. With both shared runtimes absent, first-install transfer is roughly **147 MB** at current versions; the .NET portion is an official download-size estimate, not a measured installation. See [delivery](release-delivery.md) and [native qualification](native-qualification.md) for the remaining gates.


## Windows runtime recovery and reliability — 7 October 2026

Source [`a327486`](https://github.com/Sury2797/Notchling/commit/a32748649c2d05442e513a8bbb1ba230f284070d) passed every job in [run 37608970728](https://github.com/Sury2797/Notchling/actions/runs/37608970728). [Download v0.2.5 Setup](https://github.com/Sury2797/Notchling/releases/download/notchling-evaluation-0.2.5/Notchling-0.2.5-windows-x64-evaluation-setup.exe). It replaces the recommended 0.2.0 download after the owner's Windows 10 19045.7725 prerequisite failure.

| Check | Result and limits |
| --- | --- |
| Portable regressions | 228 checks per host: 92 core, 58 commerce, 47 Debug view-model, two Release Free, 19 native-service doubles, 10 Python release fixtures; native source/XAML projections compiled. |
| Windows prerequisite fixtures | Windows PowerShell 5.1 x64 unit fixtures passed on both `windows-latest` and `windows-2022`, including old/missing/unhealthy packages, newer Singleton reuse, signature/identity rejection, restart/cancel/timeout/error handling and offline diagnostics. Unit responses are doubled. |
| Real runtime recovery | A fixture injected the reported native `0x8007007E` result, then downloaded and verified Microsoft's actual EXE, extracted its actual resources, validated all four manifests, and registered/reused packages with real Windows deployment and final detection. The injected loader error is not a spontaneous reproduction of the owner's missing module. |
| Real missing-.NET branch | A separate fixture forced initial runtime detection to missing, then used real Microsoft release metadata, SHA-512, Authenticode, installer execution and post-install runtime detection. Existing SDKs were retained; this is not a bare consumer Windows image. |
| Native build and installation | Actual WinUI/XAML build, publish, Setup, installed executable and uninstall passed on both Windows hosts. Final Setup reused the runtimes prepared by the preceding integration fixtures. |
| Installed Free UI | Media empty-state controls disabled; pin persisted; Pomodoro start/pause/reset and scratchpad persistence/navigation/clear passed. |
| Runtime discovery and reopening | Installed executable launched with deliberately invalid child-process `DOTNET_ROOT`/`DOTNET_ROOT_X64`; hiding its window and launching again reopened the original responding instance while the second process exited. |
| Hosted launch observation | First window in 794.7 ms / 1,025.4 ms; five responsiveness samples each. Working set 107.66 / 109.13 MiB; private memory 31.39 / 32.70 MiB; CPU across all cores 0.697% / 1.399% over approximately five seconds. These are startup samples, not settled-idle or hardware guarantees. |
| Public EXE | Unauthenticated direct download succeeded, valid Windows PE header, **8,881,476 bytes**. SHA-256 matched the release: `cbaf7f9e5e0207fc56b723b1a3e6db807db12ed1c3b977726efbb39c4c7c746f`. Published application payload **40,657,411 bytes**, shared runtimes excluded. |

The earlier qualification candidates were withheld: PowerShell 5.1 compression assembly loading, null native-installer exit codes, and a three-second cold-start pin-persistence timeout failed checks. Explicit compression loading, retained process handles/refreshed exit state, and a bounded ten-second persistence check with detailed failure diagnostics cleared the final run.

The owner must still retest this EXE on the reporting laptop. Hosted Windows runners do not qualify consumer Windows 10/11 hardware, mixed DPI, Narrator, high contrast, real playback/audio, sustained performance, denied UAC/offline installation, signed production updates or live billing. The native Linux desktop remains planned.


## Responsive native UI qualification — 8 October 2026

Source [`42dbdcd`](https://github.com/Sury2797/Notchling/commit/42dbdcdee797f98db9a4d3f68b89ec68b3a3c8bd) passed every job in [run 37669754772](https://github.com/Sury2797/Notchling/actions/runs/37669754772). [Download v0.2.9 Setup](https://github.com/Sury2797/Notchling/releases/download/notchling-evaluation-0.2.9/Notchling-0.2.9-windows-x64-evaluation-setup.exe). GitHub confirmed the repository moved to `Sury2797/Notchling`; source and helper update allowlists, installer links, release scripts and documentation use that exact canonical address. Signer and integrity requirements were retained.

| Check | Result and scope |
| --- | --- |
| Portable checks | **246 scenarios per host**: 103 core, 58 commerce, 53 Debug view-model, three Release Free, 19 native-service doubles, 10 Python release cases. Native source/XAML projections compiled. |
| Geometry and state regressions | Eleven linked geometry cases cover fractional DPI, small work areas, negative monitor coordinates, offsets and pixel containment. Preview migration/exit, slow refreshes, durable preference saves and unchanged-timer notification cases passed. These are functional fixtures, not physical display tests. |
| Prerequisites/native delivery | Existing Windows PowerShell 5.1 unit/integration/signed-resource recovery checks passed on both `windows-latest` and `windows-2022`; real WinUI/XAML build, publish, setup and uninstall passed. Final app Setup reused the runtimes prepared by integration fixtures. |
| Dock and collapse behavior | Actual DPI-aware pointer moves inside the panel/dock gap kept an unpinned panel expanded. Active unpinned Settings stayed expanded with the pointer outside. Every enabled Free dock control stayed within the window/work area. |
| Settings layout and editing | Vertical scrolling exposed controls; all visible elements stayed horizontally contained at eleven scroll positions. A Reduce motion toggle retained scroll position. An unapplied Pomodoro number draft survived Home/Settings navigation. |
| Real data and preview | Startup had no automatic sample label/music/preview strip. The test explicitly enabled sample mode, checked its label, exited it, and verified return to a real no-player state. |
| Free controls | Native no-player media controls were disabled; Pomodoro start/pause/reset and scratchpad editing, durable saves, navigation and clear passed. |
| Runtime and window recovery | Deliberately invalid child-process .NET environment overrides did not prevent launch. Launching another instance reopened the original responding window and exited the duplicate process. |
| Hosted startup observations | First window: **796.1 ms / 2,440.5 ms**. Five responsiveness samples each. Working set **108.50 / 111.39 MiB**; private memory **31.70 / 33.91 MiB**; CPU across all cores **0.622% / 0.851%** over roughly five seconds. These are cold/startup observations, not settled-idle or animation benchmarks. |
| Public delivery | Unauthenticated EXE download succeeded; valid Windows PE header; **8,889,407 bytes**. Published payload **40,692,808 bytes**, shared runtimes excluded. SHA-256 matched the release: `93854ce331aa863a4d35005efd166a051ebeefdf835bc58b32117c5413d044cd`. |

The initial UI candidates were withheld while the test harness was corrected: pattern-aware selection now chooses actual switches/inputs instead of identically named text labels, and the PowerShell 5.1 preview assertion constructs its middle-dot character without relying on UTF-8-without-BOM decoding. The complete interaction suite passed after these corrections; functional assertions were retained.

No live recording was supplied for this repair round. The PDF-visible issues are addressed and cloud checks passed, but the owner's actual freezes/process exits, consumer Windows 10/11 hardware, fullscreen behavior, mixed-monitor transitions, Narrator/high contrast, actual media/audio, Premium Awake, sustained performance, production signing and live billing still require their stated qualification. Linux native desktop remains planned.


## Live-video follow-up qualification — 8 October 2026

The supplied 62.33-second recording identifies **v0.2.5** in Settings and shows the old sparse panels, locked dock hover, horizontal overflow, switch-induced scroll resets, unlabeled compact sample music and expected signed-updater error. The compact strip remains present during collapse/reopen; no process crash or measured freeze is established by the recording. Timestamped observations and their resolutions are in [flaws.md](../flaws.md#live-recording-review--8-october-2026).

The follow-up source [`7d5226e`](https://github.com/Sury2797/Notchling/commit/7d5226efcb28d858458e20db74fe13beb8cf0af0) passed every job in [run 37735689610](https://github.com/Sury2797/Notchling/actions/runs/37735689610). [Download v0.2.11 Setup](https://github.com/Sury2797/Notchling/releases/download/notchling-evaluation-0.2.11/Notchling-0.2.11-windows-x64-evaluation-setup.exe).

| Check | Result and scope |
| --- | --- |
| Regression checks | **250 scenarios per platform**, Windows and Linux: 103 core, 58 commerce, 55 Debug view-model, five Release, 19 native-service doubles and 10 Python release cases. Native source/XAML projections compiled; actual native WinUI/XAML build passed on Windows. |
| Update and data safety | The real linked updater rejected automatic eligibility for the unsigned fixture before manifest/download/save. Repeated checks preserved notebook/preferences bytes even with an injected failing store. Loading/disposed command boundaries passed. |
| Installed evaluation interaction | Both `windows-latest` and `windows-2022` verified the actual visible Settings live region and its full accessible text, no error banner, retained viewport and unchanged app-owned download directory. Earlier layout/dock/preview/media-empty-state/Pomodoro/scratchpad assertions also passed. |
| Native delivery | Windows PowerShell 5.1 prerequisite fixtures, actual Microsoft runtime integration/resource fallback, publish, setup, responding native window, invalid .NET-root overrides, existing-instance reopening and uninstall passed on both hosts. Final Setup reused runtimes prepared by the integration fixtures. |
| Hosted startup observations | First window **815.8 / 1,015.4 ms**; five responsiveness samples each. Working set **108.60 / 109.59 MiB**, private memory **30.95 / 32.58 MiB**, CPU across all cores **0.699% / 1.161%** over **5.03 / 5.05 seconds**. These are startup samples, not settled-idle, frame-pacing or reporting-laptop measurements. |
| Public delivery | Unauthenticated download succeeded; valid Windows PE; **8,890,277-byte EXE**. Published app payload **40,694,898 bytes**, excluding shared runtimes. SHA-256 matched the published release: `2630421d8a6a76aa8cd2dd109bf1474d5a649e6ea52e6dbb1ddbbbc919941c29`. |

The initial v0.2.10 candidate failed the new test's certificate-inspection module import and was not published. The Windows PowerShell helper now explicitly imports its own security module instead of resolving the incompatible module search path inherited from a PowerShell 7 parent. The successful v0.2.11 run retains the full interaction assertions.

The evaluation action supplies manual release guidance. It does not weaken the signed stable updater's publisher, asset-URL, HTTPS, size or SHA-256 protections; live signed update delivery remains unqualified. The accessible status exposes its actual text and raises the polite live-region event when a listener exists, but Narrator behavior still needs consumer-device qualification. The owner's Windows 10 laptop must be retested with this repaired version. Other consumer Windows 10/11 hardware, real playback/audio, Premium Awake, mixed displays and sustained performance remain open. Linux native desktop remains planned.


## Public-testing refinement — 9 October 2026

The qualified v0.3.4 public-testing release addresses the owner's current laptop screenshots: hover sticking, oversized credential actions, poor panel density, source artwork and detailed media metadata. All 21 catalog tools and extended controls are enabled for everyone during public testing. Paid proofs remain separate; desktop and server checkout are paused. The future Freemium phase remains covered by explicit commercial-policy fixtures.

Local verification passed **345 checks**: 128 Core, 76 Commerce, 74 Debug view-model, 24 Release view-model, 33 linked native-service cases and 10 Python release cases. Source/XAML projections compiled with zero warnings/errors. Native API doubles and mocked HTTP are identified as such; they do not verify live players or accounts.

The media cases exercise Unicode/long metadata, title/artist/album/source identity, capability-aware transport, paused and non-1× timing, long durations, artwork deadlines, same-session transient failures, session switches and late responses. Connection cases distinguish absent configuration, saved-but-unverified secrets, successful reads, invalid imported files, missing audio, retry recovery, preview and disposal. Hover cases cover finite keyboard leases, leave retries, transparent regions, rapid transition geometry and work-area bounds.

The complete v0.3.4 run passed real XAML build, Setup installation, 21-tool navigation, eight connection states, actual injected pointer hover/leave, typing-lease expiry, compact credential alignment, retained drafts, featured-panel scroll reset, notes/Awake, preview, manual-update guidance, reopening and uninstall on both hosted desktops. Screenshots are best-effort native captures with explicit availability status, rather than a functional pass criterion or evidence of measured frame pacing.

Exact consumer Windows 10/11 hardware, real browser/Spotify metadata and artwork, Narrator/text scaling, multi-monitor/sleep/fullscreen scenarios, sustained resource use and 60/120/144 Hz frame pacing still need device qualification. Real reporting accounts and a licensed weather backend are not configured; automated provider contracts cannot establish their live connectivity. Linux native desktop remains planned.

The initial v0.3.0 candidate ([source 83fd7a9](https://github.com/Sury2797/Notchling/commit/83fd7a93be34e9cbb786044f1466b2bd971df3cf), [run 37939977072](https://github.com/Sury2797/Notchling/actions/runs/37939977072)) passed builds, regression jobs and installation on both Windows hosts, then failed the repeated real-hover assertion. Earlier Settings-leave and transparent-flank assertions passed. Investigation found that the helper accepted a still-animating HWND below 100 DIP as collapsed although the final strip is 40 DIP, then placed its pointer below the final strip. Qualification now waits for stable compact geometry and enters the actual compact control; the real hover assertion remains. A pending hover timer also cannot reopen an already expanded module or replace its explicit keyboard lease. No v0.3.0 release was published.

The v0.3.1 follow-up ([run 37940974974](https://github.com/Sury2797/Notchling/actions/runs/37940974974)) retained the failure on both hosts with a settled 256 × 40-DIP HWND, the pointer at its centre and a visible compact-button tooltip. Correct coordinates alone therefore did not resolve the failure or prove delivery of client pointer events. The next candidate registers handled pointer events on the compact button and root, deduplicates arming and retains bounded non-content hover diagnostics. Both unsuccessful candidates remain unpublished; a successful new native run is required.

The v0.3.2 event-path candidate ([run 37941905130](https://github.com/Sury2797/Notchling/actions/runs/37941905130)) still failed the same opener assertion and remained unpublished. Neither host recorded a collapsed hover event or timer callback despite the new direct handlers. The helper used `SetCursorPos`, which established visual cursor position but did not demonstrate WinUI input delivery. The following candidate injects mouse movement through `SendInput`, checks its accepted event count and final physical coordinates, and retains the same hover assertions. Event-routing hypotheses remain unproven until that native input test succeeds.

The v0.3.3 candidate ([source a3fd865](https://github.com/Sury2797/Notchling/commit/a3fd8655e2b4deddb17a840e6a2e0ccfccafe389), [run 37943756306](https://github.com/Sury2797/Notchling/actions/runs/37943756306)) delivered actual mouse input and passed repeated hover opening/leaving, Settings collapse and compact alignment, editing-lease expiry, draft retention, eight connection states, evaluation update guidance and preview exit on both Windows hosts. The catalog assertion then rejected the accessible word `unlocked` because its regex matched the substring `locked`. The following candidate requires a whole-word `locked` match while retaining the other paywall checks and all 21 actual-content assertions. This unsuccessful candidate remains unpublished; subsequent catalog and native-function stages still require a complete passing run.


### Qualified public-testing release — v0.3.4

Immutable source [`8ad94ce`](https://github.com/Sury2797/Notchling/commit/8ad94cefcbd6417a24312edd4c761d0de2fa28d5) passed every job in [run 37944357692](https://github.com/Sury2797/Notchling/actions/runs/37944357692). CI then published the [v0.3.4 Setup EXE](https://github.com/Sury2797/Notchling/releases/download/notchling-evaluation-0.3.4/Notchling-0.3.4-windows-x64-evaluation-setup.exe). The default README downloads now point to this qualified public-testing version. The preceding v0.3.0–v0.3.3 candidates were withheld; neither input nor catalog assertions were disabled to obtain a pass.

| Evidence | Result and scope |
| --- | --- |
| Windows/Linux regression jobs | **345 checks per host**: 128 Core, 76 Commerce, 74 Debug view-model, 24 Release view-model, 33 linked native API-double cases and 10 Python release fixtures; source/XAML projections compiled. HTTP and native doubles remain distinct from live integrations. |
| Actual Windows build and delivery | Both `windows-latest` and `windows-2022` passed WinUI/XAML build, app-only publish/audit, real Setup, installed Release launch, responding native window/icon and uninstall. These are hosted Windows Server desktops, not consumer Windows 10/11 hardware. |
| Prerequisites | Windows PowerShell 5.1 unit fixtures, forced-missing real .NET download/install and signed-resource Windows App Runtime recovery passed on both hosts. Final Setup reused those prepared shared runtimes and downloaded **0 prerequisite bytes**. SDKs were retained; bare-machine, denied-UAC and offline cases remain separate. |
| Hover and finite editing | Two actual `SendInput` hover-open/leave cycles, dock crossing, transparent dock flank, Settings leave, real keyboard grace and eventual editing-lease expiry passed. Mouse event acceptance and final physical cursor coordinates are checked. |
| Settings and connection truth | Eleven vertical positions had no horizontal overflow. Compact credential baseline/height, retained number/endpoint drafts and scroll passed. All eight connection states were checked; absent credentials/imports stayed setup guidance. No live provider account was exercised. |
| Unlocked catalog and local functions | All **21** tools opened their actual content without a purchased entitlement. Featured viewport reset, native Awake acquire/release, durable note save/navigation/deletion, no-player disabled media, valid-or-unavailable volume, Pomodoro and scratchpad round trips passed. This does not measure physical sleep prevention or real media playback. |
| Update/preview/runtime recovery | Explicit preview exit, unsigned manual-update guidance without error/download/scroll reset, deliberately invalid .NET-root child environment and existing-instance reopening passed on both hosts. Signed production update installation remains unqualified. |
| Published EXE | Unauthenticated download; valid MZ/Windows PE headers; **8,923,610 bytes (8.51 MiB)**; published SHA-256 matched. Shared runtimes are excluded. |
| Published app payload | **40,784,258 bytes (38.89 MiB)** after notice bundling on both hosts. The secondary Windows-2022 installer was 8,923,256 bytes; only the Windows-latest tag-run EXE was published. |
| Hosted startup observations | First visible window **690.6 / 1,135 ms**; five responsiveness samples each. Working set **110.85 / 111.62 MiB**; private memory **31.74 / 33.83 MiB**; CPU across all cores **1.39 / 1.56%** over **5.06 / 5.01 seconds**. These are startup samples, not settled idle, latency or animation benchmarks. |

The verified public installer SHA-256 is `13e4cd031568aa166bb2aa5e002f7887cd76b2c57c3d6a48cbb1abedbfd7bc3a`. Installer compression and build metadata can differ between hosts; artifact-archive digests must not be confused with this enclosed EXE checksum. Both diagnostics artifacts contain per-run JSON, prerequisite results and logs, plus available native captures; their retention is 14 days.

The owner's Windows 10 Pro 22H2 build 19045.7725 must still be retested with this release. Real browser/desktop-player metadata and artwork depend on the actual Windows media session supplied by each player. Consumer Windows 10/11, Narrator/high contrast/text scaling, mixed-monitor/fullscreen/sleep behavior, sustained resources and frame pacing remain open. Live Stripe/analytics and the undeployed licensed weather service require their own setup and verification. No owner-only bypass, fake paid proof, signing certificate or production connection was created. Linux native desktop remains planned.

## Icon, motion, source and architecture candidate — v0.4.12

**Status: follow-up candidate; qualification pending.** The current source addresses the latest screenshots: missing Weather glyphs, strong grey catalog feedback, a video thumbnail shown as compact source identity, dense reporting/settings layouts and stale hydration feedback on other tools. The source also adds read-only evaluation release discovery, quiet update history/indicator, explicit signed installation controls and native x86/ARM64 package paths. Its installer presents full formatted terms/privacy and approved branding; contextual help explains selected controls and offers a local quick guide. v0.4.9 progressed through full catalog/help checks on two hosts but stopped before Shelf transfer, while the other jobs exposed driver state/persistence assumptions. v0.4.12 requires a new full run. The published v0.3.4 links and measurements above remain the current qualified download.

| v0.4.0 regression evidence | Result and limit |
| --- | --- |
| Core | **141 passed**; includes media identity, temporary provider-choice expiry and distance/reduced-motion geometry cases |
| Commerce | **76 passed**; public-testing access remains separate from strict paid proofs; checkout remains paused |
| View model | **81 Debug + 31 Release passed**; discovery/install separation, cancellation/disposal, opt-in interval, notice deduplication, save failure, scoped feedback and session-choice fencing |
| Native orchestration doubles | **65 passed**, including 31 updater cases; official metadata boundaries, schema/architecture/version checks, deadlines, bounded/hash/publisher-verified downloads, progress/cancel/concurrency and prepared-file tampering |
| Python release fixtures | **13 passed**; package architecture/runtime configuration and notices remain checked |
| Compilation and syntax | Native source/XAML projections compiled with **zero warnings/errors**; PowerShell scripts and workflow YAML parsed |
| Total executed checks | **407 passed** locally and on each Windows/Linux regression host; these execute portable logic and platform/HTTP doubles, not the real WinUI renderer, Windows package deployment or live provider accounts |

### Preceding v0.4.3 local verification

Source [`ed50560`](https://github.com/Sury2797/Notchling/commit/ed50560d57d91757af938abf214cf3e4bfdb8f5c), checked locally on 10 October 2026 UTC, passed **432 cases**: 141 Core, 76 Commerce, 85 Debug view-model, 35 Release view-model, 74 linked native/service-double cases and 21 Python release cases. Native source/XAML projections compiled with zero warnings/errors. New cases cover Shelf batch persistence and bounded capture service behavior, plus complete installer-document formatting and preservation. Its hosted Linux regression also passed 432; the Windows regression and native matrix failed at the stages below. v0.4.12 must rerun the complete matrix; no new public-asset measurement is recorded yet.

Shelf source now distinguishes default file/folder references from opted-in file copies and app-supplied bitmap/virtual-file captures. Captures stay in the local workspace, obey per-file/total/count limits, and never download an image URL. Native compact dragging, picker/paste controls, opening, restart persistence, removal and failure recovery still require actual installed UI results. **Remove** keeps files/copies; capture contents need separate backup from notebook JSON exports.

The configured installed-app matrix is x64 on `windows-latest` and `windows-2022`, x86 on x64 `windows-2022`, and native ARM64 on `windows-11-arm`. Evaluation publication requires all four jobs and both regression jobs to pass. Each architecture's emitted PE/bootstrapper/runtime contract and actual launched process must match; installer wrapper architecture alone is insufficient. Setup's runtime plan distinguishes app-architecture Framework/DDLM from native-host Main/Singleton and the native Framework dependency.

The candidate has 49 bundled vector control icons and separate source logos. Automatic identity uses Windows-provided app metadata only; generic browsers keep their own brand until the user chooses a provider for the current track. A native session revision invalidates that choice even if replacement-session text is identical. No browsing history, tab scan or remote favicon lookup is introduced.

Unsigned release checks read only official metadata; they never download or execute an evaluation installer. Optional daily checks are off by default and run only inside the app, at most once per 24 hours within its session. Notifications are bounded and do not replace the current panel. Signed installation requires an explicit action and preserved installed-publisher trust; no production signing configuration or end-to-end signed-update result exists yet.

Consumer 32-bit Windows 10 and Windows 10 ARM64, real media/providers, Narrator/high contrast/text scaling, mixed-DPI/hot-plug/sleep behavior and measured 60/120/144 Hz frame pacing remain open. Native ARM64 runner success, if recorded, will establish that exact Windows 11 hosted configuration only. Do not reuse v0.3.4 package sizes, hashes or startup observations as this candidate's measurements.

### Withheld v0.4.0 candidate

Immutable source [`899d27d`](https://github.com/Sury2797/Notchling/commit/899d27d8b9c2084dc1090fd2bd5a882ef15c5193) ran in [CI 37979741879](https://github.com/Sury2797/Notchling/actions/runs/37979741879). Both regression jobs passed 407 cases. All four Windows jobs passed native build/publish and the official prerequisite fixtures. Installed qualification then failed, so publication did not run.

| Actual failure | Following repair and gate |
| --- | --- |
| x64 on both hosts and native ARM64 aborted launch with a WinUI `Path.Data` argument exception | A cached geometry dependency object had been assigned to multiple Paths. Immutable token caching with new per-control geometry replaces that ownership violation; a fresh installed launch is required |
| x86 Setup failed before prerequisite handling while compiling its resource reader with an external Framework64 compiler | Prepare the AnyCPU resource-reader helper during the build and package it in Setup, removing runtime compilation from the installer context; x86 actual setup/launch must pass |

No v0.4.0 evaluation was published. The new candidate preserves the installed-app and architecture assertions and must pass them again; successful portable checks or a repaired source projection cannot certify these native failure paths.

### Candidate qualification sequence — 10 October 2026 UTC

Each attempt has an immutable revision and its own evidence. No failed candidate replaces the verified v0.3.4 download.

| Attempt | Exact source and workflow | Executed result |
| --- | --- | --- |
| v0.4.0 evaluation candidate | [`899d27d`](https://github.com/Sury2797/Notchling/commit/899d27d8b9c2084dc1090fd2bd5a882ef15c5193), [run 37979741879](https://github.com/Sury2797/Notchling/actions/runs/37979741879) | Both regression jobs passed 407 checks. All four Windows build/publish and official prerequisite-fixture stages passed. x64/ARM64 launch failed on shared WinUI `Path.Data` geometry; x86 final Setup failed while invoking an external compiler. Publication did not run |
| v0.4.1 evaluation candidate | [`dd027e`](https://github.com/Sury2797/Notchling/commit/dd027e8fd316a1b4f6824701fe3fbb1d0df712d3), [run 37980974718](https://github.com/Sury2797/Notchling/actions/runs/37980974718) | Both regression jobs passed. Windows PowerShell 5.1 failed before prerequisite fixtures on all four Windows jobs: a parameter default evaluated `Join-Path $PSScriptRoot ...` before the script root was available. This run did not establish final Setup or launch success, and publication did not run |
| v0.4.2 validation-only run | [`61c522e`](https://github.com/Sury2797/Notchling/commit/61c522e7cd89c5d703bef403822d1246bdd70228), [run 37981575380](https://github.com/Sury2797/Notchling/actions/runs/37981575380) | Both regression jobs passed 407 checks. All four Windows builds, publishes, prerequisite fixtures and actual final Setup stages passed. Every installed-app launch then failed with `ArgumentException` (`0x80070057`): `Translation` was addressed as a visual property in `MainWindow.ResetShellAnimations`. No installed UI assertions passed. The validation tag cannot publish a release |
| v0.4.3 evaluation candidate | [`ed50560`](https://github.com/Sury2797/Notchling/commit/ed50560d57d91757af938abf214cf3e4bfdb8f5c), [run 38029187998](https://github.com/Sury2797/Notchling/actions/runs/38029187998) | Linux regression passed 432 checks. Windows Debug view-model testing failed; private logs did not expose its exact failing assertion. Inspection found that the new blocked-destination fixture expected only `IOException`, while Windows can report `UnauthorizedAccessException`; the follow-up accepts both and retains the failure/state assertions. All four native builds/publishes and prior prerequisite fixtures passed, then failed at **Install evaluation installer compiler**, before compiling final Setup. Final installation/launch/UI checks did not execute, and publication did not run |
| v0.4.4 evaluation candidate | [`b6c9131`](https://github.com/Sury2797/Notchling/commit/b6c9131f8e790a420da35f525815593a0dc269b8), [run 38029916759](https://github.com/Sury2797/Notchling/actions/runs/38029916759) | Both portable regression jobs passed 432 checks. All four Windows jobs passed build/publish and prior prerequisite fixtures, then rejected the pinned compiler's version banner after asset hash/trusted-signature checks passed. Final Setup compilation, installation, app launch and UI checks did not execute. The run failed and evaluation publication was skipped |
| v0.4.5 evaluation candidate | [`af9f5a4`](https://github.com/Sury2797/Notchling/commit/af9f5a433c7afd0173dbfc2f97b814d45d73f0da), [run 38030299775](https://github.com/Sury2797/Notchling/actions/runs/38030299775) | Both regression jobs passed. All four native builds, publishes, prerequisite fixtures, verified compiler probes and final Setup compilations passed. The visible-installer qualification step failed before its success notice; installed-app checks did not execute. Public annotations exposed only the exit code, so no more specific cause is claimed. Publication was skipped |
| v0.4.5 diagnostic validation | [`92d4ae3`](https://github.com/Sury2797/Notchling/commit/92d4ae398210ab56d6e1e826bb905bdb85cc6fd2), [run 38041376900](https://github.com/Sury2797/Notchling/actions/runs/38041376900) | Both regression jobs passed; all four visible installer checks failed at the same welcome locator: `Owned installer control did not appear: ^Notchling$`. Final Setup compiled; installed-app assertions did not execute. This diagnostic cannot publish |

The next tag, `notchling-evaluation-0.4.6` ([run 38041797354](https://github.com/Sury2797/Notchling/actions/runs/38041797354), source [`5630e54`](https://github.com/Sury2797/Notchling/commit/5630e5467410d39d1b1893b4a9c304e61d52243b)), includes the caption correction but still contains project version 0.4.5: the preceding local version-update script failed. The publication version guard prevents it from becoming a release. Its native checks failed on the same heading: the newly added exact native comparison also counted Inno's appended newline. They remain diagnostic evidence only; the matching v0.4.7 follow-up must pass independently.

The v0.4.7 follow-up reads exact visible captions from owned native windows through bounded `WM_GETTEXT`, and trims only surrounding layout whitespace. Official `Setup.WizardForm.pas` appends a newline to `WelcomeLabel1.Caption`; the previous exact-caption comparison incorrectly counted that newline as branding text. `TNewStaticText` inherits `TWinControl`, so its native caption is checked directly without assuming a particular UI Automation label role. Interactive actions and complete RichEdit text/typography checks remain unchanged; the same four-host installed matrix must pass again.

The v0.4.2 Setup result establishes that the path-binding and x86 runtime-compiler failures no longer blocked installation on those hosts. It does not establish a usable app: the launch failure prevented UI, interaction, reopening and uninstall qualification from completing. The repaired translation call compiles against the native member metadata, but only a fresh installed launch can verify its property contract at runtime.

The next run must retain the complete four-job app/architecture matrix and both regression gates. Its installer checks must exercise branded welcome, terms/privacy navigation and cancellation before installation; installed UI checks must exercise explicit Home help opening/dismissal alongside the existing controls. New results, public EXE measurements and hashes belong to that exact run, rather than being inherited from a failed attempt.

v0.4.4 introduced official `jrsoftware/issrc` acquisition for **Inno Setup 6.7.3**, pinned at **10,592,232 bytes** and SHA-256 `9c73c3bae7ed48d44112a0f48e66742c00090bdb5bef71d9d3c056c66e97b732`, with trusted Authenticode. Its no-argument version probe was incorrect: the official ISCC source prints only the major version in that banner, while the exact engine version appears during compilation. v0.4.5 retains asset verification, compiles a disposable script and requires the exact compiler-engine version before building any Notchling Setup. This changes tooling preflight rather than app runtime behavior.

The earlier v0.4.3 failure establishes an environment-dependent compiler-install/version preflight, not the precise version of that prior compiler; private logs were unavailable. Its blocked-destination fixture now accepts expected Windows/Linux exceptions while retaining failure, state and no-silent-save assertions. Both v0.4.4 regression jobs passed 432 checks after that correction. Compiler acquisition and all later native stages remain fresh qualification gates; passing a disposable compiler probe cannot fill an installed-app or installer-UI result.

The version-matched [v0.4.7 run 38042159400](https://github.com/Sury2797/Notchling/actions/runs/38042159400), source [`ad5b565`](https://github.com/Sury2797/Notchling/commit/ad5b56575e0c05665656ee7b3988d1e09bd722f8), passed both regression jobs, all native builds/publishes/probes and final Setup compilation. The branded welcome and complete terms-text comparison passed on all four hosts; typography inspection then failed because the helper referenced `TextPatternRangeEndpoint` in the wrong namespace. v0.4.8 uses `System.Windows.Automation.Text.TextPatternRangeEndpoint` and `TextUnit`, checked alongside every helper type/static member against real Windows Desktop reference metadata. Installed-app assertions had not run at that stage.

### Withheld v0.4.8

The [v0.4.8 run 38042522459](https://github.com/Sury2797/Notchling/actions/runs/38042522459), immutable source [`e07beac`](https://github.com/Sury2797/Notchling/commit/e07beacfff8db25483564f915011d85d5a83a74b), passed both 432-check regression jobs. All four Windows hosts passed native build/publish, prerequisite fixtures, verified compiler probe and final Setup compilation. At 96 DPI, the visible installer passed branded welcome, full formatted terms/privacy text and typography, explicit acceptance/Next behavior, navigation bounds and cancellation before installation.

Actual installation and matching-architecture launch then passed on both x64 hosts, x86 on the x64 host and native ARM64. The installed app responded, showed real startup data and an in-bounds dock, preserved expansion across the dock gap, collapsed unpinned Settings and transparent dock flanks on leaving, and completed repeated hover open/collapse cycles. This run verified the repaired geometry and compositor launch paths on those exact hosts.

Every native job then failed the Settings horizontal-bounds assertion on a `PopupHost` extending beyond the panel. The old failure did not identify that popup's control type or descendants; its name alone does not establish that it is a tooltip or harmless. Catalog, explicit help and Shelf interaction assertions had not yet executed. Evaluation publication was skipped and no new public download, release measurement or hash is claimed.

The v0.4.9 follow-up introduced native tooltip classification through the UI Automation `ToolTip` role, related parents and typed descendants. Those surfaces must fit the monitor work area on all four edges; other panel elements retain strict horizontal panel bounds. Unrecognized popups still fail with their type, class and descendant details. This qualification repair does not exempt all popups; the next run's actual results follow.

### Withheld v0.4.9 and pending v0.4.10

The [v0.4.9 run 38043358404](https://github.com/Sury2797/Notchling/actions/runs/38043358404), immutable source [`9265e29`](https://github.com/Sury2797/Notchling/commit/9265e295c83b35f6557aef2710d223dadcddfcfb), passed both 432-check regression jobs and all four branded installer/actual installation/matching-architecture launch checks. Qualification then reached different stages on each host; no evaluation release was published.

| Native job | Completed result and next failure |
| --- | --- |
| x64 `windows-latest` and x86 on x64 `windows-2022` | Passed the 11-position layout checks, Settings drafts, eight connection states, manual updates, all 21 tool panels, explicit help and Awake. The Shelf helper then failed because `Get-FileHash` was unavailable in its Windows PowerShell context, before an actual transfer; no file/bitmap drop pass is claimed |
| x64 `windows-2022` | The pin control reported UI Automation `On`, but the observed persisted setting remained `False`. Driver inspection found that its `Get-Content` reader did not share deletion and could block the application's atomic `File.Move`; this is a possible cause, not a proven explanation of the run |
| Native ARM64 `windows-11-arm` | The helper expected compact Home although Home was already expanded. That initial-state assumption stopped this job; it does not establish a failed hover/collapse behavior |

v0.4.10 uses framework SHA-256 for Shelf fixture integrity without a `Get-FileHash` module dependency. Shelf opening is observed through its actionable **Choose files** control rather than an assumed visual Border peer. Workspace observation uses a bounded framework reader sharing `ReadWrite | Delete`, plus bounded diagnostics that compare pin UI state with persistence. The initial Home check accepts an already expanded panel and requires the visible Pin control; all actual hover/collapse gates remain. The complete four-host installed-app matrix, both regression jobs and independent public-asset verification remain required before promotion.

### Withheld v0.4.10 and pending v0.4.11

The [v0.4.10 run 38043997768](https://github.com/Sury2797/Notchling/actions/runs/38043997768), source `ad1295f`, passed both 432-check regressions and all four installer/installation/launch checks. Both x64 jobs and x86 passed Settings, all 21 tools, help, connections and Awake, then timed out during the actual OLE file transfer. Inspection found the helper advertised format 13 (`CF_UNICODETEXT`) instead of 15 (`CF_HDROP`); v0.4.11 corrects that contract while retaining strict retrieval assertions. ARM64 missed a repeated compact hover opening despite native cursor/tooltip visibility. The follow-up adds a guarded 200 ms native compact-pointer check that starts the existing 120 ms opening delay when routed events are missed, plus bounded data-free Shelf diagnostics. Complete native file/image and hover qualification remains pending; no v0.4.10 release was published.

### Withheld v0.4.11 and pending v0.4.12

The [v0.4.11 run 38044580529](https://github.com/Sury2797/Notchling/actions/runs/38044580529), source [`375e350`](https://github.com/Sury2797/Notchling/commit/375e3503821c720bd3e7b791690b27a5a603a091), passed both 432-check regression jobs and all four branded installer/installation/launch checks. The three x64/x86 jobs passed existing tools, help, Settings, connections, updater and Awake, then the OLE helper stalled at `EnumFormatEtc` before `QueryContinueDrag`, `GetData` or app Shelf handoff events. Native ARM64 now passed hover and Settings checks, then failed real keyboard delivery/focus. v0.4.12 corrects the helper COM contract and the keyboard focus barrier while retaining actual transfer and input assertions. Full native file/image, keyboard and release qualification remains pending; v0.4.11 was withheld and no new download is claimed.
