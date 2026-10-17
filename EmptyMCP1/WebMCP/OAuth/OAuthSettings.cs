namespace WebMCP.OAuth;

public static class OAuthSettings
{
    public const string BaseUrl = "http://localhost:5157";

    public const string IssuerPath = "";
    public const string Issuer = BaseUrl + IssuerPath;

    public const string McpPath = "/";
    public const string Resource = BaseUrl;
    public const string ProtectedResourceMetadataUrl = BaseUrl + "/.well-known/oauth-protected-resource";

    public static readonly string[] Scopes = ["mcp:tools:read", "mcp:tools:write"];

    public static readonly TimeSpan ClientIdLifetime = TimeSpan.FromDays(365);
    public static readonly TimeSpan CodeLifetime = TimeSpan.FromSeconds(60);
    public static readonly TimeSpan AccessTokenLifetime = TimeSpan.FromHours(1);
    public static readonly TimeSpan RefreshTokenLifetime = TimeSpan.FromDays(30);

    /// <summary>HttpContext.Items key holding the service account JSON for the current MCP request.</summary>
    public const string CredentialItemKey = "ServiceAccountJson";
}
