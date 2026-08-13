using FluentAssertions;
using NSubstitute;
using QuotesApi.Models;
using QuotesApi.Time;

namespace Quotes.Tests.Unit;

public class CollectionClockTests
{
    [Fact]
    public void AddItem_WithFakeClock_SetsAddedAtToClockValue()
    {
        var expectedTime = new DateTimeOffset(2026, 8, 12, 9, 30, 0, TimeSpan.Zero);
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(expectedTime);
        var collection = new Collection("My Quotes", 1);

        collection.AddItem(1, clock);

        collection.Items.Single().AddedAt.Should().Be(expectedTime);
    }

    [Fact]
    public void AddItem_CalledTwiceWithDifferingClockValues_UsesEachCallsClockValue()
    {
        var firstTime = new DateTimeOffset(2026, 8, 12, 9, 30, 0, TimeSpan.Zero);
        var secondTime = new DateTimeOffset(2026, 8, 12, 9, 45, 0, TimeSpan.Zero);
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(firstTime, secondTime);
        var collection = new Collection("My Quotes", 1);

        collection.AddItem(1, clock);
        collection.AddItem(2, clock);

        collection.Items.Single(item => item.QuoteId == 1).AddedAt.Should().Be(firstTime);
        collection.Items.Single(item => item.QuoteId == 2).AddedAt.Should().Be(secondTime);
    }

    [Fact]
    public void AddItem_WhenQuoteAlreadyInCollection_ThrowsWithoutReadingClockAgain()
    {
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(DateTimeOffset.UtcNow);
        var collection = new Collection("My Quotes", 1);
        collection.AddItem(1, clock);

        var action = () => collection.AddItem(1, clock);

        action.Should().Throw<InvalidOperationException>();
        _ = clock.Received(1).UtcNow;
    }
}
