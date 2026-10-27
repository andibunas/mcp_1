using System.Text.Json;
using System.Text.Json.Nodes;

namespace AZtest1;

/// <summary>Result of handling an MCP request: HTTP status, custom headers and JSON body (null = no body).</summary>
public record McpHttpResult(int StatusCode, IDictionary<string, string> Headers, string? Body);

/// <summary>
/// Minimal JSON-RPC 2.0 / MCP (streamable HTTP, JSON responses only) emulator.
/// Supports initialize, ping, tools/list and tools/call, so HTTP headers can be controlled.
/// </summary>
public class McpEmulator
{
    private const string ProtocolVersion = "2025-03-26";

    private static readonly JsonObject GmcpTool = new()
    {
        ["name"] = "gmcp",
        ["description"] = "Responds to the user with a hello message.",
        ["inputSchema"] = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["name"] = new JsonObject { ["type"] = "string", ["description"] = "The name of the person to greet." }
            }
        }
    };

    public McpHttpResult Handle(string requestBody)
    {
        var headers = new Dictionary<string, string> { ["X-Mcp-Emulator"] = "AZtest1" };

        JsonNode? request;
        try { request = JsonNode.Parse(requestBody); }
        catch (JsonException) { return Error(headers, null, -32700, "Parse error"); }

        if (request is not JsonObject obj || obj["method"]?.GetValue<string>() is not { } method)
            return Error(headers, null, -32600, "Invalid Request");

        var id = obj["id"]?.DeepClone();

        // notifications (no id) get 202 Accepted with no body
        if (id is null)
            return new McpHttpResult(202, headers, null);

        var parameters = obj["params"] as JsonObject;

        return method switch
        {
            "initialize" => Success(headers, id, new JsonObject
            {
                ["protocolVersion"] = ProtocolVersion,
                ["capabilities"] = new JsonObject { ["tools"] = new JsonObject { ["listChanged"] = false } },
                ["serverInfo"] = new JsonObject { ["name"] = "testGMCP-emulator", ["version"] = "1.0.0" }
            }),
            "ping" => Success(headers, id, new JsonObject()),
            "tools/list" => Success(headers, id, new JsonObject { ["tools"] = new JsonArray(GmcpTool.DeepClone()) }),
            "tools/call" => CallTool(headers, id, parameters),
            _ => Error(headers, id, -32601, $"Method not found: {method}")
        };
    }

    private static McpHttpResult CallTool(Dictionary<string, string> headers, JsonNode id, JsonObject? parameters)
    {
        var toolName = parameters?["name"]?.GetValue<string>();
        if (toolName != "gmcp")
            return Error(headers, id, -32602, $"Unknown tool: {toolName}");

        var name = parameters?["arguments"]?["name"]?.GetValue<string>();
        headers["X-Mcp-Tool"] = toolName;

        return Success(headers, id, new JsonObject
        {
            ["content"] = new JsonArray(new JsonObject
            {
                ["type"] = "text",
                ["text"] = $"Hello, {name ?? "world"}! This is an MCP Tool!"
            }),
            ["isError"] = false
        });
    }

    private static McpHttpResult Success(Dictionary<string, string> headers, JsonNode? id, JsonNode result) =>
        Respond(headers, new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id, ["result"] = result });

    private static McpHttpResult Error(Dictionary<string, string> headers, JsonNode? id, int code, string message) =>
        Respond(headers, new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id,
            ["error"] = new JsonObject { ["code"] = code, ["message"] = message }
        });

    private static McpHttpResult Respond(Dictionary<string, string> headers, JsonObject body) =>
        new(200, headers, body.ToJsonString());
}
