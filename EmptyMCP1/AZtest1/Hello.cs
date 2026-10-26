using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace AZtest1;

public class Hello
{
    private readonly ILogger<Hello> _logger;

    public Hello(ILogger<Hello> logger)
    {
        _logger = logger;
    }

    [Function("Hello")]
    public IActionResult Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", "post")] HttpRequest req)
    {
        _logger.LogInformation("C# HTTP trigger function processed a request.");

        // get headers to return back in the response
        var headers = req.Headers.Select(h => $"{h.Key}: {h.Value}").ToList();  

        // build string with headers to log
        var headersString = string.Join(Environment.NewLine, headers);  
        _logger.LogInformation("Request headers:{0}", headersString);

        return new OkObjectResult("Welcome to AZtest1 Functions! <BR>" + headersString);
    }


    // function takes name parameter from query string and returns a greeting message   
    [Function("Goodbye")]
    public IActionResult RunBye(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", "post")] HttpRequest req,
        [FromQuery(Name = "name")] string? name
        )
    {
        _logger.LogInformation("Goodbye function processed a request.");

        return new OkObjectResult($"Goodbye, {name ?? "world"}! This is the end!");
    }


}
