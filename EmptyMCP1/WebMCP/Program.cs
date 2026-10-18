using Microsoft.AspNetCore.HttpOverrides;
using ModelContextProtocol.Server;
using System.ComponentModel;
using WebMCP;
using WebMCP.OAuth;

var builder = WebApplication.CreateBuilder(args);

// log4net writes to the console and to <app folder>\logs\webmcp.log (see log4net.config)
var logDir = Path.Combine(AppContext.BaseDirectory, "logs");
Directory.CreateDirectory(logDir);
log4net.GlobalContext.Properties["LogDir"] = logDir;
builder.Logging.ClearProviders();   // replace the default console provider so each line is written once
builder.Logging.AddLog4Net("log4net.config");

// Behind a hosting proxy (TLS terminated upstream), take scheme and host from X-Forwarded-* so the
// metadata advertises the public https URL. Locally there is no proxy and the request URL is used as-is.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});
builder.Services.AddDataProtection();
builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton<TokenProtector>();
builder.Services
    .AddMcpServer()
    .WithHttpTransport()
    .WithToolsFromAssembly();

var app = builder.Build();

app.UseForwardedHeaders();

app.Use(async (context, next) =>
{
    var logger = app.Logger;
    logger.LogInformation("====================== Starting =======================");
    logger.LogInformation("[RequestLogging] Incoming request : {Method} {Path} {Query}", context.Request.Method, context.Request.Path, context.Request.QueryString);
    LogHelper.LogRequestHeaders(context, logger);
    await LogHelper.LogRequestBodyAsync(context, logger);
    logger.LogInformation("[RequestLogging] ----- calling next() -----");

    var originalBody = context.Response.Body;
    using (var responseBuffer = new MemoryStream())
    {
        context.Response.Body = responseBuffer;

        try
        {
            await next();
        }
        finally
        {
            logger.LogInformation("[RequestLogging] Response: << {StatusCode} {ContentType}", context.Response.StatusCode, context.Response.ContentType);
            LogHelper.LogResponseHeaders(context, logger);
            await LogHelper.LogResponseBodyAsync(responseBuffer, originalBody, logger);
            context.Response.Body = originalBody;
        }
    }
});

app.MapDiscoveryEndpoints();    // /.well-known/*
app.MapRegisterEndpoint();      // POST /register
app.MapAuthorizeEndpoints();    // GET/POST /authorize
app.MapTokenEndpoint();         // POST /token

app.UseMiddleware<BearerAuthMiddleware>();   // guards the MCP endpoint at /
app.MapMcp(OAuthSettings.McpPath);           // MCP endpoint at /

app.Run();

[McpServerToolType]
public static class EchoTool
{
    [McpServerTool, Description("Echoes the input back.")]
    public static string Echo(string message, ILogger<Program> logger) {
        logger.LogInformation("EchoTool.Echo called with message: {Message}", message);
        return $"Echo: {message}";
    }
}




/* 

    // foreach (var (name, value) in context.Request.Headers){ Console.WriteLine($"  {name}: {value}"); }


    // Response: swap in a MemoryStream, then copy back
    var original = context.Response.Body;
    await using var buffer = new MemoryStream();
    context.Response.Body = buffer;


    try
    {
        await next();
    }
    finally
    {
        foreach (var (name, value) in context.Response.Headers)
        Console.WriteLine($"  < {name}: {value}");


        buffer.Position = 0;
        var resBody = await new StreamReader(buffer).ReadToEndAsync();
        Console.WriteLine($"<< {context.Response.StatusCode} {context.Response.ContentType}\n{resBody}");

        buffer.Position = 0;
        await buffer.CopyToAsync(original);
        context.Response.Body = original;
    }

*/