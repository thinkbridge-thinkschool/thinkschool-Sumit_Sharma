using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using QuotesApi.Telemetry;

namespace QuotesApi.Extensions;

public static class TelemetryExtensions
{
    public static IServiceCollection AddTelemetry(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var otlpEndpoint =
            Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT")
            ?? configuration["OpenTelemetry:OtlpEndpoint"];

        services.AddOpenTelemetry()
            .ConfigureResource(resource => resource
                .AddService(serviceName: "QuotesApi"))
            .WithTracing(tracing =>
            {
                tracing
                    .AddSource(QuotesApiActivitySource.Name)
                    .AddAspNetCoreInstrumentation()
                    .AddEntityFrameworkCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddOtlpExporter(otlp =>
                    {
                        if (!string.IsNullOrWhiteSpace(otlpEndpoint))
                            otlp.Endpoint = new Uri(otlpEndpoint);
                    });
            });

        return services;
    }
}
