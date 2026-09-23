using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);
builder.Services
    .AddMcpServer()
    .WithHttpTransport()
    .WithToolsFromAssembly();

var app = builder.Build();

app.MapGet("/.well-known/oauth-protected-resource", () => { 
    Console.WriteLine("Received request for /.well-known/oauth-protected-resource"); // Log the request for debugging
    return Results.Json(new
{
    resource = "http://localhost:5157/",
    authorization_servers = new[] { "http://localhost:5157/" },
    bearer_methods_supported = new[] { "header" },
    scopes_supported = new[] { "mcp:tools:read", "mcp:tools:write" }
});
});


app.Use(async (context, next) =>
{
    Console.WriteLine($"Incoming request: {context.Request.Method} {context.Request.Path} "); // Log the incoming request for debugging

    if (context.Request.Path == "/.well-known/oauth-protected-resource")
    {
        await next();
        return;
    }


    var authHeader = context.Request.Headers["Authorization"].FirstOrDefault();
    
    Console.WriteLine($"Authorization header: {authHeader}"); // Log the Authorization header for debugging 
    if (authHeader?.StartsWith("Bearer ") != true)
    {
        Console.WriteLine("Authorization header missing or does not start with 'Bearer '"); // Log the missing or incorrect header
        context.Response.StatusCode = 401;
        context.Response.Headers.Add("WWW-Authenticate", 
            "Bearer resource_metadata=\"http://localhost:5157/.well-known/oauth-protected-resource\"");
        return;
    }
    
    Console.WriteLine($"Extracted token: {authHeader.Substring("Bearer ".Length)}"); // Log the extracted token for debugging   
    var token = authHeader.Substring("Bearer ".Length);
    if (token != "your-secret-api-key")
    {
        context.Response.StatusCode = 401;
        return;
    }
    
    await next();
});



app.MapMcp();

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
