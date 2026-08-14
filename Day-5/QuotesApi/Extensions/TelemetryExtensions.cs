using Azure.Monitor.OpenTelemetry.AspNetCore;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using QuotesApi.Telemetry;

namespace QuotesApi.Extensions;

public static class TelemetryExtensions
{
    private const string ApplicationInsightsConnectionStringEnvVar =
        "APPLICATIONINSIGHTS_CONNECTION_STRING";

    public static IServiceCollection AddTelemetry(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var otlpEndpoint =
            Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT")
            ?? configuration["OpenTelemetry:OtlpEndpoint"];

        var appInsightsConnectionString = ResolveAppInsightsConnectionString(
            configuration,
            Environment.GetEnvironmentVariable(
                ApplicationInsightsConnectionStringEnvVar));

        var useAzureMonitor = !string.IsNullOrWhiteSpace(
            appInsightsConnectionString);

        var openTelemetry = services.AddOpenTelemetry()
            .ConfigureResource(resource => resource
                .AddService(serviceName: "QuotesApi"))
            .WithTracing(tracing =>
            {
                tracing
                    .AddSource(QuotesApiActivitySource.Name)
                    .AddEntityFrameworkCoreInstrumentation()
                    .AddOtlpExporter(otlp =>
                    {
                        if (!string.IsNullOrWhiteSpace(otlpEndpoint))
                            otlp.Endpoint = new Uri(otlpEndpoint);
                    });

                // UseAzureMonitor() (below) registers its own ASP.NET Core
                // and HttpClient instrumentation. Adding them again here
                // would double-instrument every request once Azure Monitor
                // is enabled, so they're only added explicitly when it isn't.
                if (!useAzureMonitor)
                {
                    tracing
                        .AddAspNetCoreInstrumentation()
                        .AddHttpClientInstrumentation();
                }
            });

        if (useAzureMonitor)
        {
            openTelemetry.UseAzureMonitor(azureMonitor =>
                azureMonitor.ConnectionString = appInsightsConnectionString);
        }

        return services;
    }

    public static string? ResolveAppInsightsConnectionString(
        IConfiguration configuration,
        string? environmentVariableValue)
    {
        return environmentVariableValue
            ?? configuration["ApplicationInsights:ConnectionString"];
    }
}
