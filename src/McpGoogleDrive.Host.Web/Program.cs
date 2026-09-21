using McpGoogleDrive.Core;
using McpGoogleDrive.Core.Configuration;
using McpGoogleDrive.Core.Drive;
using McpGoogleDrive.Core.Tools;
using Microsoft.Extensions.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);

// Layer in folder IDs saved by a previous `Host.Stdio -- setup` run, on top of appsettings/env vars.
builder.Configuration.AddAllowedFoldersFile();

builder.Services.AddDriveCoreServices(builder.Configuration);

builder.Services
    .AddMcpServer()
    .WithHttpTransport()
    .WithToolsFromAssembly(typeof(DriveTools).Assembly);

var app = builder.Build();

// Authorize eagerly at startup (opens the browser on first run, silent on later runs) rather
// than lazily on the first request, so the server doesn't accept connections before it can serve.
_ = app.Services.GetRequiredService<DriveFileService>();

app.MapMcp();

app.Run();
