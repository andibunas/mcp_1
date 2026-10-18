using System.Text.Encodings.Web;
using System.Text.RegularExpressions;
using log4net;
using Microsoft.AspNetCore.WebUtilities;

namespace WebMCP.OAuth;

/// <summary>
/// Step 4: the browser step. Shows a page where the user pastes their service account key,
/// then redirects back to the client with a sealed, 60-second authorization code.
/// </summary>
public static partial class AuthorizeEndpoint
{
    private static readonly ILog Log = LogManager.GetLogger(typeof(AuthorizeEndpoint));

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

        app.MapGet($"{path}/key-help", () => Results.Content(KeyHelp, "text/html; charset=utf-8"));

        app.MapGet(path, (HttpContext ctx, TokenProtector protector) =>
        {
            Log.Info("/authorize GET endpoint hit");
            return Handle(ctx, protector, key => ctx.Request.Query[key].ToString(), credential: null);
        });

        app.MapPost(path, async (HttpContext ctx, TokenProtector protector) =>
        {
            Log.Info("/authorize POST endpoint hit");
            if (!ctx.Request.HasFormContentType)
            {
                Log.Info("Request missing form content type");
                return Results.BadRequest("Expected a form post.");
            }
            var form = await ctx.Request.ReadFormAsync();
            Log.Info("Form data received with credential");
            return Handle(ctx,
                        protector,
                        // Log the key name only: the credential field holds the service account private key.
                        key => {
                            Log.Info($"Form key: {key}");
                            return form[key].ToString();
                            },
                        form["credential"].ToString());
        });
    }

    private static IResult Handle(HttpContext ctx, TokenProtector protector, Func<string, string> get, string? credential)
    {
        Log.Info("Starting authorization flow");
        ctx.Response.Headers.CacheControl = "no-store";
        ctx.Response.Headers["X-Frame-Options"] = "DENY";

        // Until client_id and redirect_uri are verified we must not redirect anywhere.
        var redirectUri = get("redirect_uri");
        Log.Info($"redirect_uri: {redirectUri}");
        Log.Info("Attempting to unprotect client_id");

        if (!protector.TryUnprotect<ClientPayload>(TokenProtector.ClientPurpose, get("client_id"), out var client))
        {
            Log.Warn("Failed to unprotect client_id");
            return Results.Text("Unknown client or redirect URI. Register the client again.", statusCode: 400);
        }

        Log.Info($"Client unprotected, checking redirect_uri in allowed list: {string.Join(", ", client!.RedirectUris)}");
        if (!client!.RedirectUris.Contains(redirectUri))
        {
            Log.Warn("redirect_uri not in client's allowed list");
            return Results.Text("Unknown client or redirect URI. Register the client again.", statusCode: 400);
        }
        Log.Info("Client and redirect_uri verified");

        var state = get("state");
        Log.Info($"state: {state}");
        IResult Fail(string error)
        {
            Log.Warn($"Failing with error: {error}");
            return Redirect(redirectUri, ("error", error), ("state", state));
        }

        var responseType = get("response_type");
        Log.Info($"response_type: {responseType}");
        if (responseType != "code") return Fail("unsupported_response_type");

        var challengeMethod = get("code_challenge_method");
        var codeChallenge = get("code_challenge");
        Log.Info($"code_challenge_method: {challengeMethod}, code_challenge: {codeChallenge}");
        if (challengeMethod != "S256" || !ChallengePattern().IsMatch(codeChallenge))
        {
            Log.Warn("Challenge validation failed");
            return Fail("invalid_request");
        }
        Log.Info("Challenge validation passed");

        if (credential is null)
        {
            Log.Info("No credential provided, showing authorization page");
            return Page(get, client.ClientName, error: null);
        }

        Log.Info("Validating service account key");
        if (!ServiceAccountKeyValidator.TryValidate(credential, out var normalized, out var error))
        {
            Log.Warn($"Service account validation failed: {error}");
            return Page(get, client.ClientName, error);
        }
        Log.Info("Service account key validated successfully");

        var code = protector.Protect(TokenProtector.CodePurpose,
            new CodePayload(get("client_id"), redirectUri, codeChallenge, normalized),
            OAuthSettings.CodeLifetime);

        Log.Info("Authorization code issued, redirecting");
        return Redirect(redirectUri, ("code", code), ("state", state));
    }

    private static IResult Redirect(string redirectUri, params (string Key, string Value)[] query)
    {
        var url = redirectUri;
        foreach (var (key, value) in query)
            if (!string.IsNullOrEmpty(value))
            {
                url = QueryHelpers.AddQueryString(url, key, value);
                Log.Info($"Adding query param: {key}={value}");
            }
        Log.Info($"Final redirect URL: {url}");
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
