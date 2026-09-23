


# first 

Here are the steps to build a minimal MCP server in .NET — run them one at a time:

## Console App
1. **Create a console project**
   ```
   dotnet new console -n EmptyMCP1
   ```

2. **Add the official MCP SDK package**
   ```
   dotnet add package ModelContextProtocol --prerelease
   ```

3. **Add hosting/DI packages** (needed to wire up the MCP server host)
   ```
   dotnet add package Microsoft.Extensions.Hosting
   ```

4. **Replace `Program.cs`** with a minimal MCP stdio server that auto-discovers tools:
   ```csharp
   using Microsoft.Extensions.DependencyInjection;
   using Microsoft.Extensions.Hosting;
   using ModelContextProtocol.Server;
   using System.ComponentModel;

   var builder = Host.CreateApplicationBuilder(args);
   builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);
   builder.Services
       .AddMcpServer()
       .WithStdioServerTransport()
       .WithToolsFromAssembly();

   await builder.Build().RunAsync();

   [McpServerToolType]
   public static class EchoTool
   {
       [McpServerTool, Description("Echoes the input back.")]
       public static string Echo(string message) => $"Echo: {message}";
   }
   ```

5. **Build it**
   ```
   dotnet build
   ```

6. **Test it locally with the MCP Inspector** (Node-based dev tool):
   ```
   npx @modelcontextprotocol/inspector dotnet run
   ```
   This opens a browser UI where you can call your `Echo` tool directly.

7. **Register it with a real MCP client** (e.g. Claude Desktop/Code) by pointing its config at `dotnet run --project <path>` or the built `.dll`.

8. **Add more tools** as static methods decorated with `[McpServerTool]` in `[McpServerToolType]` classes — the `WithToolsFromAssembly()` call picks them up automatically.



## Web App

**Step 1** would be `dotnet new web -n EmptyMCP1` instead of `console`.

**Step 2** package is the same `ModelContextProtocol` package, but you'd also want `ModelContextProtocol.AspNetCore` (it ships the `WithHttpTransport()` extension and endpoint mapping).

**Step 3** — no separate `Microsoft.Extensions.Hosting` needed; a web app's `WebApplication.CreateBuilder` already gives you the host/DI.

**Step 4** becomes something like:
```csharp
using ModelContextProtocol.Server;
using System.ComponentModel;

var builder = WebApplication.CreateBuilder(args);
builder.Services
    .AddMcpServer()
    .WithHttpTransport()
    .WithToolsFromAssembly();

var app = builder.Build();
app.MapMcp();   // exposes the MCP endpoint, e.g. /mcp

app.Run();

[McpServerToolType]
public static class EchoTool
{
    [McpServerTool, Description("Echoes the input back.")]
    public static string Echo(string message) => $"Echo: {message}";
}
```

Differences: `WebApplication.CreateBuilder` instead of `Host.CreateApplicationBuilder`, `WithHttpTransport()` instead of `WithStdioServerTransport()`, and `app.MapMcp()` to expose the endpoint over HTTP/SSE. Everything downstream — testing with the Inspector, adding tools — works the same way, just pointing the client at a URL instead of a process to launch.

**5. Build it** — same:
```
dotnet build
```

**6. Test it locally with the MCP Inspector** — same command, but since it's HTTP now you first run the server, then point the Inspector at its URL instead of a launch command:
```
dotnet run
```
then in another terminal:
```
npx @modelcontextprotocol/inspector
```
In the Inspector UI, choose transport "SSE" (or "Streamable HTTP") and enter the URL your app logged on startup, e.g. `http://localhost:5000/mcp`.

**7. Register it with a real MCP client** — instead of pointing config at a `dotnet run --project` command, point it at the URL:
```json
{
  "mcpServers": {
    "EmptyMCP1": {
      "url": "http://localhost:5000/mcp"
    }
  }
}
```
(For a client consumed remotely, you'd deploy the app somewhere reachable — e.g. Azure App Service, a container, etc. — and use that public URL, plus add auth since it's now network-exposed.)

**8. Add more tools** — identical: static methods with `[McpServerTool]` in an `[McpServerToolType]` class; `WithToolsFromAssembly()` still auto-discovers them.




