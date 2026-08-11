using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using QuotesApi.Data;

namespace QuotesApi.Auth;

public class AuthService : IAuthService
{
    private readonly AppDbContext db;
    private readonly IConfiguration configuration;

    public AuthService(
        AppDbContext db,
        IConfiguration configuration)
    {
        this.db = db;
        this.configuration = configuration;
    }

    public async Task<string?> LoginAsync(
        string email,
        string password,
        CancellationToken cancellationToken)
    {
        var user = await db.Users
            .FirstOrDefaultAsync(
                user => user.Email == email,
                cancellationToken);

        if (user is null)
            return null;

        var validPassword =
            BCrypt.Net.BCrypt.Verify(
                password,
                user.PasswordHash);

        if (!validPassword)
            return null;

        var key = configuration["Jwt:Key"]
            ?? throw new InvalidOperationException(
                "JWT key is not configured.");

        var issuer = configuration["Jwt:Issuer"]
            ?? throw new InvalidOperationException(
                "JWT issuer is not configured.");

        var audience = configuration["Jwt:Audience"]
            ?? throw new InvalidOperationException(
                "JWT audience is not configured.");

        var claims = new[]
        {
            new Claim(
                ClaimTypes.NameIdentifier,
                user.Id.ToString()),

            new Claim(
                ClaimTypes.Email,
                user.Email)
        };

        var credentials =
            new SigningCredentials(
                new SymmetricSecurityKey(
                    Encoding.UTF8.GetBytes(key)),
                SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(15),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler()
            .WriteToken(token);
    }
}
