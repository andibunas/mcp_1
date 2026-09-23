namespace McpGoogleDrive.Core.Auth;

/// <summary>
/// Local-disk <see cref="ITokenStore"/>, used by the stdio and local Web hosts.
/// Defaults to ~/.mcp-google-drive/tokens.
/// </summary>
public sealed class FileTokenStore : ITokenStore
{
    private readonly string _directory;

    public FileTokenStore(string? directory = null)
    {
        _directory = directory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".mcp-google-drive",
            "tokens");
        Directory.CreateDirectory(_directory);
    }

    public async Task<string?> LoadAsync(string key, CancellationToken cancellationToken = default)
    {
        var path = PathFor(key);
        return File.Exists(path) ? await File.ReadAllTextAsync(path, cancellationToken) : null;
    }

    public Task SaveAsync(string key, string value, CancellationToken cancellationToken = default) =>
        File.WriteAllTextAsync(PathFor(key), value, cancellationToken);

    public Task DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        var path = PathFor(key);
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        return Task.CompletedTask;
    }

    private string PathFor(string key) => Path.Combine(_directory, $"{Sanitize(key)}.json");

    private static string Sanitize(string key)
    {
        foreach (var invalid in Path.GetInvalidFileNameChars())
        {
            key = key.Replace(invalid, '_');
        }

        return key;
    }
}
