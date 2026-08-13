using System.Text;
using Microsoft.Extensions.Configuration;

namespace QuotesApi.Auth;

public sealed record JwtAuthenticationOptions(
    string Key,
    string Issuer,
    string Audience,
    string EntraAuthority,
    string EntraAudience);

public static class JwtAuthenticationOptionsFactory
{
    public static JwtAuthenticationOptions Create(
        IConfiguration configuration)
    {
        var key = configuration["Jwt:Key"]
            ?? throw new InvalidOperationException(
                "JWT key is not configured.");

        if (Encoding.UTF8.GetByteCount(key) < 32)
        {
            throw new InvalidOperationException(
                "JWT key must be at least 256 bits.");
        }

        var issuer = configuration["Jwt:Issuer"]
            ?? throw new InvalidOperationException(
                "JWT issuer is not configured.");

        var audience = configuration["Jwt:Audience"]
            ?? throw new InvalidOperationException(
                "JWT audience is not configured.");

        var entraAuthority = configuration["Entra:Authority"]
            ?? throw new InvalidOperationException(
                "Entra authority is not configured.");

        var entraAudience = configuration["Entra:Audience"]
            ?? throw new InvalidOperationException(
                "Entra audience is not configured.");

        return new JwtAuthenticationOptions(
            key,
            issuer,
            audience,
            entraAuthority,
            entraAudience);
    }
}
