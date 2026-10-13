# WinUI external drag/drop requires a normal-user desktop. GitHub's elevated
# runner must launch the complete unchanged smoke tree with its own limited token.
[CmdletBinding()]
param(
    [ValidateSet('App', 'UpdateHandoff', 'Diagnostics')][string]$Mode = 'App',
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
$reportFile = if ($Mode -eq 'App') { [IO.Path]::GetFullPath($ReportPath) } elseif ($Mode -eq 'Diagnostics') { Join-Path ([IO.Path]::GetFullPath($ReportDirectory)) 'windows-launch-diagnostics.json' } else { Join-Path ([IO.Path]::GetFullPath($ReportDirectory)) 'windows-update-handoff.json' }
$contextName = if ($Mode -eq 'App') { 'desktop-smoke-context.json' } elseif ($Mode -eq 'Diagnostics') { 'desktop-launch-diagnostics-context.json' } else { 'desktop-update-handoff-context.json' }
$contextFile = Join-Path ([IO.Path]::GetDirectoryName($reportFile)) $contextName
$streamPrefix = if ($Mode -eq 'App') { 'desktop-smoke' } elseif ($Mode -eq 'Diagnostics') { 'desktop-launch-diagnostics' } else { 'desktop-update-handoff' }
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
    OwnedFixtureSecurity = $null
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
    public sealed class AclSummary {
        public bool Present;
        public int AceCount, OtherAceCount;
        public uint OwnUserAllowMask, SystemAllowMask, AdministratorAllowMask;
    }
    public sealed class TokenDiagnostics {
        public int TokenType, RestrictedSidCount, LogonSidCount;
        public bool OwnerIsOwnUser, OwnerIsAdministrator, OwnerIsSystem, UIAccess;
        public uint MandatoryPolicy;
        public AclSummary DefaultDacl;
    }
    public sealed class KernelSecurityDiagnostics {
        public string Kind, Error;
        public bool Readable, OwnerIsOwnUser, OwnerIsAdministrator, OwnerIsSystem;
        public bool AccessCheckApiSucceeded, AccessCheckAllowed, NativeOpenAllowed;
        public uint AccessCheckGrantedMask, NativeOpenMask;
        public int NativeOpenError;
        public uint? MandatoryLabelRid;
        public AclSummary Dacl;
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
        public TokenDiagnostics TokenBeforeSecurityComparison, TokenAfterSecurityComparison;
        public KernelSecurityDiagnostics ProcessSecurity, ThreadSecurity;
        public bool ExplicitOwnedFixtureSecurity;
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
        public static TokenDiagnostics LastTokenBeforeDiagnostics, LastTokenAfterDiagnostics;
        public static KernelSecurityDiagnostics LastProcessDiagnostics, LastThreadDiagnostics;
        [StructLayout(LayoutKind.Sequential, CharSet=CharSet.Unicode)] private struct StartupInfo {
            public uint Size; public string Reserved, Desktop, Title;
            public uint X, Y, XSize, YSize, XChars, YChars, Fill, Flags;
            public ushort Show, ReservedSize;
            public IntPtr ReservedData, Input, Output, Error;
        }
        [StructLayout(LayoutKind.Sequential)] private struct ProcessInformation { public IntPtr Process, Thread; public uint ProcessId, ThreadId; }
        [StructLayout(LayoutKind.Sequential)] private struct SidAndAttributes { public IntPtr Sid; public uint Attributes; }
        [StructLayout(LayoutKind.Sequential)] private struct GenericMapping { public uint Read, Write, Execute, All; }
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
        [DllImport("advapi32.dll", SetLastError=true)] private static extern bool GetSecurityDescriptorDacl(IntPtr descriptor, out bool present, out IntPtr dacl, out bool defaulted);
        [DllImport("advapi32.dll", SetLastError=true)] private static extern bool AccessCheck(IntPtr descriptor, IntPtr token, uint desired, ref GenericMapping mapping, IntPtr privileges, ref uint privilegesLength, out uint granted, out bool allowed);
        [DllImport("kernel32.dll", SetLastError=true)] private static extern IntPtr OpenThread(uint access, bool inherit, uint thread);
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
        private static string SidText(IntPtr sid) {
            IntPtr text;
            if (sid == IntPtr.Zero || !ConvertSidToStringSid(sid, out text)) throw new Win32Exception(Marshal.GetLastWin32Error(), "An owned security SID could not be summarized.");
            try { return Marshal.PtrToStringUni(text); } finally { LocalFree(text); }
        }
        private static AclSummary SummarizeAcl(IntPtr acl, string ownSid) {
            var summary = new AclSummary { Present = acl != IntPtr.Zero };
            if (acl == IntPtr.Zero) return summary;
            ushort count = unchecked((ushort)Marshal.ReadInt16(acl, 4));
            if (count > 256) throw new InvalidOperationException("The owned diagnostic ACL count exceeds its bound.");
            summary.AceCount = count;
            for (uint index = 0; index < count; index++) {
                IntPtr ace;
                if (!GetAce(acl, index, out ace)) throw new Win32Exception(Marshal.GetLastWin32Error());
                if (Marshal.ReadByte(ace) != 0) { summary.OtherAceCount++; continue; }
                uint mask = unchecked((uint)Marshal.ReadInt32(ace, 4));
                string sid = SidText(IntPtr.Add(ace, 8));
                if (String.Equals(sid, ownSid, StringComparison.Ordinal)) summary.OwnUserAllowMask |= mask;
                else if (sid == "S-1-5-18") summary.SystemAllowMask |= mask;
                else if (sid == "S-1-5-32-544") summary.AdministratorAllowMask |= mask;
                else summary.OtherAceCount++;
            }
            return summary;
        }
        private static int GroupCount(IntPtr token, int kind) {
            IntPtr buffer = Information(token, kind);
            try {
                int count = Marshal.ReadInt32(buffer);
                if (count < 0 || count > 4096) throw new InvalidOperationException("An owned token diagnostic group count exceeds its bound.");
                return count;
            } finally { Marshal.FreeHGlobal(buffer); }
        }
        private static TokenDiagnostics DescribeToken(IntPtr token, string ownSid) {
            var value = new TokenDiagnostics { TokenType = Integer(token, 8), RestrictedSidCount = GroupCount(token, 11),
                LogonSidCount = GroupCount(token, 28), UIAccess = Integer(token, 26) != 0, MandatoryPolicy = unchecked((uint)Integer(token, 27)) };
            IntPtr owner = Information(token, 4), defaults = IntPtr.Zero;
            try {
                string sid = SidText(Marshal.ReadIntPtr(owner));
                value.OwnerIsOwnUser = String.Equals(sid, ownSid, StringComparison.Ordinal);
                value.OwnerIsAdministrator = sid == "S-1-5-32-544";
                value.OwnerIsSystem = sid == "S-1-5-18";
                defaults = Information(token, 6);
                value.DefaultDacl = SummarizeAcl(Marshal.ReadIntPtr(defaults), ownSid);
                return value;
            } finally { if (defaults != IntPtr.Zero) Marshal.FreeHGlobal(defaults); Marshal.FreeHGlobal(owner); }
        }
        private static IntPtr PrepareDerivedFixtureSecurity(IntPtr primary, string ownSid) {
            // The elevated-origin default DACL can omit the own user once
            // Administrators becomes deny-only. Repair only the newly derived
            // own CI fixture token/new objects; no existing ACL is modified.
            IntPtr descriptor = IntPtr.Zero, defaults = IntPtr.Zero;
            try {
                uint size;
                if (!ConvertSecurityDescriptor("D:P(A;;GA;;;" + ownSid + ")(A;;GA;;;SY)S:(ML;;NW;;;ME)", 1, out descriptor, out size)) throw new Win32Exception(Marshal.GetLastWin32Error());
                bool present, defaulted; IntPtr dacl;
                if (!GetSecurityDescriptorDacl(descriptor, out present, out dacl, out defaulted) || !present || dacl == IntPtr.Zero)
                    throw new InvalidOperationException("The own comparison descriptor does not contain its explicit default DACL.");
                defaults = Marshal.AllocHGlobal(IntPtr.Size); Marshal.WriteIntPtr(defaults, dacl);
                if (!SetTokenInformation(primary, 6, defaults, IntPtr.Size)) throw new Win32Exception(Marshal.GetLastWin32Error(), "Setting the newly derived fixture token default DACL failed.");
                IntPtr result = descriptor; descriptor = IntPtr.Zero; return result;
            } finally { if (defaults != IntPtr.Zero) Marshal.FreeHGlobal(defaults); if (descriptor != IntPtr.Zero) LocalFree(descriptor); }
        }
        private static uint? MandatoryRid(IntPtr sacl) {
            if (sacl == IntPtr.Zero) return null;
            ushort count = unchecked((ushort)Marshal.ReadInt16(sacl, 4));
            if (count > 16) throw new InvalidOperationException("The owned kernel mandatory ACL exceeds its diagnostic bound.");
            for (uint index = 0; index < count; index++) {
                IntPtr ace; if (!GetAce(sacl, index, out ace)) throw new Win32Exception(Marshal.GetLastWin32Error());
                if (Marshal.ReadByte(ace) != 0x11) continue;
                IntPtr sid = IntPtr.Add(ace, 8); byte authorities = Marshal.ReadByte(GetSidSubAuthorityCount(sid));
                if (authorities == 0) throw new InvalidOperationException("An observed owned kernel mandatory SID has no authority.");
                return unchecked((uint)Marshal.ReadInt32(GetSidSubAuthority(sid, (uint)authorities - 1)));
            }
            return null;
        }
        private static KernelSecurityDiagnostics DescribeKernelObject(IntPtr handle, bool thread, uint id, IntPtr primary, string ownSid) {
            var value = new KernelSecurityDiagnostics { Kind = thread ? "Owned child primary thread" : "Owned child process", NativeOpenMask = thread ? 0x00100810u : 0x00101040u };
            IntPtr owner, group, dacl, sacl, descriptor = IntPtr.Zero, impersonation = IntPtr.Zero, privileges = IntPtr.Zero;
            bool identityRestored = true;
            try {
                uint code = GetSecurityInfo(handle, 6, 1 | 2 | 4 | 0x10, out owner, out group, out dacl, out sacl, out descriptor);
                if (code != 0) throw new Win32Exception(unchecked((int)code), "Reading the actual suspended owned kernel object security failed.");
                value.Readable = true;
                string ownerSid = SidText(owner);
                value.OwnerIsOwnUser = String.Equals(ownerSid, ownSid, StringComparison.Ordinal);
                value.OwnerIsAdministrator = ownerSid == "S-1-5-32-544"; value.OwnerIsSystem = ownerSid == "S-1-5-18";
                value.Dacl = SummarizeAcl(dacl, ownSid); value.MandatoryLabelRid = MandatoryRid(sacl);
                if (!DuplicateTokenEx(primary, 0x0008 | 0x0004, IntPtr.Zero, 2, 2, out impersonation)) throw new Win32Exception(Marshal.GetLastWin32Error());
                var mapping = new GenericMapping { Read = thread ? 0x00020048u : 0x00020410u, Write = thread ? 0x000203B3u : 0x00020BEBu, Execute = 0x00120000, All = 0x001FFFFF };
                uint length = 4096, granted; bool allowed; privileges = Marshal.AllocHGlobal((int)length);
                value.AccessCheckApiSucceeded = AccessCheck(descriptor, impersonation, 0x02000000, ref mapping, privileges, ref length, out granted, out allowed);
                value.AccessCheckAllowed = allowed; value.AccessCheckGrantedMask = granted;
                if (!value.AccessCheckApiSucceeded) value.Error = "DACL AccessCheck unavailable; Win32=" + Marshal.GetLastWin32Error();
                // Native opens under the selected token also exercise MIC. The
                // process mask includes duplicate/query/synchronize; thread mask
                // includes set-context/query/synchronize. No writes are made.
                if (!ImpersonateLoggedOnUser(primary)) throw new Win32Exception(Marshal.GetLastWin32Error());
                identityRestored = false;
                try {
                    IntPtr observed = thread ? OpenThread(value.NativeOpenMask, false, id) : OpenProcess(value.NativeOpenMask, false, id);
                    value.NativeOpenAllowed = observed != IntPtr.Zero;
                    value.NativeOpenError = observed == IntPtr.Zero ? Marshal.GetLastWin32Error() : 0;
                    if (observed != IntPtr.Zero) CloseHandle(observed);
                } finally {
                    if (!RevertToSelf()) throw new Win32Exception(Marshal.GetLastWin32Error(), "Restoring the read-only owned kernel access-probe identity failed.");
                    identityRestored = true;
                }
            } catch (Exception error) { value.Error = error.GetType().Name + ": " + error.Message; }
            finally {
                if (privileges != IntPtr.Zero) Marshal.FreeHGlobal(privileges);
                if (impersonation != IntPtr.Zero) CloseHandle(impersonation);
                if (descriptor != IntPtr.Zero) LocalFree(descriptor);
            }
            if (!identityRestored) throw new InvalidOperationException("Owned read-only access-probe identity restoration failed; further launches are refused.");
            return value;
        }
        private static void RequireOwnedKernelSecurity(KernelSecurityDiagnostics value) {
            if (!value.Readable || value.Dacl == null || !value.Dacl.Present || value.Dacl.AceCount != 2 ||
                value.Dacl.OwnUserAllowMask == 0 || value.Dacl.SystemAllowMask == 0 || value.Dacl.AdministratorAllowMask != 0 ||
                value.MandatoryLabelRid != 8192 || !value.AccessCheckApiSucceeded || !value.AccessCheckAllowed ||
                (value.AccessCheckGrantedMask & 0x001FFFFF) != 0x001FFFFF || !value.NativeOpenAllowed || !String.IsNullOrEmpty(value.Error))
                throw new InvalidOperationException(value.Kind + " did not pass the actual own-user/SYSTEM medium security and selected-token access read-back. NativeOpen=" +
                    value.NativeOpenAllowed + ", Win32=" + value.NativeOpenError + ", integrity=" + value.MandatoryLabelRid + ", detail=" + value.Error);
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
            return Start(application, arguments, directory, environment, stdoutPath, stderrPath, false, false);
        }
        public static Launch Start(string application, string arguments, string directory, string environment, string stdoutPath, string stderrPath, bool repairOwnedFixtureSecurity, bool forceOwnedFixture) {
            IntPtr runnerToken = IntPtr.Zero, selected = IntPtr.Zero, primary = IntPtr.Zero, block = IntPtr.Zero, job = IntPtr.Zero;
            IntPtr input = IntPtr.Zero, output = IntPtr.Zero, errorOutput = IntPtr.Zero, attributeList = IntPtr.Zero, handles = IntPtr.Zero;
            bool attributesInitialized = false;
            DesktopRecovery recovery = null;
            IntPtr comparisonDescriptor = IntPtr.Zero, comparisonAttributes = IntPtr.Zero;
            TokenDiagnostics tokenBefore = null, tokenAfter = null;
            KernelSecurityDiagnostics processSecurity = null, threadSecurity = null;
            ProcessInformation process = new ProcessInformation(); bool created = false;
            LastDesktopRecovery = null;
            LastTokenBeforeDiagnostics = LastTokenAfterDiagnostics = null;
            LastProcessDiagnostics = LastThreadDiagnostics = null;
            try {
                if (!OpenProcessToken(GetCurrentProcess(), QueryDuplicate, out runnerToken)) throw new Win32Exception(Marshal.GetLastWin32Error());
                SecurityState runner = State(runnerToken); string source = "Current normal-user token";
                if (forceOwnedFixture || !Suitable(runner, runner)) {
                    if (forceOwnedFixture) {
                        selected = RestrictedFixtureToken(runnerToken, runner);
                        source = "Own restricted CI fixture token (DISABLE_MAX_PRIVILEGE | LUA_TOKEN); diagnostics comparison only, observed medium/non-elevated, same account/session, Administrators deny-only";
                    } else selected = LimitedToken(runnerToken, runner, out source);
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
                // Normal App/UpdateHandoff repairs only this newly derived own
                // restricted fixture. Forced diagnostic defaults remain original
                // so the observed regression is still reproduced exactly.
                bool effectiveRepair = repairOwnedFixtureSecurity || (ownRestricted && !forceOwnedFixture);
                if (forceOwnedFixture || effectiveRepair) { tokenBefore = DescribeToken(primary, runner.Sid); LastTokenBeforeDiagnostics = tokenBefore; }
                if (effectiveRepair) {
                    if (!ownRestricted || primary == IntPtr.Zero) throw new InvalidOperationException("Fixture security normalization is allowed only on a newly derived own restricted token.");
                    LastLaunchStage = "Prepare own restricted fixture default DACL and explicit medium process/thread descriptor";
                    comparisonDescriptor = PrepareDerivedFixtureSecurity(primary, runner.Sid);
                    tokenAfter = DescribeToken(primary, runner.Sid);
                    LastTokenAfterDiagnostics = tokenAfter;
                    if (!tokenAfter.DefaultDacl.Present || tokenAfter.DefaultDacl.AceCount != 2 || tokenAfter.DefaultDacl.OwnUserAllowMask == 0 || tokenAfter.DefaultDacl.SystemAllowMask == 0 || tokenAfter.DefaultDacl.AdministratorAllowMask != 0)
                        throw new InvalidOperationException("The actual derived fixture default DACL does not match its explicit own-user/SYSTEM comparison.");
                    SecurityState normalized = State(primary);
                    if (!Suitable(normalized, runner) || !normalized.AdministratorDenyOnly)
                        throw new InvalidOperationException("The own fixture default-DACL normalization changed its required medium/non-elevated/account/session/admin-deny-only guards.");
                    var attributes = new SecurityAttributes { Size = (uint)Marshal.SizeOf(typeof(SecurityAttributes)), Descriptor = comparisonDescriptor, Inherit = 0 };
                    comparisonAttributes = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(SecurityAttributes)));
                    Marshal.StructureToPtr(attributes, comparisonAttributes, false);
                }
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
                        ? CreateProcessAsUser(primary, application, command, comparisonAttributes, comparisonAttributes, true, flags | 0x00080000, block, directory, ref extended, out process)
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
                if (forceOwnedFixture || effectiveRepair) {
                    LastLaunchStage = "Read actual suspended process/thread security and selected-token native access";
                    processSecurity = DescribeKernelObject(process.Process, false, process.ProcessId, primary, runner.Sid);
                    threadSecurity = DescribeKernelObject(process.Thread, true, process.ThreadId, primary, runner.Sid);
                    LastProcessDiagnostics = processSecurity; LastThreadDiagnostics = threadSecurity;
                    if (effectiveRepair) {
                        RequireOwnedKernelSecurity(processSecurity);
                        RequireOwnedKernelSecurity(threadSecurity);
                    }
                }
                job = CreateJobObject(IntPtr.Zero, null);
                if (job == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error(), "The owned smoke tree job could not be created.");
                var limits = new JobExtendedLimits { Basic = new JobBasicLimits { Flags = 0x00002000 } }; // KILL_ON_JOB_CLOSE only
                if (!SetInformationJobObject(job, 9, ref limits, (uint)Marshal.SizeOf(typeof(JobExtendedLimits))) || !AssignProcessToJobObject(job, process.Process))
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "The suspended owned smoke tree could not be bounded by its job.");
                var launch = new Launch { Process = process.Process, Thread = process.Thread, Job = job, ProcessId = process.ProcessId,
                    StandardInput = input, StandardOutput = output, StandardError = errorOutput, Runner = runner, Child = child, PrimaryThreadId = process.ThreadId,
                    TokenSource = source, Desktop = desktop, LaunchApi = api, Recovery = recovery, TokenBeforeSecurityComparison = tokenBefore, TokenAfterSecurityComparison = tokenAfter, ProcessSecurity = processSecurity, ThreadSecurity = threadSecurity, ExplicitOwnedFixtureSecurity = effectiveRepair };
                created = false; job = IntPtr.Zero; input = output = errorOutput = IntPtr.Zero; recovery = null; return launch; // ownership transfers to Launch
            } finally {
                if (created) { TerminateProcess(process.Process, 1); CloseHandle(process.Thread); CloseHandle(process.Process); }
                if (job != IntPtr.Zero) CloseHandle(job);
                if (recovery != null) DisposeOwnedDesktop(recovery);
                if (attributesInitialized) DeleteProcThreadAttributeList(attributeList);
                if (attributeList != IntPtr.Zero) Marshal.FreeHGlobal(attributeList); if (handles != IntPtr.Zero) Marshal.FreeHGlobal(handles);
                if (input != IntPtr.Zero) CloseHandle(input); if (output != IntPtr.Zero) CloseHandle(output); if (errorOutput != IntPtr.Zero) CloseHandle(errorOutput);
                if (block != IntPtr.Zero) Marshal.FreeHGlobal(block);
                if (comparisonAttributes != IntPtr.Zero) Marshal.FreeHGlobal(comparisonAttributes);
                if (comparisonDescriptor != IntPtr.Zero) LocalFree(comparisonDescriptor);
                if (primary != IntPtr.Zero) CloseHandle(primary); if (selected != IntPtr.Zero) CloseHandle(selected); if (runnerToken != IntPtr.Zero) CloseHandle(runnerToken);
            }
        }
    }
}
'@
    $context.Runner = [NotchlingDesktopSmoke.Native]::CurrentSecurity()
    if ($Mode -eq 'Diagnostics') {
        # This path reports native marker evidence only. It does not install,
        # launch, waive checks for, publish, or qualify the Notchling application.
        $shell = [Diagnostics.Process]::GetCurrentProcess().MainModule.FileName
        if ([IO.Path]::GetFileName($shell) -ne 'pwsh.exe') { throw 'Native launch diagnostics require the existing PowerShell 7 host.' }
        $systemDirectory = if ($AppArchitecture -eq 'x86') { [Environment]::GetFolderPath('SystemX86') } else { [Environment]::GetFolderPath('System') }
        $probes = @(
            @{ Name = 'cmd'; Application = (Join-Path $systemDirectory 'cmd.exe'); Arguments = @('/d', '/q', '/c', 'echo Notchling owned cmd entry'); Marker = 'Notchling owned cmd entry' },
            @{ Name = 'winps'; Application = (Join-Path $systemDirectory 'WindowsPowerShell/v1.0/powershell.exe'); Arguments = @('-NoLogo', '-NoProfile', '-NonInteractive', '-ExecutionPolicy', 'Bypass', '-Command', "[Console]::Out.WriteLine('Notchling owned Windows PowerShell entry')"); Marker = 'Notchling owned Windows PowerShell entry' },
            @{ Name = 'pwsh'; Application = $shell; Arguments = @('-NoLogo', '-NoProfile', '-NonInteractive', '-ExecutionPolicy', 'Bypass', '-Command', "[Console]::Out.WriteLine('Notchling owned PowerShell 7 entry')"); Marker = 'Notchling owned PowerShell 7 entry' }
        )
        $variables = [Environment]::GetEnvironmentVariables()
        $entries = [string[]]@($variables.Keys | ForEach-Object { [string]$_ + '=' + [string]$variables[$_] })
        [Array]::Sort($entries, [StringComparer]::OrdinalIgnoreCase)
        $environmentBlock = [string]::Join([char]0, [string[]]$entries) + [char]0 + [char]0
        $diagnostics = [ordered]@{
            Completed = $false
            ApplicationQualified = $false
            Scope = 'Six bounded native marker probes on newly derived same-account/session medium CI fixtures. Explicit security comparison changes only the new fixture token and new process/thread objects. No existing ACL, policy, credential or foreign process changes. No product qualification.'
            RequestedArchitecture = $AppArchitecture
            HostArchitecture = [Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString()
            ShellArchitecture = [Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture.ToString()
            OS = [Environment]::OSVersion.VersionString
            Cases = @()
        }
        foreach ($repair in @($false, $true)) {
            foreach ($probe in $probes) {
                $comparison = if ($repair) { 'explicit-own-security' } else { 'existing-fixture-default' }
                $prefix = 'native-launch-' + $probe.Name + '-' + $comparison
                $probeStdout = Join-Path ([IO.Path]::GetDirectoryName($reportFile)) ($prefix + '.stdout.log')
                $probeStderr = Join-Path ([IO.Path]::GetDirectoryName($reportFile)) ($prefix + '.stderr.log')
                foreach ($ownedPath in @($probeStdout, $probeStderr)) { if (Test-Path -LiteralPath $ownedPath) { Remove-Item -LiteralPath $ownedPath } }
                $probeTicket = $null
                $case = [ordered]@{
                    Probe = $probe.Name
                    RequestedArchitecture = $AppArchitecture
                    ProbeArchitectureScope = if ($probe.Name -eq 'pwsh') { 'Exact existing PowerShell 7 host: ' + [Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture.ToString() } else { 'Requested system executable architecture: ' + $AppArchitecture }
                    Comparison = $comparison
                    ActualExitCode = $null
                    ActualExitCodeHex = $null
                    FreshStdoutMarker = $false
                    StdoutBytes = 0
                    StderrBytes = 0
                    MarkerSucceeded = $false
                    Runner = $null
                    Child = $null
                    ChildProcessId = $null
                    LaunchApi = $null
                    TokenSource = $null
                    ResumePreviousSuspendCount = $null
                    ChildDesktopAfterResume = $null
                    DesktopRecovery = $null
                    TokenBefore = $null
                    TokenAfter = $null
                    ProcessSecurity = $null
                    ThreadSecurity = $null
                    Stage = 'Start bounded owned marker probe'
                    Error = $null
                }
                try {
                    # cmd /c has distinct quote-stripping rules. Its command is
                    # a fixed owned marker with no input interpolation.
                    $quoted = if ($probe.Name -eq 'cmd') { '/d /q /c echo Notchling owned cmd entry' } else { @($probe.Arguments | ForEach-Object { [NotchlingDesktopSmoke.Native]::Quote([string]$_) }) -join ' ' }
                    $probeTicket = [NotchlingDesktopSmoke.Native]::Start($probe.Application, $quoted, (Get-Location).Path, $environmentBlock, $probeStdout, $probeStderr, [bool]$repair, $true)
                    $case.Runner = $probeTicket.Runner; $case.Child = $probeTicket.Child
                    $case.ChildProcessId = $probeTicket.ProcessId; $case.LaunchApi = $probeTicket.LaunchApi; $case.TokenSource = $probeTicket.TokenSource
                    $case.TokenBefore = $probeTicket.TokenBeforeSecurityComparison; $case.TokenAfter = $probeTicket.TokenAfterSecurityComparison
                    $case.ProcessSecurity = $probeTicket.ProcessSecurity; $case.ThreadSecurity = $probeTicket.ThreadSecurity
                    $case.DesktopRecovery = $probeTicket.Recovery
                    $probeTicket.Resume()
                    $case.ResumePreviousSuspendCount = $probeTicket.PreviousSuspendCount
                    $case.ChildDesktopAfterResume = $probeTicket.ChildDesktopAfterResume
                    $case.Stage = 'Wait for real owned marker child exit (15-second bound)'
                    $case.ActualExitCode = $probeTicket.Wait(15000)
                    $unsignedCode = [BitConverter]::ToUInt32([BitConverter]::GetBytes([int]$case.ActualExitCode), 0)
                    $case.ActualExitCodeHex = '0x' + $unsignedCode.ToString('X8')
                    if (Test-Path -LiteralPath $probeStdout -PathType Leaf) {
                        $case.StdoutBytes = (Get-Item -LiteralPath $probeStdout).Length
                        if ($case.StdoutBytes -gt 65536) { throw 'The owned marker output exceeds its diagnostic bound.' }
                        # The parent deliberately retains its real native output
                        # handle until cleanup. The reader must share that writer.
                        $markerStream = [IO.File]::Open($probeStdout, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]7)
                        $markerReader = $null
                        try {
                            if ($markerStream.Length -gt 65536) { throw 'The owned marker output exceeds its diagnostic bound.' }
                            $markerReader = [IO.StreamReader]::new($markerStream, [Text.Encoding]::UTF8, $true)
                            $case.FreshStdoutMarker = $markerReader.ReadToEnd().Contains([string]$probe.Marker)
                        } finally {
                            if ($markerReader) { $markerReader.Dispose() }
                            $markerStream.Dispose()
                        }
                    }
                    if (Test-Path -LiteralPath $probeStderr -PathType Leaf) { $case.StderrBytes = (Get-Item -LiteralPath $probeStderr).Length }
                    $case.MarkerSucceeded = $case.ActualExitCode -eq 0 -and $case.FreshStdoutMarker
                    $case.Stage = 'Completed actual marker observation; application not qualified'
                } catch {
                    $case.Error = $_.Exception.Message
                    if ($case.Error.Length -gt 2000) { $case.Error = $case.Error.Substring(0, 2000) }
                    $case.Stage = [NotchlingDesktopSmoke.Native]::LastLaunchStage
                    $case.DesktopRecovery = [NotchlingDesktopSmoke.Native]::LastDesktopRecovery
                    $case.TokenBefore = [NotchlingDesktopSmoke.Native]::LastTokenBeforeDiagnostics
                    $case.TokenAfter = [NotchlingDesktopSmoke.Native]::LastTokenAfterDiagnostics
                    $case.ProcessSecurity = [NotchlingDesktopSmoke.Native]::LastProcessDiagnostics
                    $case.ThreadSecurity = [NotchlingDesktopSmoke.Native]::LastThreadDiagnostics
                } finally {
                    if ($probeTicket) {
                        $case.ResumePreviousSuspendCount = $probeTicket.PreviousSuspendCount
                        $case.ChildDesktopAfterResume = $probeTicket.ChildDesktopAfterResume
                        $probeTicket.Dispose()
                        $case.DesktopRecovery = $probeTicket.Recovery
                        if ($probeTicket.Recovery -and (-not $probeTicket.Recovery.InputRestored -or $probeTicket.Recovery.RestoreError)) {
                            $case.MarkerSucceeded = $false
                            $case.Error = ([string]$case.Error + ' ' + [string]$probeTicket.Recovery.RestoreError).Trim()
                        }
                    }
                }
                $diagnostics.Cases += $case
                $diagnostics | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $reportFile -Encoding utf8
                $notice = ($case | ConvertTo-Json -Depth 8 -Compress).Replace('%', '%25').Replace("`r", '%0D').Replace("`n", '%0A')
                Write-Output "::notice title=Owned native launch probe::$notice"
            }
        }
        $diagnostics.Completed = $true
        $diagnostics | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $reportFile -Encoding utf8
        $context.DiagnosticStage = 'Completed native launch diagnostics; application not qualified'
        $context.Succeeded = $false
        Write-Output '::notice title=Native launch diagnostics completed::Six actual marker cases recorded. This diagnostics-only run does not qualify or publish the Notchling application.'
        return
    }
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
    $context.OwnedFixtureSecurity = [ordered]@{
        NormalizedNewFixtureOnly = $ticket.ExplicitOwnedFixtureSecurity
        TokenBefore = $ticket.TokenBeforeSecurityComparison
        TokenAfter = $ticket.TokenAfterSecurityComparison
        Process = $ticket.ProcessSecurity
        Thread = $ticket.ThreadSecurity
    }
    $context.DiagnosticStage = [NotchlingDesktopSmoke.Native]::LastLaunchStage
    # Publish the observed suspended child before running any PowerShell/runtime
    # stage. A later timeout must not erase the real privilege evidence.
    $context | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $contextFile -Encoding utf8
    $observed = "Mode=$Mode; runner=$($context.Runner.IntegrityRid)/elevated=$($context.Runner.Elevated); child=$($context.Launched.IntegrityRid)/elevated=$($context.Launched.Elevated); adminDenyOnly=$($context.Launched.AdministratorDenyOnly); hasRestrictions=$($context.Launched.HasRestrictions); session=$($context.Launched.SessionId); desktop=$($context.Desktop); childPid=$($context.ChildProcessId); launch=$($ticket.LaunchApi); source=$($context.TokenSource). App execution is not yet observed."
    $observed = $observed.Replace('%', '%25').Replace("`r", '%0D').Replace("`n", '%0A')
    Write-Output "::notice title=Observed CI desktop child token::$observed"
    if ($ticket.ExplicitOwnedFixtureSecurity) {
        Write-Output '::notice title=Verified owned CI fixture security::New restricted fixture default DACL and new process/thread security read back as own-user/SYSTEM; actual medium labels and selected-token native access passed. Parent and existing normal/linked/Explorer tokens and objects are unchanged.'
    }
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
