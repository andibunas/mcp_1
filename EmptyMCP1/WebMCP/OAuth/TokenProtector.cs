using System.Security.Cryptography;
using System.Text.Json;
using log4net;
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
public class TokenProtector(IDataProtectionProvider provider)
{
    private static readonly ILog Log = LogManager.GetLogger(typeof(TokenProtector));

    public const string ClientPurpose = "mcp.client";
    public const string CodePurpose = "mcp.code";
    public const string AccessPurpose = "mcp.access";
    public const string RefreshPurpose = "mcp.refresh";

    // Payloads are not logged: they carry the service account private key.
    public string Protect<T>(string purpose, T payload, TimeSpan lifetime)
    {
        Log.Info($"[TokenProtector.Protect] Starting - purpose={purpose}, lifetime={lifetime.TotalSeconds}s");
        Log.Info($"[TokenProtector.Protect] Payload type: {typeof(T).Name}");
        var protector = provider.CreateProtector(purpose).ToTimeLimitedDataProtector();
        var serialized = JsonSerializer.Serialize(payload);
        var protected_value = protector.Protect(serialized, lifetime);
        Log.Info($"[TokenProtector.Protect] Protected value created (length={protected_value.Length})");
        return protected_value;
    }

    /// <summary>Returns false when the value is malformed, tampered with, expired or for another purpose.</summary>
    public bool TryUnprotect<T>(string purpose, string? value, out T? payload) where T : class
    {
        payload = null;
        Log.Info($"[TokenProtector.TryUnprotect] Starting - purpose={purpose}, payload type={typeof(T).Name}");

        if (string.IsNullOrEmpty(value))
        {
            Log.Warn("[TokenProtector.TryUnprotect] Value is null or empty - returning false");
            return false;
        }

        Log.Info($"[TokenProtector.TryUnprotect] Value length: {value.Length}");
        try
        {
            Log.Info("[TokenProtector.TryUnprotect] Creating protector and unprotecting...");
            var protector = provider.CreateProtector(purpose).ToTimeLimitedDataProtector();
            var unprotected = protector.Unprotect(value);

            payload = JsonSerializer.Deserialize<T>(unprotected);
            return payload is not null;
        }
        catch (CryptographicException ex)
        {
            Log.Warn($"[TokenProtector.TryUnprotect] CryptographicException: {ex.Message}");
            return false;
        }
        catch (JsonException ex)
        {
            Log.Warn($"[TokenProtector.TryUnprotect] JsonException: {ex.Message}");
            return false;
        }
    }
}
