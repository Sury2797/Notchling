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
    [switch]$InstallRuntimeFromNuGet,
    [switch]$PrerequisitesVerifiedBySetup
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if (-not $IsWindows -or $env:GITHUB_ACTIONS -ne 'true') { throw 'Normal-user desktop qualification requires a disposable Windows Actions host.' }
if ($Mode -eq 'App' -and [string]::IsNullOrWhiteSpace($AppPath)) { throw 'App mode requires the installed AppPath.' }
if ($Mode -eq 'UpdateHandoff' -and ([string]::IsNullOrWhiteSpace($SetupPath) -or [string]::IsNullOrWhiteSpace($Platform))) { throw 'UpdateHandoff mode requires SetupPath and Platform.' }
$reportFile = if ($Mode -eq 'App') { [IO.Path]::GetFullPath($ReportPath) } else { Join-Path ([IO.Path]::GetFullPath($ReportDirectory)) 'windows-update-handoff.json' }
$contextName = if ($Mode -eq 'App') { 'desktop-smoke-context.json' } else { 'desktop-update-handoff-context.json' }
$contextFile = Join-Path ([IO.Path]::GetDirectoryName($reportFile)) $contextName
$streamPrefix = if ($Mode -eq 'App') { 'desktop-smoke' } else { 'desktop-update-handoff' }
$stdoutFile = Join-Path ([IO.Path]::GetDirectoryName($reportFile)) ($streamPrefix + '.stdout.log')
$stderrFile = Join-Path ([IO.Path]::GetDirectoryName($reportFile)) ($streamPrefix + '.stderr.log')
[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($reportFile)) | Out-Null
if (Test-Path -LiteralPath $reportFile) { Remove-Item -LiteralPath $reportFile }
$context = [ordered]@{
    Succeeded = $false
    Mode = $Mode
    Scope = 'Same-account/session observed medium-integrity CI tree, using an existing limited token or a separately labelled own restricted fixture. No assertion bypass, application/host privilege changes or external process changes. Consumer-token behavior and Explorer/tray integration on a shellless private desktop are not certified.'
    Runner = $null
    Launched = $null
    TokenSource = $null
    Desktop = $null
    ChildProcessId = $null
    ChildExitCode = $null
    LaunchApi = $null
    ResumePreviousSuspendCount = $null
    DiagnosticStage = 'Initialize owned launch'
    ChildDesktopAfterResume = $null
    DesktopRecovery = $null
    StdoutPath = $stdoutFile
    StderrPath = $stderrFile
    FreshSmokeReport = $false
    Error = $null
}
$ticket = $null
$failure = $null
function Read-OwnedDesktopProgress {
    $paths = @(
        (Join-Path ([IO.Path]::GetDirectoryName($reportFile)) 'windows-smoke-progress.log'),
        (Join-Path ([IO.Path]::GetDirectoryName($reportFile)) 'windows-smoke.json'),
        (Join-Path ([IO.Path]::GetDirectoryName($reportFile)) 'windows-update-handoff.json'),
        $stdoutFile,
        $stderrFile,
        (Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'Notchling/Diagnostics/startup.log')
    )
    $observations = @()
    foreach ($path in $paths) {
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { continue }
        try {
            $file = Get-Item -LiteralPath $path
            if ($file.LastWriteTimeUtc -lt $started.AddSeconds(-2)) { continue }
            if ($file.Length -gt 5MB) { $observations += "$(Split-Path -Leaf $path): exceeds bounded diagnostic size"; continue }
            $tail = (Get-Content -LiteralPath $path -Tail 10 -ErrorAction Stop) -join ' | '
            if ($tail.Length -gt 1000) { $tail = $tail.Substring($tail.Length - 1000) }
            $observations += "$(Split-Path -Leaf $path): $tail"
        } catch { $observations += "$(Split-Path -Leaf $path): unavailable ($($_.Exception.Message))" }
    }
    if ($observations.Count -eq 0) { return 'No owned progress/report/startup breadcrumb was produced.' }
    return $observations -join '; '
}
try {
    Add-Type -TypeDefinition @'
using System;
using System.ComponentModel;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
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
    public sealed class DesktopRecovery {
        internal IntPtr Owned, OriginalInput;
        internal bool CancelPreparation, PreparationFinished;
        public string Station, OriginalInputName, PrivateName;
        public bool StationOperationalAccess, PrivateOperationalAccess, InputSwitched, InputRestored;
        public uint PrivateIntegrityRid;
        public string DefaultDesktopAccess, RestoreError;
    }
    public sealed class Launch : IDisposable {
        internal IntPtr Process, Thread, Job;
        internal IntPtr StandardInput, StandardOutput, StandardError;
        public uint ProcessId, PrimaryThreadId;
        public string ChildDesktopAfterResume;
        public uint PreviousSuspendCount = UInt32.MaxValue;
        internal bool Begun;
        public SecurityState Runner, Child;
        public string TokenSource, Desktop, LaunchApi;
        public DesktopRecovery Recovery;
        public void Resume() {
            if (Recovery != null) Native.ActivateOwnedDesktop(Recovery);
            PreviousSuspendCount = Native.ResumeThread(Thread);
            if (PreviousSuspendCount == UInt32.MaxValue) throw new Win32Exception(Marshal.GetLastWin32Error(), "Resuming the owned medium-integrity smoke failed.");
            if (PreviousSuspendCount != 1) throw new InvalidOperationException("The owned primary thread returned unexpected previous suspension count " + PreviousSuspendCount + "; expected exactly 1. No additional resume or privilege change is attempted.");
            Begun = true;
            ChildDesktopAfterResume = Native.ObserveOwnedChildDesktop(PrimaryThreadId);
        }
        public string ExecutionSnapshot() {
            try {
                using (var process = System.Diagnostics.Process.GetProcessById(checked((int)ProcessId))) {
                    string image;
                    try { image = Path.GetFileName(process.MainModule.FileName); } catch (Exception error) { image = "unavailable(" + error.GetType().Name + ")"; }
                    var states = new List<string>();
                    foreach (ProcessThread thread in process.Threads) {
                        try { states.Add(thread.Id + ":" + thread.ThreadState + (thread.ThreadState == ThreadState.Wait ? "/" + thread.WaitReason : "")); }
                        catch (Exception error) { states.Add("unavailable(" + error.GetType().Name + ")"); }
                        finally { thread.Dispose(); }
                        if (states.Count >= 4) break;
                    }
                    return "pid=" + ProcessId + ", image=" + image + ", cpuMs=" + process.TotalProcessorTime.TotalMilliseconds +
                        ", threads=" + process.Threads.Count + ", sampledThreadStates=" + String.Join(" | ", states.ToArray());
                }
            } catch (Exception error) { return "Owned process snapshot unavailable: " + error.GetType().Name + ": " + error.Message; }
        }
        public int Wait(uint milliseconds) {
            uint state = Native.WaitForSingleObject(Process, milliseconds);
            if (state == 258) {
                string execution = ExecutionSnapshot();
                // Only this newly created test child is owned by the wrapper.
                // Its normal smoke script owns cleanup of its app/helpers.
                // Closing this unnamed job ends only the created child and its
                // descendants, including any helper stalled before reporting.
                if (Job != IntPtr.Zero) { Native.CloseHandle(Job); Job = IntPtr.Zero; }
                throw new TimeoutException("The owned normal-user smoke exceeded its " + (milliseconds / 1000) + "-second watchdog. " + execution);
            }
            if (state != 0) throw new Win32Exception(Marshal.GetLastWin32Error(), "Waiting for the owned smoke process failed.");
            uint code; if (!Native.GetExitCodeProcess(Process, out code)) throw new Win32Exception(Marshal.GetLastWin32Error());
            return unchecked((int)code);
        }
        public void Dispose() {
            if (!Begun && Process != IntPtr.Zero) Native.TerminateProcess(Process, 1);
            if (Job != IntPtr.Zero) { Native.CloseHandle(Job); Job = IntPtr.Zero; }
            if (Process != IntPtr.Zero && Native.WaitForSingleObject(Process, 5000) == 258 && Recovery != null)
                Recovery.RestoreError = "The owned child did not finish termination within the five-second desktop cleanup bound.";
            if (Thread != IntPtr.Zero) { Native.CloseHandle(Thread); Thread = IntPtr.Zero; }
            if (Process != IntPtr.Zero) { Native.CloseHandle(Process); Process = IntPtr.Zero; }
            if (StandardInput != IntPtr.Zero) { Native.CloseHandle(StandardInput); StandardInput = IntPtr.Zero; }
            if (StandardOutput != IntPtr.Zero) { Native.CloseHandle(StandardOutput); StandardOutput = IntPtr.Zero; }
            if (StandardError != IntPtr.Zero) { Native.CloseHandle(StandardError); StandardError = IntPtr.Zero; }
            if (Recovery != null) Native.DisposeOwnedDesktop(Recovery);
        }
    }
    public static class Native {
        private const uint QueryDuplicate = 0x0008 | 0x0002;
        public static string LastLaunchStage = "Initialize owned native launch";
        public static DesktopRecovery LastDesktopRecovery;
        [StructLayout(LayoutKind.Sequential, CharSet=CharSet.Unicode)] private struct StartupInfo {
            public uint Size; public string Reserved, Desktop, Title;
            public uint X, Y, XSize, YSize, XChars, YChars, Fill, Flags;
            public ushort Show, ReservedSize;
            public IntPtr ReservedData, Input, Output, Error;
        }
        [StructLayout(LayoutKind.Sequential)] private struct ProcessInformation { public IntPtr Process, Thread; public uint ProcessId, ThreadId; }
        [StructLayout(LayoutKind.Sequential)] private struct SidAndAttributes { public IntPtr Sid; public uint Attributes; }
        [StructLayout(LayoutKind.Sequential)] private struct SecurityAttributes { public uint Size; public IntPtr Descriptor; public int Inherit; }
        [StructLayout(LayoutKind.Sequential)] private struct StartupInfoExtended { public StartupInfo Startup; public IntPtr Attributes; }
        [StructLayout(LayoutKind.Sequential)] private struct JobBasicLimits {
            public long ProcessTime, JobTime; public uint Flags; public UIntPtr MinimumWorkingSet, MaximumWorkingSet;
            public uint ActiveProcesses; public UIntPtr Affinity; public uint Priority, Scheduling;
        }
        [StructLayout(LayoutKind.Sequential)] private struct IoCounters { public ulong ReadOperations, WriteOperations, OtherOperations, ReadBytes, WriteBytes, OtherBytes; }
        [StructLayout(LayoutKind.Sequential)] private struct JobExtendedLimits { public JobBasicLimits Basic; public IoCounters Io; public UIntPtr ProcessMemory, JobMemory, PeakProcessMemory, PeakJobMemory; }
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
        [DllImport("advapi32.dll", EntryPoint="ConvertStringSecurityDescriptorToSecurityDescriptorW", CharSet=CharSet.Unicode, SetLastError=true)] private static extern bool ConvertSecurityDescriptor(string text, uint revision, out IntPtr descriptor, out uint size);
        [DllImport("advapi32.dll", SetLastError=true)] private static extern bool ImpersonateLoggedOnUser(IntPtr token);
        [DllImport("advapi32.dll", SetLastError=true)] private static extern bool RevertToSelf();
        [DllImport("advapi32.dll")] private static extern uint GetSecurityInfo(IntPtr handle, int objectType, uint information, out IntPtr owner, out IntPtr group, out IntPtr dacl, out IntPtr sacl, out IntPtr descriptor);
        [DllImport("advapi32.dll", SetLastError=true)] private static extern bool GetAce(IntPtr acl, uint index, out IntPtr ace);
        [DllImport("user32.dll", EntryPoint="OpenWindowStationW", CharSet=CharSet.Unicode, SetLastError=true)] private static extern IntPtr OpenWindowStation(string name, bool inherit, uint access);
        [DllImport("user32.dll", SetLastError=true)] private static extern bool CloseWindowStation(IntPtr station);
        [DllImport("user32.dll", EntryPoint="CreateDesktopW", CharSet=CharSet.Unicode, SetLastError=true)] private static extern IntPtr CreateDesktop(string name, IntPtr device, IntPtr mode, uint flags, uint access, ref SecurityAttributes attributes);
        [DllImport("user32.dll", EntryPoint="OpenDesktopW", CharSet=CharSet.Unicode, SetLastError=true)] private static extern IntPtr OpenDesktop(string name, uint flags, bool inherit, uint access);
        [DllImport("user32.dll", SetLastError=true)] private static extern IntPtr OpenInputDesktop(uint flags, bool inherit, uint access);
        [DllImport("user32.dll", SetLastError=true)] private static extern bool SetThreadDesktop(IntPtr desktop);
        [DllImport("user32.dll", SetLastError=true)] private static extern bool SwitchDesktop(IntPtr desktop);
        [DllImport("user32.dll", SetLastError=true)] private static extern bool CloseDesktop(IntPtr desktop);
        [DllImport("user32.dll", SetLastError=true)] private static extern IntPtr GetProcessWindowStation();
        [DllImport("user32.dll", SetLastError=true)] private static extern IntPtr GetThreadDesktop(uint thread);
        [DllImport("user32.dll", EntryPoint="GetUserObjectInformationW", CharSet=CharSet.Unicode, SetLastError=true)] private static extern bool GetUserObjectInformation(IntPtr handle, int index, StringBuilder value, uint size, out uint required);
        [DllImport("advapi32.dll", EntryPoint="CreateProcessWithTokenW", CharSet=CharSet.Unicode, SetLastError=true)] private static extern bool CreateProcessWithToken(IntPtr token, uint logonFlags, string application, StringBuilder command, uint flags, IntPtr environment, string directory, ref StartupInfo startup, out ProcessInformation process);
        [DllImport("kernel32.dll", EntryPoint="CreateProcessW", CharSet=CharSet.Unicode, SetLastError=true)] private static extern bool CreateProcess(string application, StringBuilder command, IntPtr processAttributes, IntPtr threadAttributes, bool inherit, uint flags, IntPtr environment, string directory, ref StartupInfo startup, out ProcessInformation process);
        [DllImport("kernel32.dll", EntryPoint="CreateProcessW", CharSet=CharSet.Unicode, SetLastError=true)] private static extern bool CreateProcessExtended(string application, StringBuilder command, IntPtr processAttributes, IntPtr threadAttributes, bool inherit, uint flags, IntPtr environment, string directory, ref StartupInfoExtended startup, out ProcessInformation process);
        [DllImport("advapi32.dll", EntryPoint="CreateProcessAsUserW", CharSet=CharSet.Unicode, SetLastError=true)] private static extern bool CreateProcessAsUser(IntPtr token, string application, StringBuilder command, IntPtr processAttributes, IntPtr threadAttributes, bool inherit, uint flags, IntPtr environment, string directory, ref StartupInfoExtended startup, out ProcessInformation process);
        [DllImport("kernel32.dll", EntryPoint="CreateFileW", CharSet=CharSet.Unicode, SetLastError=true)] private static extern IntPtr CreateFile(string path, uint access, uint share, ref SecurityAttributes attributes, uint disposition, uint flags, IntPtr template);
        [DllImport("kernel32.dll", SetLastError=true)] private static extern uint GetFileType(IntPtr handle);
        [DllImport("kernel32.dll", SetLastError=true)] private static extern bool InitializeProcThreadAttributeList(IntPtr list, int count, uint flags, ref IntPtr length);
        [DllImport("kernel32.dll", SetLastError=true)] private static extern bool UpdateProcThreadAttribute(IntPtr list, uint flags, UIntPtr attribute, IntPtr value, IntPtr size, IntPtr previous, IntPtr returned);
        [DllImport("kernel32.dll")] private static extern void DeleteProcThreadAttributeList(IntPtr list);
        [DllImport("kernel32.dll", SetLastError=true)] internal static extern uint ResumeThread(IntPtr thread);
        [DllImport("kernel32.dll", SetLastError=true)] internal static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);
        [DllImport("kernel32.dll", SetLastError=true)] internal static extern bool GetExitCodeProcess(IntPtr process, out uint code);
        [DllImport("kernel32.dll", SetLastError=true)] internal static extern bool TerminateProcess(IntPtr process, uint code);
        [DllImport("kernel32.dll")] internal static extern bool CloseHandle(IntPtr handle);
        [DllImport("kernel32.dll", EntryPoint="CreateJobObjectW", CharSet=CharSet.Unicode, SetLastError=true)] private static extern IntPtr CreateJobObject(IntPtr attributes, string name);
        [DllImport("kernel32.dll", SetLastError=true)] private static extern bool SetInformationJobObject(IntPtr job, int kind, ref JobExtendedLimits information, uint length);
        [DllImport("kernel32.dll", SetLastError=true)] private static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);
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
        private static uint ObserveDesktopIntegrity(IntPtr desktop) {
            IntPtr owner, group, dacl, sacl, descriptor;
            uint code = GetSecurityInfo(desktop, 7, 0x10, out owner, out group, out dacl, out sacl, out descriptor);
            if (code != 0) throw new Win32Exception(unchecked((int)code), "Reading the owned desktop mandatory label failed.");
            try {
                if (sacl == IntPtr.Zero) throw new InvalidOperationException("The owned desktop did not expose its explicit mandatory label.");
                ushort count = unchecked((ushort)Marshal.ReadInt16(sacl, 4));
                if (count == 0 || count > 16) throw new InvalidOperationException("The owned desktop mandatory ACE count exceeds its expected bound.");
                for (uint index = 0; index < count; index++) {
                    IntPtr ace;
                    if (!GetAce(sacl, index, out ace)) throw new Win32Exception(Marshal.GetLastWin32Error());
                    if (Marshal.ReadByte(ace) != 0x11) continue;
                    uint mask = unchecked((uint)Marshal.ReadInt32(ace, 4));
                    IntPtr sid = IntPtr.Add(ace, 8);
                    byte authorities = Marshal.ReadByte(GetSidSubAuthorityCount(sid));
                    if (authorities == 0 || (mask & 1) == 0) throw new InvalidOperationException("The owned desktop lacks its mandatory no-write-up policy.");
                    return unchecked((uint)Marshal.ReadInt32(GetSidSubAuthority(sid, (uint)authorities - 1)));
                }
                throw new InvalidOperationException("The owned desktop has no observed mandatory label ACE.");
            } finally { if (descriptor != IntPtr.Zero) LocalFree(descriptor); }
        }
        private static DesktopRecovery CreateOwnedDesktop(IntPtr primary, SecurityState runner, string stationName, string defaultName) {
            // A restricted child can fail USER32 initialization on Default even
            // after process creation succeeds. Never modify that desktop/station.
            // Only the own restricted Actions fixture receives a fresh private
            // desktop; all actual UI/OLE/typing checks remain in its active tree.
            var recovery = new DesktopRecovery { Station = stationName, PrivateName = "Notchling-CI-" + Guid.NewGuid().ToString("N") };
            LastDesktopRecovery = recovery;
            if (!String.Equals(stationName, "WinSta0", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The own UI fixture requires the already interactive WinSta0 station; no station or account configuration is changed.");
            recovery.OriginalInput = OpenInputDesktop(0, false, 0x181);
            if (recovery.OriginalInput == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error(), "Opening the exact prior input desktop for eventual restoration failed.");
            try { recovery.OriginalInputName = ObjectName(recovery.OriginalInput); }
            catch { DisposeOwnedDesktop(recovery); throw; }
            IntPtr workerToken;
            if (!DuplicateTokenEx(primary, 0x02000000, IntPtr.Zero, 2, 1, out workerToken)) {
                int tokenError = Marshal.GetLastWin32Error();
                DisposeOwnedDesktop(recovery);
                throw new Win32Exception(tokenError, "Duplicating the own fixture token for its isolated desktop access probe failed.");
            }
            Exception workerError = null;
            var worker = new System.Threading.Thread(delegate() {
                IntPtr descriptor = IntPtr.Zero, originalThreadDesktop = GetThreadDesktop(GetCurrentThreadId());
                bool impersonating = false;
                try {
                    LastLaunchStage = "Prove selected token operational station access without changing existing security";
                    if (!ImpersonateLoggedOnUser(workerToken)) throw new Win32Exception(Marshal.GetLastWin32Error(), "The own fixture station access probe could not impersonate its selected token.");
                    impersonating = true;
                    // Operational rights only: no DELETE/WRITE_DAC/WRITE_OWNER.
                    IntPtr station = OpenWindowStation(stationName, false, 0x33F);
                    if (station == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error(), "The selected own fixture token cannot access the existing interactive station. An owned desktop cannot repair station denial; its security is left unchanged.");
                    if (station == GetProcessWindowStation()) throw new InvalidOperationException("The station probe returned the borrowed process handle; it is neither closed nor accepted as an independent access proof.");
                    try { recovery.StationOperationalAccess = true; }
                    finally { if (!CloseWindowStation(station)) throw new Win32Exception(Marshal.GetLastWin32Error(), "Closing the independently opened station access-probe handle failed."); }
                    IntPtr prior = OpenDesktop(defaultName, 0, false, 0x1FF);
                    if (prior == IntPtr.Zero) recovery.DefaultDesktopAccess = "Operational access denied/unavailable; Win32=" + Marshal.GetLastWin32Error();
                    else {
                        recovery.DefaultDesktopAccess = "Operational access observed; private-desktop qualification still isolates the restricted fixture";
                        if (prior == GetThreadDesktop(GetCurrentThreadId())) recovery.DefaultDesktopAccess = "Probe returned borrowed thread desktop; not accepted as an independent access proof or closed";
                        else if (!CloseDesktop(prior)) throw new Win32Exception(Marshal.GetLastWin32Error(), "Closing the independent prior desktop probe failed.");
                    }
                    if (!RevertToSelf()) throw new Win32Exception(Marshal.GetLastWin32Error(), "Restoring the owned desktop worker identity failed.");
                    impersonating = false;
                    LastLaunchStage = "Create fresh own-user/SYSTEM private medium desktop";
                    string security = "D:P(A;;GA;;;" + runner.Sid + ")(A;;GA;;;SY)S:(ML;;NW;;;ME)";
                    uint descriptorSize;
                    if (!ConvertSecurityDescriptor(security, 1, out descriptor, out descriptorSize)) throw new Win32Exception(Marshal.GetLastWin32Error(), "The explicit owned private desktop descriptor could not be prepared.");
                    var attributes = new SecurityAttributes { Size = (uint)Marshal.SizeOf(typeof(SecurityAttributes)), Descriptor = descriptor, Inherit = 0 };
                    recovery.Owned = CreateDesktop(recovery.PrivateName, IntPtr.Zero, IntPtr.Zero, 0, 0x000F01FF, ref attributes);
                    if (recovery.Owned == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error(), "Creating the fresh owned private desktop failed.");
                    recovery.PrivateIntegrityRid = ObserveDesktopIntegrity(recovery.Owned);
                    if (recovery.PrivateIntegrityRid != 8192) throw new InvalidOperationException("The actual owned desktop mandatory label is not medium.");
                    LastLaunchStage = "Prove selected token access to observed medium private desktop";
                    if (!ImpersonateLoggedOnUser(workerToken)) throw new Win32Exception(Marshal.GetLastWin32Error());
                    impersonating = true;
                    IntPtr observed = OpenDesktop(recovery.PrivateName, 0, false, 0x1FF);
                    if (observed == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error(), "The selected own fixture token cannot access the fresh medium desktop.");
                    if (observed == recovery.Owned || observed == GetThreadDesktop(GetCurrentThreadId()))
                        throw new InvalidOperationException("The private desktop access probe returned an already owned/borrowed handle; independent selected-token access is not proven and that existing handle is not closed.");
                    try {
                        if (!String.Equals(ObjectName(observed), recovery.PrivateName, StringComparison.Ordinal)) throw new InvalidOperationException("The observed owned desktop identity does not match its fresh name.");
                        recovery.PrivateOperationalAccess = true;
                    } finally { if (!CloseDesktop(observed)) throw new Win32Exception(Marshal.GetLastWin32Error(), "Closing the independent private desktop probe failed."); }
                } catch (Exception error) { workerError = error; }
                finally {
                    if (impersonating && !RevertToSelf() && workerError == null) workerError = new Win32Exception(Marshal.GetLastWin32Error(), "Restoring desktop worker impersonation failed.");
                    // CreateDesktop is documented to assign the caller thread;
                    // use a fresh thread and explicitly detach before it exits.
                    if (originalThreadDesktop != IntPtr.Zero && !SetThreadDesktop(originalThreadDesktop) && workerError == null)
                        workerError = new Win32Exception(Marshal.GetLastWin32Error(), "Restoring the fresh worker thread desktop failed.");
                    if (descriptor != IntPtr.Zero) LocalFree(descriptor);
                    CloseHandle(workerToken);
                    lock (recovery) {
                        recovery.PreparationFinished = true;
                        if (recovery.CancelPreparation) DisposeOwnedDesktop(recovery);
                    }
                }
            });
            worker.IsBackground = true;
            worker.SetApartmentState(System.Threading.ApartmentState.MTA);
            try { worker.Start(); }
            catch { CloseHandle(workerToken); DisposeOwnedDesktop(recovery); throw; }
            if (!worker.Join(30000)) {
                lock (recovery) {
                    recovery.CancelPreparation = true;
                    if (recovery.PreparationFinished) DisposeOwnedDesktop(recovery);
                }
                throw new TimeoutException("The owned private desktop access/create worker exceeded its 30-second bound.");
            }
            if (workerError != null) { DisposeOwnedDesktop(recovery); throw new InvalidOperationException("Owned private desktop preparation failed at " + LastLaunchStage + ": " + workerError.Message, workerError); }
            return recovery;
        }
        internal static void ActivateOwnedDesktop(DesktopRecovery recovery) {
            LastLaunchStage = "Activate verified own medium desktop before releasing child suspension";
            if (!recovery.StationOperationalAccess || !recovery.PrivateOperationalAccess || recovery.PrivateIntegrityRid != 8192)
                throw new InvalidOperationException("The owned desktop cannot receive real input before its access and integrity checks pass.");
            if (!SwitchDesktop(recovery.Owned)) throw new Win32Exception(Marshal.GetLastWin32Error(), "Activating the owned private input desktop failed.");
            recovery.InputSwitched = true;
            IntPtr active = OpenInputDesktop(0, false, 1);
            if (active == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error(), "Observing the actual active private input desktop failed.");
            try { if (!String.Equals(ObjectName(active), recovery.PrivateName, StringComparison.Ordinal)) throw new InvalidOperationException("The observed input desktop is not the owned private desktop."); }
            finally { CloseDesktop(active); }
            LastLaunchStage = "Release exactly one suspension on own verified desktop child";
        }
        internal static string ObserveOwnedChildDesktop(uint threadId) {
            try {
                IntPtr desktop = GetThreadDesktop(threadId);
                return desktop == IntPtr.Zero ? "Not observed after resume; Win32=" + Marshal.GetLastWin32Error() : ObjectName(desktop);
            } catch (Exception error) { return "Not observed after resume: " + error.Message; }
        }
        private static bool CloseOwnedDesktopHandle(IntPtr desktop) {
            for (int attempt = 0; attempt < 3; attempt++) {
                if (CloseDesktop(desktop)) return true;
                if (Marshal.GetLastWin32Error() != 170 || attempt == 2) return false;
                System.Threading.Thread.Sleep(25);
            }
            return false;
        }
        private static void DesktopCleanupError(DesktopRecovery recovery, string message) {
            recovery.RestoreError = String.IsNullOrEmpty(recovery.RestoreError) ? message : recovery.RestoreError + " | " + message;
        }
        internal static void DisposeOwnedDesktop(DesktopRecovery recovery) {
            // Called after closing only the owned job tree. Restore the exact
            // saved input handle, never assume that its name was Default.
            if (recovery.InputSwitched && !recovery.InputRestored) {
                if (recovery.OriginalInput == IntPtr.Zero || !SwitchDesktop(recovery.OriginalInput))
                    recovery.RestoreError = "Restoring the exact saved input desktop failed; Win32=" + Marshal.GetLastWin32Error();
                else {
                    IntPtr active = OpenInputDesktop(0, false, 1);
                    if (active == IntPtr.Zero) recovery.RestoreError = "Observing restored input desktop failed; Win32=" + Marshal.GetLastWin32Error();
                    else {
                        try {
                            recovery.InputRestored = String.Equals(ObjectName(active), recovery.OriginalInputName, StringComparison.Ordinal);
                            if (!recovery.InputRestored) recovery.RestoreError = "The observed restored input desktop differs from the exact saved desktop.";
                        } catch (Exception error) { recovery.RestoreError = error.Message; }
                        finally { CloseDesktop(active); }
                    }
                }
            } else if (!recovery.InputSwitched) recovery.InputRestored = true;
            if (recovery.Owned != IntPtr.Zero) {
                if (!CloseOwnedDesktopHandle(recovery.Owned)) DesktopCleanupError(recovery, "Closing the owned private desktop failed; Win32=" + Marshal.GetLastWin32Error());
                else recovery.Owned = IntPtr.Zero;
            }
            if (recovery.OriginalInput != IntPtr.Zero) {
                if (!CloseOwnedDesktopHandle(recovery.OriginalInput)) DesktopCleanupError(recovery, "Closing the separately opened original input desktop handle failed; Win32=" + Marshal.GetLastWin32Error());
                else recovery.OriginalInput = IntPtr.Zero;
            }
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
        private static IntPtr StandardFile(string path, bool input) {
            var attributes = new SecurityAttributes { Size = (uint)Marshal.SizeOf(typeof(SecurityAttributes)), Inherit = 1 };
            IntPtr file = CreateFile(path, input ? 0x80000000u : 0x40000000u, 7, ref attributes, input ? 3u : 2u, 0x80, IntPtr.Zero);
            if (file == new IntPtr(-1)) throw new Win32Exception(Marshal.GetLastWin32Error(), "An owned standard I/O handle could not be opened.");
            uint type = GetFileType(file);
            if (type != (input ? 2u : 1u)) { CloseHandle(file); throw new InvalidOperationException("The owned standard I/O handle has unexpected observed type " + type + "."); }
            return file;
        }
        public static Launch Start(string application, string arguments, string directory, string environment, string stdoutPath, string stderrPath) {
            IntPtr runnerToken = IntPtr.Zero, selected = IntPtr.Zero, primary = IntPtr.Zero, block = IntPtr.Zero, job = IntPtr.Zero;
            IntPtr input = IntPtr.Zero, output = IntPtr.Zero, errorOutput = IntPtr.Zero, attributeList = IntPtr.Zero, handles = IntPtr.Zero;
            bool attributesInitialized = false;
            DesktopRecovery recovery = null;
            ProcessInformation process = new ProcessInformation(); bool created = false;
            LastDesktopRecovery = null;
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
                input = StandardFile("NUL", true); output = StandardFile(stdoutPath, false); errorOutput = StandardFile(stderrPath, false);
                var startup = new StartupInfo { Size = (uint)Marshal.SizeOf(typeof(StartupInfo)), Desktop = desktop,
                    Flags = 0x00000100, Input = input, Output = output, Error = errorOutput }; // STARTF_USESTDHANDLES
                var command = new StringBuilder(Quote(application) + " " + arguments);
                block = Marshal.StringToHGlobalUni(environment);
                const uint flags = 0x00000400 | 0x08000000 | 0x00000004; // Unicode environment, no console, suspended for verification.
                bool ownRestricted = source.StartsWith("Own restricted CI fixture token", StringComparison.Ordinal);
                if (ownRestricted) {
                    string stationName = ObjectName(GetProcessWindowStation());
                    recovery = CreateOwnedDesktop(primary, runner, stationName, ObjectName(GetThreadDesktop(GetCurrentThreadId())));
                    desktop = stationName + "\\" + recovery.PrivateName;
                    startup.Desktop = desktop;
                }
                LastLaunchStage = "Create suspended own smoke child with three valid native standard handles";
                string api;
                if (primary == IntPtr.Zero || ownRestricted) {
                    IntPtr attributeBytes = IntPtr.Zero;
                    InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref attributeBytes);
                    if (attributeBytes.ToInt64() <= 0 || attributeBytes.ToInt64() > 65536) throw new Win32Exception(Marshal.GetLastWin32Error(), "The owned handle-list size is unavailable or exceeds its bound.");
                    attributeList = Marshal.AllocHGlobal(attributeBytes);
                    if (!InitializeProcThreadAttributeList(attributeList, 1, 0, ref attributeBytes)) throw new Win32Exception(Marshal.GetLastWin32Error(), "The owned handle-list could not be initialized.");
                    attributesInitialized = true;
                    handles = Marshal.AllocHGlobal(IntPtr.Size * 3);
                    Marshal.WriteIntPtr(handles, 0, input); Marshal.WriteIntPtr(handles, IntPtr.Size, output); Marshal.WriteIntPtr(handles, IntPtr.Size * 2, errorOutput);
                    if (!UpdateProcThreadAttribute(attributeList, 0, new UIntPtr(0x00020002), handles, new IntPtr(IntPtr.Size * 3), IntPtr.Zero, IntPtr.Zero))
                        throw new Win32Exception(Marshal.GetLastWin32Error(), "The child could not restrict inherited handles to its three owned standard streams.");
                    startup.Size = (uint)Marshal.SizeOf(typeof(StartupInfoExtended));
                    var extended = new StartupInfoExtended { Startup = startup, Attributes = attributeList };
                    api = ownRestricted ? "CreateProcessAsUserW; own restricted token, explicit three-handle inheritance" : "CreateProcessW; current normal token, explicit three-handle inheritance";
                    created = ownRestricted
                        ? CreateProcessAsUser(primary, application, command, IntPtr.Zero, IntPtr.Zero, true, flags | 0x00080000, block, directory, ref extended, out process)
                        : CreateProcessExtended(application, command, IntPtr.Zero, IntPtr.Zero, true, flags | 0x00080000, block, directory, ref extended, out process);
                } else {
                    api = "CreateProcessWithTokenW; verified linked/Explorer token and owned standard streams";
                    created = CreateProcessWithToken(primary, 1, application, command, flags, block, directory, ref startup, out process);
                }
                if (!created) throw new Win32Exception(Marshal.GetLastWin32Error(), "Creating the owned normal-user Windows smoke failed.");
                IntPtr childToken;
                if (!OpenProcessToken(process.Process, 0x0008, out childToken)) throw new Win32Exception(Marshal.GetLastWin32Error(), "The suspended child token could not be observed.");
                SecurityState child;
                try { child = State(childToken); } finally { CloseHandle(childToken); }
                if (!Suitable(child, runner) || source.StartsWith("Own restricted CI fixture token", StringComparison.Ordinal) && !child.AdministratorDenyOnly)
                    throw new InvalidOperationException("The actual suspended child is not the required same-account/session medium, non-elevated process with the selected administrator restriction.");
                job = CreateJobObject(IntPtr.Zero, null);
                if (job == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error(), "The owned smoke tree job could not be created.");
                var limits = new JobExtendedLimits { Basic = new JobBasicLimits { Flags = 0x00002000 } }; // KILL_ON_JOB_CLOSE only
                if (!SetInformationJobObject(job, 9, ref limits, (uint)Marshal.SizeOf(typeof(JobExtendedLimits))) || !AssignProcessToJobObject(job, process.Process))
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "The suspended owned smoke tree could not be bounded by its job.");
                var launch = new Launch { Process = process.Process, Thread = process.Thread, Job = job, ProcessId = process.ProcessId,
                    StandardInput = input, StandardOutput = output, StandardError = errorOutput, Runner = runner, Child = child, PrimaryThreadId = process.ThreadId,
                    TokenSource = source, Desktop = desktop, LaunchApi = api, Recovery = recovery };
                created = false; job = IntPtr.Zero; input = output = errorOutput = IntPtr.Zero; recovery = null; return launch; // ownership transfers to Launch
            } finally {
                if (created) { TerminateProcess(process.Process, 1); CloseHandle(process.Thread); CloseHandle(process.Process); }
                if (job != IntPtr.Zero) CloseHandle(job);
                if (recovery != null) DisposeOwnedDesktop(recovery);
                if (attributesInitialized) DeleteProcThreadAttributeList(attributeList);
                if (attributeList != IntPtr.Zero) Marshal.FreeHGlobal(attributeList); if (handles != IntPtr.Zero) Marshal.FreeHGlobal(handles);
                if (input != IntPtr.Zero) CloseHandle(input); if (output != IntPtr.Zero) CloseHandle(output); if (errorOutput != IntPtr.Zero) CloseHandle(errorOutput);
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
        if ($PrerequisitesVerifiedBySetup) { $arguments += '-PrerequisitesVerifiedBySetup' }
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
    $ticket = [NotchlingDesktopSmoke.Native]::Start($shell, $quotedArguments, (Get-Location).Path, $environmentBlock, $stdoutFile, $stderrFile)
    $context.Launched = $ticket.Child
    $context.TokenSource = $ticket.TokenSource
    $context.Desktop = $ticket.Desktop
    $context.ChildProcessId = $ticket.ProcessId
    $context.LaunchApi = $ticket.LaunchApi
    $context.DesktopRecovery = $ticket.Recovery
    $context.DiagnosticStage = [NotchlingDesktopSmoke.Native]::LastLaunchStage
    # Publish the observed suspended child before running any PowerShell/runtime
    # stage. A later timeout must not erase the real privilege evidence.
    $context | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $contextFile -Encoding utf8
    $observed = "Mode=$Mode; runner=$($context.Runner.IntegrityRid)/elevated=$($context.Runner.Elevated); child=$($context.Launched.IntegrityRid)/elevated=$($context.Launched.Elevated); adminDenyOnly=$($context.Launched.AdministratorDenyOnly); hasRestrictions=$($context.Launched.HasRestrictions); session=$($context.Launched.SessionId); desktop=$($context.Desktop); childPid=$($context.ChildProcessId); launch=$($ticket.LaunchApi); source=$($context.TokenSource). App execution is not yet observed."
    $observed = $observed.Replace('%', '%25').Replace("`r", '%0D').Replace("`n", '%0A')
    Write-Output "::notice title=Observed CI desktop child token::$observed"
    $ticket.Resume()
    $context.ResumePreviousSuspendCount = $ticket.PreviousSuspendCount
    $context.ChildDesktopAfterResume = $ticket.ChildDesktopAfterResume
    $context.DiagnosticStage = [NotchlingDesktopSmoke.Native]::LastLaunchStage
    $context | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $contextFile -Encoding utf8
    Write-Output "::notice title=Observed owned smoke thread resume::Mode=$Mode; childPid=$($ticket.ProcessId); previousSuspendCount=$($ticket.PreviousSuspendCount); exactly one owned suspension was released; stdout/stderr are fresh owned files."
    $watchdog = if ($Mode -eq 'App') { 360000 } else { 540000 }
    if ($ticket.Recovery) { Write-Output "::notice title=Owned private desktop qualification scope::Own-user/SYSTEM private medium desktop; station and selected-token access proved; actual input desktop switched and observed. This shellless CI desktop does not certify consumer Explorer/tray integration." }
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
    $nativeType = 'NotchlingDesktopSmoke.Native' -as [type]
    if ($nativeType) {
        $context.DiagnosticStage = $nativeType::LastLaunchStage
        $context.DesktopRecovery = $nativeType::LastDesktopRecovery
    }
    if ($ticket) {
        $context.ResumePreviousSuspendCount = $ticket.PreviousSuspendCount
        $context.Error += " Observed childPid=$($ticket.ProcessId); child integrity=$($ticket.Child.IntegrityRid), elevated=$($ticket.Child.Elevated), adminDenyOnly=$($ticket.Child.AdministratorDenyOnly), session=$($ticket.Child.SessionId); launch=$($ticket.LaunchApi); previousSuspendCount=$($ticket.PreviousSuspendCount); token=$($ticket.TokenSource). Owned progress: $(Read-OwnedDesktopProgress)"
        $failure = [Management.Automation.ErrorRecord]::new([InvalidOperationException]::new($context.Error, $_.Exception), 'OwnedDesktopSmokeFailed', [Management.Automation.ErrorCategory]::OperationStopped, $context.ChildProcessId)
    }
} finally {
    if ($ticket) {
        $ticket.Dispose()
        $context.DesktopRecovery = $ticket.Recovery
        if ($ticket.Recovery -and (-not $ticket.Recovery.InputRestored -or $ticket.Recovery.RestoreError)) {
            $context.Succeeded = $false
            $context.Error = ([string]$context.Error + ' ' + [string]$ticket.Recovery.RestoreError).Trim()
            if (-not $failure) { $failure = [Management.Automation.ErrorRecord]::new([InvalidOperationException]::new($context.Error), 'OwnedDesktopRestoreFailed', [Management.Automation.ErrorCategory]::OperationStopped, $context.ChildProcessId) }
        }
    }
    $context | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $contextFile -Encoding utf8
}
if ($failure) { throw $failure }
