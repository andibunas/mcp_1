using Amazon.SecretsManager;
using Amazon.SecretsManager.Model;
using McpGoogleDrive.Core.Auth;
using Moq;

namespace McpGoogleDrive.Core.Tests.Auth;

public class SecretsManagerTokenStoreTests
{
    [Fact]
    public async Task LoadAsync_ReturnsSecretString_WhenSecretExists()
    {
        var client = new Mock<IAmazonSecretsManager>();
        client
            .Setup(c => c.GetSecretValueAsync(
                It.Is<GetSecretValueRequest>(r => r.SecretId == "mcp-google-drive/default-user"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetSecretValueResponse { SecretString = "{\"refresh_token\":\"abc\"}" });
        var store = new SecretsManagerTokenStore(client.Object);

        var result = await store.LoadAsync("default-user");

        Assert.Equal("{\"refresh_token\":\"abc\"}", result);
    }

    [Fact]
    public async Task LoadAsync_ReturnsNull_WhenSecretDoesNotExist()
    {
        var client = new Mock<IAmazonSecretsManager>();
        client
            .Setup(c => c.GetSecretValueAsync(It.IsAny<GetSecretValueRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ResourceNotFoundException("not found"));
        var store = new SecretsManagerTokenStore(client.Object);

        var result = await store.LoadAsync("never-saved");

        Assert.Null(result);
    }

    [Fact]
    public async Task SaveAsync_CreatesSecret_WhenPutFailsBecauseSecretDoesNotExistYet()
    {
        var client = new Mock<IAmazonSecretsManager>();
        client
            .Setup(c => c.PutSecretValueAsync(It.IsAny<PutSecretValueRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ResourceNotFoundException("not found"));
        client
            .Setup(c => c.CreateSecretAsync(
                It.Is<CreateSecretRequest>(r => r.Name == "mcp-google-drive/default-user" && r.SecretString == "token"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CreateSecretResponse());
        var store = new SecretsManagerTokenStore(client.Object);

        await store.SaveAsync("default-user", "token");

        client.Verify(
            c => c.CreateSecretAsync(It.IsAny<CreateSecretRequest>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task SaveAsync_UpdatesExistingSecret_WithoutCreating()
    {
        var client = new Mock<IAmazonSecretsManager>();
        client
            .Setup(c => c.PutSecretValueAsync(It.IsAny<PutSecretValueRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PutSecretValueResponse());
        var store = new SecretsManagerTokenStore(client.Object);

        await store.SaveAsync("default-user", "token");

        client.Verify(
            c => c.CreateSecretAsync(It.IsAny<CreateSecretRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task DeleteAsync_ForMissingSecret_DoesNotThrow()
    {
        var client = new Mock<IAmazonSecretsManager>();
        client
            .Setup(c => c.DeleteSecretAsync(It.IsAny<DeleteSecretRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ResourceNotFoundException("not found"));
        var store = new SecretsManagerTokenStore(client.Object);

        await store.DeleteAsync("never-saved");
    }
}
