using System.Security.Cryptography;
using System.Text;

namespace ApexPost.Api.Services;

public interface IPartnerApiKeyValidator
{
    /// <summary>True when a partner key has been supplied through configuration or user-secrets.</summary>
    bool IsConfigured { get; }

    /// <summary>Constant-time comparison of the supplied key with the configured one.</summary>
    bool IsValid(string? suppliedKey);
}

/// <summary>Validates partner keys without logging them or comparing them with ordinary string equality.</summary>
public sealed class PartnerApiKeyValidator(IConfiguration configuration) : IPartnerApiKeyValidator
{
    public const string ConfigurationKey = "Partner:ApiKey";

    public bool IsConfigured => !string.IsNullOrWhiteSpace(configuration[ConfigurationKey]);

    public bool IsValid(string? suppliedKey)
    {
        var expected = configuration[ConfigurationKey];
        if (string.IsNullOrWhiteSpace(expected) || string.IsNullOrWhiteSpace(suppliedKey))
            return false;

        // Hashing first gives equal-length inputs, so FixedTimeEquals does not leak the key length.
        var expectedHash = SHA256.HashData(Encoding.UTF8.GetBytes(expected));
        var suppliedHash = SHA256.HashData(Encoding.UTF8.GetBytes(suppliedKey));
        return CryptographicOperations.FixedTimeEquals(expectedHash, suppliedHash);
    }
}
