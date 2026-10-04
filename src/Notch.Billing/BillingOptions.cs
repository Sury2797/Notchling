using System.Security.Cryptography;

namespace Notch.Billing;

public sealed class BillingOptions
{
    public string StripeSecretKey { get; set; } = "";
    public string StripeWebhookSecret { get; set; } = "";
    public string StripePriceId { get; set; } = "";
    public string SigningPrivateKeyPem { get; set; } = "";
    public string PublicOrigin { get; set; } = "";
    public string DataDirectory { get; set; } = "";
    public string SmtpHost { get; set; } = "";
    public int SmtpPort { get; set; } = 587;
    public string SmtpUsername { get; set; } = "";
    public string SmtpPassword { get; set; } = "";
    public string MailFrom { get; set; } = "";
    public bool CommercialReleaseApproved { get; set; }
    public int MaximumDevices { get; set; } = 3;
    public bool Ready
    {
        get
        {
            if ((StripeSecretKey.StartsWith("sk_live_", StringComparison.Ordinal) || StripeSecretKey.StartsWith("rk_live_", StringComparison.Ordinal)) && !CommercialReleaseApproved) return false;
            if (!(StripeSecretKey.StartsWith("sk_test_", StringComparison.Ordinal) || StripeSecretKey.StartsWith("sk_live_", StringComparison.Ordinal)
                    || StripeSecretKey.StartsWith("rk_test_", StringComparison.Ordinal) || StripeSecretKey.StartsWith("rk_live_", StringComparison.Ordinal))
                || !StripeWebhookSecret.StartsWith("whsec_", StringComparison.Ordinal)
                || !StripePriceId.StartsWith("price_", StringComparison.Ordinal) || string.IsNullOrWhiteSpace(DataDirectory)
                || string.IsNullOrWhiteSpace(SmtpHost) || string.IsNullOrWhiteSpace(SmtpUsername)
                || string.IsNullOrWhiteSpace(SmtpPassword) || !MailFrom.Contains('@')
                || !Uri.TryCreate(PublicOrigin, UriKind.Absolute, out var origin) || origin.Scheme != "https"
                || origin.UserInfo.Length != 0 || origin.Query.Length != 0 || origin.Fragment.Length != 0
                || origin.AbsolutePath != "/" || SmtpPort is < 1 or > 65535 || MaximumDevices is < 1 or > 10) return false;
            try { using var rsa = RSA.Create(); rsa.ImportFromPem(SigningPrivateKeyPem); return rsa.KeySize >= 2048 && rsa.ExportParameters(true).D is not null; }
            catch (Exception error) when (error is CryptographicException or ArgumentException) { return false; }
        }
    }
}

public sealed class BillingUnavailableException : Exception;
public sealed class BillingAuthenticationException : Exception;
public sealed class BillingValidationException(string message) : Exception(message);
