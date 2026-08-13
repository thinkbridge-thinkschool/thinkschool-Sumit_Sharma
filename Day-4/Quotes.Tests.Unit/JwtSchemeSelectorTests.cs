using System.IdentityModel.Tokens.Jwt;
using FluentAssertions;
using QuotesApi.Auth;

namespace Quotes.Tests.Unit;

public class JwtSchemeSelectorTests
{
    private const string InternalIssuer = "QuotesApi";
    private const string EntraIssuer = "https://login.microsoftonline.com/tenant/v2.0";
    private const string InternalScheme = "InternalJwt";
    private const string EntraScheme = "EntraJwt";
    private const string NoCredentialsScheme = "NoCredentials";

    [Fact]
    public void SelectScheme_WithNoAuthorizationHeader_ReturnsNoCredentialsScheme()
    {
        var result = Select(authorizationHeader: "");

        result.Should().Be(NoCredentialsScheme);
    }

    [Fact]
    public void SelectScheme_WithoutBearerPrefix_ReturnsNoCredentialsScheme()
    {
        var result = Select(authorizationHeader: "Basic dXNlcjpwYXNz");

        result.Should().Be(NoCredentialsScheme);
    }

    [Fact]
    public void SelectScheme_WithUnreadableBearerToken_ReturnsNoCredentialsScheme()
    {
        var result = Select(authorizationHeader: "Bearer not-a-real-jwt");

        result.Should().Be(NoCredentialsScheme);
    }

    [Fact]
    public void SelectScheme_WithInternalIssuer_ReturnsInternalScheme()
    {
        var token = CreateUnsignedToken(InternalIssuer);

        var result = Select(authorizationHeader: $"Bearer {token}");

        result.Should().Be(InternalScheme);
    }

    [Fact]
    public void SelectScheme_WithEntraIssuer_ReturnsEntraScheme()
    {
        var token = CreateUnsignedToken(EntraIssuer);

        var result = Select(authorizationHeader: $"Bearer {token}");

        result.Should().Be(EntraScheme);
    }

    [Fact]
    public void SelectScheme_WithUnknownIssuer_ReturnsNoCredentialsScheme()
    {
        var token = CreateUnsignedToken("https://attacker.example.com");

        var result = Select(authorizationHeader: $"Bearer {token}");

        result.Should().Be(NoCredentialsScheme);
    }

    [Fact]
    public void SelectScheme_IsCaseInsensitiveForBearerPrefix()
    {
        var token = CreateUnsignedToken(InternalIssuer);

        var result = Select(authorizationHeader: $"bearer {token}");

        result.Should().Be(InternalScheme);
    }

    private static string Select(string authorizationHeader)
    {
        return JwtSchemeSelector.SelectScheme(
            authorizationHeader,
            InternalIssuer,
            EntraIssuer,
            InternalScheme,
            EntraScheme,
            NoCredentialsScheme);
    }

    private static string CreateUnsignedToken(string issuer)
    {
        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: "any-audience",
            expires: DateTime.UtcNow.AddMinutes(15));

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
