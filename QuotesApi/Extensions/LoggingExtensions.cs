using System.Diagnostics;
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
                ResolveTraceId(context.TraceIdentifier)))
            {
                await next(context);
            }
        });
    }

    public static string ResolveTraceId(
        string fallbackTraceIdentifier)
    {
        return Activity.Current?.TraceId.ToString()
            ?? fallbackTraceIdentifier;
    }
}
