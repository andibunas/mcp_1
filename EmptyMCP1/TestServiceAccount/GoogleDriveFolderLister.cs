using Google.Apis.Auth.OAuth2;
using Google.Apis.Drive.v3;
using Google.Apis.Services;

namespace TestServiceAccount;

public class GoogleDriveFolderLister
{
    private readonly string _serviceAccountKeyPath;

    public GoogleDriveFolderLister(string serviceAccountKeyPath)
    {
        _serviceAccountKeyPath = serviceAccountKeyPath;
    }

    public async Task ListFoldersAsync()
    {
        var credential = GoogleCredential.FromFile(_serviceAccountKeyPath)
            .CreateScoped(DriveService.Scope.Drive);

        using var service = new DriveService(new BaseClientService.Initializer()
        {
            HttpClientInitializer = credential,
            ApplicationName = "TestServiceAccount"
        });

        var request = service.Files.List();
        request.Q = "mimeType = 'application/vnd.google-apps.folder' and trashed = false";
        request.PageSize = 50;
        request.Fields = "files(id, name, createdTime, modifiedTime, owners)";

        var result = await request.ExecuteAsync();

        Console.WriteLine($"Found {result.Files?.Count ?? 0} folders:\n");

        if (result.Files != null && result.Files.Count > 0)
        {
            foreach (var folder in result.Files)
            {
                Console.WriteLine($"Name: {folder.Name}");
                Console.WriteLine($"  ID: {folder.Id}");
                Console.WriteLine($"  Created: {folder.CreatedTimeDateTimeOffset}");
                Console.WriteLine($"  Modified: {folder.ModifiedTimeDateTimeOffset}");
                if (folder.Owners?.Count > 0)
                {
                    Console.WriteLine($"  Owner: {folder.Owners[0].DisplayName}");
                }
                Console.WriteLine();
            }
        }
        else
        {
            Console.WriteLine("No folders found.");
        }
    }
}
