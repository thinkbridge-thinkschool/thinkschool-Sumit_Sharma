using Microsoft.EntityFrameworkCore;
using QuotesApi.Data;
using QuotesApi.Models;

namespace QuotesApi.Messaging;

/// <summary>
/// Dedupes a subscription handler on (subscription, MessageId). Claiming
/// the pair and writing the handler's side effect happen in the same
/// <c>SaveChangesAsync</c> call, so either both land or neither does — a
/// second delivery of the same message (a genuine Service Bus redelivery,
/// or two competing-consumer workers racing on the same message) hits the
/// unique index on <see cref="ProcessedMessage"/> and is reported as
/// already-processed instead of re-applying the side effect.
/// </summary>
public static class MessageIdempotency
{
    public static async Task<bool> TryProcessOnceAsync(
        AppDbContext db,
        string subscription,
        string messageId,
        Action<AppDbContext> addSideEffect,
        CancellationToken cancellationToken)
    {
        db.ProcessedMessages.Add(new ProcessedMessage
        {
            Subscription = subscription,
            MessageId = messageId,
            ProcessedAt = DateTimeOffset.UtcNow,
        });

        addSideEffect(db);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException)
        {
            // Unique index on (Subscription, MessageId) rejected the
            // insert - this exact message was already processed on this
            // subscription by an earlier delivery.
            return false;
        }
    }
}
