using FluentAssertions;
using QuotesApi.Models;

namespace Tests.Domain;

public class CollectionItemTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void NonPositiveQuoteId_ShouldThrow(int quoteId)
    {
        var action = () => new CollectionItem(quoteId, DateTimeOffset.UtcNow);

        action.Should().Throw<ArgumentException>();
    }
}
