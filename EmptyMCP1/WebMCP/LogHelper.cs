namespace WebMCP;

public static class LogHelper
{
    private const int AuthHeaderTruncateThreshold = 60;
    private const int AuthHeaderDisplayStart = 50;
    private const int AuthHeaderDisplayEnd = 10;

    public static string FormatHeaderValue(string name, string value)
    {
        return name == "Authorization" && value.Length > AuthHeaderTruncateThreshold
            ? $"{value[..AuthHeaderDisplayStart]}...{value[^AuthHeaderDisplayEnd..]}"
            : value;
    }

    public static void LogRequestHeaders(HttpContext context, ILogger logger)
    {
        foreach (var (name, value) in context.Request.Headers)
        {
            var displayValue = FormatHeaderValue(name, value.ToString());
            logger.LogInformation("[RequestLogging]   {Name}: {Value}", name, displayValue);
        }
    }

    public static async Task LogRequestBodyAsync(HttpContext context, ILogger logger)
    {
        if (context.Request.ContentLength.HasValue && context.Request.ContentLength > 0)
        {
            logger.LogInformation("[RequestLogging] Content-Length: {Length}", context.Request.ContentLength);
            context.Request.EnableBuffering();
            using (var reader = new StreamReader(context.Request.Body, leaveOpen: true))
            {
                var body = await reader.ReadToEndAsync();
                logger.LogInformation("[RequestLogging] Request Body:\n{Body}", body);
                context.Request.Body.Position = 0;
            }
        }
        else
        {
            logger.LogInformation("[RequestLogging] No request body");
        }
    }

    public static void LogResponseHeaders(HttpContext context, ILogger logger)
    {
        foreach (var (name, value) in context.Response.Headers)
        {
            var displayValue = FormatHeaderValue(name, value.ToString());
            logger.LogInformation("[RequestLogging]   < {Name}: {Value}", name, displayValue);
        }
    }

    public static async Task LogResponseBodyAsync(MemoryStream responseBuffer, Stream originalBody, ILogger logger)
    {
        responseBuffer.Position = 0;
        var responseBody = await new StreamReader(responseBuffer).ReadToEndAsync();
        if (!string.IsNullOrEmpty(responseBody))
        {
            logger.LogInformation("[RequestLogging] Response Body:\n{Body}", responseBody);
        }

        responseBuffer.Position = 0;
        await responseBuffer.CopyToAsync(originalBody);
    }
}
