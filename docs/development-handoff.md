# Resume Notchling development here

Paused at the owner's request on 10 October 2026. Current source candidate: **v0.4.13**. Do not restart the design or expand scope; finish the two Windows qualification blockers below. All tools remain unlocked for public testing.

The latest completed run is [v0.4.12 qualification](https://github.com/Sury2797/Notchling/actions/runs/38045182796), source `890ff35df109581f543f397d476ca7a8fc9c7fba`. Both regression jobs passed 432 checks. All four branded installers, installations and architecture-matched native launches passed. Three x64/x86 jobs passed the preceding layout, help, tools and connection checks, but their real OLE drop source stalled before any app Shelf handoff. Native enumeration returned successfully, so enumeration alone was not the cause. ARM64 failed editor focus before actual typing. Full qualification failed; no new release was published.

Changes now saved for the next Windows run:

- `scripts/smoke-windows-shelf-drop.ps1`: a visible, owned WinForms source window receives actual injected MouseDown on its STA message loop before starting OLE. Real CF_HDROP/CF_DIB, Copy-only transfer, persisted file/image and decoded bitmap assertions remain required. PowerShell parsing and embedded C# compilation against .NET Framework 4.8 references passed; real Windows execution is pending.
- `scripts/smoke-windows-ui.ps1`: use the editor's actual UIA clickable point, accept its focused inner text peer only through owned ancestry, and verify the foreground process before real typing. PowerShell parsing and embedded C# compilation passed; ARM64 execution is pending.

Next: inspect the GitHub Actions run for the current main commit first. Diagnose only remaining failures; do not repeat completed broad audits. Keep all four native gates and both 432-check suites. If they pass, push the immutable `notchling-evaluation-0.4.13` tag to run the gated evaluation release workflow, then independently verify all three public EXE sizes, PE headers and SHA-256 hashes before promoting README download links. If they fail, record the exact stage in `flaws.md` and `docs/validation-notes.md` and fix the actual cause without weakening assertions.

The current qualified download remains **v0.3.4 x64**. Do not describe v0.4.13 as released or fully functional until qualified. Consumer Windows 10 Pro 22H2 build 19045.7725 retesting, physical display/accessibility/performance checks and live external-provider testing remain open. Production signing, billing, domain and licensed weather service are not configured; Linux native UI remains later work.
