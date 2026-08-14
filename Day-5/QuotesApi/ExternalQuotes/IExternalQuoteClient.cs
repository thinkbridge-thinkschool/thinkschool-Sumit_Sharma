namespace QuotesApi.ExternalQuotes;

public interface IExternalQuoteClient
{
    Task<ExternalQuote> GetRandomQuoteAsync(CancellationToken cancellationToken);
}

public sealed record ExternalQuote(string Author, string Text);
