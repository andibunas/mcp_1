using System.ComponentModel;
using System.Text;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Drive.v3;
using Google.Apis.Drive.v3.Data;
using Google.Apis.Services;
using Google.Apis.Upload;
using log4net;
using ModelContextProtocol.Server;
using WebMCP.OAuth;
using DriveFile = Google.Apis.Drive.v3.Data.File;

namespace WebMCP;

/// <summary>
/// Google Drive tools. Each call uses the service account key that BearerAuthMiddleware
/// unsealed from the caller's access token, so only folders shared with that account are visible.
/// </summary>
[McpServerToolType]
public static class DriveTools
{
    private static readonly ILog Log = LogManager.GetLogger(typeof(DriveTools));

    private const string FolderMimeType = "application/vnd.google-apps.folder";
    private const string DocMimeType = "application/vnd.google-apps.document";
    private const string SheetMimeType = "application/vnd.google-apps.spreadsheet";
    private const string SlidesMimeType = "application/vnd.google-apps.presentation";
    private const long MaxReadBytes = 1024 * 1024;
    private const string FileFields = "nextPageToken, files(id, name, mimeType, parents, modifiedTime, size)";

    [McpServerTool(Name = "list"), Description(
        "Lists the Google Drive folders and files shared with the service account. " +
        "Leave folder empty or \"/\" for the top level. " +
        "Pass a path such as \"/Reports/2026\" to list inside a folder by name, or a folder ID (no slashes) to list inside that folder.")]
    public static async Task<string> List(
        IHttpContextAccessor httpContextAccessor,
        [Description("Folder path (\"/Reports/2026\") or folder ID. Empty or \"/\" means the top level.")] string? folder = null)
    {
        Log.Info($"folder='{folder}'");

        using var drive = CreateDrive(httpContextAccessor, DriveService.Scope.DriveReadonly);

        var trimmed = folder?.Trim();
        var isRoot = string.IsNullOrEmpty(trimmed) || trimmed == "/";

        List<DriveFile> items;
        string label;
        if (isRoot)
        {
            // A service account has no "My Drive" of its own, so the top level is inferred:
            // anything it can see whose parent it cannot see.
            var all = await QueryAsync(drive, "trashed = false");
            var visibleIds = all.Select(f => f.Id).ToHashSet();
            items = all.Where(f => f.Parents is null || !f.Parents.Any(visibleIds.Contains)).ToList();
            label = "/";
        }
        else
        {
            var folderId = await ResolveFolderIdAsync(drive, trimmed!);
            items = await QueryAsync(drive, $"'{folderId}' in parents and trashed = false");
            label = trimmed!;
        }

        return Format(label, items);
    }

