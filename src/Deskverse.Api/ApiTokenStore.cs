namespace Deskverse.Api;

using System.Security.Cryptography;
using System.Runtime.Versioning;
using Deskverse.Core.Abstractions;
using Microsoft.Extensions.Logging;

/// <summary>
/// The local API token. Generated once and persisted as a DPAPI-encrypted blob
/// under the user's data root, so the secret is never stored in plaintext and
/// survives restarts. Only processes running as the same user can decrypt it.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class ApiTokenStore : IDisposable
{
    private const int TokenByteLength = 32;

    private readonly IAppEnvironment _environment;
    private readonly ILogger<ApiTokenStore> _logger;

    private byte[] _token;
    private byte[] _tokenHash;

    public ApiTokenStore(IAppEnvironment environment, ILogger<ApiTokenStore> logger)
    {
        _environment = environment;
        _logger = logger;
        _token = LoadOrCreateToken();
        _tokenHash = SHA256.HashData(_token);
    }

    private string TokenFilePath => Path.Combine(_environment.DataRoot, "api-token.bin");

    /// <summary>The hex-encoded token. Handed only to authorized local clients.</summary>
    public string TokenHex => Convert.ToHexString(_token);

    /// <summary>Constant-time validation of a hex-encoded token presented by a client.</summary>
    public bool IsValid(string presented)
    {
        if (string.IsNullOrWhiteSpace(presented))
        {
            return false;
        }

        byte[] candidate;
        try
        {
            candidate = Convert.FromHexString(presented.Trim());
        }
        catch (FormatException)
        {
            return false;
        }

        if (candidate.Length != _tokenHash.Length)
        {
            return false;
        }

        var candidateHash = SHA256.HashData(candidate);
        return CryptographicOperations.FixedTimeEquals(candidateHash, _tokenHash);
    }

    /// <summary>Regenerates the token and re-persists it encrypted.</summary>
    public void Rotate()
    {
        // The token and its hash must swap together. If only the token changed,
        // IsValid would keep accepting the old secret and reject the new one.
        var next = RandomNumberGenerator.GetBytes(TokenByteLength);
        var previous = _token;
        _token = next;
        _tokenHash = SHA256.HashData(next);
        PersistToken(next);
        CryptographicOperations.ZeroMemory(previous);
        _logger.LogInformation("Local API token rotated.");
    }

    private byte[] LoadOrCreateToken()
    {
        try
        {
            if (File.Exists(TokenFilePath))
            {
                var encrypted = File.ReadAllBytes(TokenFilePath);
                var decrypted = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
                if (decrypted.Length == TokenByteLength)
                {
                    return decrypted;
                }

                _logger.LogWarning("Stored API token had an unexpected length; regenerating.");
            }
        }
        catch (Exception ex) when (ex is CryptographicException or IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "The stored API token could not be read; regenerating it.");
        }

        var token = RandomNumberGenerator.GetBytes(TokenByteLength);
        PersistToken(token);
        return token;
    }

    private void PersistToken(byte[] token)
    {
        try
        {
            Directory.CreateDirectory(_environment.DataRoot);
            var encrypted = ProtectedData.Protect(token, null, DataProtectionScope.CurrentUser);
            File.WriteAllBytes(TokenFilePath, encrypted);
        }
        catch (Exception ex) when (ex is CryptographicException or IOException or UnauthorizedAccessException)
        {
            // The API still works this session with an ephemeral token.
            _logger.LogWarning(ex, "The API token could not be persisted; it will change on restart.");
        }
    }

    public void Dispose()
    {
        CryptographicOperations.ZeroMemory(_token);
    }
}
