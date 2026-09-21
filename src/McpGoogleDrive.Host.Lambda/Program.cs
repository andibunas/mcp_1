using Amazon.Lambda.AspNetCoreServer.Hosting;
using Amazon.SecretsManager;
using McpGoogleDrive.Core;
using McpGoogleDrive.Core.Auth;
using McpGoogleDrive.Core.Configuration;
using McpGoogleDrive.Core.Drive;
using McpGoogleDrive.Core.Tools;
using Microsoft.Extensions.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);

// Runs this same app via the Lambda runtime when executing inside Lambda (detected from
// environment variables), or via Kestrel when run locally — the rest of Program.cs doesn't need
// to know which. MCP's Streamable HTTP transport needs a long-lived/streaming response, which
// Function URLs only support in RESPONSE_STREAM invoke mode — EnableResponseStreaming here must
// match that Function URL setting (see docs/deploy-aws.md). Not yet deployed/verified against a
// real Function URL — confirm streaming actually works end-to-end before relying on this in
// production; fall back to EnableResponseStreaming = false (buffered) if it doesn't.
builder.Services.AddAWSLambdaHosting(LambdaEventSource.HttpApi, options =>
{
    options.EnableResponseStreaming = true;
});

// Lambda has no durable local disk across invocations, so use Secrets Manager for token storage
// instead of Core's default FileTokenStore. Must be registered before AddDriveCoreServices(),
// which only fills in a token store if one isn't already registered.
builder.Services.AddSingleton<IAmazonSecretsManager>(new AmazonSecretsManagerClient());
builder.Services.AddSingleton<ITokenStore>(sp =>
    new SecretsManagerTokenStore(sp.GetRequiredService<IAmazonSecretsManager>()));

builder.Configuration.AddAllowedFoldersFile();
builder.Services.AddDriveCoreServices(builder.Configuration);

builder.Services
    .AddMcpServer()
    .WithHttpTransport()
    .WithToolsFromAssembly(typeof(DriveTools).Assembly);

var app = builder.Build();

// Authorize eagerly at startup. Unlike the local hosts, this must not need a browser: it only
// works if SecretsManagerTokenStore already has a cached refresh token from a prior interactive
// `Host.Stdio -- setup` run (see docs/deploy-aws.md) — GoogleWebAuthorizationBroker refreshes
// silently when a cached credential exists, and only opens a browser when one doesn't.
_ = app.Services.GetRequiredService<DriveFileService>();

app.MapMcp();

app.Run();
