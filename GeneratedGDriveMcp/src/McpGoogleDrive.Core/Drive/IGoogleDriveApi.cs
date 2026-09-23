namespace McpGoogleDrive.Core.Drive;

/// <summary>
/// Minimal seam over the Google Drive API that <see cref="DriveFileService"/> depends on.
/// Exists so allow-list enforcement and file operations can be unit tested with a fake,
/// without hitting the real Drive API or Google.Apis.Drive.v3's sealed client types.
/// </summary>
public interface IGoogleDriveApi
{
    Task<DriveFile> GetMetadataAsync(string fileId, CancellationToken cancellationToken);

    Task<IReadOnlyList<DriveFile>> ListChildrenAsync(string folderId, string? nameContains, CancellationToken cancellationToken);

    Task<Stream> DownloadAsync(string fileId, CancellationToken cancellationToken);

    Task<DriveFile> CreateFileAsync(string parentFolderId, string name, string mimeType, Stream content, CancellationToken cancellationToken);

    Task<DriveFile> UpdateContentAsync(string fileId, Stream content, CancellationToken cancellationToken);

    Task<DriveFile> MoveAsync(string fileId, string newParentFolderId, CancellationToken cancellationToken);
}
