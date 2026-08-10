namespace QuotesApi.Models;

public class CollectionItem
{
    public int QuoteId { get; private set; }

    public DateTimeOffset AddedAt { get; private set; }

    private CollectionItem()
    {
    }

    public CollectionItem(int quoteId, DateTimeOffset addedAt)
    {
        if (quoteId <= 0)
            throw new ArgumentException(
                "QuoteId must be greater than zero.");

        QuoteId = quoteId;
        AddedAt = addedAt;
    }
}
