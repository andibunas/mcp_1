using System.Text.Json;
using log4net;

namespace WebMCP;

public static class LogHelper
{
    private static readonly ILog Log = LogManager.GetLogger(typeof(LogHelper));

    private const int AuthHeaderTruncateThreshold = 60;
    private const int AuthHeaderDisplayStart = 50;
    private const int AuthHeaderDisplayEnd = 10;
    private const int TruncatedBodyMaxLength = 1000;

    public static string FormatHeaderValue(string name, string value)
    {
        return name == "Authorization" && value.Length > AuthHeaderTruncateThreshold
            ? $"{value[..AuthHeaderDisplayStart]}...{value[^AuthHeaderDisplayEnd..]}"
            : value;
    }

    public static void LogRequestHeaders(HttpContext context)
    {
        foreach (var (name, value) in context.Request.Headers)
        {
            var displayValue = FormatHeaderValue(name, value.ToString());
            Log.Info($"  {name}: {displayValue}");
        }
    }

    // Returns the MCP tool name when the request is a tools/call, otherwise null.
    // Bodies of calls listed in truncateCalls are logged trimmed to TruncatedBodyMaxLength.
    public static async Task<string?> LogRequestBodyAsync(HttpContext context, params string[] truncateCalls)
    {
        if (context.Request.ContentLength.HasValue && context.Request.ContentLength > 0)
        {
            Log.Info($"Content-Length: {context.Request.ContentLength}");
            context.Request.EnableBuffering();
            using (var reader = new StreamReader(context.Request.Body, leaveOpen: true))
            {
                var body = await reader.ReadToEndAsync();
                var callName = GetToolCallName(body);
                Log.Info($"Request Body:\n{Format(body, callName, truncateCalls)}");
                context.Request.Body.Position = 0;
                return callName;
            }
        }

        Log.Info("No request body");
        return null;
    }

    private static string? GetToolCallName(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (root.ValueKind == JsonValueKind.Object
                && root.TryGetProperty("method", out var method) && method.GetString() == "tools/call"
                && root.TryGetProperty("params", out var parameters)
                && parameters.TryGetProperty("name", out var name))
            {
                return name.GetString();
            }
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string Format(string text, string? callName, string[] truncateCalls)
    {
        if (callName is null || !truncateCalls.Contains(callName) || text.Length <= TruncatedBodyMaxLength)
        {
            return text;
        }
        return $"{text[..TruncatedBodyMaxLength]}... [truncated, {text.Length - TruncatedBodyMaxLength} more chars]";
    }

    public static void LogResponseHeaders(HttpContext context)
    {
        foreach (var (name, value) in context.Response.Headers)
        {
            var displayValue = FormatHeaderValue(name, value.ToString());
            Log.Info($"  < {name}: {displayValue}");
        }
    }

    public static async Task LogResponseBodyAsync(MemoryStream responseBuffer, Stream originalBody, string? callName, params string[] truncateCalls)
    {
        responseBuffer.Position = 0;
        var responseBody = await new StreamReader(responseBuffer).ReadToEndAsync();
        if (!string.IsNullOrEmpty(responseBody))
        {
            Log.Info($"Response Body:\n{Format(responseBody, callName, truncateCalls)}");
        }

        responseBuffer.Position = 0;
        await responseBuffer.CopyToAsync(originalBody);
    }
}
