using McpGoogleDrive.Core.Auth;
using McpGoogleDrive.Core.Configuration;
using McpGoogleDrive.Core.Drive;
using McpGoogleDrive.Core.Setup;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace McpGoogleDrive.Core;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers everything a host needs to serve Drive-backed MCP tools: options binding,
    /// local token storage, OAuth, and <see cref="DriveFileService"/>. Shared by every host so
    /// this composition doesn't get re-written (and drift) per host. Does not register a
    /// transport or the MCP server itself — call
    /// <c>AddMcpServer().With...Transport().WithToolsFromAssembly()</c> separately.
    /// </summary>
    public static IServiceCollection AddDriveCoreServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<GoogleAuthOptions>(configuration.GetSection(GoogleAuthOptions.SectionName));
        services.Configure<DriveAccessOptions>(configuration.GetSection(DriveAccessOptions.SectionName));

        services.AddSingleton<ITokenStore>(new FileTokenStore());
        services.AddSingleton<GoogleAuthService>();
        services.AddSingleton<DriveFolderPickerService>();
        services.AddSingleton<InteractiveSetupService>();
        services.AddSingleton<DriveFileService>();
        services.AddSingleton<IGoogleDriveApi>(sp =>
        {
            var auth = sp.GetRequiredService<GoogleAuthService>();
            var driveOptions = sp.GetRequiredService<IOptions<DriveAccessOptions>>().Value;
            var authOptions = sp.GetRequiredService<IOptions<GoogleAuthOptions>>().Value;
            var credential = auth.AuthorizeAsync(driveOptions.Scopes).GetAwaiter().GetResult();
            return new GoogleDriveApi(credential, authOptions.ApplicationName);
        });

        return services;
    }
}
