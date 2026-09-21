using McpGoogleDrive.Core.Drive;

namespace McpGoogleDrive.Core.Tests.Drive;

/// <summary>
/// In-memory <see cref="IGoogleDriveApi"/> fake for exercising <see cref="DriveFileService"/>'s
/// allow-list enforcement without hitting the real Drive API.
/// </summary>
internal sealed class FakeGoogleDriveApi : IGoogleDriveApi
{
    private readonly Dictionary<string, DriveFile> _files = new();

    public FakeGoogleDriveApi AddFile(string id, string name, string mimeType, params string[] parents)
    {
        _files[id] = new DriveFile(id, name, mimeType, parents, mimeType == "application/vnd.google-apps.folder");
        return this;
    }

    public Task<DriveFile> GetMetadataAsync(string fileId, CancellationToken cancellationToken) =>
        _files.TryGetValue(fileId, out var file)
            ? Task.FromResult(file)
            : throw new KeyNotFoundException($"No such fake file: {fileId}");

    public Task<IReadOnlyList<DriveFile>> ListChildrenAsync(string folderId, string? nameContains, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<DriveFile>>(
            _files.Values.Where(f => f.Parents.Contains(folderId)).ToList());

    public Task<Stream> DownloadAsync(string fileId, CancellationToken cancellationToken) =>
        Task.FromResult<Stream>(new MemoryStream(System.Text.Encoding.UTF8.GetBytes($"content of {fileId}")));

    public Task<DriveFile> CreateFileAsync(string parentFolderId, string name, string mimeType, Stream content, CancellationToken cancellationToken)
    {
        var id = $"created-{name}";
        AddFile(id, name, mimeType, parentFolderId);
        return Task.FromResult(_files[id]);
    }

    public Task<DriveFile> UpdateContentAsync(string fileId, Stream content, CancellationToken cancellationToken) =>
        GetMetadataAsync(fileId, cancellationToken);

    public Task<DriveFile> MoveAsync(string fileId, string newParentFolderId, CancellationToken cancellationToken)
    {
        var existing = _files[fileId];
        _files[fileId] = existing with { Parents = [newParentFolderId] };
        return Task.FromResult(_files[fileId]);
    }
}
