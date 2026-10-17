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
            Console.WriteLine("[TokenEndpoint] /token endpoint hit");
            ctx.Response.Headers.CacheControl = "no-store";
            ctx.Response.Headers.Pragma = "no-cache";

            if (!ctx.Request.HasFormContentType)
            {
                Console.WriteLine("[TokenEndpoint] Request missing form content type");
                return Error("invalid_request", "Expected form-encoded body.");
            }
            var form = await ctx.Request.ReadFormAsync();
            Console.WriteLine($"[TokenEndpoint] Form data received with grant_type: {form["grant_type"]}");

            var grantType = form["grant_type"].ToString();
            Console.WriteLine($"[TokenEndpoint] Processing grant_type: {grantType}");
            var result = grantType switch
            {
                "authorization_code" => AuthorizationCodeGrant(protector, form),
                "refresh_token" => RefreshGrant(protector, form),
                _ => Error("unsupported_grant_type", "Use authorization_code or refresh_token.")
            };
            Console.WriteLine($"[TokenEndpoint] Grant processing complete, returning result");
            return result;
        });
    }

    private static IResult AuthorizationCodeGrant(TokenProtector protector, IFormCollection form)
    {
        Console.WriteLine("[AuthorizationCodeGrant] Starting authorization code grant flow");
        Console.WriteLine($"[AuthorizationCodeGrant] client_id={form["client_id"]}, redirect_uri={form["redirect_uri"]}");

        if (!protector.TryUnprotect<CodePayload>(TokenProtector.CodePurpose, form["code"], out var code))
        {
            Console.WriteLine("[AuthorizationCodeGrant] Failed to unprotect code - invalid or expired");
            return Error("invalid_grant", "Code is invalid or expired.");
        }
        Console.WriteLine($"[AuthorizationCodeGrant] Code unprotected successfully. Code ClientId={code!.ClientId}");

        if (code!.ClientId != form["client_id"] || code.RedirectUri != form["redirect_uri"])
        {
            Console.WriteLine($"[AuthorizationCodeGrant] Validation failed - code ClientId={code.ClientId} vs form={form["client_id"]}, code RedirectUri={code.RedirectUri} vs form={form["redirect_uri"]}");
            return Error("invalid_grant", "client_id or redirect_uri does not match the authorization request.");
        }
        Console.WriteLine("[AuthorizationCodeGrant] ClientId and RedirectUri validation passed");

        if (!VerifyPkce(form["code_verifier"].ToString(), code.CodeChallenge))
        {
            Console.WriteLine("[AuthorizationCodeGrant] PKCE verification failed");
            return Error("invalid_grant", "PKCE verification failed.");
        }
        Console.WriteLine("[AuthorizationCodeGrant] PKCE verification passed");

        Console.WriteLine("[AuthorizationCodeGrant] Authorization code exchanged for tokens successfully");
        return Tokens(protector, code.ClientId, code.Credential);
    }

    private static IResult RefreshGrant(TokenProtector protector, IFormCollection form)
    {
        Console.WriteLine("[RefreshGrant] Starting refresh token grant flow");
        Console.WriteLine($"[RefreshGrant] client_id={form["client_id"]}");

        if (!protector.TryUnprotect<RefreshPayload>(TokenProtector.RefreshPurpose, form["refresh_token"], out var refresh))
        {
            Console.WriteLine("[RefreshGrant] Failed to unprotect refresh token - invalid or expired");
            return Error("invalid_grant", "Refresh token is invalid or expired.");
        }
        Console.WriteLine($"[RefreshGrant] Refresh token unprotected successfully. Token ClientId={refresh!.ClientId}");

        if (refresh!.ClientId != form["client_id"])
        {
            Console.WriteLine($"[RefreshGrant] Validation failed - token ClientId={refresh.ClientId} vs form={form["client_id"]}");
            return Error("invalid_grant", "client_id does not match.");
        }
        Console.WriteLine("[RefreshGrant] ClientId validation passed");

        Console.WriteLine("[RefreshGrant] Refresh token exchanged for tokens successfully");
        return Tokens(protector, refresh.ClientId, refresh.Credential);
    }

    private static IResult Tokens(TokenProtector protector, string clientId, string credential)
    {
        Console.WriteLine("[Tokens] Generating access and refresh tokens");
        Console.WriteLine($"[Tokens] clientId={clientId}, AccessTokenLifetime={OAuthSettings.AccessTokenLifetime.TotalSeconds}s");

        var accessToken = protector.Protect(TokenProtector.AccessPurpose, new AccessPayload(credential), OAuthSettings.AccessTokenLifetime);
        Console.WriteLine("[Tokens] Access token generated");

        var refreshToken = protector.Protect(TokenProtector.RefreshPurpose, new RefreshPayload(clientId, credential), OAuthSettings.RefreshTokenLifetime);
        Console.WriteLine("[Tokens] Refresh token generated");

        var response = new
        {
            access_token = accessToken,
            token_type = "Bearer",
            expires_in = (int)OAuthSettings.AccessTokenLifetime.TotalSeconds,
            refresh_token = refreshToken,
            scope = string.Join(' ', OAuthSettings.Scopes)
        };
        Console.WriteLine("[Tokens] Token response prepared successfully");
        return Results.Json(response);
    }

    private static bool VerifyPkce(string verifier, string challenge)
    {
        Console.WriteLine("[VerifyPkce] Starting PKCE verification");
        Console.WriteLine($"[VerifyPkce] Verifier length: {verifier.Length}");

        if (verifier.Length is < 43 or > 128)
        {
            Console.WriteLine($"[VerifyPkce] Verifier length invalid - must be between 43 and 128");
            return false;
        }

        var computed = Base64Url.EncodeToString(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        Console.WriteLine($"[VerifyPkce] Computed challenge: {computed}");
        Console.WriteLine($"[VerifyPkce] Expected challenge: {challenge}");

        var result = CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(computed), Encoding.ASCII.GetBytes(challenge));
        Console.WriteLine($"[VerifyPkce] PKCE verification result: {result}");
        return result;
    }

    private static IResult Error(string error, string description)
    {
        Console.WriteLine($"[Error] {error}: {description}");
        return Results.Json(new { error, error_description = description }, statusCode: StatusCodes.Status400BadRequest);
    }
}
