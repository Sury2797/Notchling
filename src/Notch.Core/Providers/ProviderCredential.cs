namespace Notch.Core.Providers;

/// <summary>Validates user-supplied reporting credentials before storage or an HTTP request.</summary>
public static class ProviderCredential
{
    public static string Normalize(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        value = value.Trim();
        if (value.Length > 8192 || value.Any(char.IsControl))
            throw new ArgumentException("Use a credential up to 8,192 characters without control characters.", nameof(value));
        return value;
    }
}
