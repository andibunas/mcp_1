using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Extensions.Mcp;
using Microsoft.Extensions.Logging;

namespace AZtest1;

public class GCMP(ILogger<GCMP> logger)
{
    private readonly ILogger<GCMP> _logger = logger;

    [Function("gmcp")]
    public string Run(
        [McpToolTrigger("gmcp", "Responds to the user with a hello message.")] ToolInvocationContext context,
        [McpToolProperty(nameof(name), "The name of the person to greet.")] string? name
    )
    {
        _logger.LogInformation("C# MCP tool trigger function processed a request.");

        var transportInfo = System.Text.Json.JsonSerializer.Serialize(context.Transport);
        var responseVal = $"Hello, {name ?? "world"}! MCP Tool!\n ";

        var transportType = context.Transport?.GetType().Name ?? "Unknown";
        responseVal += $"Transport Type: {transportType}\n";


        if (context.Transport is HttpTransport httpTransport)
        {
            httpTransport.Headers.TryGetValue("Authorization", out var authHeader);
            authHeader ??= "No Authorization header found";
            responseVal += $"Authorization Header: {authHeader}\n";
            // _logger.LogInformation("Authorization Header: {AuthHeader}", authHeader);
            // _logger.LogInformation("HTTP Transport Headers: {Headers}", System.Text.Json.JsonSerializer.Serialize(httpTransport.Properties?.GetValueOrDefault("headers")));
        }

        // if (context.Transport?.Properties?.TryGetValue("headers", out var h) == true
        //     && h is IDictionary<string, string> headers)
        //     headers.TryGetValue("Authorization", out auth);

        // if (auth != "Bearer expected-token")
        if (name == "andi")
            throw new UnauthorizedAccessException("Unauthorized...");

        responseVal += $"Transport Info: {transportInfo}\n";

        return responseVal;
        // $"Hello, {name ?? "world"}! This is an MCP Tool!\n type:{transportType} \n authHeader:{authHeader} \nTransport: {transportInfo}";
    }
}



// sample Transport JSON for testing:
// Transport: 
// {"name":"http-streamable",
//     "properties":{
//     "headers":{
//         "Accept":"*/*",
//         "Accept-Encoding":"gzip, deflate, br",
//         "Authorization":"Bearer randomvavvvvvvooooooooooooooooooooooo",
//         "Content-Length":"170",
//         "Content-Type":"application/json",
//         "Host":"fa-test2-eybndeehh3acard0.westus3-01.azurewebsites.net",
//         "Max-Forwards":"10",
//         "User-Agent":"PostmanRuntime/7.56.1",
//         "Postman-Token":"aa7ebc5d-6dbe-428c-90c5-05a9099210fb",
//         "X-ARR-LOG-ID":"fbb5b304-4b8f-48fb-af67-d8e078c43a49",
//         "CLIENT-IP":"35.129.117.247:54031",
//         "DISGUISED-HOST":"fa-test2-eybndeehh3acard0.westus3-01.azurewebsites.net",
//         "X-SITE-DEPLOYMENT-ID":"fa-test2",
//         "WAS-DEFAULT-HOSTNAME":"fa-test2-eybndeehh3acard0.westus3-01.azurewebsites.net",
//         "X-Forwarded-Proto":"https",
//         "X-AppService-Proto":"https",
//         "X-ARR-SSL":"2048|256|CN=Microsoft TLS G2 RSA CA OCSP 16, O=Microsoft Corporation, C=US|CN=*.azurewebsites.net, O=Microsoft Corporation, L=Redmond, S=WA, C=US",
//         "X-Forwarded-TlsVersion":"1.3",
//         "X-Forwarded-For":"35.129.117.247:54031",
//         "X-Original-URL":"/runtime/webhooks/mcp",
//         "X-WAWS-Unencoded-URL":"/runtime/webhooks/mcp"
//         }
//     }
// }
