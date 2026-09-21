using System.ComponentModel;
using System.Text.Json;
using McpGoogleDrive.Core.Drive;
using ModelContextProtocol.Server;

namespace McpGoogleDrive.Core.Tools;

/// <summary>
/// MCP tools exposing <see cref="DriveFileService"/> to the calling model. Deliberately
/// transport-agnostic — no stdio/HTTP-specific code — so the same tools work under any host.
///
/// <see cref="DriveFileService"/> already enforces the folder allow-list on every call; a denied
/// access throws <see cref="FolderAccessDeniedException"/>, whose message is human-readable on its
/// own. The MCP SDK's default tool invocation catches exceptions from these methods and surfaces
/// the message as an error tool result (not a stack trace), so no extra try/catch is needed here.
/// </summary>
[McpServerToolType]
public static class DriveTools
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [McpServerTool(Name = "drive_list_files")]
    [Description(
        "Lists files inside a Google Drive folder. The folder must be one of the folders " +
        "granted during setup, or a subfolder of one — folders outside that allow-list are not reachable.")]
    public static async Task<string> ListFiles(
        DriveFileService driveFiles,
        [Description("ID of the folder to list.")] string folderId,
        [Description("Optional: only return files whose name contains this substring.")] string? nameContains = null,
        CancellationToken cancellationToken = default)
    {
        var files = await driveFiles.ListFilesAsync(folderId, nameContains, cancellationToken);
        return JsonSerializer.Serialize(files, JsonOptions);
    }

    [McpServerTool(Name = "drive_read_file")]
    [Description("Reads the text content of a file inside an allowed Drive folder.")]
    public static Task<string> ReadFile(
        DriveFileService driveFiles,
        [Description("ID of the file to read.")] string fileId,
        CancellationToken cancellationToken = default) =>
        driveFiles.ReadFileContentAsync(fileId, cancellationToken);

    [McpServerTool(Name = "drive_write_file")]
    [Description(
        "Creates a new file in an allowed Drive folder, or overwrites the content of an existing " +
        "allowed file when fileId is given. To create a new file, pass parentFolderId, name, and mimeType " +
        "instead of fileId.")]
    public static async Task<string> WriteFile(
        DriveFileService driveFiles,
        [Description("Text content to write.")] string content,
        [Description("ID of an existing file to overwrite. Omit this to create a new file instead.")] string? fileId = null,
        [Description("Required to create a file: ID of the parent folder (must be an allowed folder).")] string? parentFolderId = null,
        [Description("Required to create a file: the new file's name.")] string? name = null,
        [Description("Required to create a file: MIME type, e.g. text/plain.")] string? mimeType = null,
        CancellationToken cancellationToken = default)
    {
        DriveFile result;
        if (!string.IsNullOrWhiteSpace(fileId))
        {
            result = await driveFiles.UpdateFileContentAsync(fileId, content, cancellationToken);
        }
        else if (!string.IsNullOrWhiteSpace(parentFolderId) && !string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(mimeType))
        {
            result = await driveFiles.CreateFileAsync(parentFolderId, name, mimeType, content, cancellationToken);
        }
        else
        {
            throw new ArgumentException(
                "Provide fileId to overwrite an existing file, or parentFolderId, name, and mimeType together to create a new one.");
        }

        return JsonSerializer.Serialize(result, JsonOptions);
    }

    [McpServerTool(Name = "drive_move_file")]
    [Description("Moves a file to a different folder. Both the file and the destination folder must be inside allowed folders.")]
    public static async Task<string> MoveFile(
        DriveFileService driveFiles,
        [Description("ID of the file to move.")] string fileId,
        [Description("ID of the destination folder.")] string newParentFolderId,
        CancellationToken cancellationToken = default)
    {
        var result = await driveFiles.MoveFileAsync(fileId, newParentFolderId, cancellationToken);
        return JsonSerializer.Serialize(result, JsonOptions);
    }
}
