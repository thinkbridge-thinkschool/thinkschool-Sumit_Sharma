using FluentAssertions;
using QuotesApi.Models;

namespace Quotes.Tests.Unit;

public class QuoteFactoryTests
{
    [Fact]
    public void Create_WithValidAuthorAndText_ReturnsQuoteWithExpectedValues()
    {
        var author = "Albert Einstein";
        var text = "Life is like riding a bicycle.";

        var quote = Quote.Create(author, text);

        quote.Should().NotBeNull();
        quote.Author.Should().Be(author);
        quote.Text.Should().Be(text);
        quote.IsDeleted.Should().BeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WithInvalidAuthor_ThrowsArgumentException(string? author)
    {
        var action = () => Quote.Create(author!, "Some quote text");

        action.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Create_WithAuthorExceeding200Characters_ThrowsArgumentException()
    {
        var author = new string('A', 201);

        var action = () => Quote.Create(author, "Some quote text");

        action.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Create_WithAuthorAt200CharacterBoundary_Succeeds()
    {
        var author = new string('A', 200);

        var quote = Quote.Create(author, "Some quote text");

        quote.Author.Should().Be(author);
        quote.Author.Should().HaveLength(200);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WithInvalidText_ThrowsArgumentException(string? text)
    {
        var action = () => Quote.Create("Some Author", text!);

        action.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Create_WithTextExceeding1000Characters_ThrowsArgumentException()
    {
        var text = new string('A', 1001);

        var action = () => Quote.Create("Some Author", text);

        action.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Create_WithTextAt1000CharacterBoundary_Succeeds()
    {
        var text = new string('A', 1000);

        var quote = Quote.Create("Some Author", text);

        quote.Text.Should().Be(text);
        quote.Text.Should().HaveLength(1000);
    }

    [Fact]
    public void MarkDeleted_OnNewQuote_SetsIsDeletedToTrue()
    {
        var quote = Quote.Create("Some Author", "Some quote text");

        quote.MarkDeleted();

        quote.IsDeleted.Should().BeTrue();
    }
}
