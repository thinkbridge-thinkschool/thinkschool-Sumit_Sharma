using System.Diagnostics;
using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace QuotesApi.Tests;

[TestClass]
public class GetQuoteByIdEndpointTests
{
    [TestMethod]
    public async Task CreateQuote_ThenGetById_Returns200AndMatchingQuote()
    {
        using var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateClient();

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue(
                "Bearer",
                CreateAccessToken(factory, userId: 1, scope: "quotes.write"));

        var createResponse = await client.PostAsJsonAsync(
            "/api/quotes",
            new { author = "Day 5 Author", text = "Day 5 sample quote." });

        Assert.AreEqual(
            System.Net.HttpStatusCode.Created,
            createResponse.StatusCode);

        var location = createResponse.Headers.Location!.OriginalString;
        var quoteId = int.Parse(location.Split('/')[^1]);

        client.DefaultRequestHeaders.Authorization = null;

        var stopwatch = Stopwatch.StartNew();
        var getResponse = await client.GetAsync($"/api/quotes/{quoteId}");
        stopwatch.Stop();

        Assert.AreEqual(
            System.Net.HttpStatusCode.OK,
            getResponse.StatusCode);

        var quote = await getResponse.Content
            .ReadFromJsonAsync<QuoteDto>();

        Assert.IsNotNull(quote);
        Assert.AreEqual(quoteId, quote!.Id);
        Assert.AreEqual("Day 5 Author", quote.Author);

        // Regression guard: this endpoint previously carried an intentional
        // 1500ms delay (Day 5 Task 1). It must stay well under that.
        Assert.IsLessThan(1000, stopwatch.ElapsedMilliseconds);
    }

    [TestMethod]
    public async Task GetQuoteById_WhenNotFound_Returns404()
    {
        using var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/quotes/999999");

        Assert.AreEqual(
            System.Net.HttpStatusCode.NotFound,
            response.StatusCode);
    }

    private static string CreateAccessToken(
        WebApplicationFactory<Program> factory,
        int userId,
        string? scope)
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
            expires: DateTime.UtcNow.AddMinutes(15),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private sealed record QuoteDto(
        int Id,
        string Author,
        string Text,
        [property: JsonPropertyName("isDeleted")] bool IsDeleted);
}
