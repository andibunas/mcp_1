using System.ComponentModel;
using System.Text;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Drive.v3;
using Google.Apis.Drive.v3.Data;
using Google.Apis.Services;
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
    private const string FolderMimeType = "application/vnd.google-apps.folder";
    private const string FileFields = "nextPageToken, files(id, name, mimeType, parents, modifiedTime, size)";

    [McpServerTool(Name = "list"), Description(
        "Lists the Google Drive folders and files shared with the service account. " +
        "Leave folder empty or \"/\" for the top level. " +
        "Pass a path such as \"/Reports/2026\" to list inside a folder by name, or a folder ID (no slashes) to list inside that folder.")]
    public static async Task<string> List(
        IHttpContextAccessor httpContextAccessor,
        ILoggerFactory loggerFactory,
        [Description("Folder path (\"/Reports/2026\") or folder ID. Empty or \"/\" means the top level.")] string? folder = null)
    {
        var logger = loggerFactory.CreateLogger("WebMCP.DriveTools");
        logger.LogInformation("[DriveTools.List] folder='{Folder}'", folder);

        var json = httpContextAccessor.HttpContext?.Items[OAuthSettings.CredentialItemKey] as string
            ?? throw new InvalidOperationException("No service account credential is attached to this request.");

        var credential = GoogleCredential.FromJson(json).CreateScoped(DriveService.Scope.DriveReadonly);
        using var drive = new DriveService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = "WebMCP"
        });

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
