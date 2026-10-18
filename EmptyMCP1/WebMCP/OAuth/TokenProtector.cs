using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;

namespace WebMCP.OAuth;

// Payloads sealed inside client ids, codes and tokens. Nothing is stored server side.
public record ClientPayload(string[] RedirectUris, string? ClientName);
public record CodePayload(string ClientId, string RedirectUri, string CodeChallenge, string Credential);
public record AccessPayload(string Credential);
public record RefreshPayload(string ClientId, string Credential);

/// <summary>
/// Seals payloads into expiring, tamper-proof strings using ASP.NET Data Protection.
/// Each purpose is isolated, so a code can't be presented as an access token.
/// </summary>
public class TokenProtector(IDataProtectionProvider provider, ILogger<TokenProtector> logger)
{
    public const string ClientPurpose = "mcp.client";
    public const string CodePurpose = "mcp.code";
    public const string AccessPurpose = "mcp.access";
    public const string RefreshPurpose = "mcp.refresh";

    // Payloads are not logged: they carry the service account private key.
    public string Protect<T>(string purpose, T payload, TimeSpan lifetime)
    {
        logger.LogInformation("[TokenProtector.Protect] Starting - purpose={Purpose}, lifetime={Lifetime}s", purpose, lifetime.TotalSeconds);
        logger.LogInformation("[TokenProtector.Protect] Payload type: {PayloadType}", typeof(T).Name);
        var protector = provider.CreateProtector(purpose).ToTimeLimitedDataProtector();
        var serialized = JsonSerializer.Serialize(payload);
        var protected_value = protector.Protect(serialized, lifetime);
        logger.LogInformation("[TokenProtector.Protect] Protected value created (length={Length})", protected_value.Length);
        return protected_value;
    }

    /// <summary>Returns false when the value is malformed, tampered with, expired or for another purpose.</summary>
    public bool TryUnprotect<T>(string purpose, string? value, out T? payload) where T : class
    {
        payload = null;
        logger.LogInformation("[TokenProtector.TryUnprotect] Starting - purpose={Purpose}, payload type={PayloadType}", purpose, typeof(T).Name);

        if (string.IsNullOrEmpty(value))
        {
            logger.LogWarning("[TokenProtector.TryUnprotect] Value is null or empty - returning false");
            return false;
        }

        logger.LogInformation("[TokenProtector.TryUnprotect] Value length: {Length}", value.Length);
        try
        {
            logger.LogInformation("[TokenProtector.TryUnprotect] Creating protector and unprotecting...");
            var protector = provider.CreateProtector(purpose).ToTimeLimitedDataProtector();
            var unprotected = protector.Unprotect(value);

            payload = JsonSerializer.Deserialize<T>(unprotected);
            return payload is not null;
        }
        catch (CryptographicException ex)
        {
            logger.LogWarning("[TokenProtector.TryUnprotect] CryptographicException: {Message}", ex.Message);
            return false;
        }
        catch (JsonException ex)
        {
            logger.LogWarning("[TokenProtector.TryUnprotect] JsonException: {Message}", ex.Message);
            return false;
        }
    }
}
