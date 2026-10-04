namespace Notch.Core.Commerce;

public sealed record LoginRequest(string Email, string DeviceId);
public sealed record VerifyLoginRequest(string Email, string DeviceId, string Code);
public sealed record LoginResponse(string SessionToken, string Entitlement, DateTimeOffset ServerTime);
public sealed record EntitlementResponse(string Entitlement, DateTimeOffset ServerTime);
public sealed record BillingLinkResponse(string Url);
public sealed record BillingError(string Error);
