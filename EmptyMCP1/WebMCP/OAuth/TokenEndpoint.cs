using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using log4net;

namespace WebMCP.OAuth;

/// <summary>
/// Step 5: exchanges the code (plus PKCE verifier) for an access token, and refresh tokens for new access tokens.
/// The access token is the service account key sealed by the server.
/// </summary>
public static class TokenEndpoint
{
    private static readonly ILog Log = LogManager.GetLogger(typeof(TokenEndpoint));

    public static void MapTokenEndpoint(this WebApplication app)
    {
        app.MapPost($"{OAuthSettings.IssuerPath}/token", async (HttpContext ctx, TokenProtector protector) =>
        {
            Log.Info("/token endpoint hit");
            ctx.Response.Headers.CacheControl = "no-store";
            ctx.Response.Headers.Pragma = "no-cache";

            if (!ctx.Request.HasFormContentType)
            {
                Log.Info("Request missing form content type");
                return Error("invalid_request", "Expected form-encoded body.");
            }
            var form = await ctx.Request.ReadFormAsync();
            Log.Info($"Form data received with grant_type: {form["grant_type"]}");

            var grantType = form["grant_type"].ToString();
            var result = grantType switch
            {
                "authorization_code" => AuthorizationCodeGrant(protector, form),
                "refresh_token" => RefreshGrant(protector, form),
                _ => Error("unsupported_grant_type", "Use authorization_code or refresh_token.")
            };
            Log.Info("Grant processing complete, returning result");
            return result;
        });
    }

    private static IResult AuthorizationCodeGrant(TokenProtector protector, IFormCollection form)
    {
        Log.Info("Starting authorization code grant flow");
        Log.Info($"client_id={form["client_id"]}, redirect_uri={form["redirect_uri"]}");

        if (!protector.TryUnprotect<CodePayload>(TokenProtector.CodePurpose, form["code"], out var code))
        {
            Log.Warn("Failed to unprotect code - invalid or expired");
            return Error("invalid_grant", "Code is invalid or expired.");
        }
        Log.Info($"Code unprotected successfully. Code ClientId={code!.ClientId}");

        if (code!.ClientId != form["client_id"] || code.RedirectUri != form["redirect_uri"])
        {
            Log.Warn($"Validation failed - code ClientId={code.ClientId} vs form={form["client_id"]}, code RedirectUri={code.RedirectUri} vs form={form["redirect_uri"]}");
            return Error("invalid_grant", "client_id or redirect_uri does not match the authorization request.");
        }
        Log.Info("ClientId and RedirectUri validation passed");

        if (!VerifyPkce(form["code_verifier"].ToString(), code.CodeChallenge))
        {
            Log.Warn("PKCE verification failed");
            return Error("invalid_grant", "PKCE verification failed.");
        }
        Log.Info("PKCE verification passed");

        Log.Info("Authorization code exchanged for tokens successfully");
        return Tokens(protector, code.ClientId, code.Credential);
    }

    private static IResult RefreshGrant(TokenProtector protector, IFormCollection form)
    {
        Log.Info("Starting refresh token grant flow");
        Log.Info($"client_id={form["client_id"]}");

        if (!protector.TryUnprotect<RefreshPayload>(TokenProtector.RefreshPurpose, form["refresh_token"], out var refresh))
        {
            Log.Warn("Failed to unprotect refresh token - invalid or expired");
            return Error("invalid_grant", "Refresh token is invalid or expired.");
        }
        Log.Info($"Refresh token unprotected successfully. Token ClientId={refresh!.ClientId}");

        if (refresh!.ClientId != form["client_id"])
        {
            Log.Warn($"Validation failed - token ClientId={refresh.ClientId} vs form={form["client_id"]}");
            return Error("invalid_grant", "client_id does not match.");
        }
        Log.Info("ClientId validation passed");

        Log.Info("Refresh token exchanged for tokens successfully");
        return Tokens(protector, refresh.ClientId, refresh.Credential);
    }

    private static IResult Tokens(TokenProtector protector, string clientId, string credential)
    {
        Log.Info("Generating access and refresh tokens");
        Log.Info($"clientId={clientId}, AccessTokenLifetime={OAuthSettings.AccessTokenLifetime.TotalSeconds}s");

        var accessToken = protector.Protect(TokenProtector.AccessPurpose, new AccessPayload(credential), OAuthSettings.AccessTokenLifetime);
        Log.Info("Access token generated");

        var refreshToken = protector.Protect(TokenProtector.RefreshPurpose, new RefreshPayload(clientId, credential), OAuthSettings.RefreshTokenLifetime);
        Log.Info("Refresh token generated");

        var response = new
        {
            access_token = accessToken,
            token_type = "Bearer",
            expires_in = (int)OAuthSettings.AccessTokenLifetime.TotalSeconds,
            refresh_token = refreshToken,
            scope = string.Join(' ', OAuthSettings.Scopes)
        };
        Log.Info("Token response prepared successfully");
        return Results.Json(response);
    }

    private static bool VerifyPkce(string verifier, string challenge)
    {
        Log.Info("Starting PKCE verification");
        Log.Info($"Verifier length: {verifier.Length}");

        if (verifier.Length is < 43 or > 128)
        {
            Log.Warn("Verifier length invalid - must be between 43 and 128");
            return false;
        }

        var computed = Base64Url.EncodeToString(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        Log.Info($"Computed challenge: {computed}");
        Log.Info($"Expected challenge: {challenge}");

        var result = CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(computed), Encoding.ASCII.GetBytes(challenge));
        Log.Info($"PKCE verification result: {result}");
        return result;
    }

    private static IResult Error(string error, string description)
    {
        Log.Warn($"{error}: {description}");
        return Results.Json(new { error, error_description = description }, statusCode: StatusCodes.Status400BadRequest);
    }
}
