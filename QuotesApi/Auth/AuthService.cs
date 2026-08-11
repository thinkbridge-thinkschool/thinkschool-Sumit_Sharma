using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using QuotesApi.Data;
using QuotesApi.Models;

namespace QuotesApi.Auth;

public class AuthService : IAuthService
{
    private readonly AppDbContext db;
    private readonly IConfiguration configuration;
    private readonly ILogger<AuthService> logger;

    public AuthService(
        AppDbContext db,
        IConfiguration configuration,
        ILogger<AuthService> logger)
    {
        this.db = db;
        this.configuration = configuration;
        this.logger = logger;
    }

    public async Task<TokenPair?> LoginAsync(
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

        if (!BCrypt.Net.BCrypt.Verify(
                password,
                user.PasswordHash))
        {
            return null;
        }

        return await CreateTokenPairAsync(
            user,
            cancellationToken);
    }

    public async Task<TokenPair?> RefreshAsync(
        string refreshToken,
        CancellationToken cancellationToken)
    {
        var tokenHash = HashToken(refreshToken);

        var storedToken = await db.RefreshTokens
            .FirstOrDefaultAsync(
                token => token.Token == tokenHash,
                cancellationToken);

        if (storedToken is null)
            return null;

        if (storedToken.RevokedAt is not null)
        {
            if (storedToken.ReplacedByToken is not null)
            {
                logger.LogWarning(
                    "Refresh token reuse detected for user {UserId}",
                    storedToken.UserId);

                await RevokeTokenChainAsync(
                    storedToken,
                    cancellationToken);
            }

            return null;
        }

        if (storedToken.ExpiresAt <= DateTime.UtcNow)
            return null;

        var user = await db.Users
            .FirstOrDefaultAsync(
                user => user.Id == storedToken.UserId,
                cancellationToken);

        if (user is null)
            return null;

        var newRefreshToken = GenerateRefreshToken();
        var newRefreshHash = HashToken(newRefreshToken);

        storedToken.RevokedAt = DateTime.UtcNow;
        storedToken.ReplacedByToken = newRefreshHash;

        db.RefreshTokens.Add(new RefreshToken
        {
            Token = newRefreshHash,
            UserId = user.Id,
            ExpiresAt = DateTime.UtcNow.AddDays(7)
        });

        await db.SaveChangesAsync(cancellationToken);

        var accessToken = CreateAccessToken(user);

        return new TokenPair(
            accessToken,
            newRefreshToken,
            900);
    }

    public async Task LogoutAsync(
        string refreshToken,
        CancellationToken cancellationToken)
    {
        var tokenHash = HashToken(refreshToken);

        var storedToken = await db.RefreshTokens
            .FirstOrDefaultAsync(
                token => token.Token == tokenHash,
                cancellationToken);

        if (storedToken is null ||
            storedToken.RevokedAt is not null)
        {
            return;
        }

        storedToken.RevokedAt = DateTime.UtcNow;

        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task<TokenPair> CreateTokenPairAsync(
        User user,
        CancellationToken cancellationToken)
    {
        var refreshToken = GenerateRefreshToken();

        db.RefreshTokens.Add(new RefreshToken
        {
            Token = HashToken(refreshToken),
            UserId = user.Id,
            ExpiresAt = DateTime.UtcNow.AddDays(7)
        });

        await db.SaveChangesAsync(cancellationToken);

        return new TokenPair(
            CreateAccessToken(user),
            refreshToken,
            900);
    }

    private string CreateAccessToken(User user)
    {
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

        var credentials = new SigningCredentials(
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

    private static string GenerateRefreshToken()
    {
        return Base64UrlEncoder.Encode(
            RandomNumberGenerator.GetBytes(64));
    }

    private static string HashToken(string token)
    {
        return Convert.ToBase64String(
            SHA256.HashData(
                Encoding.UTF8.GetBytes(token)));
    }

    private async Task RevokeTokenChainAsync(
        RefreshToken token,
        CancellationToken cancellationToken)
    {
        var current = token;

        while (current.ReplacedByToken is not null)
        {
            var next = await db.RefreshTokens
                .FirstOrDefaultAsync(
                    item => item.Token == current.ReplacedByToken,
                    cancellationToken);

            if (next is null)
                break;

            if (next.RevokedAt is null)
                next.RevokedAt = DateTime.UtcNow;

            current = next;
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
