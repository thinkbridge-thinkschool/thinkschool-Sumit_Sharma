using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuotesApi.Data;
using QuotesApi.Models;

namespace Quotes.Tests.Integration.Endpoints;

[Collection(IntegrationTestCollection.Name)]
public sealed class AuthEndpointsTests : IDisposable
{
    private readonly QuotesApiFactory factory;
    private readonly HttpClient client;

    public AuthEndpointsTests(MsSqlContainerFixture sqlFixture)
    {
        factory = new QuotesApiFactory(sqlFixture.ConnectionString);
        client = factory.CreateClient();
    }

    public void Dispose()
    {
        client.Dispose();
        factory.Dispose();
    }

    [Fact]
    public async Task Login_WithValidCredentials_ReturnsTokenPair()
    {
        var (email, password) = await SeedUserAsync();

        var response = await client.PostAsJsonAsync(
            "/api/auth/login",
            new { email, password });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var tokens = await response.Content
            .ReadFromJsonAsync<TokenResponseDto>();

        Assert.NotNull(tokens);
        Assert.False(string.IsNullOrWhiteSpace(tokens!.AccessToken));
        Assert.False(string.IsNullOrWhiteSpace(tokens.RefreshToken));
        Assert.Equal(900, tokens.ExpiresIn);
    }

    [Fact]
    public async Task Login_WithInvalidPassword_Returns401()
    {
        var (email, _) = await SeedUserAsync();

        var response = await client.PostAsJsonAsync(
            "/api/auth/login",
            new { email, password = "WrongPassword!" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Refresh_WithReusedToken_Returns401AndRevokesChainInDatabase()
    {
        var (email, password) = await SeedUserAsync();
        var login = await LoginAsync(email, password);

        var firstRefresh = await client.PostAsJsonAsync(
            "/api/auth/refresh",
            new { refreshToken = login.RefreshToken });

        Assert.Equal(HttpStatusCode.OK, firstRefresh.StatusCode);

        var reuseResponse = await client.PostAsJsonAsync(
            "/api/auth/refresh",
            new { refreshToken = login.RefreshToken });

        Assert.Equal(HttpStatusCode.Unauthorized, reuseResponse.StatusCode);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var revokedCount = await db.RefreshTokens
            .CountAsync(token => token.RevokedAt != null);

        Assert.Equal(2, revokedCount);
    }

    private async Task<TokenResponseDto> LoginAsync(string email, string password)
    {
        var response = await client.PostAsJsonAsync(
            "/api/auth/login",
            new { email, password });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return (await response.Content
            .ReadFromJsonAsync<TokenResponseDto>())!;
    }

    private async Task<(string Email, string Password)> SeedUserAsync()
    {
        var email = $"user-{Guid.NewGuid()}@example.com";
        const string password = "P@ssw0rd!";

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        db.Users.Add(new User
        {
            Email = email,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(password)
        });

        await db.SaveChangesAsync();

        return (email, password);
    }

    private sealed record TokenResponseDto
    {
        [JsonPropertyName("access_token")]
        public string AccessToken { get; init; } = "";

        [JsonPropertyName("refresh_token")]
        public string RefreshToken { get; init; } = "";

        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; init; }
    }
}
