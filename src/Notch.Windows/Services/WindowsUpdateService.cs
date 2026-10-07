using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;

namespace Notch.Windows.Services;

public sealed record AvailableUpdate(string Version, string InstallerUrl, string Sha256, long SizeBytes, int MinimumWindowsBuild);
public sealed record PreparedUpdate(string Version, string InstallerPath, string Sha256);

/// <summary>Explicit stable-channel updates anchored to the installed application's trusted signer.</summary>
public sealed class WindowsUpdateService(HttpClient http)
{
    private const string Channel = "https://github.com/SuryaK999/Notchling/releases/latest/download/notchling-update.json";
    private const long MaximumInstallerBytes = 256 * 1024 * 1024;
    private readonly HttpClient _http = http;

    public Task<bool> CanVerifyPublisherAsync(CancellationToken cancellationToken = default)
        => Task.Run(() => ReadInstalledPublisherKey() is not null, cancellationToken);

    public async Task<AvailableUpdate?> CheckAsync(CancellationToken cancellationToken = default)
    {
        if (await Task.Run(ReadInstalledPublisherKey, cancellationToken) is null)
            throw new InvalidOperationException("Verified updates require an official signed application. Use the release page for evaluation builds.");
        var bytes = await ReadBoundedAsync(Channel, 16 * 1024, cancellationToken);
        var manifest = JsonSerializer.Deserialize<Manifest>(bytes, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidDataException("The update manifest is empty.");
        var update = ValidateManifest(manifest);
        var current = typeof(WindowsUpdateService).Assembly.GetName().Version ?? new Version(0, 0, 0);
        return Version.Parse(update.Version) > new Version(current.Major, current.Minor, Math.Max(0, current.Build)) ? update : null;
    }

    public async Task<PreparedUpdate> DownloadAsync(AvailableUpdate update, CancellationToken cancellationToken = default)
    {
        ValidateUpdate(update);
        var publisher = await Task.Run(ReadInstalledPublisherKey, cancellationToken) ?? throw new InvalidOperationException("The installed publisher signature cannot be verified.");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromMinutes(5));
        var directory = Path.Combine(Path.GetTempPath(), "Notchling.Update", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "Notchling-setup.exe");
        try
        {
            using var response = await _http.GetAsync(update.InstallerUrl, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
            response.EnsureSuccessStatusCode();
            RequireHttpsResponse(response);
            if (response.Content.Headers.ContentLength is { } declared && declared != update.SizeBytes)
                throw new InvalidDataException("The installer size does not match its manifest.");
            await using (var input = await response.Content.ReadAsStreamAsync(deadline.Token))
            await using (var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, FileOptions.Asynchronous))
            {
                var buffer = new byte[65536];
                long total = 0;
                int count;
                while ((count = await input.ReadAsync(buffer, deadline.Token)) > 0)
                {
                    total += count;
                    if (total > update.SizeBytes || total > MaximumInstallerBytes)
                        throw new InvalidDataException("The installer exceeds its permitted size.");
                    await output.WriteAsync(buffer.AsMemory(0, count), deadline.Token);
                }
                if (total != update.SizeBytes) throw new InvalidDataException("The installer download is incomplete.");
            }
            await RequireHashAsync(path, update.Sha256, deadline.Token);
            if (await Task.Run(() => ReadTrustedPublisherKey(path), deadline.Token) is not { } actual || !CryptographicOperations.FixedTimeEquals(publisher, actual))
                throw new InvalidDataException("The installer signature does not match the trusted application publisher.");
            return new(update.Version, path, update.Sha256);
        }
        catch
        {
            try { Directory.Delete(directory, true); } catch (IOException) { }
            throw;
        }
    }

