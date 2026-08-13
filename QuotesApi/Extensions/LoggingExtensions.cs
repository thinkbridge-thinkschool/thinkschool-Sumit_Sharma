using Serilog.Context;

namespace QuotesApi.Extensions;

public static class LoggingExtensions
{
    public static void UseTraceIdEnrichment(
        this WebApplication app)
    {
        app.Use(async (context, next) =>
        {
            using (LogContext.PushProperty(
                "TraceId",
                context.TraceIdentifier))
            {
                await next(context);
            }
        });
    }
}
