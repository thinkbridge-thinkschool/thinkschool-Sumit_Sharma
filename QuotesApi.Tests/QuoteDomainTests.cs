using Microsoft.VisualStudio.TestTools.UnitTesting;
using QuotesApi.Models;

namespace QuotesApi.Tests;

[TestClass]
public class QuoteDomainTests
{
    [TestMethod]
    public void Create_WithValidData_CreatesQuote()
    {
        var quote = Quote.Create(
            "Albert Einstein",
            "Life is like riding a bicycle.");

        Assert.AreEqual(
            "Albert Einstein",
            quote.Author);

        Assert.AreEqual(
            "Life is like riding a bicycle.",
            quote.Text);

        Assert.IsFalse(quote.IsDeleted);
    }

    [TestMethod]
    public void Create_WithEmptyAuthor_Throws()
    {
        try
        {
            Quote.Create(
                "",
                "Some quote");

            Assert.Fail("Expected an ArgumentException.");
        }
        catch (ArgumentException)
        {
            // Expected.
        }
    }

    [TestMethod]
    public void Create_WithAuthorOver200Characters_Throws()
    {
        var author = new string('A', 201);

        try
        {
            Quote.Create(
                author,
                "Some quote");

            Assert.Fail("Expected an ArgumentException.");
        }
        catch (ArgumentException)
        {
            // Expected.
        }
    }

    [TestMethod]
    public void Create_WithEmptyText_Throws()
    {
        try
        {
            Quote.Create(
                "Author",
                "");

            Assert.Fail("Expected an ArgumentException.");
        }
        catch (ArgumentException)
        {
            // Expected.
        }
    }

    [TestMethod]
    public void Create_WithTextOver1000Characters_Throws()
    {
        var text = new string('A', 1001);

        try
        {
            Quote.Create(
                "Author",
                text);

            Assert.Fail("Expected an ArgumentException.");
        }
        catch (ArgumentException)
        {
            // Expected.
        }
    }

    [TestMethod]
    public void MarkDeleted_SetsIsDeletedToTrue()
    {
        var quote = Quote.Create(
            "Author",
            "Some quote");

        quote.MarkDeleted();

        Assert.IsTrue(quote.IsDeleted);
    }
}