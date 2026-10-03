using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using Notch.Core;

namespace Notch.Windows.Services;

/// <summary>Native machine telemetry and controls. Screen time covers this app session only.</summary>
public sealed class WindowsSystemService : ISystemService
{
    private readonly object _sampleGate = new();
    private readonly object _awakeGate = new();
    private readonly SemaphoreSlim _volumeGate = new(1, 1);
    private readonly Timer _screenSampler;
    private ulong _lastIdle;
    private ulong _lastKernel;
    private ulong _lastUser;
    private bool _hasCpuSample;
    private long _lastScreenSample = Stopwatch.GetTimestamp();
    private TimeSpan _activeScreenTime;
    private BlockingCollection<AwakeRequest>? _awakeRequests;
    private Thread? _awakeThread;
    private volatile bool _disposed;
    public bool AudioAvailable { get; private set; }

    public WindowsSystemService()
    {
        // Keep session time accurate while the panel is collapsed without polling audio or rendering.
        _screenSampler = new Timer(_ =>
        {
            lock (_sampleGate)
            {
                if (!_disposed) ReadActiveScreenTime();
            }
        }, null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
    }

    public Task<SystemSnapshot> ReadAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            double cpu;
            TimeSpan screenTime;
            lock (_sampleGate)
            {
                cpu = ReadCpu();
                screenTime = ReadActiveScreenTime();
            }
            var memory = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
            if (!Native.GlobalMemoryStatusEx(ref memory)) throw new Win32Exception(Marshal.GetLastWin32Error());
            int? battery = null;
            if (Native.GetSystemPowerStatus(out var power) && power.BatteryFlag != 128 && power.BatteryLifePercent <= 100)
                battery = power.BatteryLifePercent;
            var audio = ReadAudio();
            cancellationToken.ThrowIfCancellationRequested();
            return new SystemSnapshot(cpu, memory.MemoryLoad, battery, audio.Volume, audio.Device, screenTime);
        }, cancellationToken);
    }

    public async Task SetVolumeAsync(double volume)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!double.IsFinite(volume) || volume is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(volume), "Volume must be between 0 and 1.");
        await _volumeGate.WaitAsync().ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            await Task.Run(() => WithAudioEndpoint((endpoint, _) =>
            {
                var context = Guid.Empty;
                Marshal.ThrowExceptionForHR(endpoint.SetMasterVolumeLevelScalar((float)volume, ref context));
                if (volume > 0) Marshal.ThrowExceptionForHR(endpoint.SetMute(false, ref context));
                AudioAvailable = true;
                return true;
            })).ConfigureAwait(false);
        }
        finally { _volumeGate.Release(); }
    }

    public IReadOnlyList<int> ListeningPorts()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners()
            .Select(endpoint => endpoint.Port).Distinct().Order().ToArray();
    }

    private double ReadCpu()
    {
        if (!Native.GetSystemTimes(out var idleTime, out var kernelTime, out var userTime))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        var idle = idleTime.Value;
        var kernel = kernelTime.Value;
        var user = userTime.Value;
        double percent = 0; // Initial reading establishes a baseline; there is no blocking sleep.
        if (_hasCpuSample && idle >= _lastIdle && kernel >= _lastKernel && user >= _lastUser)
        {
            var total = (kernel - _lastKernel) + (user - _lastUser);
            var idleDelta = idle - _lastIdle;
            if (total > 0) percent = Math.Clamp(100d * (total - Math.Min(total, idleDelta)) / total, 0, 100);
        }
        _lastIdle = idle;
        _lastKernel = kernel;
        _lastUser = user;
        _hasCpuSample = true;
        return percent;
    }

    private TimeSpan ReadActiveScreenTime()
    {
        var now = Stopwatch.GetTimestamp();
        var elapsed = Stopwatch.GetElapsedTime(_lastScreenSample, now);
        _lastScreenSample = now;
        // Exclude long app suspension gaps rather than turning them into fabricated active use.
        if (elapsed > TimeSpan.FromSeconds(10)) elapsed = TimeSpan.FromSeconds(10);
        var input = new LastInputInfo { Size = (uint)Marshal.SizeOf<LastInputInfo>() };
        if (Native.GetLastInputInfo(ref input))
        {
            var idleMilliseconds = unchecked((uint)Environment.TickCount - input.Time);
            var inactive = Math.Max(0, idleMilliseconds / 1000d - 60);
            _activeScreenTime += TimeSpan.FromSeconds(Math.Clamp(elapsed.TotalSeconds - inactive, 0, elapsed.TotalSeconds));
        }
        return _activeScreenTime;
    }

    private (double Volume, string Device) ReadAudio()
    {
        try
        {
            return WithAudioEndpoint((endpoint, device) =>
            {
                Marshal.ThrowExceptionForHR(endpoint.GetMasterVolumeLevelScalar(out var scalar));
                Marshal.ThrowExceptionForHR(endpoint.GetMute(out var muted));
                AudioAvailable = true;
                return (muted ? 0 : Math.Clamp((double)scalar, 0, 1), ReadDeviceName(device));
            });
        }
        catch (COMException)
        {
            AudioAvailable = false;
            return (0, "Audio output unavailable");
        }
    }

    private static T WithAudioEndpoint<T>(Func<IAudioEndpointVolume, IMMDevice, T> operation)
    {
        IMMDeviceEnumerator? enumerator = null;
        IMMDevice? device = null;
        object? activated = null;
        try
        {
            enumerator = (IMMDeviceEnumerator)new DeviceEnumerator();
            Marshal.ThrowExceptionForHR(enumerator.GetDefaultAudioEndpoint(0, 1, out device)); // Render, multimedia.
            var id = typeof(IAudioEndpointVolume).GUID;
            Marshal.ThrowExceptionForHR(device.Activate(ref id, 23, 0, out activated)); // CLSCTX_ALL.
            return operation((IAudioEndpointVolume)activated, device);
        }
        finally
        {
            Release(activated);
            Release(device);
            Release(enumerator);
        }
    }

    private static string ReadDeviceName(IMMDevice device)
    {
        IPropertyStore? properties = null;
        var value = new PropVariant();
        try
        {
            Marshal.ThrowExceptionForHR(device.OpenPropertyStore(0, out properties));
            var key = new PropertyKey(new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), 14);
            Marshal.ThrowExceptionForHR(properties.GetValue(ref key, out value));
            return value.Type == 31 ? Marshal.PtrToStringUni(value.Pointer) ?? "Default audio output" : "Default audio output";
        }
        finally
        {
            Native.PropVariantClear(ref value);
            Release(properties);
        }
    }

    private static void Release(object? value)
    {
        if (value is not null && Marshal.IsComObject(value)) Marshal.ReleaseComObject(value);
    }

    public void SetAwake(bool awake)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        lock (_awakeGate)
        {
            if (_awakeRequests is null)
            {
                if (!awake) return;
                _awakeRequests = new BlockingCollection<AwakeRequest>();
                var requests = _awakeRequests;
                _awakeThread = new Thread(() => AwakeWorker(requests)) { IsBackground = true, Name = "Notch power request" };
                _awakeThread.Start();
            }
            var request = new AwakeRequest(awake);
            _awakeRequests.Add(request);
            request.Completion.Task.GetAwaiter().GetResult();
        }
    }

    private static void AwakeWorker(BlockingCollection<AwakeRequest> requests)
    {
        try
        {
            foreach (var request in requests.GetConsumingEnumerable())
            {
                var flags = 0x80000000u | (request.Enabled ? 0x00000001u | 0x00000002u : 0u);
                if (Native.SetThreadExecutionState(flags) == 0)
                    request.Completion.TrySetException(new Win32Exception(Marshal.GetLastWin32Error()));
                else request.Completion.TrySetResult();
            }
        }
        finally { Native.SetThreadExecutionState(0x80000000); }
    }

    public void Dispose()
    {
        lock (_awakeGate)
        {
            if (_disposed) return;
            _disposed = true;
            _screenSampler.Dispose();
            if (_awakeRequests is null) return;
            var release = new AwakeRequest(false);
            _awakeRequests.Add(release);
            try { release.Completion.Task.GetAwaiter().GetResult(); }
            finally
            {
                _awakeRequests.CompleteAdding();
                _awakeThread?.Join();
                _awakeRequests.Dispose();
            }
        }
    }

    private sealed record AwakeRequest(bool Enabled)
    {
        public TaskCompletionSource Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
    [StructLayout(LayoutKind.Sequential)] private struct FileTime { public uint Low, High; public readonly ulong Value => ((ulong)High << 32) | Low; }
    [StructLayout(LayoutKind.Sequential)] private struct LastInputInfo { public uint Size, Time; }
    [StructLayout(LayoutKind.Sequential)] private struct MemoryStatus { public uint Length, MemoryLoad; public ulong TotalPhys, AvailPhys, TotalPageFile, AvailPageFile, TotalVirtual, AvailVirtual, AvailExtendedVirtual; }
    [StructLayout(LayoutKind.Sequential)] private struct PowerStatus { public byte ACLineStatus, BatteryFlag, BatteryLifePercent, SystemStatusFlag; public uint BatteryLifeTime, BatteryFullLifeTime; }
    [StructLayout(LayoutKind.Sequential)] private struct PropertyKey(Guid format, uint id) { public Guid Format = format; public uint Id = id; }
    [StructLayout(LayoutKind.Explicit, Size = 24)] private struct PropVariant { [FieldOffset(0)] public ushort Type; [FieldOffset(8)] public nint Pointer; }

    private static class Native
    {
        [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool GetSystemTimes(out FileTime idle, out FileTime kernel, out FileTime user);
        [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);
        [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool GetSystemPowerStatus(out PowerStatus status);
        [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool GetLastInputInfo(ref LastInputInfo info);
        [DllImport("kernel32.dll", SetLastError = true)] internal static extern uint SetThreadExecutionState(uint flags);
        [DllImport("ole32.dll")] internal static extern int PropVariantClear(ref PropVariant value);
    }

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")] private class DeviceEnumerator { }
    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int flow, uint mask, out nint devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int flow, int role, out IMMDevice device);
        [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);
        [PreserveSig] int RegisterEndpointNotificationCallback(nint client);
        [PreserveSig] int UnregisterEndpointNotificationCallback(nint client);
    }
    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid id, uint context, nint parameters, [MarshalAs(UnmanagedType.IUnknown)] out object instance);
        [PreserveSig] int OpenPropertyStore(uint access, out IPropertyStore properties);
        [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
        [PreserveSig] int GetState(out uint state);
    }
    [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioEndpointVolume
    {
        [PreserveSig] int RegisterControlChangeNotify(nint callback);
        [PreserveSig] int UnregisterControlChangeNotify(nint callback);
        [PreserveSig] int GetChannelCount(out uint count);
        [PreserveSig] int SetMasterVolumeLevel(float level, ref Guid context);
        [PreserveSig] int SetMasterVolumeLevelScalar(float level, ref Guid context);
        [PreserveSig] int GetMasterVolumeLevel(out float level);
        [PreserveSig] int GetMasterVolumeLevelScalar(out float level);
        [PreserveSig] int SetChannelVolumeLevel(uint channel, float level, ref Guid context);
        [PreserveSig] int SetChannelVolumeLevelScalar(uint channel, float level, ref Guid context);
        [PreserveSig] int GetChannelVolumeLevel(uint channel, out float level);
        [PreserveSig] int GetChannelVolumeLevelScalar(uint channel, out float level);
        [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid context);
        [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
        [PreserveSig] int GetVolumeStepInfo(out uint step, out uint stepCount);
        [PreserveSig] int VolumeStepUp(ref Guid context);
        [PreserveSig] int VolumeStepDown(ref Guid context);
        [PreserveSig] int QueryHardwareSupport(out uint mask);
        [PreserveSig] int GetVolumeRange(out float minimum, out float maximum, out float increment);
    }
    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int GetAt(uint index, out PropertyKey key);
        [PreserveSig] int GetValue(ref PropertyKey key, out PropVariant value);
        [PreserveSig] int SetValue(ref PropertyKey key, ref PropVariant value);
        [PreserveSig] int Commit();
    }
}
