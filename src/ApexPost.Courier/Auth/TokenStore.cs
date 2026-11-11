using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging;

namespace ApexPost.Courier.Auth;

/// <summary>Where the access token lives. The console equivalent of MAUI SecureStorage.</summary>
public interface ITokenStore
{
    Task<string?> GetAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(string token, CancellationToken cancellationToken = default);
    Task ClearAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Stores the token encrypted with ASP.NET Core Data Protection (keys protected with DPAPI on Windows).
/// The token is never written in plain text, never put in settings/preferences and never logged.
/// </summary>
public sealed class ProtectedTokenStore(IDataProtectionProvider provider, string filePath, ILogger<ProtectedTokenStore> logger)
    : ITokenStore, IDisposable
{
    private readonly IDataProtector _protector = provider.CreateProtector("ApexPost.Courier.AccessToken.v1");
    private readonly SemaphoreSlim _gate = new(1, 1);
    private string? _cached;
    private bool _loaded;

    public async Task<string?> GetAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_loaded)
                return _cached;

            _loaded = true;
            if (!File.Exists(filePath))
                return _cached = null;

            var protectedBytes = await File.ReadAllBytesAsync(filePath, cancellationToken);
            _cached = Encoding.UTF8.GetString(_protector.Unprotect(protectedBytes));
            return _cached;
        }
        catch (CryptographicException)
        {
            // Keys rotated or file tampered with: treat as logged out.
            logger.LogWarning("Stored token could not be decrypted and was discarded");
            File.Delete(filePath);
            return _cached = null;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveAsync(string token, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await File.WriteAllBytesAsync(filePath, _protector.Protect(Encoding.UTF8.GetBytes(token)), cancellationToken);
            _cached = token;
            _loaded = true;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (File.Exists(filePath))
                File.Delete(filePath);
            _cached = null;
            _loaded = true;
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose() => _gate.Dispose();
}
