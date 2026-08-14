using System.Net.Http.Json;

namespace QuotesApi.ExternalQuotes;

public sealed class ExternalQuoteClient(
    HttpClient httpClient,
    ILogger<ExternalQuoteClient> logger) : IExternalQuoteClient
{
    public async Task<ExternalQuote> GetRandomQuoteAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            var quote = await httpClient.GetFromJsonAsync<ExternalQuote>(
                "quotes/random",
                cancellationToken);

            return quote
                ?? throw new InvalidOperationException(
                    "External quote service returned an empty response.");
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Failed to fetch a random quote from the external quote service after all retries were exhausted.");

            throw;
        }
    }
}
