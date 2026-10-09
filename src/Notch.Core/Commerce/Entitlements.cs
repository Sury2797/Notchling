using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Notch.Core.Commerce;

public enum PlanTier { Free, Premium }
public sealed record EntitlementClaims(string Issuer, string Audience, string AccountId, string DeviceId,
    PlanTier Plan, DateTimeOffset IssuedAt, DateTimeOffset RefreshAfter, DateTimeOffset ExpiresAt,
    DateTimeOffset? PaidThrough, int Version = 1);
public sealed record EntitlementDecision(PlanTier Plan, bool RequiresRefresh, string Message, DateTimeOffset? ValidUntil = null)
{
    public bool IsPremium => Plan == PlanTier.Premium;
    public static EntitlementDecision Free(string reason = "Free plan") => new(PlanTier.Free, true, reason);
}
public sealed class VerifiedEntitlement
{
    private readonly EntitlementClaims _claims;
    internal VerifiedEntitlement(EntitlementClaims claims) => _claims = claims;
    public EntitlementDecision Evaluate(string deviceId, DateTimeOffset now, DateTimeOffset? lastVerifiedServerTime = null)
        => EntitlementTokens.Evaluate(_claims, deviceId, now, lastVerifiedServerTime);
}

/// <summary>Signed proofs are short-lived and contain no payment or login secrets.</summary>
public static class EntitlementTokens
{
    public const string Issuer = "notch-billing";
    public const string Audience = "notch-desktop";
    public static readonly TimeSpan RefreshInterval = TimeSpan.FromHours(6);
    public static readonly TimeSpan OfflineGrace = TimeSpan.FromHours(24);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static string Sign(EntitlementClaims claims, RSA privateKey)
    {
        var payload = Encode(JsonSerializer.SerializeToUtf8Bytes(claims, Json));
        var signature = privateKey.SignData(Encoding.ASCII.GetBytes(payload), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return payload + "." + Encode(signature);
    }

    public static EntitlementDecision Validate(string? token, string publicKeyPem, string deviceId,
        DateTimeOffset now, DateTimeOffset? lastVerifiedServerTime = null)
    {
        if (string.IsNullOrWhiteSpace(token)) return EntitlementDecision.Free();
        return ReadVerified(token, publicKeyPem)?.Evaluate(deviceId, now, lastVerifiedServerTime)
            ?? EntitlementDecision.Free("Subscription signature could not be verified. Connect to restore.");
    }

    public static VerifiedEntitlement? ReadVerified(string? token, string publicKeyPem)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(token) || token.Length > 16384) return null;
            var parts = token.Split('.');
            if (parts.Length != 2) return null;
            using var key = RSA.Create();
            key.ImportFromPem(publicKeyPem);
            if (key.KeySize < 2048 || !key.VerifyData(Encoding.ASCII.GetBytes(parts[0]), Decode(parts[1]),
                    HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1))
                return null;
            var claims = JsonSerializer.Deserialize<EntitlementClaims>(Decode(parts[0]), Json);
            return claims is null ? null : new VerifiedEntitlement(claims);
        }
        catch (Exception error) when (error is CryptographicException or JsonException or FormatException or ArgumentException)
        { return null; }
    }

    internal static EntitlementDecision Evaluate(EntitlementClaims claims, string deviceId, DateTimeOffset now, DateTimeOffset? lastVerifiedServerTime)
    {
        try
        {
            if (claims is null || claims.Version != 1 || claims.Issuer != Issuer || claims.Audience != Audience
                || claims.DeviceId != deviceId || string.IsNullOrWhiteSpace(claims.AccountId)
                || !Enum.IsDefined(claims.Plan) || claims.IssuedAt > now.AddMinutes(5)
                || claims.RefreshAfter < claims.IssuedAt || claims.RefreshAfter > claims.IssuedAt + RefreshInterval
                || claims.ExpiresAt <= claims.IssuedAt || claims.ExpiresAt > claims.RefreshAfter + OfflineGrace
                || (lastVerifiedServerTime is { } seen && now < seen.AddMinutes(-5)))
                return EntitlementDecision.Free("Subscription proof is invalid or the system clock changed. Connect to restore.");
            if (now >= claims.ExpiresAt || (claims.Plan == PlanTier.Premium &&
                (claims.PaidThrough is null || now >= claims.PaidThrough || claims.ExpiresAt > claims.PaidThrough)))
                return EntitlementDecision.Free("Offline subscription access expired. Connect to restore.");
            return new(claims.Plan, now >= claims.RefreshAfter,
                claims.Plan == PlanTier.Premium ? (now >= claims.RefreshAfter ? "Premium · offline grace" : "Premium verified") : "Free plan",
                claims.ExpiresAt);
        }
        catch (ArgumentException)
        { return EntitlementDecision.Free("Subscription proof could not be read. Connect to restore."); }
    }

    public static string Encode(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    public static byte[] Decode(string text) => Convert.FromBase64String(text.Replace('-', '+').Replace('_', '/') + new string('=', (4 - text.Length % 4) % 4));
}

public static class FeaturePolicy
{
    public static bool RequiresPremium(ModuleId module) => module is not (ModuleId.Home or ModuleId.Media or ModuleId.Focus
        or ModuleId.Scratchpad or ModuleId.Settings or ModuleId.Tools);
    public static bool CanUse(ModuleId module, EntitlementDecision entitlement) => !RequiresPremium(module) || entitlement.IsPremium;
    // Existing records remain readable/exportable after expiry; editing Premium tools is gated separately.
    public static bool CanReadStoredData(ModuleId module) => module is ModuleId.Notes or ModuleId.Links or ModuleId.Shelf or ModuleId.Calendar;
}

public enum ProductAccessPhase { PublicTesting, Freemium }

/// <summary>
/// Desktop tool availability is separate from a verified purchase and from access
/// to a provider's authenticated service. Change this single release policy when
/// the public testing phase ends; do not create or modify subscription proofs.
/// </summary>
public static class ProductAccessPolicy
{
    public const ProductAccessPhase CurrentPhase = ProductAccessPhase.PublicTesting;

    public static bool CanUseExtendedTools(bool verifiedPremium, bool developmentBuild = false,
        ProductAccessPhase phase = CurrentPhase)
        => phase == ProductAccessPhase.PublicTesting || developmentBuild || verifiedPremium;

    public static bool CanUse(ModuleId module, bool verifiedPremium, bool developmentBuild = false,
        ProductAccessPhase phase = CurrentPhase)
        => !FeaturePolicy.RequiresPremium(module) || CanUseExtendedTools(verifiedPremium, developmentBuild, phase);
}
