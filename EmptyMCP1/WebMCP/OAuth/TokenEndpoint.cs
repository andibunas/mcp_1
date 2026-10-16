using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;

namespace WebMCP.OAuth;

/// <summary>
/// Step 5: exchanges the code (plus PKCE verifier) for an access token, and refresh tokens for new access tokens.
/// The access token is the service account key sealed by the server.
/// </summary>
public static class TokenEndpoint
{
    public static void MapTokenEndpoint(this WebApplication app)
    {
        app.MapPost($"{OAuthSettings.IssuerPath}/token", async (HttpContext ctx, TokenProtector protector) =>
        {
            ctx.Response.Headers.CacheControl = "no-store";
            ctx.Response.Headers.Pragma = "no-cache";

            if (!ctx.Request.HasFormContentType) return Error("invalid_request", "Expected form-encoded body.");
            var form = await ctx.Request.ReadFormAsync();

            return form["grant_type"].ToString() switch
            {
                "authorization_code" => AuthorizationCodeGrant(protector, form),
                "refresh_token" => RefreshGrant(protector, form),
                _ => Error("unsupported_grant_type", "Use authorization_code or refresh_token.")
            };
        });
    }

    private static IResult AuthorizationCodeGrant(TokenProtector protector, IFormCollection form)
    {
        if (!protector.TryUnprotect<CodePayload>(TokenProtector.CodePurpose, form["code"], out var code))
            return Error("invalid_grant", "Code is invalid or expired.");

        if (code!.ClientId != form["client_id"] || code.RedirectUri != form["redirect_uri"])
            return Error("invalid_grant", "client_id or redirect_uri does not match the authorization request.");

        if (!VerifyPkce(form["code_verifier"].ToString(), code.CodeChallenge))
            return Error("invalid_grant", "PKCE verification failed.");

        Console.WriteLine("Authorization code exchanged for tokens");
        return Tokens(protector, code.ClientId, code.Credential);
    }

    private static IResult RefreshGrant(TokenProtector protector, IFormCollection form)
    {
        if (!protector.TryUnprotect<RefreshPayload>(TokenProtector.RefreshPurpose, form["refresh_token"], out var refresh))
            return Error("invalid_grant", "Refresh token is invalid or expired.");

        if (refresh!.ClientId != form["client_id"])
            return Error("invalid_grant", "client_id does not match.");

        Console.WriteLine("Refresh token exchanged for tokens");
        return Tokens(protector, refresh.ClientId, refresh.Credential);
    }

    private static IResult Tokens(TokenProtector protector, string clientId, string credential) => Results.Json(new
    {
        access_token = protector.Protect(TokenProtector.AccessPurpose, new AccessPayload(credential), OAuthSettings.AccessTokenLifetime),
        token_type = "Bearer",
        expires_in = (int)OAuthSettings.AccessTokenLifetime.TotalSeconds,
        refresh_token = protector.Protect(TokenProtector.RefreshPurpose, new RefreshPayload(clientId, credential), OAuthSettings.RefreshTokenLifetime),
        scope = string.Join(' ', OAuthSettings.Scopes)
    });

    private static bool VerifyPkce(string verifier, string challenge)
    {
        if (verifier.Length is < 43 or > 128) return false;
        var computed = Base64Url.EncodeToString(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        return CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(computed), Encoding.ASCII.GetBytes(challenge));
    }

    private static IResult Error(string error, string description) =>
        Results.Json(new { error, error_description = description }, statusCode: StatusCodes.Status400BadRequest);
}
