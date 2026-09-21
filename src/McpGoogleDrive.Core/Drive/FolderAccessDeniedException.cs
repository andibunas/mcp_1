namespace McpGoogleDrive.Core.Drive;

public sealed class FolderAccessDeniedException(string fileOrFolderId)
    : Exception($"Access to '{fileOrFolderId}' is denied: it is not inside any allowed folder.")
{
    public string FileOrFolderId { get; } = fileOrFolderId;
}
