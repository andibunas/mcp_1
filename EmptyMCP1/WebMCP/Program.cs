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
    Console.WriteLine($"[RequestLogging] Incoming request ====>>>>: {context.Request.Method} {context.Request.Path} {context.Request.QueryString}");

    foreach (var (name, value) in context.Request.Headers)
    {
        var valueStr = value.ToString();
        var displayValue = name == "Authorization" && valueStr.Length > 60
            ? $"{valueStr.Substring(0, 50)}...{valueStr.Substring(valueStr.Length - 10)}"
            : valueStr;
        Console.WriteLine($"[RequestLogging]   {name}: {displayValue}");
    }

    // Log request body if present
    if (context.Request.ContentLength.HasValue && context.Request.ContentLength > 0)
    {
        Console.WriteLine($"[RequestLogging] Content-Length: {context.Request.ContentLength}");
        context.Request.EnableBuffering();
        using (var reader = new StreamReader(context.Request.Body, leaveOpen: true))
        {
            var body = await reader.ReadToEndAsync();
            Console.WriteLine($"[RequestLogging] Request Body:\n{body}");
            context.Request.Body.Position = 0;
        }
    }
    else
    {
        Console.WriteLine($"[RequestLogging] No request body");
    }

    Console.WriteLine($"[RequestLogging] ----- calling next() -----");

    // Capture response body
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
            foreach (var (name, value) in context.Response.Headers)
            {
                var valueStr = value.ToString();
                var displayValue = name == "Authorization" && valueStr.Length > 60
                    ? $"{valueStr.Substring(0, 50)}...{valueStr.Substring(valueStr.Length - 10)}"
                    : valueStr;
                Console.WriteLine($"[RequestLogging]   < {name}: {displayValue}");
            }

            responseBuffer.Position = 0;
            var responseBody = await new StreamReader(responseBuffer).ReadToEndAsync();
            if (!string.IsNullOrEmpty(responseBody))
            {
                Console.WriteLine($"[RequestLogging] Response Body:\n{responseBody}");
            }

            responseBuffer.Position = 0;
            await responseBuffer.CopyToAsync(originalBody);
            context.Response.Body = originalBody;
        }
    }
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