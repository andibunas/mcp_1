namespace WebMCP.OAuth;

public static class OAuthSettings
{
    public const string BaseUrl = "http://localhost:5157";

    public const string IssuerPath = "/authservers";
    public const string Issuer = BaseUrl + IssuerPath;

    public const string McpPath = "/mcp";
    public const string Resource = BaseUrl + McpPath;
    public const string ProtectedResourceMetadataUrl = BaseUrl + "/.well-known/oauth-protected-resource" + McpPath;

    public static readonly string[] Scopes = ["mcp:tools:read", "mcp:tools:write"];

    public static readonly TimeSpan ClientIdLifetime = TimeSpan.FromDays(365);
    public static readonly TimeSpan CodeLifetime = TimeSpan.FromSeconds(60);
    public static readonly TimeSpan AccessTokenLifetime = TimeSpan.FromHours(1);
    public static readonly TimeSpan RefreshTokenLifetime = TimeSpan.FromDays(30);

    /// <summary>HttpContext.Items key holding the service account JSON for the current /mcp request.</summary>
    public const string CredentialItemKey = "ServiceAccountJson";
}
