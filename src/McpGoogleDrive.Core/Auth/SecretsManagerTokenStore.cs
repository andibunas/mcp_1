using Amazon.SecretsManager;
using Amazon.SecretsManager.Model;

namespace McpGoogleDrive.Core.Auth;

/// <summary>
/// AWS Secrets Manager-backed <see cref="ITokenStore"/>, for headless/AWS-hosted deployments
/// (Lambda, EC2, ECS) that have no durable local disk to persist <see cref="FileTokenStore"/>'s
/// files across restarts or across instances. Each key is stored as its own secret named
/// <c>{secretPrefix}{key}</c>.
/// </summary>
public sealed class SecretsManagerTokenStore(IAmazonSecretsManager client, string secretPrefix = "mcp-google-drive/") : ITokenStore
{
    public async Task<string?> LoadAsync(string key, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await client.GetSecretValueAsync(
                new GetSecretValueRequest { SecretId = SecretName(key) }, cancellationToken);
            return response.SecretString;
        }
        catch (ResourceNotFoundException)
        {
            return null;
        }
    }

    public async Task SaveAsync(string key, string value, CancellationToken cancellationToken = default)
    {
        var secretName = SecretName(key);
        try
        {
            await client.PutSecretValueAsync(
                new PutSecretValueRequest { SecretId = secretName, SecretString = value }, cancellationToken);
        }
        catch (ResourceNotFoundException)
        {
            await client.CreateSecretAsync(
                new CreateSecretRequest { Name = secretName, SecretString = value }, cancellationToken);
        }
    }

    public async Task DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        try
        {
            await client.DeleteSecretAsync(
                new DeleteSecretRequest { SecretId = SecretName(key), ForceDeleteWithoutRecovery = true },
                cancellationToken);
        }
        catch (ResourceNotFoundException)
        {
            // Already gone — deleting a missing key is a no-op, matching FileTokenStore.
        }
    }

    private string SecretName(string key) => $"{secretPrefix}{key}";
}
