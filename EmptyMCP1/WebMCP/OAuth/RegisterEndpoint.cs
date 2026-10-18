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
        var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("WebMCP.OAuth.RegisterEndpoint");

        app.MapPost($"{OAuthSettings.IssuerPath}/register", async (HttpContext ctx, TokenProtector protector) =>
        {
            logger.LogInformation("[RegisterEndpoint] /register endpoint hit");
            JsonElement body;
            try
            {
                body = await ctx.Request.ReadFromJsonAsync<JsonElement>();
                logger.LogInformation("[RegisterEndpoint] JSON body parsed successfully");
            }
            catch (JsonException)
            {
                logger.LogWarning("[RegisterEndpoint] Failed to parse JSON body");
                return Error(logger, "invalid_client_metadata", "Body must be JSON.");
            }

            logger.LogInformation("[RegisterEndpoint] Body kind: {Kind}", body.ValueKind);
            if (body.ValueKind != JsonValueKind.Object
                || !body.TryGetProperty("redirect_uris", out var uris)
                || uris.ValueKind != JsonValueKind.Array)
            {
                logger.LogWarning("[RegisterEndpoint] Invalid body structure - must be object with redirect_uris array");
                return Error(logger, "invalid_redirect_uri", "redirect_uris must be a non-empty array.");
            }

            var uriCount = uris.GetArrayLength();
            logger.LogInformation("[RegisterEndpoint] redirect_uris array length: {Count}", uriCount);
            if (uriCount is 0 or > 10)
            {
                logger.LogWarning("[RegisterEndpoint] Invalid redirect_uris count - must be 1-10, got {Count}", uriCount);
                return Error(logger, "invalid_redirect_uri", "redirect_uris must be a non-empty array.");
            }

            var redirectUris = new List<string>();
            foreach (var item in uris.EnumerateArray())
            {
                var value = item.ValueKind == JsonValueKind.String ? item.GetString() : null;
                logger.LogInformation("[RegisterEndpoint] Validating redirect_uri: {RedirectUri}", value);
                if (!IsAllowedRedirectUri(logger, value))
                {
                    logger.LogWarning("[RegisterEndpoint] Redirect URI not allowed: {RedirectUri}", value);
                    return Error(logger, "invalid_redirect_uri", $"Redirect URI not allowed: {value}");
                }
                redirectUris.Add(value!);
            }

            string? name = body.TryGetProperty("client_name", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString() : null;
            logger.LogInformation("[RegisterEndpoint] Client name: {ClientName}", name ?? "(none)");

            var clientId = protector.Protect(TokenProtector.ClientPurpose,
                new ClientPayload([.. redirectUris], name), OAuthSettings.ClientIdLifetime);
            logger.LogInformation("[RegisterEndpoint] Registered client '{ClientName}' with {Count} redirect URI(s)", name, redirectUris.Count);

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

    private static IResult Error(ILogger logger, string error, string description)
    {
        logger.LogWarning("[RegisterEndpoint.Error] {Error}: {Description}", error, description);
        return Results.Json(new { error, error_description = description }, statusCode: StatusCodes.Status400BadRequest);
    }

    private static bool IsAllowedRedirectUri(ILogger logger, string? value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
        {
            logger.LogWarning("[RegisterEndpoint.IsAllowedRedirectUri] Not a valid absolute URI: {Value}", value);
            return false;
        }
        if (uri.Fragment.Length > 0)
        {
            logger.LogWarning("[RegisterEndpoint.IsAllowedRedirectUri] URI has fragment, not allowed");
            return false;
        }
        if (BlockedSchemes.Contains(uri.Scheme))
        {
            logger.LogWarning("[RegisterEndpoint.IsAllowedRedirectUri] Scheme '{Scheme}' is blocked", uri.Scheme);
            return false;
        }
        if (uri.Scheme == Uri.UriSchemeHttp)
        {
            var allowed = uri.IsLoopback;
            logger.LogInformation("[RegisterEndpoint.IsAllowedRedirectUri] HTTP scheme - loopback={Loopback}, allowed={Allowed}", uri.IsLoopback, allowed);
            return allowed;
        }
        logger.LogInformation("[RegisterEndpoint.IsAllowedRedirectUri] Scheme '{Scheme}' is allowed", uri.Scheme);
        return true;
    }
}
