using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NSubstitute;
using QuotesApi.Data;
using QuotesApi.Repositories;

namespace Quotes.Tests.Unit;

public class CollectionRepositoryTests
{
    [Fact]
    public async Task DeleteAsync_WithNonExistentId_ReturnsFalse()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .Options;

        using var db = new AppDbContext(options);
        await db.Database.EnsureCreatedAsync();

        var sut = new CollectionRepository(
            db,
            Substitute.For<ILogger<CollectionRepository>>());

        var deleted = await sut.DeleteAsync(
            999,
            CancellationToken.None);

        deleted.Should().BeFalse();
    }
}
