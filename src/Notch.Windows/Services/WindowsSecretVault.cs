using Notch.Core;
using Windows.Security.Credentials;

namespace Notch.Windows.Services;

/// <summary>Secrets belong to the current Windows user's encrypted Credential Locker.</summary>
public sealed class WindowsSecretVault : ISecretVault
{
    private const string Resource = "Notch.Windows";
    private PasswordVault? _vault;
    private PasswordVault Vault => _vault ??= new();
    private readonly object _gate = new();

    public void Save(string name, string value)
    {
        ValidateName(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        lock (_gate)
        {
            // Preserve the existing credential if Windows rejects the replacement.
            var previous = Find(name);
            try
            {
                if (previous is not null) Vault.Remove(previous);
                Vault.Add(new PasswordCredential(Resource, name, value));
            }
            catch
            {
                if (previous is not null)
                {
                    try { Vault.Add(previous); } catch { /* Original failure is the useful error. */ }
                }
                throw;
            }
        }
    }

    public string? Read(string name)
    {
        ValidateName(name);
        lock (_gate)
        {
            var credential = Find(name);
            if (credential is null) return null;
            credential.RetrievePassword();
            return credential.Password;
        }
    }

    public void Delete(string name)
    {
        ValidateName(name);
        lock (_gate)
        {
            var credential = Find(name);
            if (credential is not null) Vault.Remove(credential);
        }
    }

    private PasswordCredential? Find(string name)
    {
        try
        {
            var credential = Vault.Retrieve(Resource, name);
            // Retrieve before removal so rollback still has the original encrypted value.
            credential.RetrievePassword();
            return credential;
        }
        catch (Exception error) when (error.HResult == unchecked((int)0x80070490))
        {
            return null; // ERROR_NOT_FOUND only; access/security failures remain visible.
        }
    }

    private static void ValidateName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (name.Length > 128) throw new ArgumentOutOfRangeException(nameof(name));
    }
}
