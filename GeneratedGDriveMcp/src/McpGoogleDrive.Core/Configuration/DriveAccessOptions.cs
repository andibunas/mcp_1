namespace McpGoogleDrive.Core.Configuration;

public sealed class DriveAccessOptions
{
    public const string SectionName = "DriveAccess";

    /// <summary>
    /// Drive folder IDs the server is allowed to read/write. Every operation is checked
    /// against this list (including walking up the parent chain) before it reaches the Drive API.
    /// </summary>
    public List<string> AllowedFolderIds { get; set; } = [];

    public List<string> Scopes { get; set; } = ["https://www.googleapis.com/auth/drive.file"];
}
