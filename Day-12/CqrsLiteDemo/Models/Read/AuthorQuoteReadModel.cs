namespace CqrsLiteDemo.Models.Read;

public class AuthorQuoteReadModel
{
    public int AuthorId { get; set; }

    public string AuthorName { get; set; } = "";

    public int QuoteId { get; set; }

    public string QuoteText { get; set; } = "";
}
