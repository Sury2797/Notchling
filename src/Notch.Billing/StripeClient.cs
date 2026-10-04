using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;

namespace Notch.Billing;

public interface IStripeBilling
{
    Task<string> CreateCustomerAsync(Account account, CancellationToken token);
    Task<DateTimeOffset?> ReadPaidThroughAsync(string customer, CancellationToken token);
    Task<string> CheckoutAsync(string customer, string accountId, CancellationToken token);
    Task<string> PortalAsync(string customer, CancellationToken token);
    Task<string?> CustomerForChargeAsync(string charge, CancellationToken token);
}

public sealed class StripeClient(HttpClient http, BillingOptions options) : IStripeBilling
{
    private DateTimeOffset _priceValidatedAt;
    private bool _priceActive;
    public async Task<string> CreateCustomerAsync(Account account, CancellationToken token)
    {
        using var result = await SendAsync(HttpMethod.Post, "customers", new()
        { ["email"] = account.Email, ["metadata[notch_account]"] = account.Id }, "customer-" + account.Id, token);
        return result.RootElement.GetProperty("id").GetString() ?? throw new InvalidDataException("No customer returned.");
    }
    public async Task<DateTimeOffset?> ReadPaidThroughAsync(string customer, CancellationToken token)
    {
        await ValidatePriceAsync(token, false);
        using var result = await SendAsync(HttpMethod.Get, "subscriptions?customer=" + Uri.EscapeDataString(customer) + "&status=all&limit=100", null, null, token);
        if (result.RootElement.TryGetProperty("has_more", out var more) && more.GetBoolean())
            throw new InvalidDataException("Subscription listing is incomplete.");
        DateTimeOffset? paidThrough = null;
        foreach (var subscription in result.RootElement.GetProperty("data").EnumerateArray())
        {
            if (subscription.GetProperty("status").GetString() != "active") continue;
            var matching = subscription.GetProperty("items").GetProperty("data").EnumerateArray()
                .Any(item => item.GetProperty("price").GetProperty("id").GetString() == options.StripePriceId);
            if (!matching) continue;
            if (!subscription.TryGetProperty("current_period_end", out var period) || !period.TryGetInt64(out var end)) continue;
            var until = DateTimeOffset.FromUnixTimeSeconds(end);
            if (until <= DateTimeOffset.UtcNow) continue;
            var invoiceId = subscription.GetProperty("latest_invoice").GetString();
            if (string.IsNullOrWhiteSpace(invoiceId)) continue;
            using var invoiceResult = await SendAsync(HttpMethod.Get, "invoices/" + Uri.EscapeDataString(invoiceId)
                + "?expand[]=payment_intent.latest_charge", null, null, token);
            var invoice = invoiceResult.RootElement;
            var disputesResolved = false;
            if (TryPaidCharge(invoice, out var paidCharge) && paidCharge.TryGetProperty("disputed", out var disputed) && disputed.GetBoolean())
            {
                if (!paidCharge.TryGetProperty("id", out var chargeId) || chargeId.ValueKind != JsonValueKind.String) continue;
                using var disputes = await SendAsync(HttpMethod.Get, "disputes?charge=" + Uri.EscapeDataString(chargeId.GetString() ?? "") + "&limit=100", null, null, token);
                if (disputes.RootElement.GetProperty("has_more").GetBoolean()) throw new BillingUnavailableException();
                var data = disputes.RootElement.GetProperty("data");
                disputesResolved = data.GetArrayLength() > 0 && data.EnumerateArray().All(item => item.GetProperty("status").GetString() is "won" or "warning_closed");
            }
            if (!InvoiceGrantsAccess(invoice, disputesResolved)) continue;
            if (paidThrough is null || until > paidThrough) paidThrough = until;
        }
        return paidThrough;
    }
    public static bool InvoiceGrantsAccess(JsonElement invoice, bool disputesResolved = false)
    {
        if (!invoice.TryGetProperty("status", out var status) || status.GetString() != "paid"
            || !invoice.TryGetProperty("paid", out var paid) || !paid.GetBoolean()) return false;
        if (!TryPaidCharge(invoice, out var charge) || !charge.TryGetProperty("paid", out var chargePaid) || !chargePaid.GetBoolean()
            || !charge.TryGetProperty("amount", out var amount) || !amount.TryGetInt64(out var charged) || charged <= 0
            || !charge.TryGetProperty("amount_refunded", out var returned) || !returned.TryGetInt64(out var refundedAmount)
            || refundedAmount < 0 || refundedAmount >= charged) return false;
        if (charge.TryGetProperty("disputed", out var disputed) && disputed.GetBoolean() && !disputesResolved) return false;
        return !charge.TryGetProperty("refunded", out var refunded) || !refunded.GetBoolean();
    }
    private static bool TryPaidCharge(JsonElement invoice, out JsonElement charge)
    {
        charge = default;
        return invoice.TryGetProperty("payment_intent", out var intent) && intent.ValueKind == JsonValueKind.Object
            && intent.TryGetProperty("latest_charge", out charge) && charge.ValueKind == JsonValueKind.Object;
    }
    public async Task<string> CheckoutAsync(string customer, string accountId, CancellationToken token)
    {
        await ValidatePriceAsync(token);
        using (var subscriptions = await SendAsync(HttpMethod.Get, "subscriptions?customer=" + Uri.EscapeDataString(customer) + "&status=all&limit=100", null, null, token))
        {
            if (subscriptions.RootElement.GetProperty("has_more").GetBoolean()) throw new BillingUnavailableException();
            foreach (var existing in subscriptions.RootElement.GetProperty("data").EnumerateArray())
                if (existing.GetProperty("status").GetString() is not ("canceled" or "incomplete_expired")
                    && existing.GetProperty("items").GetProperty("data").EnumerateArray()
                        .Any(item => item.GetProperty("price").GetProperty("id").GetString() == options.StripePriceId))
                    throw new BillingValidationException("A subscription already exists. Use Manage subscription to update its payment or cancel.");
        }
        // Recover an existing checkout after a process interruption, preventing repeat purchases.
        using (var previous = await SendAsync(HttpMethod.Get, "checkout/sessions?customer=" + Uri.EscapeDataString(customer) + "&limit=100", null, null, token))
        {
            if (previous.RootElement.GetProperty("has_more").GetBoolean()) throw new BillingUnavailableException();
            foreach (var session in previous.RootElement.GetProperty("data").EnumerateArray())
                if (session.GetProperty("status").GetString() == "open" && session.GetProperty("mode").GetString() == "subscription"
                    && session.GetProperty("client_reference_id").GetString() == accountId
                    && session.GetProperty("expires_at").GetInt64() > DateTimeOffset.UtcNow.ToUnixTimeSeconds())
                {
                    var sessionId = session.GetProperty("id").GetString();
                    if (string.IsNullOrWhiteSpace(sessionId)) throw new BillingUnavailableException();
                    using var items = await SendAsync(HttpMethod.Get, "checkout/sessions/" + Uri.EscapeDataString(sessionId) + "/line_items?limit=100", null, null, token);
                    var lines = items.RootElement.GetProperty("data");
                    if (items.RootElement.GetProperty("has_more").GetBoolean()) throw new BillingUnavailableException();
                    if (lines.GetArrayLength() == 1 && lines[0].GetProperty("price").GetProperty("id").GetString() == options.StripePriceId
                        && lines[0].GetProperty("quantity").GetInt64() == 1)
                        return session.GetProperty("url").GetString() ?? throw new InvalidDataException("No checkout URL returned.");
                    using var expired = await SendAsync(HttpMethod.Post, "checkout/sessions/" + Uri.EscapeDataString(sessionId) + "/expire", new(), "expire-" + sessionId, token);
                }
        }
        using var result = await SendAsync(HttpMethod.Post, "checkout/sessions", new()
        {
            ["mode"] = "subscription", ["customer"] = customer, ["client_reference_id"] = accountId,
            ["line_items[0][price]"] = options.StripePriceId, ["line_items[0][quantity]"] = "1",
            ["subscription_data[metadata][notch_account]"] = accountId,
            ["success_url"] = options.PublicOrigin.TrimEnd('/') + "/billing/return",
            ["cancel_url"] = options.PublicOrigin.TrimEnd('/') + "/billing/return"
        }, "checkout-" + accountId + "-" + options.StripePriceId + "-" + (DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 300).ToString(CultureInfo.InvariantCulture), token);
        return result.RootElement.GetProperty("url").GetString() ?? throw new InvalidDataException("No checkout URL returned.");
    }
    public async Task<string> PortalAsync(string customer, CancellationToken token)
    {
        using var result = await SendAsync(HttpMethod.Post, "billing_portal/sessions", new()
        { ["customer"] = customer, ["return_url"] = options.PublicOrigin.TrimEnd('/') + "/billing/return" }, null, token);
        return result.RootElement.GetProperty("url").GetString() ?? throw new InvalidDataException("No portal URL returned.");
    }
    public async Task<string?> CustomerForChargeAsync(string charge, CancellationToken token)
    {
        if (!charge.StartsWith("ch_", StringComparison.Ordinal) || charge.Length > 255) return null;
        using var result = await SendAsync(HttpMethod.Get, "charges/" + Uri.EscapeDataString(charge), null, null, token);
        return result.RootElement.TryGetProperty("customer", out var customer) && customer.ValueKind == JsonValueKind.String ? customer.GetString() : null;
    }
    private async Task ValidatePriceAsync(CancellationToken token, bool requireActive = true)
    {
        if (_priceValidatedAt > DateTimeOffset.UtcNow.AddHours(-1))
        { if (requireActive && !_priceActive) throw new BillingUnavailableException(); return; }
        using var price = await SendAsync(HttpMethod.Get, "prices/" + Uri.EscapeDataString(options.StripePriceId), null, null, token);
        var value = price.RootElement;
        if (value.GetProperty("currency").GetString() != "usd"
            || value.GetProperty("unit_amount").GetInt64() != 200 || value.GetProperty("type").GetString() != "recurring"
            || value.GetProperty("recurring").GetProperty("interval").GetString() != "month"
            || value.GetProperty("recurring").GetProperty("interval_count").GetInt64() != 1)
            throw new BillingUnavailableException();
        _priceValidatedAt = DateTimeOffset.UtcNow;
        _priceActive = value.GetProperty("active").GetBoolean();
        if (requireActive && !_priceActive) throw new BillingUnavailableException();
    }
    private async Task<JsonDocument> SendAsync(HttpMethod method, string path, Dictionary<string, string>? form, string? idempotency, CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromSeconds(20));
        using var request = new HttpRequestMessage(method, new Uri("https://api.stripe.com/v1/" + path));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.StripeSecretKey);
        request.Headers.Add("Stripe-Version", "2024-06-20");
        if (idempotency is not null) request.Headers.Add("Idempotency-Key", idempotency);
        if (form is not null) request.Content = new FormUrlEncodedContent(form);
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        if (!response.IsSuccessStatusCode) throw new BillingUnavailableException();
        await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
        using var memory = new MemoryStream(); var buffer = new byte[8192];
        while (true)
        {
            var read = await stream.ReadAsync(buffer, timeout.Token); if (read == 0) break;
            if (memory.Length + read > 4 * 1024 * 1024) throw new InvalidDataException("Billing response exceeds the limit.");
            memory.Write(buffer, 0, read);
        }
        return JsonDocument.Parse(memory.ToArray());
    }
}
