namespace WebMCP.OAuth;

public static class OAuthSettings
{
    public const string IssuerPath = "";
    public const string McpPath = "/";

    /// <summary>
    /// The public base URL the caller used to reach this server (e.g. http://localhost:5157 locally,
    /// https://mcp1.bunasan.com when hosted). Forwarded headers are applied by UseForwardedHeaders in Program.cs.
    /// </summary>
    public static string GetBaseUrl(HttpContext ctx) =>
        $"{ctx.Request.Scheme}://{ctx.Request.Host}{ctx.Request.PathBase}";

    public static string GetIssuer(HttpContext ctx) => GetBaseUrl(ctx) + IssuerPath;

    public static string GetResource(HttpContext ctx) => GetBaseUrl(ctx);

    public static string GetProtectedResourceMetadataUrl(HttpContext ctx) =>
        GetBaseUrl(ctx) + "/.well-known/oauth-protected-resource";

    public static readonly string[] Scopes = ["mcp:tools:read", "mcp:tools:write"];

    public static readonly TimeSpan ClientIdLifetime = TimeSpan.FromDays(365);
    public static readonly TimeSpan CodeLifetime = TimeSpan.FromSeconds(60);
    public static readonly TimeSpan AccessTokenLifetime = TimeSpan.FromHours(1);
    public static readonly TimeSpan RefreshTokenLifetime = TimeSpan.FromDays(30);

    /// <summary>HttpContext.Items key holding the service account JSON for the current MCP request.</summary>
    public const string CredentialItemKey = "ServiceAccountJson";
}
