using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml;
using Notch.Windows.Services;

internal static class UpdateServiceCases
{
    private const string Repo = "https://github.com/Sury2797/Notchling";
    private static readonly byte[] Publisher = SHA256.HashData(Encoding.UTF8.GetBytes("installed verified publisher"));
    private static readonly byte[] OtherPublisher = SHA256.HashData(Encoding.UTF8.GetBytes("another verified publisher"));
    private static readonly byte[] Payload = Enumerable.Range(0, 150000).Select(index => (byte)index).ToArray();
    private static readonly string Hash = Convert.ToHexString(SHA256.HashData(Payload));
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static async Task Reject<T>(Func<Task> operation) where T : Exception
    { try { await operation(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name + " was not thrown."); }
    private static string AssetUrl(string version = "0.4.0", string arch = "x64", bool evaluation = false)
        => Repo + "/releases/download/" + (evaluation ? "notchling-evaluation-" : "v") + version + "/Notchling-" + version + "-windows-" + arch + (evaluation ? "-evaluation-setup.exe" : "-setup.exe");
    private static object Release(string version = "0.4.0", string arch = "x64", bool evaluation = true, bool draft = false, bool? prerelease = null, string? assetUrl = null, string? page = null)
        => new { draft, prerelease = prerelease ?? evaluation, tag_name = (evaluation ? "notchling-evaluation-" : "v") + version,
            html_url = page ?? Repo + "/releases/tag/" + (evaluation ? "notchling-evaluation-" : "v") + version,
            assets = new[] { new { name = AssetUrl(version, arch, evaluation).Split('/')[^1], browser_download_url = assetUrl ?? AssetUrl(version, arch, evaluation), size = Payload.Length, state = "uploaded" } } };
    private static byte[] Releases(params object[] releases) => JsonSerializer.SerializeToUtf8Bytes(releases);
    private static byte[] Manifest(string version = "0.4.0", string arch = "x64", int minimumBuild = 19045, string? url = null, string? hash = null, long? size = null)
        => JsonSerializer.SerializeToUtf8Bytes(new { schemaVersion = 1, version, minimumWindowsBuild = minimumBuild,
            architecture = arch, installerUrl = url ?? AssetUrl(version, arch), sha256 = hash ?? Hash, sizeBytes = size ?? Payload.Length });
    private static byte[] MultiManifest(string[]? architectures = null, string? signer = null)
        => JsonSerializer.SerializeToUtf8Bytes(new { schemaVersion = 2, version = "0.4.0", minimumWindowsBuild = 19045,
            signerPublicKeySha256 = signer ?? Convert.ToHexString(Publisher), assets = (architectures ?? ["x64", "x86", "arm64"])
                .Select(arch => new { architecture = arch, installerUrl = AssetUrl(arch: arch), sha256 = Hash, sizeBytes = Payload.Length }).ToArray() });
    private static WindowsUpdateService Service(HttpClient http, bool signed = false, Architecture arch = Architecture.X64,
        Func<string, byte[]?>? installerPublisher = null, TimeSpan? timeout = null, int build = 19045, Version? version = null,
        Func<byte[]?>? installedPublisher = null)
        => new(http, new WindowsUpdateService.WindowsUpdateEnvironment(version ?? new(0, 3, 4), arch, build, true,
            installedPublisher ?? (() => signed ? Publisher : null), installerPublisher ?? (_ => Publisher)), timeout);
    private static HttpResponseMessage Reply(byte[] bytes, HttpStatusCode status = HttpStatusCode.OK)
        => new(status) { Content = new ByteArrayContent(bytes) };
    private static HttpClient Client(DelegateHandler handler) => new(handler) { Timeout = Timeout.InfiniteTimeSpan };
    private static AvailableUpdate Update(string version = "0.4.0", string arch = "x64", string? hash = null, long? size = null)
        => new(version, AssetUrl(version, arch), hash ?? Hash, size ?? Payload.Length, 19045, arch);
    private static void Remove(PreparedUpdate prepared) => Directory.Delete(Path.GetDirectoryName(prepared.InstallerPath)!, true);

    public static async Task<int> RunAsync()
    {
        var passed = 0;
        async Task Case(string name, Func<Task> run) { await run(); passed++; Console.WriteLine("PASS: update " + name); }
        await Case("unsigned installer checks refuse before HTTP", async () =>
        {
            using var http = Client(new((_, _) => throw new InvalidOperationException("Unsigned checks must not request an installer manifest.")));
            await Reject<InvalidOperationException>(() => Service(http).CheckAsync());
            await Reject<InvalidOperationException>(() => Service(http).DownloadAsync(Update()));
        });
        await Case("evaluation discovery finds highest compatible release including non-latest prereleases", async () =>
        {
            var handler = new DelegateHandler((_, _) => Task.FromResult(Reply(Releases(Release("0.4.1", evaluation: false), Release("0.6.0", "arm64"), Release("0.5.0"), Release("0.4.0")))));
            using var http = Client(handler); var result = await Service(http).DiscoverAsync();
            Require(result is { Version: "0.5.0", Architecture: "x64", Channel: UpdateChannel.Evaluation, CanInstallVerified: false, VerifiedUpdate: null }
                && result.ReleasePageUrl == Repo + "/releases/tag/notchling-evaluation-0.5.0" && handler.Calls == 1,
                "Discovery used GitHub's stable-only latest endpoint, list order, or an incompatible architecture.");
        });
        await Case("unsigned stable discovery remains manual", async () =>
        {
            using var http = Client(new((_, _) => Task.FromResult(Reply(Releases(Release(evaluation: false))))));
            Require(await Service(http).DiscoverAsync() is { Channel: UpdateChannel.Stable, CanInstallVerified: false, VerifiedUpdate: null }, "Metadata granted installer execution to an unsigned application.");
        });
        await Case("drafts mislabeled channels hostile pages and hostile asset URLs are ignored", async () =>
        {
            using var http = Client(new((_, _) => Task.FromResult(Reply(Releases(
                Release(draft: true), Release(evaluation: false, prerelease: true), Release(prerelease: false),
                Release(assetUrl: "https://github.com/attacker/repo/releases/download/v0.4.0/app.exe"), Release(page: "https://example.invalid/release"))))));
            Require(await Service(http).DiscoverAsync() is null, "An unofficial, draft, or mislabeled release was offered.");
        });
        await Case("malformed release entries do not obscure a valid compatible entry", async () =>
        {
            using var http = Client(new((_, _) => Task.FromResult(Reply(Releases("unrelated", new { draft = false, prerelease = true, tag_name = "notchling-evaluation-0.4.0", html_url = 6, assets = new object[] { null!, "wrong" } }, Release())))));
            Require(await Service(http).DiscoverAsync() is { Version: "0.4.0" }, "A malformed neighboring release crashed discovery.");
        });
        await Case("current older and noncanonical versions never notify", async () =>
        {
            using var http = Client(new((_, _) => Task.FromResult(Reply(Releases(Release("0.3.4"), Release("0.2.9"), Release("00.4.0"), Release("0.4.0.0"))))));
            Require(await Service(http).DiscoverAsync() is null, "A downgrade or noncanonical version was offered.");
        });
        await Case("singleflight shares one release request and a cancelled waiter leaves owner alive", async () =>
        {
            var began = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
            var handler = new DelegateHandler((_, _) => { began.TrySetResult(); return release.Task; });
            using var http = Client(handler); var service = Service(http);
            var owner = service.DiscoverAsync(); await began.Task.WaitAsync(TimeSpan.FromSeconds(2));
            using var waitingToken = new CancellationTokenSource(); var waiter = service.DiscoverAsync(waitingToken.Token); waitingToken.Cancel();
            await Reject<OperationCanceledException>(() => waiter);
            release.SetResult(Reply(Releases(Release())));
            Require(await owner is { Version: "0.4.0" } && handler.Calls == 1, "Concurrent discovery duplicated requests or cancellation poisoned its owner.");
        });
        await Case("cancelled discovery retries cleanly", async () =>
        {
            var began = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var attempt = 0;
            var handler = new DelegateHandler(async (_, token) => { if (Interlocked.Increment(ref attempt) == 1) { began.SetResult(); await Task.Delay(Timeout.Infinite, token); } return Reply(Releases(Release())); });
            using var http = Client(handler); var service = Service(http); using var token = new CancellationTokenSource();
            var request = service.DiscoverAsync(token.Token); await began.Task; token.Cancel(); await Reject<OperationCanceledException>(() => request);
            Require(await service.DiscoverAsync() is { Version: "0.4.0" } && handler.Calls == 2, "Cancelled singleflight retained a failed task or blocked retry.");
        });
        await Case("body stalls ignoring cancellation are still bounded", async () =>
        {
            using var http = Client(new((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new StalledStream()) })));
            await Reject<TimeoutException>(() => Service(http, timeout: TimeSpan.FromMilliseconds(80)).DiscoverAsync().WaitAsync(TimeSpan.FromSeconds(2)));
        });
        await Case("response byte bounds cover declared and streamed data", async () =>
        {
            using var http = Client(new((_, _) => Task.FromResult(Reply(new byte[1024 * 1024 + 1]))));
            await Reject<InvalidDataException>(() => Service(http).DiscoverAsync());
        });
        await Case("unrelated HTTPS redirect is refused", async () =>
        {
            using var http = Client(new((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new ByteArrayContent(Releases(Release())), RequestMessage = new(HttpMethod.Get, "https://example.invalid/releases") })));
            await Reject<InvalidDataException>(() => Service(http).DiscoverAsync());
        });
        await Case("GitHub CDN HTTPS response remains accepted", async () =>
        {
            using var http = Client(new((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new ByteArrayContent(Manifest()), RequestMessage = new(HttpMethod.Get, "https://release-assets.githubusercontent.com/github-production-release-asset/file") })));
            Require(await Service(http, signed: true).CheckAsync() is { Version: "0.4.0" }, "An official GitHub CDN response was rejected.");
        });
        await Case("rate-limited API falls back to official Atom and exact architecture assets", async () =>
        {
            var handler = new DelegateHandler((request, _) => Task.FromResult(request.RequestUri!.Host == "api.github.com" ? Reply([], HttpStatusCode.Forbidden)
                : request.RequestUri.AbsolutePath.EndsWith(".atom") ? Reply(Encoding.UTF8.GetBytes(Feed()))
                : Reply(Encoding.UTF8.GetBytes("<a href=\"/Sury2797/Notchling/releases/download/notchling-evaluation-0.4.0/Notchling-0.4.0-windows-x64-evaluation-setup.exe\">Setup</a>"))));
            using var http = Client(handler); var result = await Service(http).DiscoverAsync();
            Require(result is { Version: "0.4.0", CanInstallVerified: false } && handler.Calls == 3, "Rate-limit fallback missed or authorized the manual evaluation release.");
        });
        await Case("feed fallback cannot offer another architecture", async () =>
        {
            using var http = Client(new((request, _) => Task.FromResult(request.RequestUri!.Host == "api.github.com" ? Reply([], HttpStatusCode.TooManyRequests)
                : request.RequestUri.AbsolutePath.EndsWith(".atom") ? Reply(Encoding.UTF8.GetBytes(Feed()))
                : Reply(Encoding.UTF8.GetBytes("<a href=\"/Sury2797/Notchling/releases/download/notchling-evaluation-0.4.0/Notchling-0.4.0-windows-arm64-evaluation-setup.exe\">Setup</a>")))));
            Require(await Service(http).DiscoverAsync() is null, "A feed advertised an unavailable architecture.");
        });
        await Case("feed parser rejects external entities", async () =>
        {
            using var http = Client(new((request, _) => Task.FromResult(request.RequestUri!.Host == "api.github.com" ? Reply([], HttpStatusCode.Forbidden)
                : Reply(Encoding.UTF8.GetBytes("<!DOCTYPE feed [<!ENTITY secret SYSTEM 'file:///tmp/private'>]><feed xmlns='http://www.w3.org/2005/Atom'>&secret;</feed>")))));
            await Reject<XmlException>(() => Service(http).DiscoverAsync());
        });
        await Case("legacy signed manifest remains compatible", async () =>
        {
            using var http = Client(new((_, _) => Task.FromResult(Reply(Manifest()))));
            Require(await Service(http, signed: true).DiscoverAsync() is { Channel: UpdateChannel.Stable, CanInstallVerified: true, VerifiedUpdate.Architecture: "x64" }, "Legacy signed manifest lost verified stable discovery.");
        });
        await Case("schema2 selects the running app architecture across x64 x86 and ARM64", async () =>
        {
            foreach (var (arch, name) in new[] { (Architecture.X64, "x64"), (Architecture.X86, "x86"), (Architecture.Arm64, "arm64") })
            {
                using var http = Client(new((_, _) => Task.FromResult(Reply(MultiManifest()))));
                Require(await Service(http, signed: true, arch: arch).CheckAsync() is { } update && update.Architecture == name && update.InstallerUrl == AssetUrl(arch: name), "Manifest chose OS architecture rather than installed app architecture.");
            }
        });
        await Case("duplicate manifest architectures and a mismatched publisher are rejected", async () =>
        {
            using var duplicate = Client(new((_, _) => Task.FromResult(Reply(MultiManifest(["x64", "x64"])))));
            await Reject<InvalidDataException>(() => Service(duplicate, signed: true).CheckAsync());
            using var publisher = Client(new((_, _) => Task.FromResult(Reply(MultiManifest(signer: Convert.ToHexString(OtherPublisher))))));
            await Reject<InvalidDataException>(() => Service(publisher, signed: true).CheckAsync());
        });
        await Case("missing architecture unsupported OS and higher required builds are rejected", async () =>
        {
            using var missing = Client(new((_, _) => Task.FromResult(Reply(MultiManifest(["arm64"])))));
            await Reject<InvalidOperationException>(() => Service(missing, signed: true).CheckAsync());
            using var modern = Client(new((_, _) => Task.FromResult(Reply(Manifest(minimumBuild: 26100)))));
            await Reject<InvalidOperationException>(() => Service(modern, signed: true).CheckAsync());
            using var unsupported = Client(new((_, _) => throw new InvalidOperationException("An unsupported OS must not make requests.")));
            await Reject<InvalidOperationException>(() => Service(unsupported, signed: true, build: 17763).DiscoverAsync());
        });
        await Case("manifest source integrity and canonical versions are validated", async () =>
        {
            foreach (var bytes in new[] { Manifest(url: "https://example.invalid/setup.exe"), Manifest(hash: "bad"), Manifest(size: 268435457), Manifest(version: "00.4.0"), Manifest(minimumBuild: 19041) })
            {
                using var http = Client(new((_, _) => Task.FromResult(Reply(bytes))));
                await Reject<InvalidDataException>(() => Service(http, signed: true).CheckAsync());
            }
        });
        await Case("signed current and older manifests do not offer downgrades", async () =>
        {
            foreach (var version in new[] { "0.3.4", "0.2.9" })
            { using var http = Client(new((_, _) => Task.FromResult(Reply(Manifest(version))))); Require(await Service(http, signed: true).CheckAsync() is null, "Stable discovery offered an older installer."); }
        });
        await Case("downloads reject downgrades hostile URLs and wrong architecture before requesting", async () =>
        {
            using var http = Client(new((_, _) => throw new InvalidOperationException("Invalid installer must not make requests.")));
            var service = Service(http, signed: true);
            await Reject<InvalidDataException>(() => service.DownloadAsync(Update("0.3.4")));
            await Reject<InvalidDataException>(() => service.DownloadAsync(Update() with { InstallerUrl = "https://example.invalid/a.exe" }));
            await Reject<InvalidDataException>(() => service.DownloadAsync(Update(arch: "arm64")));
        });
        await Case("signed download progress reaches Ready only after bytes hash and publisher pass", async () =>
        {
            using var http = Client(new((_, _) => Task.FromResult(Reply(Payload))));
            var service = Service(http, signed: true); var progress = new CaptureProgress();
            var prepared = await service.DownloadAsync(Update(), progress);
            try
            {
                Require(File.ReadAllBytes(prepared.InstallerPath).SequenceEqual(Payload) && prepared.Architecture == "x64"
                    && progress.Values.First() is { Stage: UpdateDownloadStage.Downloading, ReceivedBytes: 0 }
                    && progress.Values[^2].Stage == UpdateDownloadStage.Verifying && progress.Values[^1].Stage == UpdateDownloadStage.Ready
                    && progress.Values[^1].Fraction == 1 && progress.Values.All(value => value.ReceivedBytes <= value.TotalBytes), "Download progress or prepared bytes misrepresented verification.");
            }
            finally { Remove(prepared); }
        });
        await Case("size declared by transport and actual truncated payload must match", async () =>
        {
            using var wrongLength = Client(new((_, _) => Task.FromResult(Reply(Payload))));
            await Reject<InvalidDataException>(() => Service(wrongLength, signed: true).DownloadAsync(Update(size: Payload.Length - 1)));
            using var truncated = Client(new((_, _) => { var reply = Reply(Payload[..^1]); reply.Content.Headers.ContentLength = null; return Task.FromResult(reply); }));
            await Reject<InvalidDataException>(() => Service(truncated, signed: true).DownloadAsync(Update()));
        });
        await Case("hash and actual signer mismatch never emit Ready", async () =>
        {
            using var wrongHash = Client(new((_, _) => Task.FromResult(Reply(Payload)))); var hashProgress = new CaptureProgress();
            await Reject<InvalidDataException>(() => Service(wrongHash, signed: true).DownloadAsync(Update(hash: new('0', 64)), hashProgress));
            Require(hashProgress.Values.All(value => value.Stage != UpdateDownloadStage.Ready), "Failed hash emitted verified Ready.");
            using var wrongSigner = Client(new((_, _) => Task.FromResult(Reply(Payload)))); var signerProgress = new CaptureProgress();
            await Reject<InvalidDataException>(() => Service(wrongSigner, signed: true, installerPublisher: _ => OtherPublisher).DownloadAsync(Update(), signerProgress));
            Require(signerProgress.Values.All(value => value.Stage != UpdateDownloadStage.Ready), "Failed publisher pin emitted Ready.");
        });
        await Case("cancelled downloads release gate and remove partial files", async () =>
        {
            var began = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var attempt = 0;
            var handler = new DelegateHandler(async (_, token) => { if (Interlocked.Increment(ref attempt) == 1) { began.SetResult(); await Task.Delay(Timeout.Infinite, token); } return Reply(Payload); });
            using var http = Client(handler); var service = Service(http, signed: true); using var token = new CancellationTokenSource();
            var directories = UpdateDirectories(); var downloading = service.DownloadAsync(Update(), token.Token); await began.Task; token.Cancel();
            await Reject<OperationCanceledException>(() => downloading);
            Require(!UpdateDirectories().Except(directories).Any(), "Cancelled download retained a partial installer directory.");
            var retry = await service.DownloadAsync(Update()); Remove(retry);
        });
        await Case("concurrent downloads never duplicate installers", async () =>
        {
            var began = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var release = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
            var handler = new DelegateHandler((_, _) => { began.SetResult(); return release.Task; }); using var http = Client(handler); var service = Service(http, signed: true);
            var first = service.DownloadAsync(Update()); await began.Task;
            await Reject<InvalidOperationException>(() => service.DownloadAsync(Update())); release.SetResult(Reply(Payload));
            var prepared = await first; Remove(prepared); Require(handler.Calls == 1, "Parallel downloads made duplicate HTTP requests.");
        });
        await Case("cancelled publisher verification releases download gate before HTTP", async () =>
        {
            var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var publisherCalls = 0;
            var handler = new DelegateHandler((_, _) => Task.FromResult(Reply(Payload)));
            using var http = Client(handler);
            var service = Service(http, signed: true, installedPublisher: () =>
            {
                if (Interlocked.Increment(ref publisherCalls) == 1) { started.TrySetResult(); release.Task.GetAwaiter().GetResult(); }
                return Publisher;
            });
            using var cancellation = new CancellationTokenSource(); var directories = UpdateDirectories();
            try
            {
                var download = service.DownloadAsync(Update(), cancellation.Token);
                await started.Task.WaitAsync(TimeSpan.FromSeconds(2)); cancellation.Cancel();
                await Reject<OperationCanceledException>(() => download.WaitAsync(TimeSpan.FromSeconds(2)));
                Require(handler.Calls == 0 && !UpdateDirectories().Except(directories).Any(), "Cancelled verification requested bytes or retained a download directory.");
                var retry = await service.DownloadAsync(Update()).WaitAsync(TimeSpan.FromSeconds(2));
                Remove(retry); Require(handler.Calls == 1, "Cancelled publisher verification retained the download gate.");
            }
            finally { release.TrySetResult(); }
        });
        await Case("publisher verification timeout is bounded before downloading bytes", async () =>
        {
            var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var handler = new DelegateHandler((_, _) => throw new InvalidOperationException("Timed-out publisher verification must not request installer bytes."));
            using var http = Client(handler);
            var service = Service(http, signed: true, timeout: TimeSpan.FromMilliseconds(80), installedPublisher: () =>
            { started.TrySetResult(); release.Task.GetAwaiter().GetResult(); return Publisher; });
            try
            {
                var download = service.DownloadAsync(Update());
                await started.Task.WaitAsync(TimeSpan.FromSeconds(2));
                await Reject<TimeoutException>(() => download.WaitAsync(TimeSpan.FromSeconds(2)));
                Require(handler.Calls == 0, "Publisher timeout allowed a download to begin.");
            }
            finally { release.TrySetResult(); }
        });
        await Case("cancelled prepared-installer publisher verification never reaches launch", async () =>
        {
            var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var publisherCalls = 0; var installerChecks = 0;
            using var http = Client(new((_, _) => Task.FromResult(Reply(Payload))));
            var service = Service(http, signed: true, installerPublisher: _ => { Interlocked.Increment(ref installerChecks); return Publisher; }, installedPublisher: () =>
            {
                if (Interlocked.Increment(ref publisherCalls) > 1) { started.TrySetResult(); release.Task.GetAwaiter().GetResult(); }
                return Publisher;
            });
            var prepared = await service.DownloadAsync(Update());
            using var cancellation = new CancellationTokenSource();
            try
            {
                var opening = service.OpenInstallerAsync(prepared, cancellation.Token);
                await started.Task.WaitAsync(TimeSpan.FromSeconds(2)); cancellation.Cancel();
                await Reject<OperationCanceledException>(() => opening.WaitAsync(TimeSpan.FromSeconds(2)));
                Require(installerChecks == 1 && File.ReadAllBytes(prepared.InstallerPath).SequenceEqual(Payload), "Cancelled opening proceeded past publisher verification or altered the prepared installer.");
            }
            finally { release.TrySetResult(); Remove(prepared); }
        });
        await Case("prepared provenance and post-download tampering are checked before execution", async () =>
        {
            using var http = Client(new((_, _) => Task.FromResult(Reply(Payload)))); var service = Service(http, signed: true);
            await Reject<InvalidDataException>(() => service.OpenInstallerAsync(new("0.4.0", Path.Combine(Path.GetTempPath(), "arbitrary.exe"), Hash)));
            var prepared = await service.DownloadAsync(Update());
            try { await File.WriteAllBytesAsync(prepared.InstallerPath, [1, 2]); await Reject<InvalidDataException>(() => service.OpenInstallerAsync(prepared)); }
            finally { Remove(prepared); }
        });
        Console.WriteLine($"PASS: {passed} updater regression cases (HTTP/publisher doubles; no installer executed).");
        return passed;
    }
    private static string Feed() => "<feed xmlns='http://www.w3.org/2005/Atom'><entry><link rel='alternate' href='" + Repo + "/releases/tag/notchling-evaluation-0.4.0'/></entry></feed>";
    private static string[] UpdateDirectories() => Directory.Exists(Path.Combine(Path.GetTempPath(), "Notchling.Update")) ? Directory.GetDirectories(Path.Combine(Path.GetTempPath(), "Notchling.Update")) : [];
    private sealed class CaptureProgress : IProgress<UpdateDownloadProgress>
    { public List<UpdateDownloadProgress> Values { get; } = []; public void Report(UpdateDownloadProgress value) => Values.Add(value); }
    private sealed class DelegateHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handle) : HttpMessageHandler
    {
        public int Calls;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        { Interlocked.Increment(ref Calls); var response = await handle(request, cancellationToken); response.RequestMessage ??= request; return response; }
    }
    private sealed class StalledStream : Stream
    {
        public override bool CanRead => true; public override bool CanSeek => false; public override bool CanWrite => false;
        public override long Length => 0; public override long Position { get => 0; set => throw new NotSupportedException(); }
        public override void Flush() { } public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => new(new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously).Task);
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException(); public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
