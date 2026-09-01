namespace QuotesApi.Models;

/// <summary>
/// One row per (subscription, MessageId) pair a handler has actually
/// committed side effects for. The unique index on those two columns
/// (see <c>AppDbContext.OnModelCreating</c>) is what makes handlers
/// idempotent: a redelivered message racing to insert the same pair a
/// second time hits a unique-constraint violation and is treated as a
/// duplicate rather than reprocessed.
/// </summary>
public class ProcessedMessage
{
    public int Id { get; set; }

    public string Subscription { get; set; } = "";

    public string MessageId { get; set; } = "";

    public DateTimeOffset ProcessedAt { get; set; }
}
