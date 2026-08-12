using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuotesApi.Data;

namespace Quotes.Tests.Integration.Endpoints;

[Collection(IntegrationTestCollection.Name)]
public sealed class QuoteEndpointsTests : IDisposable
{
    private readonly QuotesApiFactory factory;
    private readonly HttpClient client;

    public QuoteEndpointsTests(MsSqlContainerFixture sqlFixture)
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
    public async Task CreateQuote_WithScopeClaim_Returns201AndPersistsQuote()
    {
        Authorize(userId: 1, scope: "quotes.write");

        var response = await client.PostAsJsonAsync(
            "/api/quotes",
            new { author = "Ada Lovelace", text = "That which is not exact is nothing." });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var persisted = await db.Quotes.SingleAsync();

        Assert.Equal("Ada Lovelace", persisted.Author);
        Assert.Equal("That which is not exact is nothing.", persisted.Text);
    }

    [Fact]
    public async Task CreateQuote_Unauthenticated_Returns401()
    {
        var response = await client.PostAsJsonAsync(
            "/api/quotes",
            new { author = "Ada Lovelace", text = "A quote." });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task CreateQuote_WithoutScopeClaim_Returns403()
    {
        Authorize(userId: 1, scope: null);

        var response = await client.PostAsJsonAsync(
            "/api/quotes",
            new { author = "Ada Lovelace", text = "A quote." });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task CreateQuote_WithEmptyAuthor_ReturnsValidationProblem()
    {
        Authorize(userId: 1, scope: "quotes.write");

        var response = await client.PostAsJsonAsync(
            "/api/quotes",
            new { author = "", text = "A quote." });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var problem = await response.Content
            .ReadFromJsonAsync<ValidationProblemDto>(TestJson.Options);

        Assert.NotNull(problem);
        Assert.Contains("quote", problem!.Errors.Keys);
    }

    [Fact]
    public async Task GetQuoteById_WhenNotFound_Returns404()
    {
        var response = await client.GetAsync("/api/quotes/999999");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task DeleteQuote_AsAuthorized_SoftDeletesAndPersistsFlag()
    {
        Authorize(userId: 1, scope: "quotes.write");
        var quoteId = await CreateQuoteAsync();

        var response = await client.DeleteAsync($"/api/quotes/{quoteId}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var persisted = await db.Quotes.SingleAsync(q => q.Id == quoteId);

        Assert.True(persisted.IsDeleted);
    }

    [Fact]
    public async Task GetQuotes_ReturnsPagedResults()
    {
        Authorize(userId: 1, scope: "quotes.write");
        await CreateQuoteAsync();
        await CreateQuoteAsync();
        await CreateQuoteAsync();
        Unauthorize();

        var response = await client.GetAsync("/api/quotes?page=1&size=2");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var quotes = await response.Content
            .ReadFromJsonAsync<List<QuoteDto>>(TestJson.Options);

        Assert.NotNull(quotes);
        Assert.Equal(2, quotes!.Count);
    }

    private async Task<int> CreateQuoteAsync()
    {
        var response = await client.PostAsJsonAsync(
            "/api/quotes",
            new { author = "Author", text = $"Quote {Guid.NewGuid()}" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var location = response.Headers.Location!.OriginalString;

        return int.Parse(location.Split('/')[^1]);
    }

    private void Authorize(int userId, string? scope)
    {
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue(
                "Bearer",
                JwtTokenFactory.CreateAccessToken(factory, userId, scope));
    }

    private void Unauthorize()
    {
        client.DefaultRequestHeaders.Authorization = null;
    }

    private sealed record QuoteDto(int Id, string Author, string Text, bool IsDeleted);

    private sealed record ValidationProblemDto(
        [property: JsonPropertyName("errors")] Dictionary<string, string[]> Errors);
}
