using McpGoogleDrive.Core;
using McpGoogleDrive.Core.Configuration;
using McpGoogleDrive.Core.Drive;
using McpGoogleDrive.Core.Setup;
using McpGoogleDrive.Core.Tools;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

if (args is ["setup", ..])
{
    await RunSetupAsync();
    return;
}

await RunServerAsync();

static async Task RunServerAsync()
{
    var builder = Host.CreateApplicationBuilder();

    // Layer in folder IDs saved by a previous `setup` run, on top of appsettings/env vars.
    builder.Configuration.AddAllowedFoldersFile();

    builder.Services.AddDriveCoreServices(builder.Configuration);

    // Stdio transport uses stdout exclusively for the MCP protocol; any other stdout write
    // (including the default console logger) would corrupt it, so logs go to stderr instead.
    builder.Logging.ClearProviders();
    builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);

    builder.Services
        .AddMcpServer()
        .WithStdioServerTransport()
        .WithToolsFromAssembly(typeof(DriveTools).Assembly);

    var host = builder.Build();

    // Authorize eagerly at startup (opens the browser on first run, silent on later runs) rather
    // than lazily on the first tool call, so the MCP handshake doesn't stall mid-request on sign-in.
    _ = host.Services.GetRequiredService<DriveFileService>();

    await host.RunAsync();
}

static async Task RunSetupAsync()
{
    var builder = Host.CreateApplicationBuilder();
    builder.Services.AddDriveCoreServices(builder.Configuration);

    var host = builder.Build();
    var setup = host.Services.GetRequiredService<InteractiveSetupService>();

    Console.WriteLine("Opening your browser to sign in and pick Drive folders...");
    var folders = await setup.RunAsync();

    Console.WriteLine($"Granted access to {folders.Count} folder(s):");
    foreach (var folder in folders)
    {
        Console.WriteLine($"  {folder.Name}  ({folder.Id})");
    }

    Console.WriteLine("Saved. These folders will be picked up automatically the next time the server runs.");
}
