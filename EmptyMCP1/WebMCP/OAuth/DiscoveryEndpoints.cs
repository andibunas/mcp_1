namespace WebMCP.OAuth;

/// <summary>Step 1-2: the metadata documents a client reads after its first 401.</summary>
public static class DiscoveryEndpoints
{
    public static void MapDiscoveryEndpoints(this WebApplication app)
    {
        IResult ProtectedResource()
        {
            Console.WriteLine("Processing protected resource metadata");
            return Results.Json(new
            {
                resource = OAuthSettings.Resource,
                authorization_servers = new[] { OAuthSettings.Issuer },
                bearer_methods_supported = new[] { "header" },
                scopes_supported = OAuthSettings.Scopes
            });
        }

        // Path-suffixed form (RFC 9728) first, plain form as a fallback.
        app.MapGet("/.well-known/oauth-protected-resource" + OAuthSettings.McpPath, ProtectedResource);
        app.MapGet("/.well-known/oauth-protected-resource", ProtectedResource);

        app.MapGet("/.well-known/oauth-authorization-server" + OAuthSettings.IssuerPath, () =>
        {
            Console.WriteLine("Processing authorization server metadata");
            return Results.Json(new
            {
                issuer = OAuthSettings.Issuer,
                authorization_endpoint = $"{OAuthSettings.Issuer}/authorize",
                token_endpoint = $"{OAuthSettings.Issuer}/token",
                registration_endpoint = $"{OAuthSettings.Issuer}/register",
                response_types_supported = new[] { "code" },
                grant_types_supported = new[] { "authorization_code", "refresh_token" },
                code_challenge_methods_supported = new[] { "S256" },
                token_endpoint_auth_methods_supported = new[] { "none" },
                scopes_supported = OAuthSettings.Scopes
            });
        });
    }
}
