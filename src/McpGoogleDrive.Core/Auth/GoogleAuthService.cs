using Google.Apis.Auth.OAuth2;
using McpGoogleDrive.Core.Configuration;
using Microsoft.Extensions.Options;

namespace McpGoogleDrive.Core.Auth;

/// <summary>
/// Runs the OAuth2 "installed app" flow (opens the system browser once, then reuses the
/// cached refresh token from the configured <see cref="ITokenStore"/> on subsequent runs).
/// </summary>
public sealed class GoogleAuthService(IOptions<GoogleAuthOptions> options, ITokenStore tokenStore)
{
    private readonly GoogleAuthOptions _options = options.Value;

    public async Task<UserCredential> AuthorizeAsync(
        IEnumerable<string> scopes,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_options.ClientId) || string.IsNullOrWhiteSpace(_options.ClientSecret))
        {
            throw new InvalidOperationException(
                "GoogleAuth:ClientId and GoogleAuth:ClientSecret are not configured. " +
                "See docs/setup.md for how to create OAuth credentials in Google Cloud Console.");
        }

        var secrets = new ClientSecrets
        {
            ClientId = _options.ClientId,
            ClientSecret = _options.ClientSecret,
        };

        return await GoogleWebAuthorizationBroker.AuthorizeAsync(
            secrets,
            scopes,
            "default-user",
            cancellationToken,
            new GoogleTokenDataStore(tokenStore));
    }
}
