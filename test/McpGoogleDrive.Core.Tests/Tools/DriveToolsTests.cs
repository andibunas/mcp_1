using System.Text.Json;
using McpGoogleDrive.Core.Configuration;
using McpGoogleDrive.Core.Drive;
using McpGoogleDrive.Core.Tests.Drive;
using McpGoogleDrive.Core.Tools;
using Microsoft.Extensions.Options;

namespace McpGoogleDrive.Core.Tests.Tools;

public class DriveToolsTests
{
    private const string FolderMimeType = "application/vnd.google-apps.folder";

    private static DriveFileService CreateService(FakeGoogleDriveApi api, params string[] allowedFolderIds)
    {
        var options = Options.Create(new DriveAccessOptions { AllowedFolderIds = [.. allowedFolderIds] });
        return new DriveFileService(api, options);
    }

    [Fact]
    public async Task ListFiles_ReturnsJsonArrayOfFiles()
    {
        var api = new FakeGoogleDriveApi()
            .AddFile("folder-1", "Allowed", FolderMimeType)
            .AddFile("file-1", "doc.txt", "text/plain", "folder-1");
        var service = CreateService(api, "folder-1");

        var json = await DriveTools.ListFiles(service, "folder-1");
        var files = JsonSerializer.Deserialize<List<DriveFile>>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.Single(files!);
        Assert.Equal("doc.txt", files![0].Name);
    }

    [Fact]
    public async Task WriteFile_WithFileId_UpdatesExistingFile()
    {
        var api = new FakeGoogleDriveApi()
            .AddFile("folder-1", "Allowed", FolderMimeType)
            .AddFile("file-1", "doc.txt", "text/plain", "folder-1");
        var service = CreateService(api, "folder-1");

        var json = await DriveTools.WriteFile(service, content: "updated", fileId: "file-1");

        var result = JsonSerializer.Deserialize<DriveFile>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Equal("file-1", result!.Id);
    }

    [Fact]
    public async Task WriteFile_WithoutFileIdOrCreateParams_ThrowsArgumentException()
    {
        var api = new FakeGoogleDriveApi().AddFile("folder-1", "Allowed", FolderMimeType);
        var service = CreateService(api, "folder-1");

        await Assert.ThrowsAsync<ArgumentException>(() => DriveTools.WriteFile(service, content: "x"));
    }

    [Fact]
    public async Task WriteFile_WithCreateParams_CreatesNewFile()
    {
        var api = new FakeGoogleDriveApi().AddFile("folder-1", "Allowed", FolderMimeType);
        var service = CreateService(api, "folder-1");

        var json = await DriveTools.WriteFile(
            service, content: "hello", parentFolderId: "folder-1", name: "new.txt", mimeType: "text/plain");

        var result = JsonSerializer.Deserialize<DriveFile>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Equal("new.txt", result!.Name);
    }

    [Fact]
    public async Task ReadFile_OutsideAllowList_ThrowsFolderAccessDenied()
    {
        var api = new FakeGoogleDriveApi()
            .AddFile("other-folder", "Elsewhere", FolderMimeType)
            .AddFile("secret.txt", "secret.txt", "text/plain", "other-folder");
        var service = CreateService(api, "allowed-folder");

        await Assert.ThrowsAsync<FolderAccessDeniedException>(() => DriveTools.ReadFile(service, "secret.txt"));
    }

    [Fact]
    public async Task MoveFile_ReturnsUpdatedFileWithNewParent()
    {
        var api = new FakeGoogleDriveApi()
            .AddFile("folder-1", "Allowed", FolderMimeType)
            .AddFile("folder-2", "Also allowed", FolderMimeType)
            .AddFile("file-1", "doc.txt", "text/plain", "folder-1");
        var service = CreateService(api, "folder-1", "folder-2");

        var json = await DriveTools.MoveFile(service, "file-1", "folder-2");

        var result = JsonSerializer.Deserialize<DriveFile>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Contains("folder-2", result!.Parents);
    }
}
