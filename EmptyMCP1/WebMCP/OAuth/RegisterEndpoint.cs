using System.Text.Json;
using log4net;

namespace WebMCP.OAuth;

/// <summary>
/// Step 3: Dynamic Client Registration (RFC 7591). Stateless: the client_id is the
/// registered redirect URIs sealed by the server, so /authorize can verify them later.
/// </summary>
public static class RegisterEndpoint
{
    private static readonly ILog Log = LogManager.GetLogger(typeof(RegisterEndpoint));

    private static readonly string[] BlockedSchemes = ["javascript", "data", "file", "vbscript"];

    public static void MapRegisterEndpoint(this WebApplication app)
    {
        app.MapPost($"{OAuthSettings.IssuerPath}/register", async (HttpContext ctx, TokenProtector protector) =>
        {
            Log.Info("/register endpoint hit");
            JsonElement body;
            try
            {
                body = await ctx.Request.ReadFromJsonAsync<JsonElement>();
                Log.Info("JSON body parsed successfully");
            }
            catch (JsonException)
            {
                Log.Warn("Failed to parse JSON body");
                return Error("invalid_client_metadata", "Body must be JSON.");
            }

            Log.Info($"Body kind: {body.ValueKind}");
            if (body.ValueKind != JsonValueKind.Object
                || !body.TryGetProperty("redirect_uris", out var uris)
                || uris.ValueKind != JsonValueKind.Array)
            {
                Log.Warn("Invalid body structure - must be object with redirect_uris array");
                return Error("invalid_redirect_uri", "redirect_uris must be a non-empty array.");
            }

            var uriCount = uris.GetArrayLength();
            Log.Info($"redirect_uris array length: {uriCount}");
            if (uriCount is 0 or > 10)
            {
                Log.Warn($"Invalid redirect_uris count - must be 1-10, got {uriCount}");
                return Error("invalid_redirect_uri", "redirect_uris must be a non-empty array.");
            }

            var redirectUris = new List<string>();
            foreach (var item in uris.EnumerateArray())
            {
                var value = item.ValueKind == JsonValueKind.String ? item.GetString() : null;
                Log.Info($"Validating redirect_uri: {value}");
                if (!IsAllowedRedirectUri(value))
                {
                    Log.Warn($"Redirect URI not allowed: {value}");
                    return Error("invalid_redirect_uri", $"Redirect URI not allowed: {value}");
                }
                redirectUris.Add(value!);
            }

            string? name = body.TryGetProperty("client_name", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString() : null;
            Log.Info($"Client name: {name ?? "(none)"}");

            var clientId = protector.Protect(TokenProtector.ClientPurpose,
                new ClientPayload([.. redirectUris], name), OAuthSettings.ClientIdLifetime);
            Log.Info($"Registered client '{name}' with {redirectUris.Count} redirect URI(s)");

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

    private static IResult Error(string error, string description)
    {
        Log.Warn($"{error}: {description}");
        return Results.Json(new { error, error_description = description }, statusCode: StatusCodes.Status400BadRequest);
    }

    private static bool IsAllowedRedirectUri(string? value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
        {
            Log.Warn($"Not a valid absolute URI: {value}");
            return false;
        }
        if (uri.Fragment.Length > 0)
        {
            Log.Warn("URI has fragment, not allowed");
            return false;
        }
        if (BlockedSchemes.Contains(uri.Scheme))
        {
            Log.Warn($"Scheme '{uri.Scheme}' is blocked");
            return false;
        }
        if (uri.Scheme == Uri.UriSchemeHttp)
        {
            var allowed = uri.IsLoopback;
            Log.Info($"HTTP scheme - loopback={uri.IsLoopback}, allowed={allowed}");
            return allowed;
        }
        Log.Info($"Scheme '{uri.Scheme}' is allowed");
        return true;
    }
}
