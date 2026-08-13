using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using QuotesApi.Auth;

namespace Quotes.Tests.Unit;

public class JwtAuthenticationOptionsFactoryTests
{
    [Fact]
    public void Create_WithAllValuesConfigured_ReturnsOptions()
    {
        var config = FullConfig();
        var configuration = BuildConfiguration(config);

        var result = Invoke(configuration);

        result.Key.Should().Be(config["Jwt:Key"]);
        result.Issuer.Should().Be(config["Jwt:Issuer"]);
        result.Audience.Should().Be(config["Jwt:Audience"]);
        result.EntraAuthority.Should().Be(config["Entra:Authority"]);
        result.EntraAudience.Should().Be(config["Entra:Audience"]);
    }

    [Fact]
    public void Create_WithMissingKey_Throws()
    {
        var config = FullConfig();
        config.Remove("Jwt:Key");
        var configuration = BuildConfiguration(config);

        var act = () => Invoke(configuration);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*JWT key is not configured*");
    }

    [Fact]
    public void Create_WithKeyShorterThan256Bits_Throws()
    {
        var config = FullConfig();
        config["Jwt:Key"] = "too-short-key";
        var configuration = BuildConfiguration(config);

        var act = () => Invoke(configuration);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*JWT key must be at least 256 bits*");
    }

    [Fact]
    public void Create_WithMissingIssuer_Throws()
    {
        var config = FullConfig();
        config.Remove("Jwt:Issuer");
        var configuration = BuildConfiguration(config);

        var act = () => Invoke(configuration);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*JWT issuer is not configured*");
    }

    [Fact]
    public void Create_WithMissingAudience_Throws()
    {
        var config = FullConfig();
        config.Remove("Jwt:Audience");
        var configuration = BuildConfiguration(config);

        var act = () => Invoke(configuration);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*JWT audience is not configured*");
    }

    [Fact]
    public void Create_WithMissingEntraAuthority_Throws()
    {
        var config = FullConfig();
        config.Remove("Entra:Authority");
        var configuration = BuildConfiguration(config);

        var act = () => Invoke(configuration);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Entra authority is not configured*");
    }

    [Fact]
    public void Create_WithMissingEntraAudience_Throws()
    {
        var config = FullConfig();
        config.Remove("Entra:Audience");
        var configuration = BuildConfiguration(config);

        var act = () => Invoke(configuration);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Entra audience is not configured*");
    }

    private static JwtAuthenticationOptions Invoke(
        IConfiguration configuration)
    {
        var jwtOptions = Options.Create(
            configuration.GetSection("Jwt").Get<JwtOptions>()
                ?? new JwtOptions());

        return JwtAuthenticationOptionsFactory.Create(
            jwtOptions,
            configuration);
    }

    private static Dictionary<string, string?> FullConfig() => new()
    {
        ["Jwt:Key"] = "unit-test-signing-key-with-sufficient-length-000000",
        ["Jwt:Issuer"] = "QuotesApi.Tests",
        ["Jwt:Audience"] = "QuotesApi.Tests.Clients",
        ["Entra:Authority"] = "https://login.microsoftonline.com/00000000-0000-0000-0000-000000000000/v2.0",
        ["Entra:Audience"] = "api://00000000-0000-0000-0000-000000000000"
    };

    private static IConfiguration BuildConfiguration(
        Dictionary<string, string?> values)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
    }
}
