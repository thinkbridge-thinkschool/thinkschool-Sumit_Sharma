using FluentAssertions;
using QuotesApi.Models;
using QuotesApi.Time;

namespace Tests.Domain;

public class CollectionTests
{
    private static Collection CreateCollection()
    {
        return new Collection("My Quotes", 1);
    }

    private static IClock CreateClock()
    {
        return new FakeClock(
            new DateTimeOffset(
                2026,
                8,
                11,
                10,
                0,
                0,
                TimeSpan.Zero));
    }

    [Fact]
    public void EmptyName_ShouldThrow()
    {
        var action = () => new Collection("", 1);

        action.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void NameOver80Characters_ShouldThrow()
    {
        var longName = new string('A', 81);

        var action = () => new Collection(longName, 1);

        action.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void NonPositiveOwnerId_ShouldThrow(int ownerId)
    {
        var action = () => new Collection("My Quotes", ownerId);

        action.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Rename_WithValidName_UpdatesName()
    {
        var collection = CreateCollection();

        collection.Rename("Updated Quotes");

        collection.Name.Should().Be("Updated Quotes");
    }

    [Fact]
    public void Rename_WithInvalidName_ShouldThrow()
    {
        var collection = CreateCollection();

        var action = () => collection.Rename("ab");

        action.Should().Throw<ArgumentException>();
        collection.Name.Should().Be("My Quotes");
    }

    [Fact]
    public void Adding51stItem_ShouldThrow()
    {
        var collection = CreateCollection();
        var clock = CreateClock();

        for (var i = 1; i <= 50; i++)
        {
            collection.AddItem(i, clock);
        }

        var action = () => collection.AddItem(51, clock);

        action.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void DuplicateQuoteId_ShouldThrow()
    {
        var collection = CreateCollection();
        var clock = CreateClock();

        collection.AddItem(1, clock);

        var action = () => collection.AddItem(1, clock);

        action.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void RemovingNonExistentItem_ShouldThrow()
    {
        var collection = CreateCollection();

        var action = () => collection.RemoveItem(999);

        action.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void AddingThenRemovingItem_ShouldLeaveZeroItems()
    {
        var collection = CreateCollection();
        var clock = CreateClock();

        collection.AddItem(1, clock);
        collection.RemoveItem(1);

        collection.Items.Should().BeEmpty();
    }

    private sealed class FakeClock : IClock
    {
        public FakeClock(DateTimeOffset utcNow)
        {
            UtcNow = utcNow;
        }

        public DateTimeOffset UtcNow { get; }
    }
}
