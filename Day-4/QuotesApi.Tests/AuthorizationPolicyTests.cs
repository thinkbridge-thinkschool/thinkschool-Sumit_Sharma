using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace QuotesApi.Tests;

[TestClass]
public class AuthorizationPolicyTests
{
    [TestMethod]
    public async Task CreateQuote_WithScopeClaim_Succeeds()
    {
        using var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateClient();

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue(
                "Bearer",
                CreateAccessToken(factory, userId: 1, scope: "quotes.write"));

        var response = await client.PostAsJsonAsync(
            "/api/quotes",
            new { author = "Ada Lovelace", text = "That which is not exact is nothing." });

        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);
    }

    [TestMethod]
    public async Task CreateQuote_WithoutScopeClaim_Returns403()
    {
        using var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateClient();

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue(
                "Bearer",
                CreateAccessToken(factory, userId: 1, scope: null));

        var response = await client.PostAsJsonAsync(
            "/api/quotes",
            new { author = "Ada Lovelace", text = "That which is not exact is nothing." });

        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [TestMethod]
    public async Task DeleteCollection_AsOwner_Succeeds()
    {
        using var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateClient();

        var collectionId = await CreateCollectionAsync(
            client,
            factory,
            ownerId: 501);

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue(
                "Bearer",
                CreateAccessToken(factory, userId: 501, scope: null));

        var response = await client.DeleteAsync(
            $"/api/collections/{collectionId}");

        Assert.AreEqual(HttpStatusCode.NoContent, response.StatusCode);
    }

    [TestMethod]
    public async Task DeleteCollection_AsNonOwner_Returns403()
    {
        using var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateClient();

        var collectionId = await CreateCollectionAsync(
            client,
            factory,
            ownerId: 502);

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue(
                "Bearer",
                CreateAccessToken(factory, userId: 999, scope: null));

        var response = await client.DeleteAsync(
            $"/api/collections/{collectionId}");

        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [TestMethod]
    public async Task DeleteQuote_WithScopeClaim_Succeeds()
    {
        using var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateClient();

        var quoteId = await CreateQuoteAsync(client, factory);

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue(
                "Bearer",
                CreateAccessToken(factory, userId: 1, scope: "quotes.write"));

        var response = await client.DeleteAsync(
            $"/api/quotes/{quoteId}");

        Assert.AreEqual(HttpStatusCode.NoContent, response.StatusCode);
    }

    [TestMethod]
    public async Task DeleteQuote_WithoutScopeClaim_Returns403()
    {
        using var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateClient();

        var quoteId = await CreateQuoteAsync(client, factory);

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue(
                "Bearer",
                CreateAccessToken(factory, userId: 1, scope: null));

        var response = await client.DeleteAsync(
            $"/api/quotes/{quoteId}");

        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [TestMethod]
    public async Task AddItemToCollection_AsOwner_Succeeds()
    {
        using var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateClient();

        var collectionId = await CreateCollectionAsync(
            client,
            factory,
            ownerId: 601);

        var quoteId = await CreateQuoteAsync(client, factory);

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue(
                "Bearer",
                CreateAccessToken(factory, userId: 601, scope: null));

        var response = await client.PostAsync(
            $"/api/collections/{collectionId}/quotes/{quoteId}",
            content: null);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
    }

    [TestMethod]
    public async Task AddItemToCollection_AsNonOwner_Returns403()
    {
        using var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateClient();

        var collectionId = await CreateCollectionAsync(
            client,
            factory,
            ownerId: 602);

        var quoteId = await CreateQuoteAsync(client, factory);

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue(
                "Bearer",
                CreateAccessToken(factory, userId: 999, scope: null));

        var response = await client.PostAsync(
            $"/api/collections/{collectionId}/quotes/{quoteId}",
            content: null);

        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [TestMethod]
    public async Task RemoveItemFromCollection_AsOwner_Succeeds()
    {
        using var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateClient();

        var collectionId = await CreateCollectionAsync(
            client,
            factory,
            ownerId: 603);

        var quoteId = await CreateQuoteAsync(client, factory);

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue(
                "Bearer",
                CreateAccessToken(factory, userId: 603, scope: null));

        await client.PostAsync(
            $"/api/collections/{collectionId}/quotes/{quoteId}",
            content: null);

        var response = await client.DeleteAsync(
            $"/api/collections/{collectionId}/quotes/{quoteId}");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
    }

    [TestMethod]
    public async Task RemoveItemFromCollection_AsNonOwner_Returns403()
    {
        using var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateClient();

        var collectionId = await CreateCollectionAsync(
            client,
            factory,
            ownerId: 604);

        var quoteId = await CreateQuoteAsync(client, factory);

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue(
                "Bearer",
                CreateAccessToken(factory, userId: 604, scope: null));

        await client.PostAsync(
            $"/api/collections/{collectionId}/quotes/{quoteId}",
            content: null);

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue(
                "Bearer",
                CreateAccessToken(factory, userId: 999, scope: null));

        var response = await client.DeleteAsync(
            $"/api/collections/{collectionId}/quotes/{quoteId}");

        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [TestMethod]
    public async Task CreateQuote_Unauthenticated_Returns401()
    {
        using var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/quotes",
            new { author = "Ada Lovelace", text = "That which is not exact is nothing." });

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task CreateQuote_WithExpiredToken_Returns401()
    {
        using var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateClient();

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue(
                "Bearer",
                CreateAccessToken(
                    factory,
                    userId: 1,
                    scope: "quotes.write",
                    expires: DateTime.UtcNow.AddMinutes(-10)));

        var response = await client.PostAsJsonAsync(
            "/api/quotes",
            new { author = "Ada Lovelace", text = "That which is not exact is nothing." });

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static async Task<int> CreateCollectionAsync(
        HttpClient client,
        WebApplicationFactory<Program> factory,
        int ownerId)
    {
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue(
                "Bearer",
                CreateAccessToken(factory, userId: ownerId, scope: null));

        var response = await client.PostAsJsonAsync(
            "/api/collections",
            new { name = $"Collection {Guid.NewGuid()}", ownerId });

        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);

        var location = response.Headers.Location!.OriginalString;

        return int.Parse(location.Split('/')[^1]);
    }

    private static async Task<int> CreateQuoteAsync(
        HttpClient client,
        WebApplicationFactory<Program> factory)
    {
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue(
                "Bearer",
                CreateAccessToken(factory, userId: 1, scope: "quotes.write"));

        var response = await client.PostAsJsonAsync(
            "/api/quotes",
            new { author = "Ada Lovelace", text = $"Quote {Guid.NewGuid()}" });

        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);

        var location = response.Headers.Location!.OriginalString;

        return int.Parse(location.Split('/')[^1]);
    }

    private static string CreateAccessToken(
        WebApplicationFactory<Program> factory,
        int userId,
        string? scope,
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
