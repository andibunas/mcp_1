using Google.Apis.Drive.v3;
using Google.Apis.Http;
using Google.Apis.Services;
using Google.Apis.Upload;
using GoogleData = Google.Apis.Drive.v3.Data;

namespace McpGoogleDrive.Core.Drive;

/// <summary>
/// Real <see cref="IGoogleDriveApi"/> implementation, backed by <see cref="DriveService"/>.
/// </summary>
public sealed class GoogleDriveApi : IGoogleDriveApi, IDisposable
{
    private const string FolderMimeType = "application/vnd.google-apps.folder";
    private const string FileFields = "id, name, mimeType, parents";

    private readonly DriveService _service;

    public GoogleDriveApi(IConfigurableHttpClientInitializer credential, string applicationName)
    {
        _service = new DriveService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = applicationName,
        });
    }

    public async Task<DriveFile> GetMetadataAsync(string fileId, CancellationToken cancellationToken)
    {
        var request = _service.Files.Get(fileId);
        request.Fields = FileFields;
        return Map(await request.ExecuteAsync(cancellationToken));
    }

    public async Task<IReadOnlyList<DriveFile>> ListChildrenAsync(string folderId, string? nameContains, CancellationToken cancellationToken)
    {
        var query = $"'{folderId}' in parents and trashed = false";
        if (!string.IsNullOrWhiteSpace(nameContains))
        {
            query += $" and name contains '{EscapeQueryValue(nameContains)}'";
        }

        var request = _service.Files.List();
        request.Q = query;
        request.Fields = $"files({FileFields})";
        var result = await request.ExecuteAsync(cancellationToken);
        return result.Files.Select(Map).ToList();
    }

    public async Task<Stream> DownloadAsync(string fileId, CancellationToken cancellationToken)
    {
        var stream = new MemoryStream();
        await _service.Files.Get(fileId).DownloadAsync(stream, cancellationToken);
        stream.Position = 0;
        return stream;
    }

    public async Task<DriveFile> CreateFileAsync(string parentFolderId, string name, string mimeType, Stream content, CancellationToken cancellationToken)
    {
        var metadata = new GoogleData.File
        {
            Name = name,
            MimeType = mimeType,
            Parents = [parentFolderId],
        };

        var request = _service.Files.Create(metadata, content, mimeType);
        request.Fields = FileFields;
        var progress = await request.UploadAsync(cancellationToken);
        if (progress.Status != UploadStatus.Completed)
        {
            throw new IOException($"Failed to upload file '{name}': {progress.Exception?.Message}", progress.Exception);
        }

        return Map(request.ResponseBody);
    }

    public async Task<DriveFile> UpdateContentAsync(string fileId, Stream content, CancellationToken cancellationToken)
    {
        var existing = await GetMetadataAsync(fileId, cancellationToken);
        var request = _service.Files.Update(new GoogleData.File(), fileId, content, existing.MimeType);
        request.Fields = FileFields;
        var progress = await request.UploadAsync(cancellationToken);
        if (progress.Status != UploadStatus.Completed)
        {
            throw new IOException($"Failed to update file '{fileId}': {progress.Exception?.Message}", progress.Exception);
        }

        return Map(request.ResponseBody);
    }

    public async Task<DriveFile> MoveAsync(string fileId, string newParentFolderId, CancellationToken cancellationToken)
    {
        var existing = await GetMetadataAsync(fileId, cancellationToken);
        var request = _service.Files.Update(new GoogleData.File(), fileId);
        request.AddParents = newParentFolderId;
        request.RemoveParents = string.Join(',', existing.Parents);
        request.Fields = FileFields;
        return Map(await request.ExecuteAsync(cancellationToken));
    }

    private static DriveFile Map(GoogleData.File file) => new(
        file.Id,
        file.Name,
        file.MimeType,
        file.Parents?.ToList() ?? [],
        file.MimeType == FolderMimeType);

    private static string EscapeQueryValue(string value) => value.Replace("\\", "\\\\").Replace("'", "\\'");

    public void Dispose() => _service.Dispose();
}
