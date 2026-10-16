using ModelContextProtocol.Server;
using System.ComponentModel;
using WebMCP.OAuth;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddDataProtection();
builder.Services.AddSingleton<TokenProtector>();
builder.Services
    .AddMcpServer()
    .WithHttpTransport()
    .WithToolsFromAssembly();

var app = builder.Build();

app.Use(async (context, next) =>
{
    Console.WriteLine($"Incoming request: {context.Request.Method} {context.Request.Path} ");
    await next();
});

app.MapDiscoveryEndpoints();    // /.well-known/*
app.MapRegisterEndpoint();      // POST /authservers/register
app.MapAuthorizeEndpoints();    // GET/POST /authservers/authorize
app.MapTokenEndpoint();         // POST /authservers/token

app.MapGet("/", () => Results.Text("Hello, World!"));

app.UseMiddleware<BearerAuthMiddleware>();   // guards /mcp
app.MapMcp(OAuthSettings.McpPath);

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