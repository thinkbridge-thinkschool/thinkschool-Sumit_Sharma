using FluentAssertions;
using Microsoft.Extensions.Configuration;
using QuotesApi.Extensions;

namespace Quotes.Tests.Unit;

public class TelemetryExtensionsTests
{
    [Fact]
    public void ResolveAppInsightsConnectionString_WithEnvironmentVariableSet_PrefersEnvironmentVariable()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["ApplicationInsights:ConnectionString"] = "InstrumentationKey=from-config"
        });

        var resolved = TelemetryExtensions.ResolveAppInsightsConnectionString(
            configuration,
            "InstrumentationKey=from-env-var");

        resolved.Should().Be("InstrumentationKey=from-env-var");
    }

    [Fact]
    public void ResolveAppInsightsConnectionString_WithoutEnvironmentVariable_FallsBackToConfiguration()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["ApplicationInsights:ConnectionString"] = "InstrumentationKey=from-config"
        });

        var resolved = TelemetryExtensions.ResolveAppInsightsConnectionString(
            configuration,
            environmentVariableValue: null);

        resolved.Should().Be("InstrumentationKey=from-config");
    }

    [Fact]
    public void ResolveAppInsightsConnectionString_WithNeitherSource_ReturnsNull()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>());

        var resolved = TelemetryExtensions.ResolveAppInsightsConnectionString(
            configuration,
            environmentVariableValue: null);

        resolved.Should().BeNull();
    }

    private static IConfiguration BuildConfiguration(
        Dictionary<string, string?> values)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
    }
}
