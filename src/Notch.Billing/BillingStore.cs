using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Notch.Billing;

public sealed record Account(string Id, string Email, string? CustomerId = null,
    DateTimeOffset? PaidThrough = null, DateTimeOffset? RefreshedAt = null);
public sealed record LoginChallenge(string Email, string DeviceId, string CodeHash, DateTimeOffset ExpiresAt, int Attempts = 0);
public sealed record LoginSession(string TokenHash, string AccountId, string DeviceId, DateTimeOffset ExpiresAt);
public sealed class BillingState
{
    public List<Account> Accounts { get; set; } = [];
    public List<LoginChallenge> Challenges { get; set; } = [];
    public List<LoginSession> Sessions { get; set; } = [];
    public Dictionary<string, DateTimeOffset> ProcessedEvents { get; set; } = [];
    public Dictionary<string, DateTimeOffset> LoginRequests { get; set; } = [];
}

/// <summary>One service process with an owner-only durable volume. Never use an ephemeral container disk.</summary>
public sealed class BillingStore(BillingOptions options)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);
    public async Task<T> TransactionAsync<T>(Func<BillingState, Task<T>> action, CancellationToken token = default)
    {
        if (!options.Ready) throw new BillingUnavailableException();
        await _gate.WaitAsync(token);
        try
        {
            var directory = Path.GetFullPath(options.DataDirectory);
            Directory.CreateDirectory(directory);
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            var path = Path.Combine(directory, "billing.json");
            BillingState state;
            if (File.Exists(path))
            {
                await using var input = File.OpenRead(path);
                state = await JsonSerializer.DeserializeAsync<BillingState>(input, _json, token)
                    ?? throw new InvalidDataException("Billing state cannot be read.");
            }
            else state = new();
            var result = await action(state);
            var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                    4096, FileOptions.WriteThrough))
                {
                    if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(temporary, UnixFileMode.UserRead | UnixFileMode.UserWrite);
                    await JsonSerializer.SerializeAsync(output, state, _json, token);
                    await output.FlushAsync(token); output.Flush(true);
                }
                File.Move(temporary, path, true);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
            return result;
        }
        finally { _gate.Release(); }
    }
    public static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    public static bool Matches(string expected, string actual) => CryptographicOperations.FixedTimeEquals(
        Encoding.ASCII.GetBytes(expected), Encoding.ASCII.GetBytes(Hash(actual)));
}
