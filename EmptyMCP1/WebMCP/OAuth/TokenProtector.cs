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
public class TokenProtector(IDataProtectionProvider provider)
{
    public const string ClientPurpose = "mcp.client";
    public const string CodePurpose = "mcp.code";
    public const string AccessPurpose = "mcp.access";
    public const string RefreshPurpose = "mcp.refresh";

    public string Protect<T>(string purpose, T payload, TimeSpan lifetime)
    {
        var protector = provider.CreateProtector(purpose).ToTimeLimitedDataProtector();
        return protector.Protect(JsonSerializer.Serialize(payload), lifetime);
    }

    /// <summary>Returns false when the value is malformed, tampered with, expired or for another purpose.</summary>
    public bool TryUnprotect<T>(string purpose, string? value, out T? payload) where T : class
    {
        payload = null;
        if (string.IsNullOrEmpty(value)) return false;
        try
        {
            var protector = provider.CreateProtector(purpose).ToTimeLimitedDataProtector();
            payload = JsonSerializer.Deserialize<T>(protector.Unprotect(value));
            return payload is not null;
        }
        catch (CryptographicException) { return false; }
        catch (JsonException) { return false; }
    }
}
