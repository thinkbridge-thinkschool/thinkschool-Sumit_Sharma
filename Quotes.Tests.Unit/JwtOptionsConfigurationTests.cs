using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using QuotesApi.Auth;

namespace Quotes.Tests.Unit;

public class JwtOptionsConfigurationTests
{
    [Fact]
    public void Configure_WithJwtSection_BindsIOptionsAndIOptionsSnapshot()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Key"] = "appsettings-signing-key-with-sufficient-length-00000",
                ["Jwt:Issuer"] = "QuotesApi",
                ["Jwt:Audience"] = "QuotesApi.Clients"
            })
            .Build();

        var services = new ServiceCollection();
        services.Configure<JwtOptions>(configuration.GetSection("Jwt"));
        using var provider = services.BuildServiceProvider();

        var options = provider.GetRequiredService<IOptions<JwtOptions>>().Value;
        var snapshot = provider.GetRequiredService<IOptionsSnapshot<JwtOptions>>().Value;

        options.Key.Should().Be("appsettings-signing-key-with-sufficient-length-00000");
        options.Issuer.Should().Be("QuotesApi");
        options.Audience.Should().Be("QuotesApi.Clients");

        snapshot.Should().Be(options);
    }

    [Fact]
    public void Configuration_Precedence_EnvironmentSettingsOverrideBaseSettings()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Key"] = "appsettings-json-key-with-sufficient-length-0000000",
                ["Jwt:Issuer"] = "base-issuer",
                ["Jwt:Audience"] = "base-audience"
            })
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Issuer"] = "development-issuer"
            })
            .Build();

        var options = Bind(configuration);

        options.Key.Should().Be("appsettings-json-key-with-sufficient-length-0000000");
        options.Issuer.Should().Be("development-issuer");
        options.Audience.Should().Be("base-audience");
    }

    [Fact]
    public void Configuration_Precedence_EnvironmentVariableOverridesJsonSettings()
    {
        const string envVarName = "Jwt__Key";
        const string envVarValue = "environment-variable-key-with-sufficient-length-000";
        var previousValue = Environment.GetEnvironmentVariable(envVarName);

        try
        {
            Environment.SetEnvironmentVariable(envVarName, envVarValue);

            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Jwt:Key"] = "appsettings-json-key-with-sufficient-length-0000000",
                    ["Jwt:Issuer"] = "QuotesApi",
                    ["Jwt:Audience"] = "QuotesApi.Clients"
                })
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Jwt:Key"] = "appsettings-development-key-with-sufficient-length-0"
                })
                .AddEnvironmentVariables()
                .Build();

            var options = Bind(configuration);

            options.Key.Should().Be(envVarValue);
            options.Issuer.Should().Be("QuotesApi");
            options.Audience.Should().Be("QuotesApi.Clients");
        }
        finally
        {
            Environment.SetEnvironmentVariable(envVarName, previousValue);
        }
    }

    private static JwtOptions Bind(IConfiguration configuration)
    {
        var services = new ServiceCollection();
        services.Configure<JwtOptions>(configuration.GetSection("Jwt"));
        using var provider = services.BuildServiceProvider();

        return provider.GetRequiredService<IOptions<JwtOptions>>().Value;
    }
}
