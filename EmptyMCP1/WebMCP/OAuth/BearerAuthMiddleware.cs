namespace WebMCP.OAuth;

/// <summary>
/// Step 6: guards the MCP endpoint (root). Unseals the access token and exposes the service account JSON
/// to tools through HttpContext.Items[OAuthSettings.CredentialItemKey].
/// </summary>
public class BearerAuthMiddleware(RequestDelegate next, TokenProtector protector)
{
    public async Task InvokeAsync(HttpContext context)
    {
        // Console.WriteLine($"[BearerAuthMiddleware] Processing request: {context.Request.Method} {context.Request.Path}");

        // Exact match: the MCP endpoint is the root, so StartsWithSegments("/") would match every path.
        if (context.Request.Path.Value != OAuthSettings.McpPath)
        {
            Console.WriteLine("[BearerAuthMiddleware] Path is not the MCP endpoint, skipping auth");
            await next(context);
            return;
        }

        // Console.WriteLine("[BearerAuthMiddleware] Path is the MCP endpoint, requiring bearer token");

        var header = context.Request.Headers.Authorization.FirstOrDefault();
        // Console.WriteLine($"[BearerAuthMiddleware] Authorization header: {(header is null ? "(missing)" : "(present)")}");

        if (header is null || !header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine("[BearerAuthMiddleware] Missing or invalid Bearer token, sending challenge");
            await Challenge(context, error: null);
            return;
        }

        var token = header["Bearer ".Length..].Trim();
        Console.WriteLine("[BearerAuthMiddleware] Attempting to unprotect access token");

        if (!protector.TryUnprotect<AccessPayload>(TokenProtector.AccessPurpose, token, out var access))
        {
            Console.WriteLine("[BearerAuthMiddleware] Failed to unprotect token, sending challenge");
            await Challenge(context, error: "invalid_token");
            return;
        }

        Console.WriteLine("[BearerAuthMiddleware] Token unprotected successfully, attaching credential to context");
        context.Items[OAuthSettings.CredentialItemKey] = access!.Credential;
        await next(context);
    }

    private static Task Challenge(HttpContext context, string? error)
    {
        var value = $"Bearer resource_metadata=\"{OAuthSettings.ProtectedResourceMetadataUrl}\"";
        if (error is not null)
        {
            value += $", error=\"{error}\"";
            Console.WriteLine($"[BearerAuthMiddleware.Challenge] Sending 401 with error: {error}");
        }
        else
        {
            Console.WriteLine("[BearerAuthMiddleware.Challenge] Sending 401 challenge");
        }

        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        context.Response.Headers.WWWAuthenticate = value;
        return Task.CompletedTask;
    }
}
