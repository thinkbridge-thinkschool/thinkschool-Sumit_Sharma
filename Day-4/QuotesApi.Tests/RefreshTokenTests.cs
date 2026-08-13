using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using QuotesApi.Data;
using QuotesApi.Models;

namespace QuotesApi.Tests;

[TestClass]
public class RefreshTokenTests
{
    [TestMethod]
    public async Task Refresh_WithValidToken_Succeeds()
    {
        using var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateClient();

        var (email, password) = await SeedUserAsync(factory);

        var login = await LoginAsync(client, email, password);

        var refreshResponse = await client.PostAsJsonAsync(
            "/api/auth/refresh",
            new { refreshToken = login.RefreshToken });

        Assert.AreEqual(HttpStatusCode.OK, refreshResponse.StatusCode);
    }

    [TestMethod]
    public async Task Refresh_WithReusedToken_Returns401()
    {
        using var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateClient();

        var (email, password) = await SeedUserAsync(factory);

        var login = await LoginAsync(client, email, password);

        var firstRefresh = await client.PostAsJsonAsync(
            "/api/auth/refresh",
            new { refreshToken = login.RefreshToken });

        Assert.AreEqual(HttpStatusCode.OK, firstRefresh.StatusCode);

        var reuseResponse = await client.PostAsJsonAsync(
            "/api/auth/refresh",
            new { refreshToken = login.RefreshToken });

        Assert.AreEqual(HttpStatusCode.Unauthorized, reuseResponse.StatusCode);
    }

    [TestMethod]
    public async Task Refresh_AfterLogout_Returns401()
    {
        using var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateClient();

        var (email, password) = await SeedUserAsync(factory);

        var login = await LoginAsync(client, email, password);

        var logoutResponse = await client.PostAsJsonAsync(
            "/api/auth/logout",
            new { refreshToken = login.RefreshToken });

        Assert.AreEqual(HttpStatusCode.NoContent, logoutResponse.StatusCode);

        var refreshResponse = await client.PostAsJsonAsync(
            "/api/auth/refresh",
            new { refreshToken = login.RefreshToken });

        Assert.AreEqual(HttpStatusCode.Unauthorized, refreshResponse.StatusCode);
    }

    private static async Task<TokenResponse> LoginAsync(
        HttpClient client,
        string email,
        string password)
    {
        var loginResponse = await client.PostAsJsonAsync(
            "/api/auth/login",
            new { email, password });

        Assert.AreEqual(HttpStatusCode.OK, loginResponse.StatusCode);

        return (await loginResponse.Content
            .ReadFromJsonAsync<TokenResponse>())!;
    }

    private static async Task<(string Email, string Password)> SeedUserAsync(
        WebApplicationFactory<Program> factory)
    {
        var email = $"user-{Guid.NewGuid()}@example.com";
        const string password = "P@ssw0rd!";

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider
            .GetRequiredService<AppDbContext>();

        db.Users.Add(new User
        {
            Email = email,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(password)
        });

        await db.SaveChangesAsync();

        return (email, password);
    }

    private sealed record TokenResponse
    {
        [JsonPropertyName("access_token")]
        public string AccessToken { get; init; } = "";

        [JsonPropertyName("refresh_token")]
        public string RefreshToken { get; init; } = "";

        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; init; }
    }
}
