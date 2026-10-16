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
    Console.WriteLine("Processing /.well-known/oauth-protected-resource"); // Log the request for debugging
    return Results.Json(new {
        resource = "http://localhost:5157/resource/",
        authorization_servers = new[] { "http://localhost:5157/authservers/" },
        bearer_methods_supported = new[] { "header" },
        scopes_supported = new[] { "mcp:tools:read", "mcp:tools:write" }
    });
});

const string baseUrl = "http://localhost:5157";
const string issuer = baseUrl + "/authservers";
app.MapGet("/.well-known/oauth-authorization-server/authservers", () =>  {
    Console.WriteLine("Processing /.well-known/oauth-authorization-server/authservers"); // Log the request for debugging
    return  Results.Json(new {
        issuer,
        authorization_endpoint = $"{issuer}/authorize",
        token_endpoint = $"{issuer}/token",
        registration_endpoint = $"{issuer}/register",
        response_types_supported = new[] { "code" },
        grant_types_supported = new[] { "authorization_code", "refresh_token" },
        code_challenge_methods_supported = new[] { "S256" },
        token_endpoint_auth_methods_supported = new[] { "none" },
        scopes_supported = new[] { "mcp:tools:read", "mcp:tools:write" }
    });
});


app.MapGet("/", () => { 
    Console.WriteLine("Processing /"); // Log the request for debugging
    return Results.Text("Hello, World!");
});





app.Use(async (context, next) =>
{
    // go throught the heaeders and log them for debugging

    Console.WriteLine($"Incoming request: {context.Request.Method} {context.Request.Path} "); // Log the incoming request for debugging

    // if (context.Request.Path == "/.well-known/oauth-protected-resource")
    if (context.Request.Path != "/mcp")
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
        context.Response.Headers.Append("WWW-Authenticate", 
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



app.MapMcp("/mcp");

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