# Genuine OLE transfer on an owned disposable Windows desktop. This helper never
# calls a view method or simulates a saved item by writing application state.
[CmdletBinding()]
param(
    [Parameter(Mandatory)][int]$AppProcessId,
    [Parameter(Mandatory)][long]$WindowHandle,
    [Parameter(Mandatory)][string]$ReportPath
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest
$clock = [Diagnostics.Stopwatch]::StartNew()
$failure = $null
$report = [ordered]@{
    Succeeded = $false
    Scope = "Real CF_HDROP and CF_DIB OLE drag/drop with SendInput on a disposable Windows CI desktop; no view injection or external clipboard mutation."
    Stage = "Initialize native OLE client"
    FileDropCopy = $false
    BitmapDropCopy = $false
    FileFormatRetrieved = $false
    BitmapFormatRetrieved = $false
    CompactDropOpenedShelf = $false
    OriginalFilePreserved = $false
    PersistedFileReferenceVerified = $false
    PersistedBitmapVerified = $false
    PersistedBitmapFormat = $null
    ShelfCountObserved = $null
    FileDrag = $null
    BitmapDrag = $null
    SourceFile = $null
    SavedBitmap = $null
    Actions = @()
    ElapsedSeconds = $null
    Error = $null
}

function Get-OwnedFileSha256([string]$Path) {
    # Framework crypto is available in Windows PowerShell 5.1 even when an
    # inherited PowerShell module path cannot resolve Get-FileHash.
    $inputStream = [IO.File]::OpenRead($Path)
    $algorithm = [Security.Cryptography.SHA256]::Create()
    try { return [BitConverter]::ToString($algorithm.ComputeHash($inputStream)).Replace("-", "") }
    finally { $algorithm.Dispose(); $inputStream.Dispose() }
}

function Read-OwnedAtomicText([string]$Path) {
    # Observe JSON without blocking the app's atomic replacement on Windows.
    # Get-Content can hold a reader without delete sharing during a save.
    $stream = [IO.FileStream]::new($Path, [IO.FileMode]::Open, [IO.FileAccess]::Read,
        ([IO.FileShare]::ReadWrite -bor [IO.FileShare]::Delete))
    $reader = $null
    try {
        if ($stream.Length -gt 10MB) { throw "The owned JSON observation exceeds its size bound." }
        $reader = [IO.StreamReader]::new($stream, [Text.Encoding]::UTF8, $true)
        return $reader.ReadToEnd()
    } finally {
        if ($reader) { $reader.Dispose() } else { $stream.Dispose() }
    }
}

function Assert-Budget {
    if ($clock.Elapsed.TotalSeconds -gt 30) { throw "The owned Shelf drop check exceeded its 30-second operation budget." }
    $process = Get-Process -Id $AppProcessId -ErrorAction Stop
    if ($process.HasExited) { throw "Notchling exited during the real OLE drop check." }
}

function Find-Control([string]$Name, [string]$Id, $Pattern = $null) {
    Assert-Budget
    $property = if ($Id) { [System.Windows.Automation.AutomationElement]::AutomationIdProperty } else { [System.Windows.Automation.AutomationElement]::NameProperty }
    $value = if ($Id) { $Id } else { $Name }
    $condition = [System.Windows.Automation.PropertyCondition]::new($property, $value)
    foreach ($candidate in $root.FindAll([System.Windows.Automation.TreeScope]::Descendants, $condition)) {
        try {
            $current = $candidate.Current
            if ($current.IsOffscreen -or $current.BoundingRectangle.IsEmpty -or -not $current.IsEnabled) { continue }
            if ($null -ne $Pattern) {
                $provider = $null
                if (-not $candidate.TryGetCurrentPattern($Pattern, [ref]$provider)) { continue }
            }
            return $candidate
        } catch [System.Windows.Automation.ElementNotAvailableException] { continue }
    }
    return $null
}

function Wait-Control([string]$Name, [string]$Id, $Pattern = $null) {
    $wait = [Diagnostics.Stopwatch]::StartNew()
    do {
        $control = Find-Control $Name $Id $Pattern
        if ($control) { return $control }
        Start-Sleep -Milliseconds 70
    } while ($wait.Elapsed.TotalSeconds -lt 5)
    throw "The real drop check could not reach an owned visible control: $Name $Id."
}

function Set-Pin([bool]$Pinned) {
    $pin = Wait-Control "Keep Notchling expanded" "" ([System.Windows.Automation.TogglePattern]::Pattern)
    $toggle = $pin.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern)
    $expected = if ($Pinned) { [System.Windows.Automation.ToggleState]::On } else { [System.Windows.Automation.ToggleState]::Off }
    if ($toggle.Current.ToggleState -ne $expected) { $toggle.Toggle() }
}

