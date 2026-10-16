using System.Text.Json;

namespace WebMCP.OAuth;

/// <summary>
/// Step 3: Dynamic Client Registration (RFC 7591). Stateless: the client_id is the
/// registered redirect URIs sealed by the server, so /authorize can verify them later.
/// </summary>
public static class RegisterEndpoint
{
    private static readonly string[] BlockedSchemes = ["javascript", "data", "file", "vbscript"];

    public static void MapRegisterEndpoint(this WebApplication app)
    {
        app.MapPost($"{OAuthSettings.IssuerPath}/register", async (HttpContext ctx, TokenProtector protector) =>
        {
            JsonElement body;
            try { body = await ctx.Request.ReadFromJsonAsync<JsonElement>(); }
            catch (JsonException) { return Error("invalid_client_metadata", "Body must be JSON."); }

            if (body.ValueKind != JsonValueKind.Object
                || !body.TryGetProperty("redirect_uris", out var uris)
                || uris.ValueKind != JsonValueKind.Array
                || uris.GetArrayLength() is 0 or > 10)
                return Error("invalid_redirect_uri", "redirect_uris must be a non-empty array.");

            var redirectUris = new List<string>();
            foreach (var item in uris.EnumerateArray())
            {
                var value = item.ValueKind == JsonValueKind.String ? item.GetString() : null;
                if (!IsAllowedRedirectUri(value)) return Error("invalid_redirect_uri", $"Redirect URI not allowed: {value}");
                redirectUris.Add(value!);
            }

            string? name = body.TryGetProperty("client_name", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString() : null;

            var clientId = protector.Protect(TokenProtector.ClientPurpose,
                new ClientPayload([.. redirectUris], name), OAuthSettings.ClientIdLifetime);
            Console.WriteLine($"Registered client '{name}' with {redirectUris.Count} redirect URI(s)");

            return Results.Json(new
            {
                client_id = clientId,
                client_name = name,
                redirect_uris = redirectUris,
                grant_types = new[] { "authorization_code", "refresh_token" },
                response_types = new[] { "code" },
                token_endpoint_auth_method = "none"
            }, statusCode: StatusCodes.Status201Created);
        });
    }

    private static IResult Error(string error, string description) =>
        Results.Json(new { error, error_description = description }, statusCode: StatusCodes.Status400BadRequest);

    private static bool IsAllowedRedirectUri(string? value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Fragment.Length > 0) return false;
        if (BlockedSchemes.Contains(uri.Scheme)) return false;
        // Plain http is only acceptable for loopback callbacks (native / desktop clients).
        if (uri.Scheme == Uri.UriSchemeHttp) return uri.IsLoopback;
        return true;
    }
}
