using System.Text.Json;
using McpGoogleDrive.Core.Auth;
using McpGoogleDrive.Core.Configuration;
using Microsoft.Extensions.Options;

namespace McpGoogleDrive.Core.Setup;

/// <summary>
/// Runs the full interactive grant flow: OAuth sign-in, then the Picker-based folder chooser,
/// then writes the selected folder IDs to disk so <see cref="ConfigurationBuilderExtensions.AddAllowedFoldersFile"/>
/// can load them back into <see cref="DriveAccessOptions.AllowedFolderIds"/> on future runs.
/// </summary>
public sealed class InteractiveSetupService(
    GoogleAuthService authService,
    DriveFolderPickerService pickerService,
    IOptions<DriveAccessOptions> driveOptions,
    string? allowedFoldersPath = null)
{
    private readonly DriveAccessOptions _driveOptions = driveOptions.Value;
    private readonly string _allowedFoldersPath = allowedFoldersPath ?? DefaultAllowedFoldersPath;

    public static string DefaultAllowedFoldersPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".mcp-google-drive",
        "allowed-folders.json");

    public async Task<IReadOnlyList<PickedFolder>> RunAsync(CancellationToken cancellationToken = default)
    {
        var credential = await authService.AuthorizeAsync(_driveOptions.Scopes, cancellationToken);
        var accessToken = await credential.GetAccessTokenForRequestAsync(cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("Google sign-in did not return an access token.");

        var folders = await pickerService.PickFoldersAsync(accessToken, cancellationToken);

        Directory.CreateDirectory(Path.GetDirectoryName(_allowedFoldersPath)!);
        var json = JsonSerializer.Serialize(folders, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(_allowedFoldersPath, json, cancellationToken);

        return folders;
    }
}
