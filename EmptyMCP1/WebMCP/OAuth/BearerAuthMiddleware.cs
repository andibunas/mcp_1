using log4net;

namespace WebMCP.OAuth;

/// <summary>
/// Step 6: guards the MCP endpoint (root). Unseals the access token and exposes the service account JSON
/// to tools through HttpContext.Items[OAuthSettings.CredentialItemKey].
/// </summary>
public class BearerAuthMiddleware(RequestDelegate next, TokenProtector protector)
{
    private static readonly ILog Log = LogManager.GetLogger(typeof(BearerAuthMiddleware));

    public async Task InvokeAsync(HttpContext context)
    {
        // Exact match: the MCP endpoint is the root, so StartsWithSegments("/") would match every path.
        if (context.Request.Path.Value != OAuthSettings.McpPath)
        {
            Log.Info("Path is not the MCP endpoint, skipping auth");
            await next(context);
            return;
        }

        var header = context.Request.Headers.Authorization.FirstOrDefault();

        if (header is null || !header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            Log.Warn("Missing or invalid Bearer token, sending challenge");
            await Challenge(context, error: null);
            return;
        }

        var token = header["Bearer ".Length..].Trim();
        Log.Info("Attempting to unprotect access token");

        if (!protector.TryUnprotect<AccessPayload>(TokenProtector.AccessPurpose, token, out var access))
        {
            Log.Warn("Failed to unprotect token, sending challenge");
            await Challenge(context, error: "invalid_token");
            return;
        }

        Log.Info("Token unprotected successfully, attaching credential to context");
        context.Items[OAuthSettings.CredentialItemKey] = access!.Credential;
        Log.Info($"Caller identity: {LogHelper.DescribeCaller(access.Credential, token)}");
        await next(context);
    }

    private static Task Challenge(HttpContext context, string? error)
    {
        var value = $"Bearer resource_metadata=\"{OAuthSettings.GetProtectedResourceMetadataUrl(context)}\"";
        if (error is not null)
        {
            value += $", error=\"{error}\"";
            Log.Warn($"Sending 401 with error: {error}");
        }
        else
        {
            Log.Warn("Sending 401 challenge");
        }

        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        context.Response.Headers.WWWAuthenticate = value;
        return Task.CompletedTask;
    }
}
