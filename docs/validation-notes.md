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

The v0.3.0 candidate addresses the owner's current laptop screenshots: hover sticking, oversized credential actions, poor panel density, source artwork and detailed media metadata. All 21 catalog tools and extended controls are enabled for everyone during public testing. Paid proofs remain separate; desktop and server checkout are paused. The future Freemium phase remains covered by explicit commercial-policy fixtures.

Local verification passed **345 checks**: 128 Core, 76 Commerce, 74 Debug view-model, 24 Release view-model, 33 linked native-service cases and 10 Python release cases. Source/XAML projections compiled with zero warnings/errors. Native API doubles and mocked HTTP are identified as such; they do not verify live players or accounts.

The media cases exercise Unicode/long metadata, title/artist/album/source identity, capability-aware transport, paused and non-1× timing, long durations, artwork deadlines, same-session transient failures, session switches and late responses. Connection cases distinguish absent configuration, saved-but-unverified secrets, successful reads, invalid imported files, missing audio, retry recovery, preview and disposal. Hover cases cover finite keyboard leases, leave retries, transparent regions, rapid transition geometry and work-area bounds.

The Windows qualification candidate must complete real XAML build, Setup installation, 21-tool navigation, eight connection states, real pointer hover/leave, typing-lease expiry, compact credential alignment, retained drafts, featured-panel scroll reset, notes/Awake, preview, manual-update guidance, reopening and uninstall on both hosted desktops before the public download is replaced. Screenshots are best-effort native captures with explicit availability status, rather than a functional pass criterion.

Exact consumer Windows 10/11 hardware, real browser/Spotify metadata and artwork, Narrator/text scaling, multi-monitor/sleep/fullscreen scenarios, sustained resource use and 60/120/144 Hz frame pacing still need device qualification. Real reporting accounts and a licensed weather backend are not configured; automated provider contracts cannot establish their live connectivity. Linux native desktop remains planned.

The initial v0.3.0 candidate ([source 83fd7a9](https://github.com/Sury2797/Notchling/commit/83fd7a93be34e9cbb786044f1466b2bd971df3cf), [run 37939977072](https://github.com/Sury2797/Notchling/actions/runs/37939977072)) passed builds, regression jobs and installation on both Windows hosts, then failed the repeated real-hover assertion. Earlier Settings-leave and transparent-flank assertions passed. Investigation found that the helper accepted a still-animating HWND below 100 DIP as collapsed although the final strip is 40 DIP, then placed its pointer below the final strip. Qualification now waits for stable compact geometry and enters the actual compact control; the real hover assertion remains. A pending hover timer also cannot reopen an already expanded module or replace its explicit keyboard lease. No v0.3.0 release was published.

The v0.3.1 follow-up ([run 37940974974](https://github.com/Sury2797/Notchling/actions/runs/37940974974)) retained the failure on both hosts with a settled 256 × 40-DIP HWND, the pointer at its centre and a visible compact-button tooltip. Correct coordinates alone therefore did not resolve the failure or prove delivery of client pointer events. The next candidate registers handled pointer events on the compact button and root, deduplicates arming and retains bounded non-content hover diagnostics. Both unsuccessful candidates remain unpublished; a successful new native run is required.

The v0.3.2 event-path candidate ([run 37941905130](https://github.com/Sury2797/Notchling/actions/runs/37941905130)) still failed the same opener assertion and remained unpublished. Neither host recorded a collapsed hover event or timer callback despite the new direct handlers. The helper used `SetCursorPos`, which established visual cursor position but did not demonstrate WinUI input delivery. The following candidate injects mouse movement through `SendInput`, checks its accepted event count and final physical coordinates, and retains the same hover assertions. Event-routing hypotheses remain unproven until that native input test succeeds.

The v0.3.3 candidate ([source a3fd865](https://github.com/Sury2797/Notchling/commit/a3fd8655e2b4deddb17a840e6a2e0ccfccafe389), [run 37943756306](https://github.com/Sury2797/Notchling/actions/runs/37943756306)) delivered actual mouse input and passed repeated hover opening/leaving, Settings collapse and compact alignment, editing-lease expiry, draft retention, eight connection states, evaluation update guidance and preview exit on the first Windows host. The catalog assertion then rejected the accessible word `unlocked` because its regex matched the substring `locked`. The following candidate requires a whole-word `locked` match while retaining the other paywall checks and all 21 actual-content assertions. This unsuccessful candidate remains unpublished; subsequent catalog and native-function stages still require a complete passing run.
