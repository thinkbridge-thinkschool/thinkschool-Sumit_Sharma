using Microsoft.AspNetCore.Mvc;
using QuotesApi.Models;
using QuotesApi.Repositories;

namespace QuotesApi.Extensions;

public static class QuoteEndpointExtensions
{
    public static void MapQuoteEndpoints(this WebApplication app)
    {
        app.MapGet("/api/quotes", async (
            [FromQuery] int? page,
            [FromQuery] int? size,
            IQuoteRepository repository,
            CancellationToken cancellationToken) =>
        {
            page = page is null or < 1 ? 1 : page.Value;
            size = size is null or < 1
                ? 10
                : Math.Min(size.Value, 100);

            return Results.Ok(
                await repository.GetAllAsync(
                    page.Value,
                    size.Value,
                    cancellationToken));
        });

        app.MapGet("/api/quotes/{id:int}", async (
            int id,
            IQuoteRepository repository,
            CancellationToken cancellationToken) =>
        {
            var quote = await repository.GetByIdAsync(
                id,
                cancellationToken);

            return quote is null
                ? Results.NotFound()
                : Results.Ok(quote);
        });

        app.MapPost("/api/quotes", async (
            QuoteRequest request,
            IQuoteRepository repository,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var quote = Quote.Create(
                    request.Author,
                    request.Text);

                var created = await repository.AddAsync(
                    quote,
                    cancellationToken);

                return Results.Created(
                    $"/api/quotes/{created.Id}",
                    created);
            }
            catch (ArgumentException ex)
            {
                return Results.ValidationProblem(
                    new Dictionary<string, string[]>
                    {
                        ["quote"] = new[] { ex.Message }
                    });
            }
        })
        .RequireAuthorization();

        app.MapDelete("/api/quotes/{id:int}", async (
            int id,
            IQuoteRepository repository,
            CancellationToken cancellationToken) =>
        {
            var deleted = await repository.DeleteAsync(
                id,
                cancellationToken);

            return deleted
                ? Results.NoContent()
                : Results.NotFound();
        })
        .RequireAuthorization();
    }
}

public sealed record QuoteRequest(
    string Author,
    string Text);
