namespace QuotesApi.Models;

/// <summary>
/// A message <see cref="DeadLetterMonitorWorker" /> found sitting in a
/// subscription's dead-letter subqueue after Service Bus itself moved it
/// there — because the handler threw on every one of
/// <c>MaxDeliveryCount</c> delivery attempts (a poison message).
/// </summary>
public class DeadLetteredMessage
{
    public int Id { get; set; }

    public string Subscription { get; set; } = "";

    public string MessageId { get; set; } = "";

    public string DeadLetterReason { get; set; } = "";

    public string DeadLetterErrorDescription { get; set; } = "";

    public string BodyPreview { get; set; } = "";

    public int DeliveryCount { get; set; }

    public DateTimeOffset DeadLetteredAt { get; set; }
}
