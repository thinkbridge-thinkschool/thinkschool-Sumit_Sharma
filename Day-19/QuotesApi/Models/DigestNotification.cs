namespace QuotesApi.Models;

/// <summary>
/// A row written by one of the competing <c>DigestConsumerPool</c> workers
/// for every distinct message it has actually processed off the
/// "digest-notifications" subscription. <see cref="WorkerId"/> is what
/// makes the competing-consumer behavior observable: several rows with
/// different worker ids prove more than one worker instance pulled
/// messages off the same subscription concurrently.
/// </summary>
public class DigestNotification
{
    public int Id { get; set; }

    public string MessageId { get; set; } = "";

    public string EventType { get; set; } = "";

    public int QuoteId { get; set; }

    public string Author { get; set; } = "";

    public string Text { get; set; } = "";

    public string WorkerId { get; set; } = "";

    public int DeliveryCount { get; set; }

    public DateTimeOffset ReceivedAt { get; set; }
}
