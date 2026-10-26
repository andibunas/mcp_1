using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Extensions.Mcp;
using Microsoft.Extensions.Logging;

namespace AZtest1;

public class GCMP
{
    private ILogger<GCMP> _logger;

    public GCMP(ILogger<GCMP> logger)
    {
        _logger = logger;
    }

    [Function("gmcp")]
    public string Run(
        [McpToolTrigger("gmcp", "Responds to the user with a hello message.")] ToolInvocationContext context,
        [McpToolProperty(nameof(name), "The name of the person to greet.")] string? name
    )
    {
        _logger.LogInformation("C# MCP tool trigger function processed a request.");
        return $"Hello, {name ?? "world"}! This is an MCP Tool!";
    }
}
