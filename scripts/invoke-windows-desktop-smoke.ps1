# WinUI external drag/drop requires a normal-user desktop. GitHub's elevated
# runner must launch the complete unchanged smoke tree with its own limited token.
[CmdletBinding()]
param(
    [ValidateSet('App', 'UpdateHandoff')][string]$Mode = 'App',
    [string]$AppPath,
    [string]$SetupPath,
    [Parameter(Mandatory)][ValidateSet('x64', 'x86', 'arm64')][string]$AppArchitecture,
    [ValidateSet('x64', 'x86', 'ARM64')][string]$Platform,
    [string]$AssetsPath = 'src/Notch.Windows/obj/project.assets.json',
    [string]$ReportPath = 'artifacts/windows-smoke.json',
    [string]$ReportDirectory = 'artifacts',
    [switch]$InstallRuntimeFromNuGet
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if (-not $IsWindows -or $env:GITHUB_ACTIONS -ne 'true') { throw 'Normal-user desktop qualification requires a disposable Windows Actions host.' }
if ($Mode -eq 'App' -and [string]::IsNullOrWhiteSpace($AppPath)) { throw 'App mode requires the installed AppPath.' }
if ($Mode -eq 'UpdateHandoff' -and ([string]::IsNullOrWhiteSpace($SetupPath) -or [string]::IsNullOrWhiteSpace($Platform))) { throw 'UpdateHandoff mode requires SetupPath and Platform.' }
$reportFile = if ($Mode -eq 'App') { [IO.Path]::GetFullPath($ReportPath) } else { Join-Path ([IO.Path]::GetFullPath($ReportDirectory)) 'windows-update-handoff.json' }
$contextName = if ($Mode -eq 'App') { 'desktop-smoke-context.json' } else { 'desktop-update-handoff-context.json' }
$contextFile = Join-Path ([IO.Path]::GetDirectoryName($reportFile)) $contextName
[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($reportFile)) | Out-Null
if (Test-Path -LiteralPath $reportFile) { Remove-Item -LiteralPath $reportFile }
$context = [ordered]@{
    Succeeded = $false
    Mode = $Mode
    Scope = 'Same-account/session observed medium-integrity CI tree, using an existing limited token or a separately labelled own restricted fixture. No assertion bypass, application/host privilege changes or external process changes. Consumer-token behavior is not certified.'
    Runner = $null
    Launched = $null
    TokenSource = $null
    Desktop = $null
    ChildProcessId = $null
    ChildExitCode = $null
    FreshSmokeReport = $false
    Error = $null
}
$ticket = $null
$failure = $null
try {
    Add-Type -TypeDefinition @'
using System;
using System.ComponentModel;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
namespace NotchlingDesktopSmoke {
    public sealed class SecurityState {
        internal string Sid;
        public uint IntegrityRid;
        public bool Elevated;
        public int ElevationType;
        public int SessionId;
        public bool AdministratorDenyOnly;
        public bool HasRestrictions;
        public string Integrity { get { return IntegrityRid == 8192 ? "Medium" : IntegrityRid >= 16384 ? "System" : IntegrityRid >= 12288 ? "High" : "Other"; } }
    }
    public sealed class Launch : IDisposable {
        internal IntPtr Process, Thread;
        public uint ProcessId;
        internal bool Begun;
        public SecurityState Runner, Child;
        public string TokenSource, Desktop;
        public void Resume() { if (Native.ResumeThread(Thread) == UInt32.MaxValue) throw new Win32Exception(Marshal.GetLastWin32Error(), "Resuming the owned medium-integrity smoke failed."); Begun = true; }
        public int Wait(uint milliseconds) {
            uint state = Native.WaitForSingleObject(Process, milliseconds);
            if (state == 258) {
                // Only this newly created test child is owned by the wrapper.
                // Its normal smoke script owns cleanup of its app/helpers.
                Native.TerminateProcess(Process, 1);
                throw new TimeoutException("The owned normal-user smoke exceeded its " + (milliseconds / 1000) + "-second watchdog.");
            }
            if (state != 0) throw new Win32Exception(Marshal.GetLastWin32Error(), "Waiting for the owned smoke process failed.");
            uint code; if (!Native.GetExitCodeProcess(Process, out code)) throw new Win32Exception(Marshal.GetLastWin32Error());
            return unchecked((int)code);
        }
        public void Dispose() {
            if (!Begun && Process != IntPtr.Zero) Native.TerminateProcess(Process, 1);
            if (Thread != IntPtr.Zero) { Native.CloseHandle(Thread); Thread = IntPtr.Zero; }
            if (Process != IntPtr.Zero) { Native.CloseHandle(Process); Process = IntPtr.Zero; }
        }
    }
    public static class Native {
        private const uint QueryDuplicate = 0x0008 | 0x0002;
        [StructLayout(LayoutKind.Sequential, CharSet=CharSet.Unicode)] private struct StartupInfo {
            public uint Size; public string Reserved, Desktop, Title;
            public uint X, Y, XSize, YSize, XChars, YChars, Fill, Flags;
            public ushort Show, ReservedSize;
            public IntPtr ReservedData, Input, Output, Error;
        }
        [StructLayout(LayoutKind.Sequential)] private struct ProcessInformation { public IntPtr Process, Thread; public uint ProcessId, ThreadId; }
        [StructLayout(LayoutKind.Sequential)] private struct SidAndAttributes { public IntPtr Sid; public uint Attributes; }
        [DllImport("kernel32.dll")] private static extern IntPtr GetCurrentProcess();
        [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
        [DllImport("kernel32.dll", SetLastError=true)] private static extern IntPtr OpenProcess(uint access, bool inherit, uint process);
        [DllImport("advapi32.dll", SetLastError=true)] private static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);
        [DllImport("advapi32.dll", SetLastError=true)] private static extern bool GetTokenInformation(IntPtr token, int informationClass, IntPtr information, int size, out int returned);
        [DllImport("advapi32.dll", SetLastError=true)] private static extern bool DuplicateTokenEx(IntPtr token, uint access, IntPtr attributes, int level, int type, out IntPtr duplicate);
        [DllImport("advapi32.dll", SetLastError=true)] private static extern bool CreateRestrictedToken(IntPtr token, uint flags, uint disabledCount,
            [In, MarshalAs(UnmanagedType.LPArray, SizeParamIndex=2)] SidAndAttributes[] disabledSids,
            uint deletedPrivilegeCount, IntPtr deletedPrivileges, uint restrictedSidCount, IntPtr restrictedSids, out IntPtr restrictedToken);
        [DllImport("advapi32.dll", EntryPoint="ConvertStringSidToSidW", CharSet=CharSet.Unicode, SetLastError=true)] private static extern bool ConvertStringSidToSid(string text, out IntPtr sid);
        [DllImport("advapi32.dll", SetLastError=true)] private static extern bool SetTokenInformation(IntPtr token, int informationClass, IntPtr information, int size);
        [DllImport("advapi32.dll")] private static extern bool EqualSid(IntPtr left, IntPtr right);
        [DllImport("advapi32.dll")] private static extern uint GetLengthSid(IntPtr sid);
        [DllImport("advapi32.dll", EntryPoint="ConvertSidToStringSidW", SetLastError=true)] private static extern bool ConvertSidToStringSid(IntPtr sid, out IntPtr text);
        [DllImport("advapi32.dll")] private static extern IntPtr GetSidSubAuthorityCount(IntPtr sid);
        [DllImport("advapi32.dll")] private static extern IntPtr GetSidSubAuthority(IntPtr sid, uint index);
        [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr memory);
        [DllImport("user32.dll")] private static extern IntPtr GetProcessWindowStation();
        [DllImport("user32.dll")] private static extern IntPtr GetThreadDesktop(uint thread);
        [DllImport("user32.dll", EntryPoint="GetUserObjectInformationW", CharSet=CharSet.Unicode, SetLastError=true)] private static extern bool GetUserObjectInformation(IntPtr handle, int index, StringBuilder value, uint size, out uint required);
        [DllImport("advapi32.dll", EntryPoint="CreateProcessWithTokenW", CharSet=CharSet.Unicode, SetLastError=true)] private static extern bool CreateProcessWithToken(IntPtr token, uint logonFlags, string application, StringBuilder command, uint flags, IntPtr environment, string directory, ref StartupInfo startup, out ProcessInformation process);
        [DllImport("kernel32.dll", EntryPoint="CreateProcessW", CharSet=CharSet.Unicode, SetLastError=true)] private static extern bool CreateProcess(string application, StringBuilder command, IntPtr processAttributes, IntPtr threadAttributes, bool inherit, uint flags, IntPtr environment, string directory, ref StartupInfo startup, out ProcessInformation process);
        [DllImport("kernel32.dll", SetLastError=true)] internal static extern uint ResumeThread(IntPtr thread);
        [DllImport("kernel32.dll", SetLastError=true)] internal static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);
        [DllImport("kernel32.dll", SetLastError=true)] internal static extern bool GetExitCodeProcess(IntPtr process, out uint code);
        [DllImport("kernel32.dll", SetLastError=true)] internal static extern bool TerminateProcess(IntPtr process, uint code);
        [DllImport("kernel32.dll")] internal static extern bool CloseHandle(IntPtr handle);
        private static IntPtr Information(IntPtr token, int kind) {
            int size; GetTokenInformation(token, kind, IntPtr.Zero, 0, out size);
            if (size <= 0 || size > 65536) throw new Win32Exception(Marshal.GetLastWin32Error(), "Token information is unavailable or exceeds its bound.");
            IntPtr buffer = Marshal.AllocHGlobal(size);
            if (!GetTokenInformation(token, kind, buffer, size, out size)) { int error = Marshal.GetLastWin32Error(); Marshal.FreeHGlobal(buffer); throw new Win32Exception(error); }
            return buffer;
        }
        private static int Integer(IntPtr token, int kind) {
            IntPtr buffer = Information(token, kind);
            try { return Marshal.ReadInt32(buffer); } finally { Marshal.FreeHGlobal(buffer); }
        }
        private static SecurityState State(IntPtr token) {
            var value = new SecurityState { Elevated = Integer(token, 20) != 0, ElevationType = Integer(token, 18), SessionId = Integer(token, 12),
                AdministratorDenyOnly = AdminDenyOnly(token), HasRestrictions = Integer(token, 21) != 0 };
            IntPtr user = Information(token, 1), integrity = IntPtr.Zero, sidText = IntPtr.Zero;
            try {
                if (!ConvertSidToStringSid(Marshal.ReadIntPtr(user), out sidText)) throw new Win32Exception(Marshal.GetLastWin32Error());
                value.Sid = Marshal.PtrToStringUni(sidText);
                integrity = Information(token, 25);
                IntPtr sid = Marshal.ReadIntPtr(integrity);
                byte count = Marshal.ReadByte(GetSidSubAuthorityCount(sid));
                if (count == 0) throw new InvalidOperationException("The token has no integrity authority.");
                value.IntegrityRid = unchecked((uint)Marshal.ReadInt32(GetSidSubAuthority(sid, (uint)count - 1)));
                return value;
            } finally { if (sidText != IntPtr.Zero) LocalFree(sidText); if (integrity != IntPtr.Zero) Marshal.FreeHGlobal(integrity); Marshal.FreeHGlobal(user); }
        }
        private static bool AdminDenyOnly(IntPtr token) {
            IntPtr administrator = IntPtr.Zero, groups = IntPtr.Zero;
            try {
                if (!ConvertStringSidToSid("S-1-5-32-544", out administrator)) throw new Win32Exception(Marshal.GetLastWin32Error());
                groups = Information(token, 2);
                int count = Marshal.ReadInt32(groups), size = Marshal.SizeOf(typeof(SidAndAttributes));
                if (count < 0 || count > 4096) throw new InvalidOperationException("The observed token group count exceeds its bound.");
                for (int index = 0; index < count; index++) {
                    var group = (SidAndAttributes)Marshal.PtrToStructure(IntPtr.Add(groups, IntPtr.Size + index * size), typeof(SidAndAttributes));
                    if (EqualSid(group.Sid, administrator)) return (group.Attributes & 0x10) != 0 && (group.Attributes & 0x4) == 0;
                }
                return false;
            } finally { if (groups != IntPtr.Zero) Marshal.FreeHGlobal(groups); if (administrator != IntPtr.Zero) LocalFree(administrator); }
        }
        private static IntPtr RestrictedFixtureToken(IntPtr runnerToken, SecurityState runner) {
            IntPtr administrator = IntPtr.Zero, restricted = IntPtr.Zero, primary = IntPtr.Zero, mediumSid = IntPtr.Zero, label = IntPtr.Zero;
            try {
                if (!ConvertStringSidToSid("S-1-5-32-544", out administrator)) throw new Win32Exception(Marshal.GetLastWin32Error());
                // Restrict only this owned CI tree. DISABLE_MAX_PRIVILEGE removes
                // all privileges except ChangeNotify; LUA_TOKEN creates a limited
                // user-account token. No SANDBOX_INERT or policy-changing flags.
                if (!CreateRestrictedToken(runnerToken, 1 | 4, 1, new SidAndAttributes[] { new SidAndAttributes { Sid = administrator } },
                    0, IntPtr.Zero, 0, IntPtr.Zero, out restricted)) throw new Win32Exception(Marshal.GetLastWin32Error(), "Deriving the own-runner restricted CI fixture token failed.");
                // The source handle can have query/duplicate rights only. Give
                // this newly derived primary token explicit adjust-default rights
                // before lowering its mandatory label, without changing the runner.
                if (!DuplicateTokenEx(restricted, 0x0008 | 0x0002 | 0x0001 | 0x0080, IntPtr.Zero, 2, 1, out primary))
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "Duplicating the restricted fixture primary token failed.");
                if (!ConvertStringSidToSid("S-1-16-8192", out mediumSid)) throw new Win32Exception(Marshal.GetLastWin32Error());
                int structureSize = Marshal.SizeOf(typeof(SidAndAttributes)), sidSize = checked((int)GetLengthSid(mediumSid));
                if (sidSize <= 0 || sidSize > 68) throw new InvalidOperationException("The requested medium integrity SID exceeds its bound.");
                label = Marshal.AllocHGlobal(structureSize + sidSize);
                var sidBytes = new byte[sidSize]; Marshal.Copy(mediumSid, sidBytes, 0, sidSize);
                IntPtr copiedSid = IntPtr.Add(label, structureSize); Marshal.Copy(sidBytes, 0, copiedSid, sidSize);
                Marshal.StructureToPtr(new SidAndAttributes { Sid = copiedSid, Attributes = 0x20 }, label, false); // SE_GROUP_INTEGRITY
                if (!SetTokenInformation(primary, 25, label, structureSize + sidSize)) throw new Win32Exception(Marshal.GetLastWin32Error(), "Lowering the own restricted fixture integrity label failed.");
                SecurityState observed = State(primary);
                if (!Suitable(observed, runner) || !observed.AdministratorDenyOnly)
                    throw new InvalidOperationException("The derived CI fixture did not satisfy observed privilege guards: " + DescribeCandidate("Restricted fixture", observed, runner) + ", Administrators deny-only=" + observed.AdministratorDenyOnly + ", hasRestrictions=" + observed.HasRestrictions + ". No elevation flag is assumed or changed.");
                IntPtr result = primary; primary = IntPtr.Zero; return result;
            } finally {
                if (label != IntPtr.Zero) Marshal.FreeHGlobal(label); if (mediumSid != IntPtr.Zero) LocalFree(mediumSid);
                if (primary != IntPtr.Zero) CloseHandle(primary); if (restricted != IntPtr.Zero) CloseHandle(restricted);
                if (administrator != IntPtr.Zero) LocalFree(administrator);
            }
        }
        private static bool Suitable(SecurityState candidate, SecurityState runner) {
            return candidate.IntegrityRid == 8192 && !candidate.Elevated && candidate.SessionId == runner.SessionId && String.Equals(candidate.Sid, runner.Sid, StringComparison.Ordinal);
        }
        private static string DescribeCandidate(string label, SecurityState candidate, SecurityState runner) {
            return label + ": rid=" + candidate.IntegrityRid + ", elevated=" + candidate.Elevated +
                ", session=" + candidate.SessionId + ", sameSession=" + (candidate.SessionId == runner.SessionId) +
                ", sameAccount=" + String.Equals(candidate.Sid, runner.Sid, StringComparison.Ordinal);
        }
        public static SecurityState CurrentSecurity() {
            IntPtr token; if (!OpenProcessToken(GetCurrentProcess(), QueryDuplicate, out token)) throw new Win32Exception(Marshal.GetLastWin32Error());
            try { return State(token); } finally { CloseHandle(token); }
        }
        private static IntPtr LimitedToken(IntPtr runnerToken, SecurityState runner, out string source) {
            source = "No eligible token";
            var observations = new List<string>();
            IntPtr linked = IntPtr.Zero;
            try {
                IntPtr buffer = Information(runnerToken, 19);
                try { linked = Marshal.ReadIntPtr(buffer); } finally { Marshal.FreeHGlobal(buffer); }
                if (linked != IntPtr.Zero) {
                    SecurityState state = State(linked);
                    observations.Add(DescribeCandidate("Linked token", state, runner));
                    if (Suitable(state, runner)) { source = "Own linked limited token; account and session verified"; return linked; }
                } else observations.Add("Linked token: no handle returned");
            } catch (Win32Exception error) { observations.Add("Linked token: unavailable (Win32 " + error.NativeErrorCode + ")"); }
            if (linked != IntPtr.Zero) CloseHandle(linked);
            var explorers = Process.GetProcessesByName("explorer");
            try {
                foreach (var explorer in explorers) {
                    if (observations.Count >= 8) break;
                    if (explorer.SessionId != runner.SessionId) { observations.Add("Explorer " + explorer.Id + ": session=" + explorer.SessionId + ", sameSession=False; token not inspected"); continue; }
                    IntPtr process = OpenProcess(0x1000, false, unchecked((uint)explorer.Id));
                    if (process == IntPtr.Zero) { observations.Add("Explorer " + explorer.Id + ": process query unavailable (Win32 " + Marshal.GetLastWin32Error() + ")"); continue; }
                    IntPtr token = IntPtr.Zero;
                    try {
                        if (OpenProcessToken(process, QueryDuplicate, out token)) {
                            SecurityState state = State(token);
                            observations.Add(DescribeCandidate("Explorer " + explorer.Id, state, runner));
                            if (Suitable(state, runner)) {
                                source = "Interactive Explorer token; account and session verified";
                                IntPtr selected = token; token = IntPtr.Zero; return selected;
                            }
                        }
                        else observations.Add("Explorer " + explorer.Id + ": token query unavailable (Win32 " + Marshal.GetLastWin32Error() + ")");
                    } catch (Win32Exception error) { observations.Add("Explorer " + explorer.Id + ": token observation unavailable (Win32 " + error.NativeErrorCode + ")"); }
                    finally { if (token != IntPtr.Zero) CloseHandle(token); CloseHandle(process); }
                }
            } finally { foreach (var explorer in explorers) explorer.Dispose(); }
            if (explorers.Length == 0) observations.Add("No interactive Explorer process exists");
            try {
                IntPtr fixture = RestrictedFixtureToken(runnerToken, runner);
                source = "Own restricted CI fixture token (DISABLE_MAX_PRIVILEGE | LUA_TOKEN); observed medium/non-elevated, same account/session, Administrators deny-only; consumer token not certified";
                return fixture;
            } catch (Exception error) {
                throw new InvalidOperationException("No eligible existing normal-user token and no valid own restricted CI fixture token. Runner rid=" + runner.IntegrityRid + ", elevated=" + runner.Elevated + ", session=" + runner.SessionId + ". Observed existing candidates: " + String.Join(" | ", observations.ToArray()) + ". Restriction result: " + error.Message, error);
            }
        }
        private static string ObjectName(IntPtr handle) {
            var value = new StringBuilder(256); uint size;
            if (handle == IntPtr.Zero || !GetUserObjectInformation(handle, 2, value, (uint)value.Capacity * 2, out size)) throw new Win32Exception(Marshal.GetLastWin32Error(), "The caller desktop identity could not be observed.");
            return value.ToString();
        }
        public static string Quote(string value) {
            if (value == null || value.IndexOf('\0') >= 0) throw new ArgumentException("An owned launch argument contains an invalid character.");
            var result = new StringBuilder("\""); int slashes = 0;
            foreach (char character in value) {
                if (character == '\\') { slashes++; continue; }
                if (character == '"') result.Append('\\', slashes * 2 + 1).Append('"');
                else result.Append('\\', slashes).Append(character);
                slashes = 0;
            }
            return result.Append('\\', slashes * 2).Append('"').ToString();
        }
        public static Launch Start(string application, string arguments, string directory, string environment) {
            IntPtr runnerToken = IntPtr.Zero, selected = IntPtr.Zero, primary = IntPtr.Zero, block = IntPtr.Zero;
            ProcessInformation process = new ProcessInformation(); bool created = false;
            try {
                if (!OpenProcessToken(GetCurrentProcess(), QueryDuplicate, out runnerToken)) throw new Win32Exception(Marshal.GetLastWin32Error());
                SecurityState runner = State(runnerToken); string source = "Current normal-user token";
                if (!Suitable(runner, runner)) {
                    selected = LimitedToken(runnerToken, runner, out source);
                    if (!DuplicateTokenEx(selected, 0x02000000, IntPtr.Zero, 2, 1, out primary)) throw new Win32Exception(Marshal.GetLastWin32Error(), "The verified limited token could not be duplicated for the owned test child.");
                    SecurityState duplicated = State(primary);
                    if (!Suitable(duplicated, runner) || source.StartsWith("Own restricted CI fixture token", StringComparison.Ordinal) && !duplicated.AdministratorDenyOnly)
                        throw new InvalidOperationException("Duplicating the selected token changed the required account/session/integrity/admin-deny-only guards.");
                }
                string desktop = ObjectName(GetProcessWindowStation()) + "\\" + ObjectName(GetThreadDesktop(GetCurrentThreadId()));
                var startup = new StartupInfo { Size = (uint)Marshal.SizeOf(typeof(StartupInfo)), Desktop = desktop };
                var command = new StringBuilder(Quote(application) + " " + arguments);
                block = Marshal.StringToHGlobalUni(environment);
                const uint flags = 0x00000400 | 0x08000000 | 0x00000004; // Unicode environment, no console, suspended for verification.
                created = primary == IntPtr.Zero
                    ? CreateProcess(application, command, IntPtr.Zero, IntPtr.Zero, false, flags, block, directory, ref startup, out process)
                    : CreateProcessWithToken(primary, 1, application, command, flags, block, directory, ref startup, out process);
                if (!created) throw new Win32Exception(Marshal.GetLastWin32Error(), "Creating the owned normal-user Windows smoke failed.");
                IntPtr childToken;
                if (!OpenProcessToken(process.Process, 0x0008, out childToken)) throw new Win32Exception(Marshal.GetLastWin32Error(), "The suspended child token could not be observed.");
                SecurityState child;
                try { child = State(childToken); } finally { CloseHandle(childToken); }
                if (!Suitable(child, runner) || source.StartsWith("Own restricted CI fixture token", StringComparison.Ordinal) && !child.AdministratorDenyOnly)
                    throw new InvalidOperationException("The actual suspended child is not the required same-account/session medium, non-elevated process with the selected administrator restriction.");
                var launch = new Launch { Process = process.Process, Thread = process.Thread, ProcessId = process.ProcessId, Runner = runner, Child = child, TokenSource = source, Desktop = desktop };
                created = false; return launch; // ownership transfers to Launch
            } finally {
                if (created) { TerminateProcess(process.Process, 1); CloseHandle(process.Thread); CloseHandle(process.Process); }
                if (block != IntPtr.Zero) Marshal.FreeHGlobal(block);
                if (primary != IntPtr.Zero) CloseHandle(primary); if (selected != IntPtr.Zero) CloseHandle(selected); if (runnerToken != IntPtr.Zero) CloseHandle(runnerToken);
            }
        }
    }
}
'@
    $context.Runner = [NotchlingDesktopSmoke.Native]::CurrentSecurity()
    if ($Mode -eq 'App') {
        $script = Join-Path $PSScriptRoot 'smoke-windows.ps1'
        $shell = [Diagnostics.Process]::GetCurrentProcess().MainModule.FileName
        if ([IO.Path]::GetFileName($shell) -ne 'pwsh.exe') { throw 'The complete native smoke requires the same PowerShell 7 host as qualification.' }
        $arguments = @('-NoLogo', '-NoProfile', '-NonInteractive', '-ExecutionPolicy', 'Bypass', '-File', $script,
            '-AppPath', [IO.Path]::GetFullPath($AppPath), '-AppArchitecture', $AppArchitecture,
            '-AssetsPath', [IO.Path]::GetFullPath($AssetsPath), '-ReportPath', $reportFile)
        if ($InstallRuntimeFromNuGet) { $arguments += '-InstallRuntimeFromNuGet' }
    } else {
        $script = Join-Path $PSScriptRoot 'smoke-windows-update-handoff.ps1'
        $shell = Join-Path ([Environment]::GetFolderPath('System')) 'WindowsPowerShell/v1.0/powershell.exe'
        $arguments = @('-NoLogo', '-NoProfile', '-NonInteractive', '-Mta', '-ExecutionPolicy', 'Bypass', '-File', $script,
            '-SetupPath', [IO.Path]::GetFullPath($SetupPath), '-AppArchitecture', $AppArchitecture,
            '-Platform', $Platform, '-ReportDirectory', [IO.Path]::GetFullPath($ReportDirectory))
    }
    $quotedArguments = @($arguments | ForEach-Object { [NotchlingDesktopSmoke.Native]::Quote([string]$_) }) -join ' '
    # Preserve exact inherited variables, including GITHUB_ACTIONS, architecture
    # configuration and the deliberate DOTNET_ROOT launch sentinel. Never log it.
    $variables = [Environment]::GetEnvironmentVariables()
    $entries = [string[]]@($variables.Keys | ForEach-Object { [string]$_ + '=' + [string]$variables[$_] })
    [Array]::Sort($entries, [StringComparer]::OrdinalIgnoreCase)
    $environmentBlock = [string]::Join([char]0, [string[]]$entries) + [char]0 + [char]0
    $started = [DateTime]::UtcNow
    $ticket = [NotchlingDesktopSmoke.Native]::Start($shell, $quotedArguments, (Get-Location).Path, $environmentBlock)
    $context.Launched = $ticket.Child
    $context.TokenSource = $ticket.TokenSource
    $context.Desktop = $ticket.Desktop
    $context.ChildProcessId = $ticket.ProcessId
    $ticket.Resume()
    $watchdog = if ($Mode -eq 'App') { 360000 } else { 540000 }
    $context.ChildExitCode = $ticket.Wait($watchdog)
    if (-not (Test-Path -LiteralPath $reportFile -PathType Leaf)) { throw "The owned normal-user smoke exited $($context.ChildExitCode) without its required report." }
    $file = Get-Item -LiteralPath $reportFile
    if ($file.Length -gt 5MB -or $file.LastWriteTimeUtc -lt $started.AddSeconds(-2)) { throw 'The owned smoke report is stale or exceeds its bound.' }
    $context.FreshSmokeReport = $true
    $smoke = Get-Content -LiteralPath $reportFile -Raw | ConvertFrom-Json
    $summary = "Mode=$Mode; runner integrity=$($context.Runner.IntegrityRid)/elevated=$($context.Runner.Elevated); child integrity=$($context.Launched.IntegrityRid)/elevated=$($context.Launched.Elevated); token=$($context.TokenSource); same session=$($context.Launched.SessionId); desktop=$($context.Desktop); child exit=$($context.ChildExitCode); succeeded=$($smoke.Succeeded)."
    if ($Mode -eq 'App') {
        $toolProperty = $smoke.UIInteractions.PSObject.Properties['ToolsOpened']
        $toolCount = if ($toolProperty) { @($toolProperty.Value).Count } else { 0 }
        $summary += " App integrity=$($smoke.AppIntegrityRid)/elevated=$($smoke.AppElevated); architecture=$($smoke.ActualProcessArchitecture); tools=$toolCount."
    }
    else { $summary += " GracefulShutdown=$($smoke.GracefulShutdown); ActualInnoUpgrade=$($smoke.ActualInnoUpgrade); ActualRelaunch=$($smoke.ActualRelaunch); WorkspaceRetained=$($smoke.WorkspaceRetained); OriginalInstallerUnchanged=$($smoke.OriginalInstallerUnchanged)." }
    $encoded = $summary.Replace('%', '%25').Replace("`r", '%0D').Replace("`n", '%0A')
    Write-Output "::notice title=Normal-user Windows desktop qualification::$encoded"
    $valid = $context.ChildExitCode -eq 0 -and $smoke.Succeeded
    if ($Mode -eq 'App') { $valid = $valid -and $smoke.AppIntegrityRid -eq 8192 -and -not $smoke.AppElevated }
    else {
        foreach ($field in @('TemporarySignatureCopyOnly', 'TrustedPublisherVerified', 'GracefulShutdown', 'ActualInnoUpgrade', 'ActualRelaunch', 'WorkspaceRetained', 'OriginalInstallerUnchanged')) {
            if (-not $smoke.$field) { $valid = $false }
        }
    }
    if (-not $valid) {
        $detail = [string]$smoke.Error
        if ($detail.Length -gt 4000) { $detail = $detail.Substring(0, 4000) }
        throw "Normal-user Windows smoke failed (real child exit $($context.ChildExitCode)): $detail"
    }
    $context.Succeeded = $true
} catch {
    $failure = $_
    $context.Error = $_.Exception.Message
} finally {
    if ($ticket) { $ticket.Dispose() }
    $context | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $contextFile -Encoding utf8
}
if ($failure) { throw $failure }
