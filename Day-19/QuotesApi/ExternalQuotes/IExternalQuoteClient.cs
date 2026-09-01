using System.Text.Json.Serialization;

namespace QuotesApi.ExternalQuotes;

public interface IExternalQuoteClient
{
    Task<ExternalQuote> GetRandomQuoteAsync(CancellationToken cancellationToken);
}

public sealed record ExternalQuote(
    string Author,
    [property: JsonPropertyName("quote")] string Text);