function Reach-Compact {
    # A real visit and leave exercises the existing hover policy. Selecting Shelf
    # or calling the application's overlay methods would bypass the reported bug.
    Set-Pin $false
    $bounds = [NotchlingShelfOle.Native]::WindowBounds([IntPtr]::new($WindowHandle))
    $work = [NotchlingShelfOle.Native]::WorkArea([IntPtr]::new($WindowHandle))
    $scale = [NotchlingShelfOle.Native]::GetDpiForWindow([IntPtr]::new($WindowHandle)) / 96.0
    [NotchlingShelfOle.Native]::MoveCursor([int](($bounds.Left + $bounds.Right) / 2), $bounds.Top + [int](20 * $scale))
    Start-Sleep -Milliseconds 80
    $script:outsideX = $work.Right - 12
    $script:outsideY = $work.Bottom - 12
    if ($outsideX -ge $bounds.Left -and $outsideX -lt $bounds.Right -and $outsideY -ge $bounds.Top -and $outsideY -lt $bounds.Bottom) {
        throw "This disposable desktop has no outside-overlay point for the Shelf check."
    }
    [NotchlingShelfOle.Native]::MoveCursor($outsideX, $outsideY)
    $wait = [Diagnostics.Stopwatch]::StartNew()
    do {
        $compact = Find-Control "Open Notchling" "" ([System.Windows.Automation.InvokePattern]::Pattern)
        $bounds = [NotchlingShelfOle.Native]::WindowBounds([IntPtr]::new($WindowHandle))
        if ($compact -and ($bounds.Bottom - $bounds.Top) -le 42 * $scale) {
            # Require two settled samples so the drag targets the final compact
            # island rather than an intermediate collapsing HWND rectangle.
            Start-Sleep -Milliseconds 100
            $bounds = [NotchlingShelfOle.Native]::WindowBounds([IntPtr]::new($WindowHandle))
            if (($bounds.Bottom - $bounds.Top) -le 42 * $scale) { return $compact.Current.BoundingRectangle }
        }
        Start-Sleep -Milliseconds 70
    } while ($wait.Elapsed.TotalSeconds -lt 6)
    throw "The notch did not reach its real compact state before OLE drag/drop."
}

function Read-Shelf {
    if (-not (Test-Path -LiteralPath $workspacePath -PathType Leaf)) { return @() }
    try {
        $workspace = Read-OwnedAtomicText $workspacePath | ConvertFrom-Json
        $property = $workspace.PSObject.Properties["Shelf"]
        if ($property) { return @($property.Value) }
    } catch { } # Atomic workspace replacement can race this bounded observation.
    return @()
}

function Wait-PersistedPath([string]$ExactPath, [string[]]$ExistingPaths = @()) {
    $wait = [Diagnostics.Stopwatch]::StartNew()
    do {
        Assert-Budget
        foreach ($item in @(Read-Shelf)) {
            if ($ExactPath -and $item.Path -eq $ExactPath) { return [string]$item.Path }
            if (-not $ExactPath -and $ExistingPaths -notcontains $item.Path -and
                [IO.Path]::GetFullPath($item.Path).StartsWith($captureDirectory + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
                return [string]$item.Path
            }
        }
        Start-Sleep -Milliseconds 70
    } while ($wait.Elapsed.TotalSeconds -lt 5)
    throw "The genuine OLE drop did not persist its expected Shelf file or captured bitmap."
}

function Assert-VisiblePath([string]$Path) {
    foreach ($element in $root.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)) {
        try {
            $current = $element.Current
            if (-not $current.IsOffscreen -and -not $current.BoundingRectangle.IsEmpty -and
                $current.AutomationId.StartsWith("ShelfItemPath-", [StringComparison]::Ordinal) -and
                $current.Name.IndexOf($Path, [StringComparison]::OrdinalIgnoreCase) -ge 0) { return }
        } catch [System.Windows.Automation.ElementNotAvailableException] { continue }
    }
    throw "The persisted dropped item has no visible accessible path in Shelf: $Path."
}

