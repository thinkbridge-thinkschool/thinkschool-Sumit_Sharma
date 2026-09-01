namespace QuotesApi.Messaging;

public interface IQuoteEventPublisher
{
    /// <summary>
    /// Publishes a "QuoteCreated" event to the topic. When
    /// <paramref name="simulateCrashOnFirstDelivery"/> is set, every
    /// subscription's handler commits its side effect on the first
    /// delivery and then deliberately throws before the message would be
    /// auto-completed — Service Bus abandons and redelivers it, and the
    /// second delivery is expected to be recognized as a duplicate and
    /// skipped, proving the handler is idempotent.
    /// </summary>
    Task<QuoteEventMessage> PublishQuoteCreatedAsync(
        int quoteId,
        string author,
        string text,
        bool simulateCrashOnFirstDelivery,
        CancellationToken cancellationToken);

    /// <summary>
    /// Publishes a message whose body is not valid <see cref="QuoteEventMessage"/>
    /// JSON. Every subscription's handler will fail to deserialize it on
    /// every delivery attempt, so once each subscription's
    /// <c>MaxDeliveryCount</c> is exhausted, Service Bus moves its copy to
    /// that subscription's dead-letter subqueue.
    /// </summary>
    Task<string> PublishPoisonMessageAsync(CancellationToken cancellationToken);
}
