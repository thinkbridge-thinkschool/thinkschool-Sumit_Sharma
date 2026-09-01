using System.Text.Json;
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Options;

namespace QuotesApi.Messaging;

public sealed class QuoteEventPublisher : IQuoteEventPublisher, IAsyncDisposable
{
    public const string SimulateCrashProperty = "SimulateCrashOnFirstDelivery";

    private readonly ServiceBusSender _sender;
    private readonly ServiceBusOptions _options;
    private readonly ILogger<QuoteEventPublisher> _logger;

    public QuoteEventPublisher(
        ServiceBusClient client,
        IOptions<ServiceBusOptions> options,
        ILogger<QuoteEventPublisher> logger)
    {
        _options = options.Value;
        _logger = logger;
        _sender = client.CreateSender(_options.TopicName);
    }

    public async Task<QuoteEventMessage> PublishQuoteCreatedAsync(
        int quoteId,
        string author,
        string text,
        bool simulateCrashOnFirstDelivery,
        CancellationToken cancellationToken)
    {
        var quoteEvent = new QuoteEventMessage(
            Guid.NewGuid(),
            quoteId,
            author,
            text,
            "QuoteCreated",
            DateTimeOffset.UtcNow);

        var message = new ServiceBusMessage(JsonSerializer.SerializeToUtf8Bytes(quoteEvent))
        {
            MessageId = quoteEvent.EventId.ToString(),
            ContentType = "application/json",
            Subject = quoteEvent.EventType,
        };

        if (simulateCrashOnFirstDelivery)
        {
            message.ApplicationProperties[SimulateCrashProperty] = true;
        }

        await _sender.SendMessageAsync(message, cancellationToken);

        _logger.LogInformation(
            "Published {EventType} event {EventId} for quote {QuoteId} to topic '{Topic}' (fans out to '{AuditSub}' and '{DigestSub}').",
            quoteEvent.EventType,
            quoteEvent.EventId,
            quoteId,
            _options.TopicName,
            _options.AuditSubscriptionName,
            _options.DigestSubscriptionName);

        return quoteEvent;
    }

    public async Task<string> PublishPoisonMessageAsync(CancellationToken cancellationToken)
    {
        var messageId = Guid.NewGuid().ToString();

        var message = new ServiceBusMessage("{ not-valid-quote-event-json"u8.ToArray())
        {
            MessageId = messageId,
            ContentType = "application/json",
            Subject = "PoisonPill",
        };

        await _sender.SendMessageAsync(message, cancellationToken);

        _logger.LogWarning(
            "Published a deliberately malformed poison message {MessageId} to topic '{Topic}'. " +
            "Every subscription's handler will fail to deserialize it on every attempt.",
            messageId,
            _options.TopicName);

        return messageId;
    }

    public async ValueTask DisposeAsync()
    {
        await _sender.DisposeAsync();
    }
}
