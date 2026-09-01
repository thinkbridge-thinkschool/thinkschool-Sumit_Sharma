namespace QuotesApi.Models;

/// <summary>
/// A row written by <c>AuditLogSubscriptionWorker</c> for every distinct
/// message it has actually processed off the "audit-log" subscription.
/// </summary>
public class AuditLogEntry
{
    public int Id { get; set; }

    public string MessageId { get; set; } = "";

    public string EventType { get; set; } = "";

    public int QuoteId { get; set; }

    public string Author { get; set; } = "";

    public string Text { get; set; } = "";

    public int DeliveryCount { get; set; }

    public DateTimeOffset ReceivedAt { get; set; }
}
