using System.Diagnostics;
using FluentAssertions;
using QuotesApi.Extensions;

namespace Quotes.Tests.Unit;

public class LoggingExtensionsTests
{
    [Fact]
    public void ResolveTraceId_WithActiveActivity_ReturnsOpenTelemetryTraceId()
    {
        using var activity = new Activity("test-operation").Start();

        var traceId = LoggingExtensions.ResolveTraceId("fallback-trace-id");

        traceId.Should().Be(activity.TraceId.ToString());
        traceId.Should().NotBe("fallback-trace-id");
    }

    [Fact]
    public void ResolveTraceId_WithoutActiveActivity_FallsBackToHttpContextTraceIdentifier()
    {
        Activity.Current = null;

        var traceId = LoggingExtensions.ResolveTraceId("fallback-trace-id");

        traceId.Should().Be("fallback-trace-id");
    }
}
