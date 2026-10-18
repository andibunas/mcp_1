using log4net;

namespace WebMCP;

public static class LogHelper
{
    private static readonly ILog Log = LogManager.GetLogger(typeof(LogHelper));

    private const int AuthHeaderTruncateThreshold = 60;
    private const int AuthHeaderDisplayStart = 50;
    private const int AuthHeaderDisplayEnd = 10;

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
            Log.Info($"[RequestLogging]   {name}: {displayValue}");
        }
    }

    public static async Task LogRequestBodyAsync(HttpContext context)
    {
        if (context.Request.ContentLength.HasValue && context.Request.ContentLength > 0)
        {
            Log.Info($"[RequestLogging] Content-Length: {context.Request.ContentLength}");
            context.Request.EnableBuffering();
            using (var reader = new StreamReader(context.Request.Body, leaveOpen: true))
            {
                var body = await reader.ReadToEndAsync();
                Log.Info($"[RequestLogging] Request Body:\n{body}");
                context.Request.Body.Position = 0;
            }
        }
        else
        {
            Log.Info("[RequestLogging] No request body");
        }
    }

    public static void LogResponseHeaders(HttpContext context)
    {
        foreach (var (name, value) in context.Response.Headers)
        {
            var displayValue = FormatHeaderValue(name, value.ToString());
            Log.Info($"[RequestLogging]   < {name}: {displayValue}");
        }
    }

    public static async Task LogResponseBodyAsync(MemoryStream responseBuffer, Stream originalBody)
    {
        responseBuffer.Position = 0;
        var responseBody = await new StreamReader(responseBuffer).ReadToEndAsync();
        if (!string.IsNullOrEmpty(responseBody))
        {
            Log.Info($"[RequestLogging] Response Body:\n{responseBody}");
        }

        responseBuffer.Position = 0;
        await responseBuffer.CopyToAsync(originalBody);
    }
}
