# Resume Notchling development here

Paused at the owner's request on 10 October 2026. Current source candidate: **v0.4.13**. Do not restart the design or expand scope; finish the two Windows qualification blockers below. All tools remain unlocked for public testing.

The latest completed run is [v0.4.12 qualification](https://github.com/Sury2797/Notchling/actions/runs/38045182796), source `890ff35df109581f543f397d476ca7a8fc9c7fba`. Both regression jobs passed 432 checks. All four branded installers, installations and architecture-matched native launches passed. Three x64/x86 jobs passed the preceding layout, help, tools and connection checks, but their real OLE drop source stalled before any app Shelf handoff. Native enumeration returned successfully, so enumeration alone was not the cause. ARM64 failed editor focus before actual typing. Full qualification failed; no new release was published.

Changes now saved for the next Windows run:

- `scripts/smoke-windows-shelf-drop.ps1`: a visible, owned WinForms source window receives actual injected MouseDown on its STA message loop before starting OLE. Real CF_HDROP/CF_DIB, Copy-only transfer, persisted file/image and decoded bitmap assertions remain required. PowerShell parsing and embedded C# compilation against .NET Framework 4.8 references passed; real Windows execution is pending.
- `scripts/smoke-windows-ui.ps1`: use the editor's actual UIA clickable point, accept its focused inner text peer only through owned ancestry, and verify the foreground process before real typing. PowerShell parsing and embedded C# compilation passed; ARM64 execution is pending.

Next: inspect the GitHub Actions run for application-code commit `1a8b0b2e1c1aff9e47b8da80eeffce9f38053703` first; subsequent documentation-only checkpoint updates may skip CI. Diagnose only remaining failures; do not repeat completed broad audits. Keep all four native gates and both 432-check suites. If they pass, push the immutable `notchling-evaluation-0.4.13` tag to run the gated evaluation release workflow, then independently verify all three public EXE sizes, PE headers and SHA-256 hashes before promoting README download links. If they fail, record the exact stage in `flaws.md` and `docs/validation-notes.md` and fix the actual cause without weakening assertions.

The current qualified download remains **v0.3.4 x64**. Do not describe v0.4.13 as released or fully functional until qualified. Consumer Windows 10 Pro 22H2 build 19045.7725 retesting, physical display/accessibility/performance checks and live external-provider testing remain open. Production signing, billing, domain and licensed weather service are not configured; Linux native UI remains later work.

## Owner-requested next feature: real AI usage monitoring

After the current qualification blockers, implement automatic local discovery and loading of actual Claude, Codex and other supported AI/LLM usage into Notchling. This is a saved implementation request, not an implemented feature.

- Investigate each provider's supported official usage APIs and documented local CLI/app telemetry. Automatically discover supported local installations and usage sources; use authorized, read-only access and explicit account connection where required. Keep credentials protected and out of logs.
- Display only real measurements from the identified source. Never simulate usage, invent remaining percentages or infer subscription quota from local token counts. Distinguish local session/token usage, API billing usage and account subscription limits; these are different measurements.
- Show remaining allowance, usage windows and reset times only when the source actually supplies them. Unsupported providers or unavailable account quotas must say unavailable or connection required; missing data must not appear as zero usage. Mark stale data and show its source and last successful refresh.
- Refresh automatically with efficient background loading, caching, rate-limit backoff and recovery from offline/authentication failures. Keep the island responsive and provide a manual refresh action.
- Build a precise, polished native usage panel with consistent provider icons, aligned counters and readable progress/reset information across supported display sizes and scaling. Verify values against actual provider/local records and test failure states before claiming an integration works.
