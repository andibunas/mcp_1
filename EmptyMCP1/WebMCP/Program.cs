using ModelContextProtocol.Server;
using System.ComponentModel;
using WebMCP;
using WebMCP.OAuth;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddDataProtection();
builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton<TokenProtector>();
builder.Services
    .AddMcpServer()
    .WithHttpTransport()
    .WithToolsFromAssembly();

var app = builder.Build();

app.Use(async (context, next) =>
{
    Console.WriteLine($"====================== Starting =======================");
    Console.WriteLine($"[RequestLogging] Incoming request : {context.Request.Method} {context.Request.Path} {context.Request.QueryString}");
    LogHelper.LogRequestHeaders(context);
    await LogHelper.LogRequestBodyAsync(context);
    Console.WriteLine($"[RequestLogging] ----- calling next() -----");

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
            Console.WriteLine($"[RequestLogging] Response: << {context.Response.StatusCode} {context.Response.ContentType}");
            LogHelper.LogResponseHeaders(context);
            await LogHelper.LogResponseBodyAsync(responseBuffer, originalBody);
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
    public static string Echo(string message) {
        Console.WriteLine($"EchoTool.Echo called with message: {message}"); 
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