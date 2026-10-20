using System.ComponentModel;
using System.IO.Compression;
using System.Net;
using System.Text;
using System.Xml.Linq;
using Google;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Drive.v3;
using Google.Apis.Drive.v3.Data;
using Google.Apis.Services;
using Google.Apis.Upload;
using log4net;
using ModelContextProtocol.Protocol;
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
    private const string DocxMimeType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document";
    private const long MaxTransferBytes = 10 * 1024 * 1024;
    private const string FileFields = "nextPageToken, files(id, name, mimeType, parents, modifiedTime, size)";

    private static readonly Dictionary<string, string> MimeTypeByExtension = new(StringComparer.OrdinalIgnoreCase)
    {
        [".txt"] = "text/plain",
        [".md"] = "text/markdown",
        [".csv"] = "text/csv",
        [".html"] = "text/html",
        [".htm"] = "text/html",
        [".json"] = "application/json",
        [".xml"] = "application/xml",
        [".pdf"] = "application/pdf",
        [".png"] = "image/png",
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".gif"] = "image/gif",
        [".webp"] = "image/webp",
        [".heic"] = "image/heic",
        [".heif"] = "image/heif",
        [".svg"] = "image/svg+xml",
        [".docx"] = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        [".xlsx"] = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        [".pptx"] = "application/vnd.openxmlformats-officedocument.presentationml.presentation",
        [".zip"] = "application/zip",
        [".mp3"] = "audio/mpeg",
        [".mp4"] = "video/mp4",
    };

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
        "Reads a file from Google Drive. Text files are returned as text. Other files (PDF, Word, Excel, images including HEIC, etc.) are returned as base64 " +
        "with their MIME type. Google Docs, Sheets and Slides return text by default (Sheets as CSV); " +
        "set exportAs to get them in another format, such as application/pdf or the Word/Excel MIME type, returned as base64. " +
        "Word (.docx) files are returned as their extracted text unless raw is true. " +
        "Pass a path such as \"/Reports/2026/notes.txt\" or a file ID (no slashes). Files over 10 MB are refused.")]
    public static async Task<CallToolResult> Read(
        IHttpContextAccessor httpContextAccessor,
        [Description("File path (\"/Reports/2026/notes.txt\") or file ID.")] string path,
        [Description("Optional MIME type to export a Google Doc, Sheet or Slides file as, e.g. application/pdf. Only for Google files.")] string? exportAs = null,
        [Description("True to return a .docx as base64 instead of extracted text. Defaults to false.")] bool raw = false)
    {
        Log.Info($"path='{path}' exportAs='{exportAs}'");

        using var drive = CreateDrive(httpContextAccessor, DriveService.Scope.DriveReadonly);
        var file = await ResolveFileAsync(drive, path.Trim());

        if (file.MimeType == FolderMimeType)
        {
            throw new InvalidOperationException($"'{path}' is a folder. Use list to see its contents.");
        }

        byte[] bytes;
        string mimeType;
        if (file.MimeType.StartsWith("application/vnd.google-apps.", StringComparison.Ordinal))
        {
            // Google-native files have no bytes of their own, so they are exported to another format.
            mimeType = exportAs ?? DefaultExportMimeType(file.MimeType)
                ?? throw new InvalidOperationException($"'{path}' is a Google {file.MimeType}; pass exportAs to choose a format.");
            using var buffer = new MemoryStream();
            await drive.Files.Export(file.Id, mimeType).DownloadAsync(buffer);
            bytes = buffer.ToArray();
        }
        else
        {
            if (exportAs is not null)
            {
                throw new InvalidOperationException("exportAs applies only to Google Docs, Sheets and Slides files.");
            }
            if (file.Size is > MaxTransferBytes)
            {
                throw new InvalidOperationException($"'{path}' is {file.Size} bytes; the limit is {MaxTransferBytes} bytes.");
            }
            mimeType = file.MimeType;
            // DownloadAsync requests alt=media. ExecuteAsStreamAsync would return the file's metadata JSON instead.
            using var buffer = new MemoryStream();
            await drive.Files.Get(file.Id).DownloadAsync(buffer);
            bytes = buffer.ToArray();
        }

        if (bytes.Length > MaxTransferBytes)
        {
            throw new InvalidOperationException($"'{path}' is {bytes.Length} bytes; the limit is {MaxTransferBytes} bytes.");
        }

        if (IsTextMimeType(mimeType))
        {
            return TextResult(Encoding.UTF8.GetString(bytes));
        }

        if (mimeType == DocxMimeType && !raw)
        {
            return TextResult(ExtractDocxText(bytes));
        }

        // Everything else, images included, is returned as base64 text with its MIME type. The client decodes it.
        return TextResult($"{file.Name} ({mimeType}, {bytes.Length} bytes), base64:\n{Convert.ToBase64String(bytes)}");
    }

    [McpServerTool(Name = "write"), Description(
        "Writes a file to Google Drive. By default content is text saved as text/plain. For other files (PDF, Word, Excel, images, etc.) set base64 to true " +
        "and pass the file's bytes as base64; mimeType is inferred from the file extension unless given. " +
        "To create a Google Doc, Sheet or Slides file, set convertTo to \"document\", \"spreadsheet\" or \"presentation\" and give the source format in mimeType, " +
        "e.g. text/csv for a Sheet, text/html or a Word MIME type for a Doc, or an Excel MIME type for a Sheet. " +
        "The folder must exist and be shared with Editor access. An existing file with the same name is overwritten; convertTo applies only when creating a new file. " +
        "Content over 10 MB is refused.")]
    public static async Task<string> Write(
        IHttpContextAccessor httpContextAccessor,
        [Description("File path (\"/Reports/2026/notes.txt\"). The file name is the last segment; the folder must exist.")] string path,
        [Description("Text content, or base64 content when base64 is true.")] string content,
        [Description("True when content is base64 (for binary files). Defaults to false.")] bool base64 = false,
        [Description("MIME type of the content, e.g. application/pdf. Defaults to text/plain for text, or is inferred from the extension for base64.")] string? mimeType = null,
        [Description("Google type to convert into: \"document\", \"spreadsheet\" or \"presentation\". Only applies when creating a new file.")] string? convertTo = null)
    {
        Log.Info($"path='{path}' base64={base64} mimeType='{mimeType}' convertTo='{convertTo}'");

        var (folder, name) = SplitPath(path);
        if (folder.Length == 0)
        {
            throw new InvalidOperationException("Pass a path inside a folder, such as \"/Reports/notes.txt\". The top level cannot hold files.");
        }

        byte[] bytes;
        try
        {
            bytes = base64 ? Convert.FromBase64String(content) : Encoding.UTF8.GetBytes(content);
        }
        catch (FormatException)
        {
            throw new InvalidOperationException("content is not valid base64.");
        }
        if (bytes.Length > MaxTransferBytes)
        {
            throw new InvalidOperationException($"Content is {bytes.Length} bytes; the limit is {MaxTransferBytes} bytes.");
        }

        var uploadMimeType = mimeType ?? (base64 ? InferMimeType(name) : "text/plain");
        var convertedMimeType = convertTo is null ? null : GoogleTypeFor(convertTo);

        using var drive = CreateDrive(httpContextAccessor, DriveService.Scope.Drive);
        var parentId = await ResolveFolderIdAsync(drive, folder);

        var existing = await QueryAsync(drive,
            $"name = '{EscapeQuery(name)}' and '{parentId}' in parents and mimeType != '{FolderMimeType}' and trashed = false");
        if (existing.Count > 1)
        {
            throw new InvalidOperationException($"More than one file named '{name}' exists in '{folder}'. Remove the duplicates first.");
        }

        using var stream = new MemoryStream(bytes);
        IUploadProgress progress;
        string action;
        if (existing.Count == 1)
        {
            if (convertedMimeType is not null)
            {
                throw new InvalidOperationException($"'{name}' already exists; convertTo applies only when creating a new file.");
            }
            var update = drive.Files.Update(new DriveFile(), existing[0].Id, stream, uploadMimeType);
            update.SupportsAllDrives = true;
            progress = await update.UploadAsync();
            action = "Overwrote";
        }
        else
        {
            var body = new DriveFile { Name = name, Parents = new List<string> { parentId }, MimeType = convertedMimeType };
            var create = drive.Files.Create(body, stream, uploadMimeType);
            create.SupportsAllDrives = true;
            progress = await create.UploadAsync();
            action = convertedMimeType is null ? "Created" : $"Created (converted to {convertTo})";
        }

        if (progress.Status == UploadStatus.Failed)
        {
            throw new InvalidOperationException($"Upload failed: {progress.Exception?.Message}");
        }

        return $"{action} '{path.Trim()}' ({bytes.Length} bytes).";
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

    private static string? DefaultExportMimeType(string googleMimeType) => googleMimeType switch
    {
        DocMimeType or SlidesMimeType => "text/plain",
        SheetMimeType => "text/csv",
        _ => null
    };

    private static string GoogleTypeFor(string convertTo) => convertTo.ToLowerInvariant() switch
    {
        "document" => DocMimeType,
        "spreadsheet" => SheetMimeType,
        "presentation" => SlidesMimeType,
        _ => throw new InvalidOperationException("convertTo must be \"document\", \"spreadsheet\" or \"presentation\".")
    };

    private static string InferMimeType(string name) =>
        MimeTypeByExtension.TryGetValue(Path.GetExtension(name), out var mimeType) ? mimeType : "application/octet-stream";

    private static bool IsTextMimeType(string mimeType) =>
        mimeType.StartsWith("text/", StringComparison.Ordinal) ||
        mimeType is "application/json" or "application/xml" or "application/javascript" or "application/yaml";

    private static string ExtractDocxText(byte[] bytes)
    {
        using var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        var entry = zip.GetEntry("word/document.xml")
            ?? throw new InvalidOperationException("The file is not a valid .docx (word/document.xml is missing).");

        using var xmlStream = entry.Open();
        var document = XDocument.Load(xmlStream);
        XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

        var paragraphs = document.Descendants(w + "p").Select(p => string.Concat(p.Descendants().Select(e =>
            e.Name == w + "t" ? e.Value :
            e.Name == w + "tab" ? "\t" :
            e.Name == w + "br" ? "\n" : "")));
        return string.Join("\n", paragraphs);
    }

    private static CallToolResult TextResult(string text) =>
        new() { Content = [new TextContentBlock { Text = text }] };

    private static async Task<string> ResolveFolderIdAsync(DriveService drive, string folder)
    {
        string? parentId = null;
        var segments = folder.Split('/', StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < segments.Length; i++)
        {
            var segment = segments[i];

            // The first segment may be a folder ID. Anything that is not a folder the account can see falls back to a name lookup.
            if (i == 0 && await TryGetFolderIdAsync(drive, segment) is { } folderId)
            {
                parentId = folderId;
                continue;
            }

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

        return parentId ?? throw new InvalidOperationException($"'{folder}' does not name a folder.");
    }

    private static async Task<string?> TryGetFolderIdAsync(DriveService drive, string id)
    {
        try
        {
            var request = drive.Files.Get(id);
            request.SupportsAllDrives = true;
            var file = await request.ExecuteAsync();
            return file.MimeType == FolderMimeType ? file.Id : null;
        }
        catch (GoogleApiException ex) when (ex.HttpStatusCode is HttpStatusCode.NotFound or HttpStatusCode.BadRequest)
        {
            return null;
        }
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
