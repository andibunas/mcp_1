using System.Text.Json;
using McpGoogleDrive.Core.Setup;
using Microsoft.Extensions.Configuration;

namespace McpGoogleDrive.Core.Configuration;

public static class ConfigurationBuilderExtensions
{
    /// <summary>
    /// Layers the folder IDs saved by <see cref="InteractiveSetupService"/> (if any) on top of
    /// whatever config the host already built, as <c>DriveAccess:AllowedFolderIds</c> entries.
    /// A no-op if the setup command hasn't been run yet — hosts can call this unconditionally.
    /// </summary>
    public static IConfigurationBuilder AddAllowedFoldersFile(this IConfigurationBuilder builder, string? path = null)
    {
        path ??= InteractiveSetupService.DefaultAllowedFoldersPath;
        if (!File.Exists(path))
        {
            return builder;
        }

        var folders = JsonSerializer.Deserialize<List<PickedFolder>>(File.ReadAllText(path)) ?? [];
        var data = folders
            .Select((folder, index) => new KeyValuePair<string, string?>(
                $"{DriveAccessOptions.SectionName}:AllowedFolderIds:{index}", folder.Id))
            .ToList();

        return builder.AddInMemoryCollection(data);
    }
}
