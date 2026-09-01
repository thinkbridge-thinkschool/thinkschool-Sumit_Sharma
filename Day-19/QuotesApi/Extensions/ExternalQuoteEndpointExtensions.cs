using QuotesApi.ExternalQuotes;

namespace QuotesApi.Extensions;

public static class ExternalQuoteEndpointExtensions
{
    public static void MapExternalQuoteEndpoints(this WebApplication app)
    {
        app.MapGet("/api/quotes/external/random", async (
            IExternalQuoteClient client,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var quote = await client.GetRandomQuoteAsync(cancellationToken);

                return Results.Ok(quote);
            }
            catch (Exception)
            {
                return Results.Problem(
                    title: "External quote service is unavailable.",
                    statusCode: StatusCodes.Status502BadGateway);
            }
        });
    }
}
