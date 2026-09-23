using TestServiceAccount;

Console.WriteLine("Google Drive Service Account Test\n");

var keyPath = args.Length > 0 ? args[0] : "service-account-key.json";

if (!File.Exists(keyPath))
{
    Console.Error.WriteLine($"Error: Service account key file not found at: {keyPath}");
    Console.Error.WriteLine("Usage: dotnet run <path-to-service-account-key.json>");
    Environment.Exit(1);
}

try
{
    var lister = new GoogleDriveFolderLister(keyPath);
    await lister.ListFoldersAsync();
    Console.WriteLine("✓ Successfully retrieved folders from Google Drive");
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Error: {ex.Message}");
    Environment.Exit(1);
}
