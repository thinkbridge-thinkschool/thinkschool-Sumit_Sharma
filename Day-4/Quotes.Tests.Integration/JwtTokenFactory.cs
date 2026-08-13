using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace Quotes.Tests.Integration;

public static class JwtTokenFactory
{
    public static string CreateAccessToken(
        QuotesApiFactory factory,
        int userId,
        string? scope = null,
        DateTime? expires = null)
    {
        var configuration = factory.Services
            .GetRequiredService<IConfiguration>();

        var key = configuration["Jwt:Key"]!;
        var issuer = configuration["Jwt:Issuer"]!;
        var audience = configuration["Jwt:Audience"]!;

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId.ToString())
        };

        if (scope is not null)
            claims.Add(new Claim("scope", scope));

        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)),
            SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: claims,
            expires: expires ?? DateTime.UtcNow.AddMinutes(15),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