    [McpServerTool(Name = "read"), Description(
        "Reads a text file from Google Drive and returns its contents. " +
        "Pass a path such as \"/Reports/2026/notes.txt\" or a file ID (no slashes). " +
        "Google Docs are returned as plain text, Sheets as CSV. Binary files and files over 1 MB are refused.")]
    public static async Task<string> Read(
        IHttpContextAccessor httpContextAccessor,
        [Description("File path (\"/Reports/2026/notes.txt\") or file ID.")] string path)
    {
        Log.Info($"path='{path}'");

        using var drive = CreateDrive(httpContextAccessor, DriveService.Scope.DriveReadonly);
        var file = await ResolveFileAsync(drive, path.Trim());

        // Google-native files have no bytes of their own, so they are exported to a text format.
        string? exportMimeType = file.MimeType switch
        {
            DocMimeType or SlidesMimeType => "text/plain",
            SheetMimeType => "text/csv",
            _ => null
        };
        if (exportMimeType is null)
        {
            if (file.MimeType.StartsWith("application/vnd.google-apps.", StringComparison.Ordinal))
                throw new InvalidOperationException($"'{path}' is a Google {file.MimeType} and cannot be read as text.");
            if (!IsTextMimeType(file.MimeType))
                throw new InvalidOperationException($"'{path}' is not a text file ({file.MimeType}).");
        }

        if (file.Size is > MaxReadBytes)
        {
            throw new InvalidOperationException($"'{path}' is {file.Size} bytes; the read limit is {MaxReadBytes} bytes.");
        }

        using var stream = exportMimeType is null
            ? await drive.Files.Get(file.Id).ExecuteAsStreamAsync()
            : await drive.Files.Export(file.Id, exportMimeType).ExecuteAsStreamAsync();
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer);
        if (buffer.Length > MaxReadBytes)
        {
            throw new InvalidOperationException($"'{path}' is larger than the read limit of {MaxReadBytes} bytes.");
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    [McpServerTool(Name = "write"), Description(
        "Writes a text file to Google Drive. Pass a path such as \"/Reports/2026/notes.txt\"; the folder must already exist and be shared " +
        "with the service account with Editor access. If a file with that name already exists in the folder it is overwritten, otherwise it is created.")]
    public static async Task<string> Write(
        IHttpContextAccessor httpContextAccessor,
        [Description("File path (\"/Reports/2026/notes.txt\"). The file name is the last segment; the folder must exist.")] string path,
        [Description("Text to write to the file.")] string content)
    {
        Log.Info($"path='{path}' bytes={Encoding.UTF8.GetByteCount(content)}");

        var (folder, name) = SplitPath(path);
        if (folder.Length == 0)
        {
            throw new InvalidOperationException("Pass a path inside a folder, such as \"/Reports/notes.txt\". The top level cannot hold files.");
        }

        using var drive = CreateDrive(httpContextAccessor, DriveService.Scope.Drive);
        var parentId = await ResolveFolderIdAsync(drive, folder);

        var existing = await QueryAsync(drive,
            $"name = '{EscapeQuery(name)}' and '{parentId}' in parents and mimeType != '{FolderMimeType}' and trashed = false");
        if (existing.Count > 1)
        {
            throw new InvalidOperationException($"More than one file named '{name}' exists in '{folder}'. Remove the duplicates first.");
        }

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));
        IUploadProgress progress;
        string action;
        if (existing.Count == 1)
        {
            var update = drive.Files.Update(new DriveFile(), existing[0].Id, stream, "text/plain");
            update.SupportsAllDrives = true;
            progress = await update.UploadAsync();
            action = "Overwrote";
        }
        else
        {
            var create = drive.Files.Create(new DriveFile { Name = name, Parents = new List<string> { parentId } }, stream, "text/plain");
            create.SupportsAllDrives = true;
            progress = await create.UploadAsync();
            action = "Created";
        }

        if (progress.Status == UploadStatus.Failed)
        {
            throw new InvalidOperationException($"Upload failed: {progress.Exception?.Message}");
        }

