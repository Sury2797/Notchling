# Architecture

```mermaid
flowchart LR
  User[Mouse and keyboard] --> UI[WinUI views]
  UI --> VM[MainViewModel]
  VM --> Core[Portable core]
  VM --> Native[Windows services]
  VM --> HTTP[Opt-in data adapters]
  Native --> OS[Windows APIs]
  Core --> Disk[Atomic local JSON]
  HTTP --> Vault[Windows Credential Locker]
  VM --> Host[Win32 overlay host]
  Host --> Tray[Tray and global shortcut]
```

`Notch.Core` contains typed records, the tool catalog, clock-driven focus timers, the overlay state machine, bounded atomic local storage, unit conversions and explicit provider/import adapters. It is a package-free .NET library with no reference to WinUI or Windows APIs. Its tests run on Linux and Windows.

`Notch.Windows` is the Windows application. WinUI owns native controls and content composition. Win32 owns monitor placement, window regions, the tray and the global shortcut. GSMTC supplies event-driven media state; Core Audio supplies volume; lightweight native calls supply system data and session-only screen time. A thread dedicated to the Awake request restores normal power behavior on disposal.

The view model is the integration boundary. Native events enter through the UI dispatcher. Heavy or asynchronous operations expose cancellation and errors. Newer requests invalidate older responses so a late network result cannot overwrite a new selection or explicit demo state. A broken optional import does not prevent local timers and other tools from starting.

Views are created on demand and detached from the content host while the notch is collapsed. Native system snapshots are read for the relevant visible panels; unrelated views do not rebuild for every system update. Stopwatch display updates run only when that tool is visible. The tiny session screen-time sampler is separate from UI rendering. Weather uses a retained adapter with a cache. External services do not require an always-running polling backend.

Notes, scratchpad, reminders, links and shelf references are local plaintext workspace data. Writes replace an existing file atomically only after successful bounded serialization. The same 10 MB serialized-size limit applies to writes and reads. A corrupt notebook is retained and its autosave is disabled until recovery, rather than being replaced with an empty workspace. Provider credentials are kept separately in Windows Credential Locker. Clipboard capture is off by default and memory-only when enabled.

The design deliberately avoids a web runtime, a second application process, a background HTTP server, a custom plugin execution host, and a billing service in the base app. Those mechanisms are unnecessary for the local interaction model. A future Linux interface can reuse the core and adapter contracts, while replacing the presentation and native service implementations.

Content animation runs through the compositor. Native outer-window resize is currently immediate, so shell morphing and measured smoothness are remaining Windows work. CPU, memory, startup time, input latency and long-running resource use must be measured on real Windows hardware using the release matrix. Framework choice alone is not a performance result.
