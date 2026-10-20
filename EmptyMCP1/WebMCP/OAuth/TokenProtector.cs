using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using log4net;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace WebMCP.OAuth;

// Payloads sealed inside client ids, codes and tokens. Nothing is stored server side.
public record ClientPayload(string[] RedirectUris, string? ClientName);
public record CodePayload(string ClientId, string RedirectUri, string CodeChallenge, string Credential);
public record AccessPayload(string Credential);
public record RefreshPayload(string ClientId, string Credential);

/// <summary>
/// Seals payloads into expiring, tamper-proof, encrypted JSON Web Tokens (signed then encrypted).
/// Each purpose is a claim that is checked on read, so a code can't be presented as an access token.
/// </summary>
public class TokenProtector
{
    private static readonly ILog Log = LogManager.GetLogger(typeof(TokenProtector));

    // TEMPORARY dummy key for local use. Replace with a server-side secret read from the environment.
    private const string KeyMaterial = "dummy-local-key-replace-before-production";
    private static readonly byte[] KeyBytes = SHA256.HashData(Encoding.UTF8.GetBytes(KeyMaterial));
    private static readonly SymmetricSecurityKey SigningKey = new(KeyBytes);
    private static readonly SymmetricSecurityKey EncryptionKey = new(KeyBytes);
    private static readonly JsonWebTokenHandler Handler = new();

    public const string ClientPurpose = "mcp.client";
    public const string CodePurpose = "mcp.code";
    public const string AccessPurpose = "mcp.access";
    public const string RefreshPurpose = "mcp.refresh";

    // Payloads are not logged: they carry the service account private key.
    public string Protect<T>(string purpose, T payload, TimeSpan lifetime)
    {
        Log.Info($"Starting - purpose={purpose}, lifetime={lifetime.TotalSeconds}s");
        Log.Info($"Payload type: {typeof(T).Name}");
        var descriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(
            [
                new Claim("purpose", purpose),
                new Claim("data", JsonSerializer.Serialize(payload)),
            ]),
            Expires = DateTime.UtcNow.Add(lifetime),
            SigningCredentials = new SigningCredentials(SigningKey, SecurityAlgorithms.HmacSha256),
            EncryptingCredentials = new EncryptingCredentials(EncryptionKey, SecurityAlgorithms.Aes256KeyWrap, SecurityAlgorithms.Aes256CbcHmacSha512),
        };
        var protected_value = Handler.CreateToken(descriptor);
        Log.Info($"Protected value created (length={protected_value.Length}) {protected_value}");
        return protected_value;
    }

    /// <summary>Returns false when the value is malformed, tampered with, expired or for another purpose.</summary>
    public bool TryUnprotect<T>(string purpose, string? value, out T? payload) where T : class
    {
        payload = null;
        Log.Info($"Starting - purpose={purpose}, payload type={typeof(T).Name}");

        if (string.IsNullOrEmpty(value))
        {
            Log.Warn("Value is null or empty - returning false");
            return false;
        }

        Log.Info($"Value length: {value.Length}");
        var parameters = new TokenValidationParameters
        {
            IssuerSigningKey = SigningKey,
            TokenDecryptionKey = EncryptionKey,
            RequireSignedTokens = true,
            RequireExpirationTime = true,
            ValidateIssuer = false,
            ValidateAudience = false,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero,
        };

        var result = Handler.ValidateTokenAsync(value, parameters).GetAwaiter().GetResult();
        if (!result.IsValid)
        {
            Log.Warn($"Token rejected: {result.Exception?.GetType().Name}: {result.Exception?.Message}");
            return false;
        }

        var claims = result.ClaimsIdentity;
        if (claims.FindFirst("purpose")?.Value != purpose)
        {
            Log.Warn("Token is for another purpose - returning false");
            return false;
        }

        try
        {
            payload = JsonSerializer.Deserialize<T>(claims.FindFirst("data")?.Value ?? "");
            return payload is not null;
        }
        catch (JsonException ex)
        {
            Log.Warn($"JsonException: {ex.Message}");
            return false;
        }
    }
}
