using Azure.Monitor.OpenTelemetry.Exporter;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Azure.Functions.Worker.OpenTelemetry;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry;

Console.WriteLine("Starting AZtest1 Functions...");


// old way to build the host for Azure Functions with OpenTelemetry support
// var host = new HostBuilder()
//     .ConfigureFunctionsWebApplication()
//     .Build();

// host.Run();

// new way to build the host for Azure Functions with OpenTelemetry support
var builder = FunctionsApplication.CreateBuilder(args);

builder.ConfigureFunctionsWebApplication();

if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("APPLICATIONINSIGHTS_CONNECTION_STRING")))
{
    Console.WriteLine("Application Insights connection string found. Configuring OpenTelemetry...", Environment.GetEnvironmentVariable("APPLICATIONINSIGHTS_CONNECTION_STRING"));
    builder.Services.AddOpenTelemetry()
        .UseFunctionsWorkerDefaults()
        .UseAzureMonitorExporter();
}

builder.Build().Run();
