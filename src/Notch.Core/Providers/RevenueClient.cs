using System.Net.Http.Headers;
using System.Text.Json;

namespace Notch.Core.Providers;

/// <summary>Read-only payment reporting. Payment totals are not a recurring-revenue calculation.</summary>
public sealed class RevenueClient(HttpClient client, ISecretVault vault, TimeProvider? timeProvider = null)
{
    private static readonly HashSet<string> SupportedCurrencies = new(StringComparer.OrdinalIgnoreCase)
    { "AED", "AFN", "ALL", "AMD", "ANG", "AOA", "ARS", "AUD", "AWG", "AZN", "BAM", "BBD", "BDT", "BGN", "BHD", "BIF", "BMD", "BND", "BOB", "BRL", "BSD", "BWP", "BZD", "CAD", "CDF", "CHF", "CLP", "CNY", "COP", "CRC", "CVE", "CZK", "DJF", "DKK", "DOP", "DZD", "EEK", "EGP", "ETB", "EUR", "FJD", "FKP", "GBP", "GEL", "GIP", "GMD", "GNF", "GTQ", "GYD", "HKD", "HNL", "HRK", "HTG", "HUF", "IDR", "ILS", "INR", "ISK", "JMD", "JOD", "JPY", "KES", "KGS", "KHR", "KMF", "KRW", "KWD", "KYD", "KZT", "LAK", "LBP", "LKR", "LRD", "LSL", "LTL", "LVL", "MAD", "MDL", "MGA", "MKD", "MNT", "MOP", "MRO", "MUR", "MVR", "MWK", "MXN", "MYR", "MZN", "NAD", "NGN", "NIO", "NOK", "NPR", "NZD", "OMR", "PAB", "PEN", "PGK", "PHP", "PKR", "PLN", "PYG", "QAR", "RON", "RSD", "RUB", "RWF", "SAR", "SBD", "SCR", "SEK", "SGD", "SHP", "SLL", "SOS", "SRD", "STD", "SVC", "SZL", "THB", "TJS", "TND", "TOP", "TRY", "TTD", "TWD", "TZS", "UAH", "UGX", "USD", "UYU", "UZS", "VEF", "VND", "VUV", "WST", "XAF", "XCD", "XOF", "XPF", "YER", "ZAR", "ZMW" };
    private static readonly HashSet<string> ZeroDecimalCurrencies = new(StringComparer.OrdinalIgnoreCase)
    { "BIF", "CLP", "DJF", "GNF", "JPY", "KMF", "KRW", "MGA", "PYG", "RWF", "VND", "VUV", "XAF", "XOF", "XPF" };
    private static readonly HashSet<string> ThreeDecimalCurrencies = new(StringComparer.OrdinalIgnoreCase)
    { "BHD", "JOD", "KWD", "OMR", "TND" };

    public async Task<RevenueSnapshot> ReadAsync(RevenueProvider provider, int days, string? adSenseAccount = null, CancellationToken cancellationToken = default)
    {
        if (days is < 1 or > 366) throw new ArgumentOutOfRangeException(nameof(days), "Choose 1 through 366 days.");
        if (provider != RevenueProvider.Stripe)
            throw new NotSupportedException($"{provider} is not connected: this build has no verified API adapter for that provider. No revenue data has been inferred.");
        var secret = ProviderHttp.RequireSecret(vault, "stripe");
        using var operation = new ProviderOperation(client, cancellationToken);
        try { return await ReadReportAsync(provider, days, secret, operation.Token).ConfigureAwait(false); }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("The revenue report did not finish in time. Choose a shorter range or try again.");
        }
    }

    private async Task<RevenueSnapshot> ReadReportAsync(RevenueProvider provider, int days, string secret, CancellationToken cancellationToken)
    {
        var now = (timeProvider ?? TimeProvider.System).GetUtcNow();
        var start = new DateTimeOffset(now.UtcDateTime.Date.AddDays(1 - days), TimeSpan.Zero);
        var payments = new List<RevenuePayment>();
        var daily = new decimal[days];
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var cursors = new HashSet<string>(StringComparer.Ordinal);
        string? cursor = null, currency = null;
        var complete = false;
        for (var page = 0; page < 100; page++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var url = $"https://api.stripe.com/v1/charges?limit=100&created%5Bgte%5D={start.ToUnixTimeSeconds()}&created%5Blte%5D={now.ToUnixTimeSeconds()}";
            if (cursor is not null) url += "&starting_after=" + Uri.EscapeDataString(cursor);
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", secret);
            using var document = await ProviderHttp.ReadJsonAsync(client, request, cancellationToken).ConfigureAwait(false);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array || data.GetArrayLength() > 100)
                throw new InvalidDataException("Stripe returned an invalid charge list.");
            string? lastId = null;
            foreach (var charge in data.EnumerateArray())
            {
                var id = ProviderHttp.Text(charge, "id", 200);
                if (string.IsNullOrWhiteSpace(id) || id.Any(char.IsControl))
                    throw new InvalidDataException("Stripe returned an invalid charge identifier.");
                lastId = id;
                if (!ids.Add(id)) continue;
                if (!IsTrue(charge, "paid") || !IsTrue(charge, "captured") || ProviderHttp.Text(charge, "status", 20) != "succeeded") continue;
                var amount = ProviderHttp.Integer(charge, "amount_captured");
                var refunded = ProviderHttp.Integer(charge, "amount_refunded");
                if (refunded > amount) throw new InvalidDataException("Stripe returned a refund greater than the captured charge.");
                var code = ProviderHttp.Text(charge, "currency", 3).ToUpperInvariant();
                if (code.Length != 3 || code.Any(ch => !char.IsAsciiLetter(ch)) || !SupportedCurrencies.Contains(code))
                    throw new InvalidDataException("Stripe returned an invalid currency code.");
                if (currency is not null && currency != code)
                    throw new InvalidDataException("This range contains multiple currencies. A single total cannot combine them; use a single-currency account or a currency-specific report.");
                currency = code;
                var divisor = ZeroDecimalCurrencies.Contains(code) ? 1m : ThreeDecimalCurrencies.Contains(code) ? 1000m : 100m;
                // Stripe represents ISK and UGX charges in hundredths for API compatibility.
                var net = (amount - refunded) / divisor;
                var created = DateTimeOffset.FromUnixTimeSeconds(ProviderHttp.Integer(charge, "created", 253402300799));
                var day = (int)(created.UtcDateTime.Date - start.UtcDateTime.Date).TotalDays;
                if (day < 0 || day >= days || created > now) throw new InvalidDataException("Stripe returned a charge outside the requested range.");
                var description = charge.TryGetProperty("description", out var descriptionValue) && descriptionValue.ValueKind == JsonValueKind.String
                    ? descriptionValue.GetString() ?? "Payment" : "Payment";
                if (description.Length > 500) description = description[..500];
                daily[day] += net;
                payments.Add(new(id, description, net, code, created));
            }
            if (!root.TryGetProperty("has_more", out var more) || more.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                throw new InvalidDataException("Stripe omitted its pagination status.");
            if (!more.GetBoolean()) { complete = true; break; }
            if (lastId is null || !cursors.Add(lastId)) throw new InvalidDataException("Stripe pagination did not advance.");
            cursor = lastId;
        }
        return new(provider, payments.Sum(payment => payment.Amount), currency ?? string.Empty,
            payments.OrderByDescending(payment => payment.CreatedAt).ToArray(), daily, now, complete,
            RequestedDays: days, RangeStart: start, RangeEnd: now);
    }

    private static bool IsTrue(JsonElement element, string name) => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;
}
