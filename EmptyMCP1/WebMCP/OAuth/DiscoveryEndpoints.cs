namespace WebMCP.OAuth;

/// <summary>Step 1-2: the metadata documents a client reads after its first 401.</summary>
public static class DiscoveryEndpoints
{
    public static void MapDiscoveryEndpoints(this WebApplication app)
    {
        var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("WebMCP.OAuth.DiscoveryEndpoints");

        IResult ProtectedResource(HttpContext ctx)
        {
            logger.LogInformation("Processing protected resource metadata");
            return Results.Json(new
            {
                resource = OAuthSettings.GetResource(ctx),
                authorization_servers = new[] { OAuthSettings.GetIssuer(ctx) },
                bearer_methods_supported = new[] { "header" },
                scopes_supported = OAuthSettings.Scopes
            });
        }

        app.MapGet("/.well-known/oauth-protected-resource", ProtectedResource);

        app.MapGet("/.well-known/oauth-authorization-server" + OAuthSettings.IssuerPath, (HttpContext ctx) =>
        {
            logger.LogInformation("Processing authorization server metadata");
            var issuer = OAuthSettings.GetIssuer(ctx);
            return Results.Json(new
            {
                issuer,
                authorization_endpoint = $"{issuer}/authorize",
                token_endpoint = $"{issuer}/token",
                registration_endpoint = $"{issuer}/register",
                response_types_supported = new[] { "code" },
                grant_types_supported = new[] { "authorization_code", "refresh_token" },
                code_challenge_methods_supported = new[] { "S256" },
                token_endpoint_auth_methods_supported = new[] { "none" },
                scopes_supported = OAuthSettings.Scopes
            });
        });
    }
}
