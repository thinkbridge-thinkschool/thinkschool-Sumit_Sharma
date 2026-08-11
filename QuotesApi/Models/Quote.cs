namespace QuotesApi.Models;

public class Quote
{
    public int Id { get; private set; }

    public string Author { get; private set; } = "";

    public string Text { get; private set; } = "";

    public bool IsDeleted { get; private set; }

    private Quote()
    {
    }

    private Quote(
        string author,
        string text)
    {
        Author = author;
        Text = text;
    }

    public static Quote Create(
        string author,
        string text)
    {
        if (string.IsNullOrWhiteSpace(author) ||
            author.Length > 200)
        {
            throw new ArgumentException(
                "Author must be between 1 and 200 characters.");
        }

        if (string.IsNullOrWhiteSpace(text) ||
            text.Length > 1000)
        {
            throw new ArgumentException(
                "Text must be between 1 and 1000 characters.");
        }

        return new Quote(author, text);
    }

    public void MarkDeleted()
    {
        IsDeleted = true;
    }
}
