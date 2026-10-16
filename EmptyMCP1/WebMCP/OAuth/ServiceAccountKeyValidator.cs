using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace WebMCP.OAuth;

/// <summary>
/// Offline sanity check of a pasted Google service account key (the JSON key file).
/// Confirms shape only. Whether Google accepts the key is checked separately.
/// </summary>
public static partial class ServiceAccountKeyValidator
{
    private const int MaxLength = 10_000;
    private static readonly string[] KeptFields =
        ["type", "project_id", "private_key_id", "private_key", "client_email", "client_id", "token_uri"];

    [GeneratedRegex(@"^[a-z0-9-]+@[a-z0-9.-]+\.iam\.gserviceaccount\.com$")]
    private static partial Regex EmailPattern();

    [GeneratedRegex(@"^[0-9a-f]{32,64}$")]
    private static partial Regex KeyIdPattern();

    /// <summary>On success, <paramref name="normalized"/> is compact JSON with only the fields needed to authenticate.</summary>
    public static bool TryValidate(string? json, out string normalized, out string? error)
    {
        normalized = "";
        error = null;

        if (string.IsNullOrWhiteSpace(json)) { error = "Paste the service account JSON key."; return false; }
        if (json.Length > MaxLength) { error = "That is too large to be a service account key."; return false; }

        Dictionary<string, string> fields = [];
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) { error = "Expected a JSON object."; return false; }

            foreach (var name in KeptFields)
            {
                if (doc.RootElement.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String)
                    fields[name] = value.GetString()!;
            }
        }
        catch (JsonException) { error = "That is not valid JSON."; return false; }

        foreach (var required in new[] { "type", "project_id", "private_key_id", "private_key", "client_email" })
        {
            if (!fields.ContainsKey(required)) { error = $"Missing field \"{required}\"."; return false; }
        }

        if (fields["type"] != "service_account") { error = "\"type\" must be \"service_account\"."; return false; }
        if (!EmailPattern().IsMatch(fields["client_email"])) { error = "\"client_email\" is not a service account address (…@….iam.gserviceaccount.com)."; return false; }
        if (!KeyIdPattern().IsMatch(fields["private_key_id"])) { error = "\"private_key_id\" does not look like a Google key id."; return false; }
        if (fields.TryGetValue("token_uri", out var tokenUri) && tokenUri != "https://oauth2.googleapis.com/token")
        { error = "\"token_uri\" must be https://oauth2.googleapis.com/token."; return false; }

        try
        {
            using var rsa = RSA.Create();
            rsa.ImportFromPem(fields["private_key"]);
            if (rsa.KeySize < 2048) { error = "The private key is too small."; return false; }
        }
        catch (Exception ex) when (ex is ArgumentException or CryptographicException)
        { error = "\"private_key\" is not a valid PEM private key."; return false; }

        // TODO(later): verify the key against Google Drive (e.g. a files.list call) before accepting it.

        normalized = JsonSerializer.Serialize(fields);
        return true;
    }
}