        return $"{action} '{path.Trim()}' ({Encoding.UTF8.GetByteCount(content)} bytes).";
    }

    [McpServerTool(Name = "create_folder"), Description(
        "Creates a folder in Google Drive. Pass a path such as \"/Reports/2026\"; the parent folder must already exist and be shared " +
        "with the service account with Editor access. A single name such as \"/Reports\" creates it at the top level.")]
    public static async Task<string> CreateFolder(
        IHttpContextAccessor httpContextAccessor,
        [Description("Folder path to create (\"/Reports/2026\").")] string path)
    {
        Log.Info($"path='{path}'");

        var (parent, name) = SplitPath(path);
        using var drive = CreateDrive(httpContextAccessor, DriveService.Scope.Drive);

        var body = new DriveFile { Name = name, MimeType = FolderMimeType };
        if (parent.Length > 0)
        {
            var parentId = await ResolveFolderIdAsync(drive, parent);
            var existing = await QueryAsync(drive,
                $"name = '{EscapeQuery(name)}' and '{parentId}' in parents and mimeType = '{FolderMimeType}' and trashed = false");
            if (existing.Count > 0)
            {
                throw new InvalidOperationException($"A folder named '{name}' already exists in '{parent}'.");
            }
            body.Parents = new List<string> { parentId };
        }

        var create = drive.Files.Create(body);
        create.Fields = "id";
        create.SupportsAllDrives = true;
        var created = await create.ExecuteAsync();
        return $"Created folder '{path.Trim()}' (id: {created.Id}).";
    }

    [McpServerTool(Name = "move"), Description(
        "Renames or moves a file or folder in Google Drive. Pass the current path or ID, and the new path. " +
        "The last segment of the new path is the new name. The destination folder must already exist and cannot be the top level. " +
        "Use the same folder in both paths to rename only.")]
    public static async Task<string> Move(
        IHttpContextAccessor httpContextAccessor,
        [Description("Current path (\"/Reports/old.txt\") or file/folder ID.")] string path,
        [Description("New path (\"/Archive/2026/notes.txt\"). The last segment is the new name.")] string newPath)
    {
        Log.Info($"path='{path}' newPath='{newPath}'");

        using var drive = CreateDrive(httpContextAccessor, DriveService.Scope.Drive);
        var item = await ResolveFileAsync(drive, path.Trim());

        var (destFolder, newName) = SplitPath(newPath);
        if (destFolder.Length == 0)
        {
            throw new InvalidOperationException("The new path must be inside a folder, such as \"/Archive/notes.txt\".");
        }
        var destId = await ResolveFolderIdAsync(drive, destFolder);

        var update = drive.Files.Update(new DriveFile { Name = newName }, item.Id);
        update.AddParents = destId;
        var oldParents = string.Join(",", (item.Parents ?? new List<string>()).Where(p => p != destId));
        update.RemoveParents = oldParents.Length > 0 ? oldParents : null;
        update.Fields = "id";
        update.SupportsAllDrives = true;
        await update.ExecuteAsync();

        return $"Moved '{path.Trim()}' to '{newPath.Trim()}'.";
    }

    [McpServerTool(Name = "search"), Description(
        "Searches the Google Drive folders and files shared with the service account, matching file names and text contents. " +
        "Returns each match with its ID.")]
    public static async Task<string> Search(
        IHttpContextAccessor httpContextAccessor,
        [Description("Text to look for in file names and contents.")] string text)
    {
        Log.Info($"text='{text}'");

        var term = text.Trim();
        if (term.Length == 0)
        {
            throw new InvalidOperationException("Pass some text to search for.");
        }

        using var drive = CreateDrive(httpContextAccessor, DriveService.Scope.DriveReadonly);
        var escaped = EscapeQuery(term);
        var items = await QueryAsync(drive, $"(name contains '{escaped}' or fullText contains '{escaped}') and trashed = false");
        return Format($"search '{term}'", items);
    }

    [McpServerTool(Name = "metadata"), Description(
        "Returns the details of a file or folder without its contents: ID, type, size, parent folder IDs, created and modified times, and a link. " +
        "Pass a path such as \"/Reports/2026/notes.txt\" or a file/folder ID.")]
    public static async Task<string> Metadata(
        IHttpContextAccessor httpContextAccessor,
        [Description("File or folder path, or file/folder ID.")] string path)
    {
        Log.Info($"path='{path}'");

        using var drive = CreateDrive(httpContextAccessor, DriveService.Scope.DriveReadonly);
        var item = await ResolveFileAsync(drive, path.Trim());

        var get = drive.Files.Get(item.Id);
        get.Fields = "id, name, mimeType, size, parents, createdTime, modifiedTime, webViewLink";
        var full = await get.ExecuteAsync();

        var sb = new StringBuilder();
        sb.AppendLine($"Name: {full.Name}");
        sb.AppendLine($"ID: {full.Id}");
        sb.AppendLine($"Type: {full.MimeType}");
        sb.AppendLine($"Size: {(full.Size is null ? "-" : $"{full.Size} bytes")}");
        sb.AppendLine($"Parent IDs: {(full.Parents is null || full.Parents.Count == 0 ? "-" : string.Join(", ", full.Parents))}");
        sb.AppendLine($"Created: {full.CreatedTimeDateTimeOffset?.ToString("yyyy-MM-dd HH:mm") ?? "-"}");
        sb.AppendLine($"Modified: {full.ModifiedTimeDateTimeOffset?.ToString("yyyy-MM-dd HH:mm") ?? "-"}");
        sb.Append($"Link: {full.WebViewLink ?? "-"}");
        return sb.ToString();
    }

    private static (string Parent, string Name) SplitPath(string path)
    {
        var trimmed = path.Trim().TrimEnd('/');
        var slash = trimmed.LastIndexOf('/');
        var name = slash < 0 ? trimmed : trimmed[(slash + 1)..];
        if (string.IsNullOrEmpty(name))
        {
            throw new InvalidOperationException($"'{path}' does not name a file or folder.");
        }
        var parent = slash <= 0 ? "" : trimmed[..slash];
        return (parent, name);
    }

    private static DriveService CreateDrive(IHttpContextAccessor httpContextAccessor, string scope)
    {
        var json = httpContextAccessor.HttpContext?.Items[OAuthSettings.CredentialItemKey] as string
            ?? throw new InvalidOperationException("No service account credential is attached to this request.");

        var credential = GoogleCredential.FromJson(json).CreateScoped(scope);
        return new DriveService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = "WebMCP"
        });
    }

    private static async Task<DriveFile> ResolveFileAsync(DriveService drive, string path)
    {
        if (!path.Contains('/'))
        {
            var getById = drive.Files.Get(path);
            getById.Fields = "id, name, mimeType, size, parents";
            return await getById.ExecuteAsync();
        }

        var slash = path.LastIndexOf('/');
        var name = path[(slash + 1)..];
        var folder = path[..slash];
        if (string.IsNullOrEmpty(name))
        {
            throw new InvalidOperationException($"'{path}' does not name a file.");
        }

        var q = $"name = '{EscapeQuery(name)}' and trashed = false";
        if (folder.Length > 0)
        {
            q += $" and '{await ResolveFolderIdAsync(drive, folder)}' in parents";
        }

        var matches = await QueryAsync(drive, q);
        if (matches.Count == 0)
        {
            throw new InvalidOperationException($"File '{name}' not found in '{path}'.");
        }
        if (matches.Count > 1)
        {
            throw new InvalidOperationException($"More than one file named '{name}' is visible. Pass its file ID instead.");
        }
        return matches[0];
    }

    private static bool IsTextMimeType(string mimeType) =>
        mimeType.StartsWith("text/", StringComparison.Ordinal) ||
        mimeType is "application/json" or "application/xml" or "application/javascript" or "application/yaml";

    private static async Task<string> ResolveFolderIdAsync(DriveService drive, string folder)
    {
        if (!folder.Contains('/'))
        {
            var folderById = await drive.Files.Get(folder).ExecuteAsync();
            if (folderById.MimeType != FolderMimeType)
            {
                throw new InvalidOperationException($"'{folder}' is not a folder.");
            }
            return folder;
        }

        string? parentId = null;
        foreach (var segment in folder.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            var q = $"name = '{EscapeQuery(segment)}' and mimeType = '{FolderMimeType}' and trashed = false";
            if (parentId is not null)
            {
                q += $" and '{parentId}' in parents";
            }

            var matches = await QueryAsync(drive, q);
            if (matches.Count == 0)
            {
                throw new InvalidOperationException($"Folder '{segment}' not found in '{folder}'.");
            }
            if (matches.Count > 1)
            {
                throw new InvalidOperationException($"More than one folder named '{segment}' is visible. Pass its folder ID instead.");
            }
            parentId = matches[0].Id;
        }

        return parentId!;
    }

    private static async Task<List<DriveFile>> QueryAsync(DriveService drive, string q)
    {
        var files = new List<DriveFile>();
        string? pageToken = null;
        do
        {
            var request = drive.Files.List();
            request.Q = q;
            request.Fields = FileFields;
            request.PageSize = 1000;
            request.PageToken = pageToken;
            request.IncludeItemsFromAllDrives = true;
            request.SupportsAllDrives = true;

            var response = await request.ExecuteAsync();
            if (response.Files is not null)
            {
                files.AddRange(response.Files);
            }
            pageToken = response.NextPageToken;
        } while (pageToken is not null);

        return files;
    }

    private static string Format(string label, List<DriveFile> items)
    {
        if (items.Count == 0)
        {
            return $"No folders or files found in {label}.";
        }

        var sb = new StringBuilder();
        sb.AppendLine($"{items.Count} item(s) in {label}:");
        foreach (var item in items.OrderBy(i => i.MimeType != FolderMimeType).ThenBy(i => i.Name, StringComparer.OrdinalIgnoreCase))
        {
            var kind = item.MimeType == FolderMimeType ? "[folder]" : "[file]  ";
            var modified = item.ModifiedTimeDateTimeOffset?.ToString("yyyy-MM-dd HH:mm") ?? "-";
            sb.AppendLine($"{kind} {item.Name}  (id: {item.Id}, modified: {modified})");
        }
        return sb.ToString();
    }

    private static string EscapeQuery(string value) =>
        value.Replace("\\", "\\\\").Replace("'", "\\'");
}
