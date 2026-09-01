namespace QuotesApi.Messaging;

/// <summary>
/// The JSON payload published to the "quotes.events" topic.
/// <see cref="EventId"/> becomes the Service Bus <c>MessageId</c> — the
/// value every subscription's handler dedupes on.
/// </summary>
public sealed record QuoteEventMessage(
    Guid EventId,
    int QuoteId,
    string Author,
    string Text,
    string EventType,
    DateTimeOffset OccurredAt);
