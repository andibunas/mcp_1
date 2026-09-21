using McpGoogleDrive.Core.Configuration;
using McpGoogleDrive.Core.Drive;
using Microsoft.Extensions.Options;

namespace McpGoogleDrive.Core.Tests.Drive;

public class DriveFileServiceTests
{
    private const string FolderMimeType = "application/vnd.google-apps.folder";

    private static DriveFileService CreateService(FakeGoogleDriveApi api, params string[] allowedFolderIds)
    {
        var options = Options.Create(new DriveAccessOptions { AllowedFolderIds = [.. allowedFolderIds] });
        return new DriveFileService(api, options);
    }

    [Fact]
    public async Task ListFiles_ForAllowedFolder_Succeeds()
    {
        var api = new FakeGoogleDriveApi()
            .AddFile("folder-1", "Allowed", FolderMimeType)
            .AddFile("file-1", "doc.txt", "text/plain", "folder-1");
        var service = CreateService(api, "folder-1");

        var files = await service.ListFilesAsync("folder-1");

        Assert.Single(files);
        Assert.Equal("file-1", files[0].Id);
    }

    [Fact]
    public async Task ListFiles_ForFolderOutsideAllowList_ThrowsAccessDenied()
    {
        var api = new FakeGoogleDriveApi()
            .AddFile("root", "My Drive", FolderMimeType)
            .AddFile("folder-1", "Not allowed", FolderMimeType, "root");
        var service = CreateService(api, "some-other-folder");

        await Assert.ThrowsAsync<FolderAccessDeniedException>(() => service.ListFilesAsync("folder-1"));
    }

    [Fact]
    public async Task ReadFileContent_ForFileNestedInsideAllowedFolder_WalksParentChainAndSucceeds()
    {
        var api = new FakeGoogleDriveApi()
            .AddFile("allowed-folder", "Allowed", FolderMimeType)
            .AddFile("sub-folder", "Nested", FolderMimeType, "allowed-folder")
            .AddFile("deep-file", "notes.txt", "text/plain", "sub-folder");
        var service = CreateService(api, "allowed-folder");

        var content = await service.ReadFileContentAsync("deep-file");

        Assert.Equal("content of deep-file", content);
    }

    [Fact]
    public async Task ReadFileContent_ForFileOutsideAnyAllowedFolder_ThrowsAccessDenied()
    {
        var api = new FakeGoogleDriveApi()
            .AddFile("other-folder", "Elsewhere", FolderMimeType)
            .AddFile("secret.txt", "secret.txt", "text/plain", "other-folder");
        var service = CreateService(api, "allowed-folder");

        await Assert.ThrowsAsync<FolderAccessDeniedException>(() => service.ReadFileContentAsync("secret.txt"));
    }

    [Fact]
    public async Task MoveFile_ToFolderOutsideAllowList_ThrowsAccessDenied()
    {
        var api = new FakeGoogleDriveApi()
            .AddFile("allowed-folder", "Allowed", FolderMimeType)
            .AddFile("other-folder", "Elsewhere", FolderMimeType)
            .AddFile("file-1", "doc.txt", "text/plain", "allowed-folder");
        var service = CreateService(api, "allowed-folder");

        await Assert.ThrowsAsync<FolderAccessDeniedException>(
            () => service.MoveFileAsync("file-1", "other-folder"));
    }

    [Fact]
    public async Task CreateFile_UnderAllowedParent_Succeeds()
    {
        var api = new FakeGoogleDriveApi().AddFile("allowed-folder", "Allowed", FolderMimeType);
        var service = CreateService(api, "allowed-folder");

        var created = await service.CreateFileAsync("allowed-folder", "new.txt", "text/plain", "hello");

        Assert.Equal("new.txt", created.Name);
    }
}
