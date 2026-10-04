using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Notch.Billing;

public static class StripeWebhook
{
    public static bool Verify(ReadOnlySpan<byte> body, string signatureHeader, string secret, DateTimeOffset now)
    {
        if (body.Length > 1024 * 1024 || signatureHeader.Length > 4096 || string.IsNullOrWhiteSpace(secret)) return false;
        var fields = signatureHeader.Split(',');
        var timestampText = fields.FirstOrDefault(field => field.StartsWith("t=", StringComparison.Ordinal))?[2..];
        if (!long.TryParse(timestampText, NumberStyles.None, CultureInfo.InvariantCulture, out var timestamp)
            || timestamp < now.ToUnixTimeSeconds() - 300 || timestamp > now.ToUnixTimeSeconds() + 300) return false;
        var prefix = Encoding.UTF8.GetBytes(timestampText + ".");
        var message = new byte[prefix.Length + body.Length]; prefix.CopyTo(message, 0); body.CopyTo(message.AsSpan(prefix.Length));
        var expected = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), message);
        foreach (var field in fields.Where(field => field.StartsWith("v1=", StringComparison.Ordinal)))
        {
            try { if (CryptographicOperations.FixedTimeEquals(expected, Convert.FromHexString(field[3..]))) return true; }
            catch (FormatException) { }
        }
        return false;
    }
}
