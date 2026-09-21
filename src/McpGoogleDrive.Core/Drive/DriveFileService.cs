using System.Text;
using McpGoogleDrive.Core.Configuration;
using Microsoft.Extensions.Options;

namespace McpGoogleDrive.Core.Drive;

/// <summary>
/// All Drive operations the MCP tools expose, scoped to <see cref="DriveAccessOptions.AllowedFolderIds"/>.
/// Every method checks the target is inside an allowed folder (walking up the parent chain if
/// needed) before calling <see cref="IGoogleDriveApi"/> — this is where "granular access" is enforced,
/// not left to callers.
/// </summary>
public sealed class DriveFileService(IGoogleDriveApi api, IOptions<DriveAccessOptions> options)
{
    private const int MaxAncestorDepth = 20;

    private readonly DriveAccessOptions _options = options.Value;

    public async Task<IReadOnlyList<DriveFile>> ListFilesAsync(
        string folderId, string? nameContains = null, CancellationToken cancellationToken = default)
    {
        await EnsureAccessAsync(folderId, cancellationToken);
        return await api.ListChildrenAsync(folderId, nameContains, cancellationToken);
    }

    public async Task<string> ReadFileContentAsync(string fileId, CancellationToken cancellationToken = default)
    {
        await EnsureAccessAsync(fileId, cancellationToken);
        await using var stream = await api.DownloadAsync(fileId, cancellationToken);
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync(cancellationToken);
    }

    public async Task<DriveFile> CreateFileAsync(
        string parentFolderId, string name, string mimeType, string content, CancellationToken cancellationToken = default)
    {
        await EnsureAccessAsync(parentFolderId, cancellationToken);
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));
        return await api.CreateFileAsync(parentFolderId, name, mimeType, stream, cancellationToken);
    }

    public async Task<DriveFile> UpdateFileContentAsync(
        string fileId, string content, CancellationToken cancellationToken = default)
    {
        await EnsureAccessAsync(fileId, cancellationToken);
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));
        return await api.UpdateContentAsync(fileId, stream, cancellationToken);
    }

    public async Task<DriveFile> MoveFileAsync(
        string fileId, string newParentFolderId, CancellationToken cancellationToken = default)
    {
        await EnsureAccessAsync(fileId, cancellationToken);
        await EnsureAccessAsync(newParentFolderId, cancellationToken);
        return await api.MoveAsync(fileId, newParentFolderId, cancellationToken);
    }

    /// <summary>
    /// Throws <see cref="FolderAccessDeniedException"/> unless <paramref name="fileOrFolderId"/> is
    /// itself an allowed folder, or has an allowed folder somewhere in its ancestor chain.
    /// </summary>
    internal async Task EnsureAccessAsync(string fileOrFolderId, CancellationToken cancellationToken)
    {
        if (_options.AllowedFolderIds.Contains(fileOrFolderId))
        {
            return;
        }

        var currentId = fileOrFolderId;
        for (var depth = 0; depth < MaxAncestorDepth; depth++)
        {
            var metadata = await api.GetMetadataAsync(currentId, cancellationToken);
            if (metadata.Parents.Count == 0)
            {
                break;
            }

            foreach (var parentId in metadata.Parents)
            {
                if (_options.AllowedFolderIds.Contains(parentId))
                {
                    return;
                }
            }

            currentId = metadata.Parents[0];
        }

        throw new FolderAccessDeniedException(fileOrFolderId);
    }
}
