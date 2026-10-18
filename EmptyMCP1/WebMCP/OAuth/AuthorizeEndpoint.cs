using System.Text.Encodings.Web;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.WebUtilities;

namespace WebMCP.OAuth;

/// <summary>
/// Step 4: the browser step. Shows a page where the user pastes their service account key,
/// then redirects back to the client with a sealed, 60-second authorization code.
/// </summary>
public static partial class AuthorizeEndpoint
{
    private static readonly string[] Passthrough =
        ["response_type", "client_id", "redirect_uri", "code_challenge", "code_challenge_method", "state", "scope"];

    // Edit OAuth/authorize.html to change the page. It is copied next to the binary at build time.
    private static readonly string Template =
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "OAuth", "authorize.html"));

    private static readonly string KeyHelp =
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "OAuth", "key-help.html"));

    [GeneratedRegex(@"^[A-Za-z0-9_-]{43}$")]
    private static partial Regex ChallengePattern();

    public static void MapAuthorizeEndpoints(this WebApplication app)
    {
        var path = $"{OAuthSettings.IssuerPath}/authorize";
        var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("WebMCP.OAuth.AuthorizeEndpoint");

        app.MapGet($"{path}/key-help", () => Results.Content(KeyHelp, "text/html; charset=utf-8"));

        app.MapGet(path, (HttpContext ctx, TokenProtector protector) =>
        {
            logger.LogInformation("[AuthorizeEndpoint.MapGet] /authorize GET endpoint hit");
            return Handle(ctx, protector, logger, key => ctx.Request.Query[key].ToString(), credential: null);
        });

        app.MapPost(path, async (HttpContext ctx, TokenProtector protector) =>
        {
            logger.LogInformation("[AuthorizeEndpoint.MapPost] /authorize POST endpoint hit");
            if (!ctx.Request.HasFormContentType)
            {
                logger.LogInformation("[AuthorizeEndpoint.MapPost] Request missing form content type");
                return Results.BadRequest("Expected a form post.");
            }
            var form = await ctx.Request.ReadFormAsync();
            logger.LogInformation("[AuthorizeEndpoint.MapPost] Form data received with credential");
            return Handle(ctx,
                        protector,
                        logger,
                        // Log the key name only: the credential field holds the service account private key.
                        key => {
                            logger.LogInformation("[AuthorizeEndpoint.MapPost] Form key: {Key}", key);
                            return form[key].ToString();
                            },
                        form["credential"].ToString());
        });
    }

    private static IResult Handle(HttpContext ctx, TokenProtector protector, ILogger logger, Func<string, string> get, string? credential)
    {
        logger.LogInformation("[AuthorizeEndpoint.Handle] Starting authorization flow");
        ctx.Response.Headers.CacheControl = "no-store";
        ctx.Response.Headers["X-Frame-Options"] = "DENY";

        // Until client_id and redirect_uri are verified we must not redirect anywhere.
        var redirectUri = get("redirect_uri");
        logger.LogInformation("[AuthorizeEndpoint.Handle] redirect_uri: {RedirectUri}", redirectUri);
        logger.LogInformation("[AuthorizeEndpoint.Handle] Attempting to unprotect client_id");

        if (!protector.TryUnprotect<ClientPayload>(TokenProtector.ClientPurpose, get("client_id"), out var client))
        {
            logger.LogWarning("[AuthorizeEndpoint.Handle] Failed to unprotect client_id");
            return Results.Text("Unknown client or redirect URI. Register the client again.", statusCode: 400);
        }

        logger.LogInformation("[AuthorizeEndpoint.Handle] Client unprotected, checking redirect_uri in allowed list: {RedirectUris}", string.Join(", ", client!.RedirectUris));
        if (!client!.RedirectUris.Contains(redirectUri))
        {
            logger.LogWarning("[AuthorizeEndpoint.Handle] redirect_uri not in client's allowed list");
            return Results.Text("Unknown client or redirect URI. Register the client again.", statusCode: 400);
        }
        logger.LogInformation("[AuthorizeEndpoint.Handle] Client and redirect_uri verified");

        var state = get("state");
        logger.LogInformation("[AuthorizeEndpoint.Handle] state: {State}", state);
        IResult Fail(string error)
        {
            logger.LogWarning("[AuthorizeEndpoint.Handle] Failing with error: {Error}", error);
            return Redirect(logger, redirectUri, ("error", error), ("state", state));
        }

        var responseType = get("response_type");
        logger.LogInformation("[AuthorizeEndpoint.Handle] response_type: {ResponseType}", responseType);
        if (responseType != "code") return Fail("unsupported_response_type");

        var challengeMethod = get("code_challenge_method");
        var codeChallenge = get("code_challenge");
        logger.LogInformation("[AuthorizeEndpoint.Handle] code_challenge_method: {Method}, code_challenge: {Challenge}", challengeMethod, codeChallenge);
        if (challengeMethod != "S256" || !ChallengePattern().IsMatch(codeChallenge))
        {
            logger.LogWarning("[AuthorizeEndpoint.Handle] Challenge validation failed");
            return Fail("invalid_request");
        }
        logger.LogInformation("[AuthorizeEndpoint.Handle] Challenge validation passed");

        if (credential is null)
        {
            logger.LogInformation("[AuthorizeEndpoint.Handle] No credential provided, showing authorization page");
            return Page(get, client.ClientName, error: null);
        }

        logger.LogInformation("[AuthorizeEndpoint.Handle] Validating service account key");
        if (!ServiceAccountKeyValidator.TryValidate(credential, logger, out var normalized, out var error))
        {
            logger.LogWarning("[AuthorizeEndpoint.Handle] Service account validation failed: {Error}", error);
            return Page(get, client.ClientName, error);
        }
        logger.LogInformation("[AuthorizeEndpoint.Handle] Service account key validated successfully");

        var code = protector.Protect(TokenProtector.CodePurpose,
            new CodePayload(get("client_id"), redirectUri, codeChallenge, normalized),
            OAuthSettings.CodeLifetime);

        logger.LogInformation("[AuthorizeEndpoint.Handle] Authorization code issued, redirecting");
        return Redirect(logger, redirectUri, ("code", code), ("state", state));
    }

    private static IResult Redirect(ILogger logger, string redirectUri, params (string Key, string Value)[] query)
    {
        var url = redirectUri;
        foreach (var (key, value) in query)
            if (!string.IsNullOrEmpty(value))
            {
                url = QueryHelpers.AddQueryString(url, key, value);
                logger.LogInformation("[AuthorizeEndpoint.Redirect] Adding query param: {Key}={Value}", key, value);
            }
        logger.LogInformation("[AuthorizeEndpoint.Redirect] Final redirect URL: {Url}", url);
        return Results.Redirect(url);
    }

    private static IResult Page(Func<string, string> get, string? clientName, string? error)
    {
        var enc = HtmlEncoder.Default;
        var hidden = string.Concat(Passthrough.Select(k =>
            $"<input type=\"hidden\" name=\"{k}\" value=\"{enc.Encode(get(k))}\">"));
        var errorHtml = error is null ? "" : $"<p class=\"error\">{enc.Encode(error)}</p>";
        var who = string.IsNullOrEmpty(clientName) ? "An application" : enc.Encode(clientName);

        var html = Template
            .Replace("{{who}}", who)
            .Replace("{{error}}", errorHtml)
            .Replace("{{hidden}}", hidden);
        return Results.Content(html, "text/html; charset=utf-8", statusCode: error is null ? 200 : 400);
    }
}