try {
    if ($env:GITHUB_ACTIONS -ne "true") { throw "Native Shelf drop QA requires a disposable GitHub Actions desktop." }
    if ($PSVersionTable.PSEdition -ne "Desktop") { throw "Run this helper with Windows PowerShell 5.1." }
    $process = Get-Process -Id $AppProcessId -ErrorAction Stop
    if ([IO.Path]::GetFileName($process.Path) -ne "Notchling.Windows.exe") { throw "The supplied process is not the expected app." }
    Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Drawing
    Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using System.Threading;

namespace NotchlingShelfOle {
    public sealed class DragDiagnostics {
        private readonly List<string> events = new List<string>();
        private readonly System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();
        public string Phase;
        public int CallerThread, WorkerThread, QueryContinueCalls, GiveFeedbackCalls, QueryGetDataCalls, GetDataCalls;
        public uint LastKeys;
        public bool ButtonDownVerified, ButtonUpVerified, SourceSawRelease, DragReturned, TransferPumpCompleted;
        public string[] Events { get { lock (events) return events.ToArray(); } }
        public void Record(string value) {
            lock (events) {
                if (events.Count < 48) events.Add(clock.ElapsedMilliseconds + "ms thread=" + Thread.CurrentThread.ManagedThreadId + " apartment=" + Thread.CurrentThread.GetApartmentState() + " " + value);
            }
        }
        public string Summary() {
            return "phase=" + Phase + "; queries=" + QueryContinueCalls + "; keys=0x" + LastKeys.ToString("X") +
                "; sourceRelease=" + SourceSawRelease + "; physicalDown=" + ButtonDownVerified + "; physicalUp=" + ButtonUpVerified +
                "; getData=" + GetDataCalls + "; dragReturned=" + DragReturned + "; pumpCompleted=" + TransferPumpCompleted +
                "; events=[" + String.Join(" | ", Events) + "]";
        }
    }
    [ComImport, Guid("00000121-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IDropSource {
        [PreserveSig] int QueryContinueDrag([MarshalAs(UnmanagedType.Bool)] bool escape, uint keys);
        [PreserveSig] int GiveFeedback(uint effect);
    }
    [ComVisible(true), ClassInterface(ClassInterfaceType.None)]
    public sealed class DropSource : IDropSource {
        private readonly DateTime deadline;
        private readonly DragDiagnostics diagnostics;
        public volatile bool Cancel;
        public DropSource(DragDiagnostics diagnostics) { this.diagnostics = diagnostics; deadline = DateTime.UtcNow.AddSeconds(5); diagnostics.Record("Created IDropSource"); }
        public int QueryContinueDrag(bool escape, uint keys) {
            int count = Interlocked.Increment(ref diagnostics.QueryContinueCalls);
            diagnostics.LastKeys = keys;
            if (count <= 4) diagnostics.Record("QueryContinueDrag keys=0x" + keys.ToString("X") + " escape=" + escape);
            if (Cancel || escape || DateTime.UtcNow >= deadline) { diagnostics.Record("QueryContinueDrag cancelled"); return 0x00040101; }
            if ((keys & 1) == 0) { diagnostics.SourceSawRelease = true; diagnostics.Record("QueryContinueDrag requested real drop"); return 0x00040100; }
            return 0;
        }
        public int GiveFeedback(uint effect) { if (Interlocked.Increment(ref diagnostics.GiveFeedbackCalls) <= 3) diagnostics.Record("GiveFeedback effect=" + effect); return 0x00040102; }
    }
    [ComVisible(true), ClassInterface(ClassInterfaceType.None)]
    public sealed class DropData : IDataObject {
        private readonly byte[] bytes;
        private readonly FORMATETC format;
        private readonly List<int> requested = new List<int>();
        private readonly DragDiagnostics diagnostics;
        public DropData(short clipboardFormat, byte[] bytes, DragDiagnostics diagnostics) {
            this.bytes = bytes;
            this.diagnostics = diagnostics;
            format = new FORMATETC { cfFormat = clipboardFormat, dwAspect = DVASPECT.DVASPECT_CONTENT, lindex = -1, ptd = IntPtr.Zero, tymed = TYMED.TYMED_HGLOBAL };
            diagnostics.Record("Created IDataObject format=" + clipboardFormat + " bytes=" + bytes.Length);
        }
        public int[] RequestedFormats { get { lock (requested) return requested.ToArray(); } }
        public void GetData(ref FORMATETC asked, out STGMEDIUM medium) {
            Interlocked.Increment(ref diagnostics.GetDataCalls);
            diagnostics.Record("GetData entered format=" + asked.cfFormat + " tymed=" + asked.tymed);
            if (QueryGetData(ref asked) != 0) throw new COMException("Unsupported drop format.", unchecked((int)0x80040064));
            IntPtr block = Native.GlobalAlloc(2, new UIntPtr((uint)bytes.Length));
            if (block == IntPtr.Zero) throw new OutOfMemoryException("OLE HGLOBAL allocation failed.");
            IntPtr memory = Native.GlobalLock(block);
            if (memory == IntPtr.Zero) { Native.GlobalFree(block); throw new InvalidOperationException("OLE HGLOBAL lock failed."); }
            try { Marshal.Copy(bytes, 0, memory, bytes.Length); }
            finally { Native.GlobalUnlock(block); }
            lock (requested) requested.Add(asked.cfFormat);
            medium = new STGMEDIUM { tymed = TYMED.TYMED_HGLOBAL, unionmember = block, pUnkForRelease = null };
            diagnostics.Record("GetData returned format=" + asked.cfFormat + " bytes=" + bytes.Length);
        }
        public void GetDataHere(ref FORMATETC asked, ref STGMEDIUM medium) { throw new COMException("GetDataHere is unsupported.", unchecked((int)0x80040069)); }
        public int QueryGetData(ref FORMATETC asked) {
            if (Interlocked.Increment(ref diagnostics.QueryGetDataCalls) <= 8) diagnostics.Record("QueryGetData format=" + asked.cfFormat + " tymed=" + asked.tymed);
            return asked.cfFormat == format.cfFormat && asked.dwAspect == DVASPECT.DVASPECT_CONTENT && asked.lindex == -1 && (asked.tymed & TYMED.TYMED_HGLOBAL) != 0 ? 0 : unchecked((int)0x80040064);
        }
        public int GetCanonicalFormatEtc(ref FORMATETC asked, out FORMATETC canonical) { canonical = asked; canonical.ptd = IntPtr.Zero; return 0x00040130; }
        public void SetData(ref FORMATETC asked, ref STGMEDIUM medium, bool release) { throw new COMException("The test source is immutable.", unchecked((int)0x80004001)); }
        public IEnumFORMATETC EnumFormatEtc(DATADIR direction) {
            diagnostics.Record("EnumFormatEtc direction=" + direction);
            if (direction != DATADIR.DATADIR_GET) throw new COMException("The test source is read-only.", unchecked((int)0x80004001));
            // Use Windows' actual native enumerator. Returning a newly created
            // managed enumerator CCW from this callback can stall its initial
            // COM/array marshaling before DoDragDrop reaches any target.
            IEnumFORMATETC enumerator;
            int hr = Native.SHCreateStdEnumFmtEtc(1, new FORMATETC[] { format }, out enumerator);
            diagnostics.Record("SHCreateStdEnumFmtEtc returned HRESULT=" + hr + " format=" + format.cfFormat);
            if (hr < 0) Marshal.ThrowExceptionForHR(hr);
            if (enumerator == null) throw new COMException("Windows returned no native format enumerator.", unchecked((int)0x80004005));
            diagnostics.Record("EnumFormatEtc returning native enumerator");
            return enumerator;
        }
        public int DAdvise(ref FORMATETC asked, ADVF flags, IAdviseSink sink, out int connection) { connection = 0; return unchecked((int)0x80040003); }
        public void DUnadvise(int connection) { throw new COMException("Advice is unsupported.", unchecked((int)0x80040003)); }
        public int EnumDAdvise(out IEnumSTATDATA advice) { advice = null; return unchecked((int)0x80040003); }
    }
    public sealed class DragResult {
        public int HResult = Int32.MinValue;
        public int Effect;
        public int[] RequestedFormats = new int[0];
        public DragDiagnostics Diagnostics;
    }
    public static class Native {
        public const short FileDropFormat = 15; // CF_HDROP; 13 is CF_UNICODETEXT.
        public const short BitmapFormat = 8; // CF_DIB.
        public static DragResult LastDragResult { get; private set; }
        [StructLayout(LayoutKind.Sequential)] public struct Rect { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)] public struct Point { public int X, Y; }
        [StructLayout(LayoutKind.Sequential)] private struct Message { public IntPtr Window; public uint Id; public UIntPtr WParam; public IntPtr LParam; public uint Time; public Point Position; public uint Private; }
        [StructLayout(LayoutKind.Sequential)] private struct MonitorInfo { public int Size; public Rect Monitor, Work; public uint Flags; }
        [StructLayout(LayoutKind.Sequential)] private struct MouseInput { public int X, Y; public uint Data, Flags, Time; public IntPtr Extra; }
        [StructLayout(LayoutKind.Sequential)] private struct KeyboardInput { public ushort Key, Scan; public uint Flags, Time; public IntPtr Extra; }
        [StructLayout(LayoutKind.Explicit)] private struct InputUnion { [FieldOffset(0)] public MouseInput Mouse; [FieldOffset(0)] public KeyboardInput Keyboard; }
        [StructLayout(LayoutKind.Sequential)] private struct Input { public uint Type; public InputUnion Data; }
        [DllImport("ole32.dll")] private static extern int OleInitialize(IntPtr reserved);
        [DllImport("ole32.dll")] private static extern void OleUninitialize();
        [DllImport("ole32.dll")] private static extern int DoDragDrop([MarshalAs(UnmanagedType.Interface)] IDataObject data, [MarshalAs(UnmanagedType.Interface)] IDropSource source, int allowed, out int effect);
        [DllImport("shell32.dll", ExactSpelling=true)] public static extern int SHCreateStdEnumFmtEtc(uint count,
            [In, MarshalAs(UnmanagedType.LPArray, SizeParamIndex=0)] FORMATETC[] formats,
            [MarshalAs(UnmanagedType.Interface)] out IEnumFORMATETC enumerator);
        [DllImport("kernel32.dll", SetLastError=true)] public static extern IntPtr GlobalAlloc(uint flags, UIntPtr size);
        [DllImport("kernel32.dll", SetLastError=true)] public static extern IntPtr GlobalLock(IntPtr memory);
        [DllImport("kernel32.dll")] public static extern bool GlobalUnlock(IntPtr memory);
        [DllImport("kernel32.dll")] public static extern IntPtr GlobalFree(IntPtr memory);
        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out Rect bounds);
        [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);
        [DllImport("user32.dll", EntryPoint="GetMonitorInfoW")] private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo information);
        [DllImport("user32.dll")] public static extern bool GetCursorPos(out Point point);
        [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
        [DllImport("user32.dll")] public static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
        [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr window);
        [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
        [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
        [DllImport("user32.dll", EntryPoint="PeekMessageW")] private static extern bool PeekMessage(out Message message, IntPtr window, uint first, uint last, uint remove);
        [DllImport("user32.dll")] private static extern bool TranslateMessage(ref Message message);
        [DllImport("user32.dll", EntryPoint="DispatchMessageW")] private static extern IntPtr DispatchMessage(ref Message message);
        [DllImport("user32.dll", SetLastError=true)] private static extern uint SendInput(uint count, Input[] inputs, int size);
        public static Rect WindowBounds(IntPtr window) { Rect value; if (!GetWindowRect(window, out value)) throw new InvalidOperationException("Owned window bounds unavailable."); return value; }
        public static Rect WorkArea(IntPtr window) { var value = new MonitorInfo { Size = Marshal.SizeOf(typeof(MonitorInfo)) }; if (!GetMonitorInfo(MonitorFromWindow(window, 2), ref value)) throw new InvalidOperationException("Owned work area unavailable."); return value.Work; }
        public static void MoveCursor(int x, int y) {
            int left = GetSystemMetrics(76), top = GetSystemMetrics(77), width = GetSystemMetrics(78), height = GetSystemMetrics(79);
            if (width <= 0 || height <= 0 || x < left || x >= (long)left + width || y < top || y >= (long)top + height) throw new ArgumentOutOfRangeException("x", "Drag coordinates exceed the physical desktop.");
            var input = new Input[1];
            input[0].Data.Mouse.X = Math.Max(0, Math.Min(65535, (int)Math.Floor(((long)x - left + .5) * 65536 / width)));
            input[0].Data.Mouse.Y = Math.Max(0, Math.Min(65535, (int)Math.Floor(((long)y - top + .5) * 65536 / height)));
            input[0].Data.Mouse.Flags = 0x0001 | 0x8000 | 0x4000;
            Send(input);
            DateTime deadline = DateTime.UtcNow.AddSeconds(1);
            do { Point actual; if (GetCursorPos(out actual) && actual.X == x && actual.Y == y) return; Thread.Sleep(10); } while (DateTime.UtcNow < deadline);
            throw new InvalidOperationException("The disposable desktop did not deliver the actual drag pointer.");
        }
        private static void Send(Input[] input) { if (SendInput((uint)input.Length, input, Marshal.SizeOf(typeof(Input))) != input.Length) throw new InvalidOperationException("Windows rejected owned drag input (Win32 " + Marshal.GetLastWin32Error() + ")."); }
        private static void LeftButton(bool down) { var input = new Input[1]; input[0].Data.Mouse.Flags = down ? 2u : 4u; Send(input); }
        private static void WaitLeftButton(bool down) {
            DateTime deadline = DateTime.UtcNow.AddSeconds(1);
            do { if (((GetAsyncKeyState(1) & 0x8000) != 0) == down) return; Thread.Sleep(10); } while (DateTime.UtcNow < deadline);
            throw new InvalidOperationException("Windows did not deliver the injected physical left-button " + (down ? "press." : "release."));
        }
        public static byte[] Dib() {
            const int dimension = 16;
            var bytes = new byte[40 + dimension * dimension * 4];
            using (var stream = new MemoryStream(bytes)) using (var writer = new BinaryWriter(stream)) {
                writer.Write(40); writer.Write(dimension); writer.Write(dimension); writer.Write((short)1); writer.Write((short)32);
                writer.Write(0); writer.Write(dimension * dimension * 4); writer.Write(2835); writer.Write(2835); writer.Write(0); writer.Write(0);
                for (int y = 0; y < dimension; y++) for (int x = 0; x < dimension; x++) { writer.Write((byte)(80 + x * 5)); writer.Write((byte)(100 + y * 5)); writer.Write((byte)35); writer.Write((byte)255); }
            }
            return bytes;
        }
        public static void WriteFixtureBmp(string path) {
            byte[] dib = Dib();
            using (var stream = File.Create(path)) using (var writer = new BinaryWriter(stream)) { writer.Write((short)0x4d42); writer.Write(dib.Length + 14); writer.Write(0); writer.Write(54); writer.Write(dib); }
        }
        public static DragResult DropFile(string path, int originX, int originY, int targetX, int targetY) {
            byte[] names = Encoding.Unicode.GetBytes(Path.GetFullPath(path) + "\0\0");
            byte[] bytes = new byte[20 + names.Length];
            Buffer.BlockCopy(BitConverter.GetBytes(20), 0, bytes, 0, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(1), 0, bytes, 16, 4); // DROPFILES.fWide
            Buffer.BlockCopy(names, 0, bytes, 20, names.Length);
            return Perform(FileDropFormat, bytes, originX, originY, targetX, targetY);
        }
        public static DragResult DropBitmap(int originX, int originY, int targetX, int targetY) { return Perform(BitmapFormat, Dib(), originX, originY, targetX, targetY); }
        private static DragResult Perform(short format, byte[] bytes, int originX, int originY, int targetX, int targetY) {
            DropData data = null; DropSource source = null;
            var started = new ManualResetEvent(false); var finished = new ManualResetEvent(false);
            var diagnostics = new DragDiagnostics { CallerThread = Thread.CurrentThread.ManagedThreadId, Phase = "Preparing real drag" };
            var result = new DragResult { Diagnostics = diagnostics }; Exception error = null;
            LastDragResult = result;
            var worker = new Thread(delegate() {
                bool initialized = false; IntPtr dpi = IntPtr.Zero;
                try {
                    diagnostics.WorkerThread = Thread.CurrentThread.ManagedThreadId;
                    dpi = SetThreadDpiAwarenessContext(new IntPtr(-4));
                    int hr = OleInitialize(IntPtr.Zero); if (hr < 0) Marshal.ThrowExceptionForHR(hr); initialized = true;
                    // Create and first expose both CCWs in the same initialized
                    // STA that owns the real native DoDragDrop operation.
                    data = new DropData(format, bytes, diagnostics); source = new DropSource(diagnostics);
                    diagnostics.Phase = "DoDragDrop"; diagnostics.Record("Entering DoDragDrop");
                    started.Set(); result.HResult = DoDragDrop(data, source, 1, out result.Effect);
                    diagnostics.DragReturned = true;
                    diagnostics.Phase = "Transfer message pump"; diagnostics.Record("DoDragDrop returned HRESULT=" + result.HResult + " effect=" + result.Effect);
                    // WinUI may read a DataPackage asynchronously after its
                    // native Drop handler returns. Keep the source's apartment
                    // pumping until the offered format is retrieved, rather
                    // than manufacturing an unavailable-source failure.
                    if (result.HResult == 0x00040100 && result.Effect == 1) {
                        DateTime transferDeadline = DateTime.UtcNow.AddSeconds(3);
                        DateTime? retrievedAt = null;
                        do {
                            Message message;
                            while (PeekMessage(out message, IntPtr.Zero, 0, 0, 1)) { TranslateMessage(ref message); DispatchMessage(ref message); }
                            if (data.RequestedFormats.Length != 0 && !retrievedAt.HasValue) retrievedAt = DateTime.UtcNow;
                            if (retrievedAt.HasValue && DateTime.UtcNow - retrievedAt.Value >= TimeSpan.FromMilliseconds(200)) break;
                            Thread.Sleep(10);
                        } while (DateTime.UtcNow < transferDeadline);
                    } else diagnostics.Record("No accepted Copy transfer: retention pump unnecessary");
                    diagnostics.TransferPumpCompleted = true;
                    result.RequestedFormats = data.RequestedFormats;
                } catch (Exception caught) { error = caught; diagnostics.Record("Worker failed: " + caught.GetType().Name + ": " + caught.Message); started.Set(); }
                finally {
                    diagnostics.Phase = "OleUninitialize"; diagnostics.Record("Leaving OLE apartment");
                    if (initialized) OleUninitialize(); if (dpi != IntPtr.Zero) SetThreadDpiAwarenessContext(dpi);
                    diagnostics.Phase = "Finished"; diagnostics.Record("Worker finished"); finished.Set();
                }
            });
            worker.IsBackground = true; worker.SetApartmentState(ApartmentState.STA);
            try {
                MoveCursor(originX, originY); LeftButton(true); WaitLeftButton(true);
                diagnostics.ButtonDownVerified = true; diagnostics.Record("Injected real left press verified");
                worker.Start();
                if (!started.WaitOne(1000)) throw new TimeoutException("The STA OLE drag did not initialize.");
                if (error != null) throw new InvalidOperationException("The native OLE source failed to initialize.", error);
                Thread.Sleep(100);
                MoveCursor(targetX, targetY);
                // Genuine DragEnter/DragOver reaches the compact island and has
                // enough time to reveal its final Shelf target before release.
                Thread.Sleep(1000);
                LeftButton(false); WaitLeftButton(false);
                diagnostics.ButtonUpVerified = true; diagnostics.Record("Injected real left release verified");
                if (!finished.WaitOne(5000)) throw new TimeoutException("OLE did not finish the owned drop within its bounded watchdog. " + diagnostics.Summary());
                if (error != null) throw new InvalidOperationException("The native OLE source failed.", error);
                result.RequestedFormats = data.RequestedFormats;
                return result;
            } finally {
                if (source != null) source.Cancel = true;
                try { LeftButton(false); MoveCursor(targetX, targetY); } catch (Exception cleanup) { diagnostics.Record("Input cleanup: " + cleanup.Message); }
                if (worker.IsAlive) finished.WaitOne(1500);
                if (data != null) result.RequestedFormats = data.RequestedFormats;
                if (!worker.IsAlive) { started.Dispose(); finished.Dispose(); }
            }
        }
    }
}
'@
    $originalDpi = [NotchlingShelfOle.Native]::SetThreadDpiAwarenessContext([IntPtr]::new(-4))
    $originalCursor = [NotchlingShelfOle.Native+Point]::new()
    $restoreCursor = [NotchlingShelfOle.Native]::GetCursorPos([ref]$originalCursor)
    $root = [System.Windows.Automation.AutomationElement]::FromHandle([IntPtr]::new($WindowHandle))
    if (-not $root -or $root.Current.ProcessId -ne $AppProcessId) { throw "The supplied HWND is not owned by the expected app." }
    $directory = [IO.Path]::GetDirectoryName([IO.Path]::GetFullPath($ReportPath))
    $fixtures = Join-Path $directory "shelf-drop-fixtures"
    [IO.Directory]::CreateDirectory($fixtures) | Out-Null
    $sourceFile = Join-Path $fixtures ("notchling-ole-" + [guid]::NewGuid().ToString("N") + ".bmp")
    [NotchlingShelfOle.Native]::WriteFixtureBmp($sourceFile)
    $report.SourceFile = $sourceFile
    $beforeHash = (Get-OwnedFileSha256 $sourceFile)
    $workspacePath = Join-Path ([Environment]::GetFolderPath("LocalApplicationData")) "Notch/workspace.json"
    $captureDirectory = [IO.Path]::GetFullPath((Join-Path ([Environment]::GetFolderPath("LocalApplicationData")) "Notch/shelf-captures"))

    $report.Stage = "Real CF_HDROP file from compact island"
    $compact = Reach-Compact
    $fileDrag = [NotchlingShelfOle.Native]::DropFile($sourceFile, $outsideX, $outsideY,
        [int]($compact.Left + $compact.Width / 2), [int]($compact.Top + $compact.Height / 2))
    $report.FileDrag = $fileDrag
    if ($fileDrag.HResult -ne 0x00040100 -or $fileDrag.Effect -ne 1) { throw "Windows rejected the real file Copy drop (HRESULT $($fileDrag.HResult), effect $($fileDrag.Effect))." }
    if ($fileDrag.RequestedFormats -notcontains [NotchlingShelfOle.Native]::FileDropFormat) { throw "The drop target did not retrieve the offered native CF_HDROP data." }
    $report.FileDropCopy = $true; $report.FileFormatRetrieved = $true
    # Border is a visual drop surface rather than an interactive automation
    # peer. Verify the Shelf's real actionable control before item/count checks.
    Wait-Control "Choose files" "" ([System.Windows.Automation.InvokePattern]::Pattern) | Out-Null
    Set-Pin $true
    $persistedFile = Wait-PersistedPath $sourceFile
    Assert-VisiblePath $persistedFile
    $report.PersistedFileReferenceVerified = $true
    $report.Actions += "CF_HDROP caused the compact island to open Shelf, accepted only Copy, retrieved the real format and persisted the original file reference."

    $report.Stage = "Real CF_DIB bitmap from compact island"
    $beforeBitmap = @(@(Read-Shelf) | ForEach-Object { [string]$_.Path })
    $compact = Reach-Compact
    $bitmapDrag = [NotchlingShelfOle.Native]::DropBitmap($outsideX, $outsideY,
        [int]($compact.Left + $compact.Width / 2), [int]($compact.Top + $compact.Height / 2))
    $report.BitmapDrag = $bitmapDrag
    if ($bitmapDrag.HResult -ne 0x00040100 -or $bitmapDrag.Effect -ne 1) { throw "Windows rejected the real bitmap Copy drop (HRESULT $($bitmapDrag.HResult), effect $($bitmapDrag.Effect))." }
    if ($bitmapDrag.RequestedFormats -notcontains [NotchlingShelfOle.Native]::BitmapFormat) { throw "The drop target did not retrieve the offered native CF_DIB bytes." }
    $report.BitmapDropCopy = $true; $report.BitmapFormatRetrieved = $true
    # Border is a visual drop surface rather than an interactive automation
    # peer. Verify the Shelf's real actionable control before item/count checks.
    Wait-Control "Choose files" "" ([System.Windows.Automation.InvokePattern]::Pattern) | Out-Null
    Set-Pin $true
    $savedBitmap = Wait-PersistedPath "" $beforeBitmap
    Assert-VisiblePath $savedBitmap
    if (-not (Test-Path -LiteralPath $savedBitmap -PathType Leaf)) { throw "The actual captured bitmap file is missing." }
    if ((Get-Item -LiteralPath $savedBitmap).Length -gt 4MB) { throw "The captured 16 by 16 bitmap exceeds its fixture size bound." }
    $bitmapBytes = [IO.File]::ReadAllBytes($savedBitmap)
    $validPng = $bitmapBytes.Length -ge 24 -and [Convert]::ToBase64String($bitmapBytes, 0, 8) -eq "iVBORw0KGgo=" -and
        $bitmapBytes[16] -eq 0 -and $bitmapBytes[17] -eq 0 -and $bitmapBytes[18] -eq 0 -and $bitmapBytes[19] -eq 16 -and
        $bitmapBytes[20] -eq 0 -and $bitmapBytes[21] -eq 0 -and $bitmapBytes[22] -eq 0 -and $bitmapBytes[23] -eq 16
    $validBmp = $bitmapBytes.Length -ge 54 -and $bitmapBytes[0] -eq 0x42 -and $bitmapBytes[1] -eq 0x4d -and
        [BitConverter]::ToInt32($bitmapBytes, 18) -eq 16 -and [Math]::Abs([BitConverter]::ToInt32($bitmapBytes, 22)) -eq 16
    if (-not $validPng -and -not $validBmp) { throw "The native CF_DIB transfer did not persist its real bounded 16 by 16 PNG or BMP stream." }
    # Decode the complete captured stream as well as checking its header. This
    # detects truncated output and proves the transfer retained actual pixels.
    $imageStream = [IO.MemoryStream]::new($bitmapBytes, $false)
    $decodedImage = $null
    try {
        $decodedImage = [Drawing.Bitmap]::new($imageStream)
        if ($decodedImage.Width -ne 16 -or $decodedImage.Height -ne 16) { throw "The captured bitmap decoded with incorrect dimensions." }
        $pixel = $decodedImage.GetPixel(8, 8)
        if ($pixel.R -ne 35 -or $pixel.G -lt 100 -or $pixel.G -gt 175 -or $pixel.B -lt 80 -or $pixel.B -gt 155) {
            throw "The captured bitmap did not preserve the fixture's actual pixels."
        }
    } finally {
        if ($decodedImage) { $decodedImage.Dispose() }
        $imageStream.Dispose()
    }
    $report.PersistedBitmapFormat = if ($validPng) { "PNG" } else { "BMP" }
    $report.SavedBitmap = $savedBitmap; $report.PersistedBitmapVerified = $true
    $report.CompactDropOpenedShelf = $true
    $count = Wait-Control "" "ShelfCount"
    $report.ShelfCountObserved = $count.Current.Name
    if ($count.Current.Name -notmatch '^\d+ items?') { throw "Shelf did not expose an accessible real item count." }
    if (-not (Test-Path -LiteralPath $sourceFile -PathType Leaf) -or (Get-OwnedFileSha256 $sourceFile) -ne $beforeHash) {
        throw "The Copy-only Shelf drop moved, removed or changed the original source file."
    }
    $report.OriginalFilePreserved = $true
    $report.Actions += "CF_DIB caused the compact island to open Shelf and persisted its real 16 by 16 image stream; the original BMP remains unchanged."
    $errorBar = Find-Control "Notchling error" ""
    if ($errorBar) { throw "The actual file/bitmap drop displayed an application error." }
    $report.Stage = "Completed genuine OLE file and bitmap drops"
    $report.Succeeded = $true
} catch {
    $failure = $_
    if (('NotchlingShelfOle.Native' -as [type]) -and [NotchlingShelfOle.Native]::LastDragResult) {
        if ($report.Stage -eq "Real CF_HDROP file from compact island") { $report.FileDrag = [NotchlingShelfOle.Native]::LastDragResult }
        elseif ($report.Stage -eq "Real CF_DIB bitmap from compact island") { $report.BitmapDrag = [NotchlingShelfOle.Native]::LastDragResult }
    }
    $report.Error = "Stage [$($report.Stage)]: $($_.Exception.Message). Script stack: $($_.ScriptStackTrace)"
} finally {
    if (Get-Variable -Name restoreCursor -ErrorAction SilentlyContinue) {
        if ($restoreCursor) { [NotchlingShelfOle.Native]::SetCursorPos($originalCursor.X, $originalCursor.Y) | Out-Null }
    }
    if (Get-Variable -Name originalDpi -ErrorAction SilentlyContinue) {
        if ($originalDpi -ne [IntPtr]::Zero) { [NotchlingShelfOle.Native]::SetThreadDpiAwarenessContext($originalDpi) | Out-Null }
    }
    $report.ElapsedSeconds = [Math]::Round($clock.Elapsed.TotalSeconds, 2)
    $reportFullPath = [IO.Path]::GetFullPath($ReportPath)
    [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($reportFullPath)) | Out-Null
    $report | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $reportFullPath -Encoding utf8
}
if ($failure) { Write-Error -Message $report.Error -ErrorAction Continue; exit 1 }
