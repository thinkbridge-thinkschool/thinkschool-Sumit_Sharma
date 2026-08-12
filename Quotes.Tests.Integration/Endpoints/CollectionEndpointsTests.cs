using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuotesApi.Data;

namespace Quotes.Tests.Integration.Endpoints;

[Collection(IntegrationTestCollection.Name)]
public sealed class CollectionEndpointsTests : IDisposable
{
    private readonly QuotesApiFactory factory;
    private readonly HttpClient client;

    public CollectionEndpointsTests(MsSqlContainerFixture sqlFixture)
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
    public async Task CreateCollection_WithValidData_Returns201()
    {
        Authorize(userId: 100);

        var response = await client.PostAsJsonAsync(
            "/api/collections",
            new { name = "My Quotes", ownerId = 100 });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(response.Headers.Location);
    }

    [Fact]
    public async Task CreateCollection_WithNameTooShort_ReturnsProblem400()
    {
        Authorize(userId: 100);

        var response = await client.PostAsJsonAsync(
            "/api/collections",
            new { name = "ab", ownerId = 100 });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task AddQuoteToCollection_AsOwner_UsesInjectedClockForAddedAt()
    {
        var collectionId = await CreateCollectionAsync(ownerId: 201);
        var quoteId = await CreateQuoteAsync();

        var expectedTime = new DateTimeOffset(2026, 3, 4, 12, 0, 0, TimeSpan.Zero);
        factory.Clock.UtcNow = expectedTime;

        Authorize(userId: 201);

        var response = await client.PostAsync(
            $"/api/collections/{collectionId}/quotes/{quoteId}",
            content: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var collection = await response.Content
            .ReadFromJsonAsync<CollectionDto>(TestJson.Options);

        Assert.NotNull(collection);
        var item = Assert.Single(collection!.Items);
        Assert.Equal(quoteId, item.QuoteId);
        Assert.Equal(expectedTime, item.AddedAt);
    }

    [Fact]
    public async Task AddQuoteToCollection_AsNonOwner_Returns403()
    {
        var collectionId = await CreateCollectionAsync(ownerId: 202);
        var quoteId = await CreateQuoteAsync();

        Authorize(userId: 999);

        var response = await client.PostAsync(
            $"/api/collections/{collectionId}/quotes/{quoteId}",
            content: null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task AddQuoteToCollection_WhenCollectionMissing_Returns404()
    {
        var quoteId = await CreateQuoteAsync();

        Authorize(userId: 203);

        var response = await client.PostAsync(
            $"/api/collections/999999/quotes/{quoteId}",
            content: null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task DeleteCollection_AsOwner_Returns204AndRemovesFromDatabase()
    {
        var collectionId = await CreateCollectionAsync(ownerId: 301);

        Authorize(userId: 301);

        var response = await client.DeleteAsync($"/api/collections/{collectionId}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var stillExists = await db.Collections
            .AnyAsync(c => c.Id == collectionId);

        Assert.False(stillExists);
    }

    private async Task<int> CreateCollectionAsync(int ownerId)
    {
        Authorize(ownerId);

        var response = await client.PostAsJsonAsync(
            "/api/collections",
            new { name = $"Collection {Guid.NewGuid()}", ownerId });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var location = response.Headers.Location!.OriginalString;

        Unauthorize();

        return int.Parse(location.Split('/')[^1]);
    }

    private async Task<int> CreateQuoteAsync()
    {
        Authorize(userId: 1, scope: "quotes.write");

        var response = await client.PostAsJsonAsync(
            "/api/quotes",
            new { author = "Author", text = $"Quote {Guid.NewGuid()}" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var location = response.Headers.Location!.OriginalString;

        Unauthorize();

        return int.Parse(location.Split('/')[^1]);
    }

    private void Authorize(int userId, string? scope = null)
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

    private sealed record CollectionDto(
        int Id,
        string Name,
        int OwnerId,
        List<CollectionItemDto> Items);

    private sealed record CollectionItemDto(int QuoteId, DateTimeOffset AddedAt);
}
