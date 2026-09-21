namespace McpGoogleDrive.Core.Drive;

public sealed record DriveFile(
    string Id,
    string Name,
    string MimeType,
    IReadOnlyList<string> Parents,
    bool IsFolder);
