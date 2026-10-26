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
        // dump everything in Transport
        _logger.LogInformation("Transport: {Transport}", System.Text.Json.JsonSerializer.Serialize(context.Transport));

        _logger.LogInformation("C# MCP tool trigger function processed a request.");
        return $"Hello, {name ?? "world"}! This is an MCP Tool!";
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
