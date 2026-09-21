namespace McpGoogleDrive.Core.Auth;

/// <summary>
/// Persists opaque token blobs (OAuth refresh tokens, picked-folder state) under a string key.
/// Pluggable per host: local file on disk for stdio/local hosts, AWS Secrets Manager for
/// AWS-hosted deployments.
/// </summary>
public interface ITokenStore
{
    Task<string?> LoadAsync(string key, CancellationToken cancellationToken = default);

    Task SaveAsync(string key, string value, CancellationToken cancellationToken = default);

    Task DeleteAsync(string key, CancellationToken cancellationToken = default);
}
