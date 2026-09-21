using System.Text.Json;
using Google.Apis.Util.Store;

namespace McpGoogleDrive.Core.Auth;

/// <summary>
/// Bridges our <see cref="ITokenStore"/> abstraction into the Google API client library's
/// own <see cref="IDataStore"/> interface, so <c>GoogleWebAuthorizationBroker</c> persists
/// refresh tokens through whichever token store the host configured.
/// </summary>
public sealed class GoogleTokenDataStore(ITokenStore tokenStore) : IDataStore
{
    public async Task StoreAsync<T>(string key, T value)
    {
        var json = JsonSerializer.Serialize(value);
        await tokenStore.SaveAsync(key, json);
    }

    public Task DeleteAsync<T>(string key) => tokenStore.DeleteAsync(key);

    public async Task<T> GetAsync<T>(string key)
    {
        var json = await tokenStore.LoadAsync(key);
        return json is null ? default! : JsonSerializer.Deserialize<T>(json)!;
    }

    public Task ClearAsync() =>
        throw new NotSupportedException("Clearing all tokens at once is not supported; delete individual keys instead.");
}
