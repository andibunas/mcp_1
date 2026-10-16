namespace WebMCP.OAuth;

/// <summary>
/// Step 6: guards /mcp. Unseals the access token and exposes the service account JSON
/// to tools through HttpContext.Items[OAuthSettings.CredentialItemKey].
/// </summary>
public class BearerAuthMiddleware(RequestDelegate next, TokenProtector protector)
{
    public async Task InvokeAsync(HttpContext context)
    {
        if (!context.Request.Path.StartsWithSegments(OAuthSettings.McpPath))
        {
            await next(context);
            return;
        }

        var header = context.Request.Headers.Authorization.FirstOrDefault();
        if (header is null || !header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            await Challenge(context, error: null);
            return;
        }

        if (!protector.TryUnprotect<AccessPayload>(TokenProtector.AccessPurpose, header["Bearer ".Length..].Trim(), out var access))
        {
            await Challenge(context, error: "invalid_token");
            return;
        }

        context.Items[OAuthSettings.CredentialItemKey] = access!.Credential;
        await next(context);
    }

    private static Task Challenge(HttpContext context, string? error)
    {
        var value = $"Bearer resource_metadata=\"{OAuthSettings.ProtectedResourceMetadataUrl}\"";
        if (error is not null) value += $", error=\"{error}\"";

        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        context.Response.Headers.WWWAuthenticate = value;
        return Task.CompletedTask;
    }
}
