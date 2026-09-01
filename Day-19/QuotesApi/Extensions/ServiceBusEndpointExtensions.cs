using Microsoft.EntityFrameworkCore;
using QuotesApi.Data;
using QuotesApi.Messaging;

namespace QuotesApi.Extensions;

public static class ServiceBusEndpointExtensions
{
    public static void MapServiceBusEventEndpoints(this WebApplication app)
    {
        app.MapPost("/api/events/quote-created", async (
            PublishQuoteEventRequest request,
            IQuoteEventPublisher publisher,
            CancellationToken cancellationToken) =>
        {
            var quoteEvent = await publisher.PublishQuoteCreatedAsync(
                request.QuoteId ?? 0,
                request.Author,
                request.Text,
                request.SimulateCrash ?? false,
                cancellationToken);

            return Results.Accepted(value: quoteEvent);
        })
        .RequireAuthorization("CanEditQuotes");

        app.MapPost("/api/events/publish-poison", async (
            IQuoteEventPublisher publisher,
            CancellationToken cancellationToken) =>
        {
            var messageId = await publisher.PublishPoisonMessageAsync(cancellationToken);

            return Results.Accepted(value: new { messageId });
        })
        .RequireAuthorization("CanEditQuotes");

        app.MapGet("/api/events/audit-log", async (
            AppDbContext db,
            CancellationToken cancellationToken) =>
            Results.Ok(
                // Ordered by the autoincrement Id (not ReceivedAt) - SQLite
                // can't translate ORDER BY over a DateTimeOffset column, and
                // Id is monotonic with insertion order anyway.
                await db.AuditLogEntries
                    .OrderByDescending(entry => entry.Id)
                    .Take(50)
                    .ToListAsync(cancellationToken)));

        app.MapGet("/api/events/digest", async (
            AppDbContext db,
            CancellationToken cancellationToken) =>
            Results.Ok(
                await db.DigestNotifications
                    .OrderByDescending(entry => entry.Id)
                    .Take(50)
                    .ToListAsync(cancellationToken)));

        app.MapGet("/api/events/dead-letters", async (
            AppDbContext db,
            CancellationToken cancellationToken) =>
            Results.Ok(
                await db.DeadLetteredMessages
                    .OrderByDescending(entry => entry.Id)
                    .Take(50)
                    .ToListAsync(cancellationToken)));
    }
}

public sealed record PublishQuoteEventRequest(
    int? QuoteId,
    string Author,
    string Text,
    bool? SimulateCrash);
