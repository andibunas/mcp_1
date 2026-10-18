namespace WebMCP.OAuth;

/// <summary>
/// Step 6: guards the MCP endpoint (root). Unseals the access token and exposes the service account JSON
/// to tools through HttpContext.Items[OAuthSettings.CredentialItemKey].
/// </summary>
public class BearerAuthMiddleware(RequestDelegate next, TokenProtector protector, ILogger<BearerAuthMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        // Exact match: the MCP endpoint is the root, so StartsWithSegments("/") would match every path.
        if (context.Request.Path.Value != OAuthSettings.McpPath)
        {
            logger.LogInformation("[BearerAuthMiddleware] Path is not the MCP endpoint, skipping auth");
            await next(context);
            return;
        }

        var header = context.Request.Headers.Authorization.FirstOrDefault();

        if (header is null || !header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            logger.LogWarning("[BearerAuthMiddleware] Missing or invalid Bearer token, sending challenge");
            await Challenge(context, logger, error: null);
            return;
        }

        var token = header["Bearer ".Length..].Trim();
        logger.LogInformation("[BearerAuthMiddleware] Attempting to unprotect access token");

        if (!protector.TryUnprotect<AccessPayload>(TokenProtector.AccessPurpose, token, out var access))
        {
            logger.LogWarning("[BearerAuthMiddleware] Failed to unprotect token, sending challenge");
            await Challenge(context, logger, error: "invalid_token");
            return;
        }

        logger.LogInformation("[BearerAuthMiddleware] Token unprotected successfully, attaching credential to context");
        context.Items[OAuthSettings.CredentialItemKey] = access!.Credential;
        await next(context);
    }

    private static Task Challenge(HttpContext context, ILogger logger, string? error)
    {
        var value = $"Bearer resource_metadata=\"{OAuthSettings.GetProtectedResourceMetadataUrl(context)}\"";
        if (error is not null)
        {
            value += $", error=\"{error}\"";
            logger.LogWarning("[BearerAuthMiddleware.Challenge] Sending 401 with error: {Error}", error);
        }
        else
        {
            logger.LogWarning("[BearerAuthMiddleware.Challenge] Sending 401 challenge");
        }

        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        context.Response.Headers.WWWAuthenticate = value;
        return Task.CompletedTask;
    }
}
