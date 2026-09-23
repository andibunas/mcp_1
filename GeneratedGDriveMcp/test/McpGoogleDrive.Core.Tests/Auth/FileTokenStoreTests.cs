using McpGoogleDrive.Core.Auth;

namespace McpGoogleDrive.Core.Tests.Auth;

public class FileTokenStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"mcp-drive-tests-{Guid.NewGuid()}");

    [Fact]
    public async Task SaveThenLoad_ReturnsSameValue()
    {
        var store = new FileTokenStore(_directory);

        await store.SaveAsync("default-user", "{\"refresh_token\":\"abc\"}");
        var loaded = await store.LoadAsync("default-user");

        Assert.Equal("{\"refresh_token\":\"abc\"}", loaded);
    }

    [Fact]
    public async Task Load_ForMissingKey_ReturnsNull()
    {
        var store = new FileTokenStore(_directory);

        var loaded = await store.LoadAsync("never-saved");

        Assert.Null(loaded);
    }

    [Fact]
    public async Task Delete_RemovesStoredValue()
    {
        var store = new FileTokenStore(_directory);
        await store.SaveAsync("default-user", "token");

        await store.DeleteAsync("default-user");

        Assert.Null(await store.LoadAsync("default-user"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
