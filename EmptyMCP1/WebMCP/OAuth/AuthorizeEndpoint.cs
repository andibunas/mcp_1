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

    [GeneratedRegex(@"^[A-Za-z0-9_-]{43}$")]
    private static partial Regex ChallengePattern();

    public static void MapAuthorizeEndpoints(this WebApplication app)
    {
        var path = $"{OAuthSettings.IssuerPath}/authorize";

        app.MapGet(path, (HttpContext ctx, TokenProtector protector) =>
            Handle(ctx, protector, key => ctx.Request.Query[key].ToString(), credential: null));

        app.MapPost(path, async (HttpContext ctx, TokenProtector protector) =>
        {
            if (!ctx.Request.HasFormContentType) return Results.BadRequest("Expected a form post.");
            var form = await ctx.Request.ReadFormAsync();
            return Handle(ctx, protector, key => form[key].ToString(), form["credential"].ToString());
        });
    }

    private static IResult Handle(HttpContext ctx, TokenProtector protector, Func<string, string> get, string? credential)
    {
        ctx.Response.Headers.CacheControl = "no-store";
        ctx.Response.Headers["X-Frame-Options"] = "DENY";

        // Until client_id and redirect_uri are verified we must not redirect anywhere.
        var redirectUri = get("redirect_uri");
        if (!protector.TryUnprotect<ClientPayload>(TokenProtector.ClientPurpose, get("client_id"), out var client)
            || !client!.RedirectUris.Contains(redirectUri))
            return Results.Text("Unknown client or redirect URI. Register the client again.", statusCode: 400);

        var state = get("state");
        IResult Fail(string error) => Redirect(redirectUri, ("error", error), ("state", state));

        if (get("response_type") != "code") return Fail("unsupported_response_type");
        if (get("code_challenge_method") != "S256" || !ChallengePattern().IsMatch(get("code_challenge")))
            return Fail("invalid_request");

        if (credential is null) return Page(get, client.ClientName, error: null);

        if (!ServiceAccountKeyValidator.TryValidate(credential, out var normalized, out var error))
            return Page(get, client.ClientName, error);

        var code = protector.Protect(TokenProtector.CodePurpose,
            new CodePayload(get("client_id"), redirectUri, get("code_challenge"), normalized),
            OAuthSettings.CodeLifetime);

        Console.WriteLine("Issued authorization code");
        return Redirect(redirectUri, ("code", code), ("state", state));
    }

    private static IResult Redirect(string redirectUri, params (string Key, string Value)[] query)
    {
        var url = redirectUri;
        foreach (var (key, value) in query)
            if (!string.IsNullOrEmpty(value)) url = QueryHelpers.AddQueryString(url, key, value);
        return Results.Redirect(url);
    }

    private static IResult Page(Func<string, string> get, string? clientName, string? error)
    {
        var enc = HtmlEncoder.Default;
        var hidden = string.Concat(Passthrough.Select(k =>
            $"<input type=\"hidden\" name=\"{k}\" value=\"{enc.Encode(get(k))}\">"));
        var errorHtml = error is null ? "" : $"<p class=\"error\">{enc.Encode(error)}</p>";
        var who = string.IsNullOrEmpty(clientName) ? "An application" : enc.Encode(clientName);

        var html = $$"""
            <!doctype html>
            <html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
            <title>Authorize access</title>
            <style>
              body{font-family:system-ui,sans-serif;max-width:640px;margin:2rem auto;padding:0 1rem}
              textarea{width:100%;height:16rem;font-family:ui-monospace,monospace;font-size:.8rem}
              .error{color:#b00020}
              button{padding:.5rem 1.2rem;margin-top:.8rem}
            </style></head>
            <body>
              <h1>Authorize access</h1>
              <p>{{who}} wants to use your Google service account through this MCP server.</p>
              <p>Paste the full contents of the service account JSON key file.</p>
              {{errorHtml}}
              <form method="post">
                {{hidden}}
                <textarea name="credential" required spellcheck="false" autocomplete="off"></textarea>
                <button type="submit">Authorize</button>
              </form>
            </body></html>
            """;
        return Results.Content(html, "text/html; charset=utf-8", statusCode: error is null ? 200 : 400);
    }
}
