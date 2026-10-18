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
        var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("WebMCP.OAuth.TokenEndpoint");

        app.MapPost($"{OAuthSettings.IssuerPath}/token", async (HttpContext ctx, TokenProtector protector) =>
        {
            logger.LogInformation("[TokenEndpoint] /token endpoint hit");
            ctx.Response.Headers.CacheControl = "no-store";
            ctx.Response.Headers.Pragma = "no-cache";

            if (!ctx.Request.HasFormContentType)
            {
                logger.LogInformation("[TokenEndpoint] Request missing form content type");
                return Error(logger, "invalid_request", "Expected form-encoded body.");
            }
            var form = await ctx.Request.ReadFormAsync();
            logger.LogInformation("[TokenEndpoint] Form data received with grant_type: {GrantType}", form["grant_type"].ToString());

            var grantType = form["grant_type"].ToString();
            var result = grantType switch
            {
                "authorization_code" => AuthorizationCodeGrant(protector, logger, form),
                "refresh_token" => RefreshGrant(protector, logger, form),
                _ => Error(logger, "unsupported_grant_type", "Use authorization_code or refresh_token.")
            };
            logger.LogInformation("[TokenEndpoint] Grant processing complete, returning result");
            return result;
        });
    }

    private static IResult AuthorizationCodeGrant(TokenProtector protector, ILogger logger, IFormCollection form)
    {
        logger.LogInformation("[AuthorizationCodeGrant] Starting authorization code grant flow");
        logger.LogInformation("[AuthorizationCodeGrant] client_id={ClientId}, redirect_uri={RedirectUri}", form["client_id"].ToString(), form["redirect_uri"].ToString());

        if (!protector.TryUnprotect<CodePayload>(TokenProtector.CodePurpose, form["code"], out var code))
        {
            logger.LogWarning("[AuthorizationCodeGrant] Failed to unprotect code - invalid or expired");
            return Error(logger, "invalid_grant", "Code is invalid or expired.");
        }
        logger.LogInformation("[AuthorizationCodeGrant] Code unprotected successfully. Code ClientId={ClientId}", code!.ClientId);

        if (code!.ClientId != form["client_id"] || code.RedirectUri != form["redirect_uri"])
        {
            logger.LogWarning("[AuthorizationCodeGrant] Validation failed - code ClientId={CodeClientId} vs form={FormClientId}, code RedirectUri={CodeRedirectUri} vs form={FormRedirectUri}",
                code.ClientId, form["client_id"].ToString(), code.RedirectUri, form["redirect_uri"].ToString());
            return Error(logger, "invalid_grant", "client_id or redirect_uri does not match the authorization request.");
        }
        logger.LogInformation("[AuthorizationCodeGrant] ClientId and RedirectUri validation passed");

        if (!VerifyPkce(logger, form["code_verifier"].ToString(), code.CodeChallenge))
        {
            logger.LogWarning("[AuthorizationCodeGrant] PKCE verification failed");
            return Error(logger, "invalid_grant", "PKCE verification failed.");
        }
        logger.LogInformation("[AuthorizationCodeGrant] PKCE verification passed");

        logger.LogInformation("[AuthorizationCodeGrant] Authorization code exchanged for tokens successfully");
        return Tokens(protector, logger, code.ClientId, code.Credential);
    }

    private static IResult RefreshGrant(TokenProtector protector, ILogger logger, IFormCollection form)
    {
        logger.LogInformation("[RefreshGrant] Starting refresh token grant flow");
        logger.LogInformation("[RefreshGrant] client_id={ClientId}", form["client_id"].ToString());

        if (!protector.TryUnprotect<RefreshPayload>(TokenProtector.RefreshPurpose, form["refresh_token"], out var refresh))
        {
            logger.LogWarning("[RefreshGrant] Failed to unprotect refresh token - invalid or expired");
            return Error(logger, "invalid_grant", "Refresh token is invalid or expired.");
        }
        logger.LogInformation("[RefreshGrant] Refresh token unprotected successfully. Token ClientId={ClientId}", refresh!.ClientId);

        if (refresh!.ClientId != form["client_id"])
        {
            logger.LogWarning("[RefreshGrant] Validation failed - token ClientId={TokenClientId} vs form={FormClientId}", refresh.ClientId, form["client_id"].ToString());
            return Error(logger, "invalid_grant", "client_id does not match.");
        }
        logger.LogInformation("[RefreshGrant] ClientId validation passed");

        logger.LogInformation("[RefreshGrant] Refresh token exchanged for tokens successfully");
        return Tokens(protector, logger, refresh.ClientId, refresh.Credential);
    }

    private static IResult Tokens(TokenProtector protector, ILogger logger, string clientId, string credential)
    {
        logger.LogInformation("[Tokens] Generating access and refresh tokens");
        logger.LogInformation("[Tokens] clientId={ClientId}, AccessTokenLifetime={Lifetime}s", clientId, OAuthSettings.AccessTokenLifetime.TotalSeconds);

        var accessToken = protector.Protect(TokenProtector.AccessPurpose, new AccessPayload(credential), OAuthSettings.AccessTokenLifetime);
        logger.LogInformation("[Tokens] Access token generated");

        var refreshToken = protector.Protect(TokenProtector.RefreshPurpose, new RefreshPayload(clientId, credential), OAuthSettings.RefreshTokenLifetime);
        logger.LogInformation("[Tokens] Refresh token generated");

        var response = new
        {
            access_token = accessToken,
            token_type = "Bearer",
            expires_in = (int)OAuthSettings.AccessTokenLifetime.TotalSeconds,
            refresh_token = refreshToken,
            scope = string.Join(' ', OAuthSettings.Scopes)
        };
        logger.LogInformation("[Tokens] Token response prepared successfully");
        return Results.Json(response);
    }

    private static bool VerifyPkce(ILogger logger, string verifier, string challenge)
    {
        logger.LogInformation("[VerifyPkce] Starting PKCE verification");
        logger.LogInformation("[VerifyPkce] Verifier length: {Length}", verifier.Length);

        if (verifier.Length is < 43 or > 128)
        {
            logger.LogWarning("[VerifyPkce] Verifier length invalid - must be between 43 and 128");
            return false;
        }

        var computed = Base64Url.EncodeToString(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        logger.LogInformation("[VerifyPkce] Computed challenge: {Computed}", computed);
        logger.LogInformation("[VerifyPkce] Expected challenge: {Expected}", challenge);

        var result = CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(computed), Encoding.ASCII.GetBytes(challenge));
        logger.LogInformation("[VerifyPkce] PKCE verification result: {Result}", result);
        return result;
    }

    private static IResult Error(ILogger logger, string error, string description)
    {
        logger.LogWarning("[Error] {Error}: {Description}", error, description);
        return Results.Json(new { error, error_description = description }, statusCode: StatusCodes.Status400BadRequest);
    }
}