    public async Task OpenInstallerAsync(PreparedUpdate update, CancellationToken cancellationToken = default)
    {
        var publisher = await Task.Run(ReadInstalledPublisherKey, cancellationToken) ?? throw new InvalidOperationException("The application publisher cannot be verified.");
        await RequireHashAsync(update.InstallerPath, update.Sha256, cancellationToken);
        if (await Task.Run(() => ReadTrustedPublisherKey(update.InstallerPath), cancellationToken) is not { } actual || !CryptographicOperations.FixedTimeEquals(publisher, actual))
            throw new InvalidDataException("The prepared installer failed signature verification.");
        Process.Start(new ProcessStartInfo(update.InstallerPath) { UseShellExecute = true });
        // Inno Setup asks the user to save/quit while the application mutex is
        // held. This helper never terminates the running app or edits its data.
    }

    private static AvailableUpdate ValidateManifest(Manifest manifest)
    {
        if (manifest.SchemaVersion != 1 || manifest.Architecture != "x64") throw new InvalidDataException("Unsupported update manifest.");
        var update = new AvailableUpdate(manifest.Version ?? "", manifest.InstallerUrl ?? "", manifest.Sha256 ?? "", manifest.SizeBytes, manifest.MinimumWindowsBuild);
        ValidateUpdate(update);
        return update;
    }
    private static void ValidateUpdate(AvailableUpdate update)
    {
        if (!Version.TryParse(update.Version, out var version) || version.Revision >= 0 || version.Build < 0
            || update.Version != $"{version.Major}.{version.Minor}.{version.Build}"
            || update.InstallerUrl != $"https://github.com/SuryaK999/Notchling/releases/download/v{update.Version}/Notchling-{update.Version}-windows-x64-setup.exe"
            || update.Sha256.Length != 64 || !update.Sha256.All(Uri.IsHexDigit)
            || update.SizeBytes <= 0 || update.SizeBytes > MaximumInstallerBytes || update.MinimumWindowsBuild < 19045)
            throw new InvalidDataException("Invalid update asset or integrity information.");
        if (!OperatingSystem.IsWindows() || RuntimeInformation.OSArchitecture != Architecture.X64 || Environment.OSVersion.Version.Build < update.MinimumWindowsBuild)
            throw new InvalidOperationException("This update requires a supported Windows x64 build.");
    }
    private async Task<byte[]> ReadBoundedAsync(string uri, int maximum, CancellationToken token)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(20));
        using var response = await _http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
        response.EnsureSuccessStatusCode(); RequireHttpsResponse(response);
        await using var input = await response.Content.ReadAsStreamAsync(deadline.Token);
        using var output = new MemoryStream();
        var buffer = new byte[4096];
        int count;
        while ((count = await input.ReadAsync(buffer, deadline.Token)) > 0)
        {
            if (output.Length + count > maximum) throw new InvalidDataException("The update manifest exceeds its size limit.");
            output.Write(buffer, 0, count);
        }
        return output.ToArray();
    }
    private static void RequireHttpsResponse(HttpResponseMessage response)
    {
        if (response.RequestMessage?.RequestUri?.Scheme != Uri.UriSchemeHttps) throw new InvalidDataException("An insecure update redirect was refused.");
    }
    private static async Task RequireHashAsync(string path, string expected, CancellationToken token)
    {
        await using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.Asynchronous);
        var actual = await SHA256.HashDataAsync(file, token);
        if (expected.Length != 64 || !CryptographicOperations.FixedTimeEquals(actual, Convert.FromHexString(expected)))
            throw new InvalidDataException("Installer SHA-256 verification failed.");
    }
    private static byte[]? ReadInstalledPublisherKey()
    {
        if (Environment.ProcessPath is not { } executable) return null;
        var key = ReadTrustedPublisherKey(executable);
        var assembly = ReadTrustedPublisherKey(typeof(WindowsUpdateService).Assembly.Location);
        // A signed dotnet.exe host alone cannot authorize an unsigned DLL.
        return key is not null && assembly is not null && CryptographicOperations.FixedTimeEquals(key, assembly) ? key : null;
    }
    private static byte[]? ReadTrustedPublisherKey(string path)
    {
        if (!OperatingSystem.IsWindows() || !File.Exists(path)) return null;
        return VerifyAndReadPublisherKey(path);
    }

    private static byte[]? VerifyAndReadPublisherKey(string path)
    {
        var file = new TrustFile { Size = (uint)Marshal.SizeOf<TrustFile>(), Path = path };
        var pointer = Marshal.AllocHGlobal(Marshal.SizeOf<TrustFile>());
        Marshal.StructureToPtr(file, pointer, false);
        var data = new TrustData { Size = (uint)Marshal.SizeOf<TrustData>(), UiChoice = 2, RevocationChecks = 1, UnionChoice = 1, File = pointer, StateAction = 1, ProviderFlags = 0x80 };
        var action = new Guid("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");
        try
        {
            if (WinVerifyTrust(new IntPtr(-1), ref action, ref data) != 0) return null;
            var provider = WTHelperProvDataFromStateData(data.StateData);
            if (provider == 0) return null;
            var signer = WTHelperGetProvSignerFromChain(provider, 0, false, 0);
            if (signer == 0) return null;
            var providerCertificate = WTHelperGetProvCertFromChain(signer, 0);
            if (providerCertificate == 0) return null;
            // CRYPT_PROVIDER_CERT starts with DWORD cbStruct then aligned
            // PCCERT_CONTEXT pCert. Read the verified provider's leaf signer,
            // rather than independently guessing a certificate in the PE file.
            var contextPointer = Marshal.ReadIntPtr(providerCertificate, IntPtr.Size);
            if (contextPointer == 0) return null;
            var context = Marshal.PtrToStructure<CertificateContext>(contextPointer);
            if (context.Encoded == 0 || context.EncodedLength is 0 or > 131072) return null;
            var encoded = new byte[context.EncodedLength];
            Marshal.Copy(context.Encoded, encoded, 0, encoded.Length);
            using var certificate = X509CertificateLoader.LoadCertificate(encoded);
            return SHA256.HashData(certificate.GetPublicKey());
        }
        catch (CryptographicException) { return null; }
        finally
        {
            data.StateAction = 2; WinVerifyTrust(new IntPtr(-1), ref action, ref data);
            Marshal.DestroyStructure<TrustFile>(pointer); Marshal.FreeHGlobal(pointer);
        }
    }
    [DllImport("wintrust.dll", ExactSpelling = true, PreserveSig = true)]
    private static extern int WinVerifyTrust(nint window, ref Guid action, ref TrustData data);
    [DllImport("wintrust.dll", ExactSpelling = true)]
    private static extern nint WTHelperProvDataFromStateData(nint stateData);
    [DllImport("wintrust.dll", ExactSpelling = true)]
    private static extern nint WTHelperGetProvSignerFromChain(nint provider, uint signer, [MarshalAs(UnmanagedType.Bool)] bool counterSigner, uint counterSignerIndex);
    [DllImport("wintrust.dll", ExactSpelling = true)]
    private static extern nint WTHelperGetProvCertFromChain(nint signer, uint certificate);
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct TrustFile { public uint Size; [MarshalAs(UnmanagedType.LPWStr)] public string Path; public nint File; public nint KnownSubject; }
    [StructLayout(LayoutKind.Sequential)]
    private struct TrustData { public uint Size; public nint PolicyCallback; public nint SipClient; public uint UiChoice; public uint RevocationChecks; public uint UnionChoice; public nint File; public uint StateAction; public nint StateData; public nint Url; public uint ProviderFlags; public uint UiContext; }
    [StructLayout(LayoutKind.Sequential)]
    private struct CertificateContext { public uint Encoding; public nint Encoded; public uint EncodedLength; public nint Information; public nint Store; }
    private sealed record Manifest(int SchemaVersion, string? Version, int MinimumWindowsBuild, string? Architecture, string? InstallerUrl, string? Sha256, long SizeBytes);
}
