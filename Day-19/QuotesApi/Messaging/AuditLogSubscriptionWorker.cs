using System.Text.Json;
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Options;
using QuotesApi.Data;
using QuotesApi.Models;

namespace QuotesApi.Messaging;

/// <summary>
/// A single, always-on consumer of the topic's "audit-log" subscription —
/// every "QuoteCreated" event gets written here, once, for good. Contrast
/// with <see cref="DigestConsumerPool"/>, which puts several competing
/// consumer instances on the *other* subscription.
/// </summary>
public sealed class AuditLogSubscriptionWorker(
    ServiceBusClient client,
    IOptions<ServiceBusOptions> options,
    IServiceScopeFactory scopeFactory,
    ILogger<AuditLogSubscriptionWorker> logger) : BackgroundService
{
    private const string WorkerId = "audit-worker-1";

    private readonly ServiceBusOptions _options = options.Value;

    private ServiceBusProcessor? _processor;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _processor = client.CreateProcessor(
            _options.TopicName,
            _options.AuditSubscriptionName,
            new ServiceBusProcessorOptions
            {
                MaxConcurrentCalls = 1,
                AutoCompleteMessages = true,
            });

        _processor.ProcessMessageAsync += HandleMessageAsync;
        _processor.ProcessErrorAsync += HandleErrorAsync;

        await _processor.StartProcessingAsync(stoppingToken);

        logger.LogInformation(
            "{WorkerId} started, consuming subscription '{Subscription}'.",
            WorkerId,
            _options.AuditSubscriptionName);

        try
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown path.
        }
    }

    private async Task HandleMessageAsync(ProcessMessageEventArgs args)
    {
        var messageId = args.Message.MessageId;
        var deliveryCount = args.Message.DeliveryCount;

        // Poison bodies throw here on every delivery attempt; with
        // AutoCompleteMessages = true the processor abandons the message
        // automatically, and once MaxDeliveryCount is exhausted Service Bus
        // moves it to this subscription's dead-letter subqueue.
        var quoteEvent = JsonSerializer.Deserialize<QuoteEventMessage>(args.Message.Body)
            ?? throw new InvalidOperationException($"Message {messageId} deserialized to null.");

        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var processed = await MessageIdempotency.TryProcessOnceAsync(
            db,
            _options.AuditSubscriptionName,
            messageId,
            addSideEffect: db => db.AuditLogEntries.Add(new AuditLogEntry
            {
                MessageId = messageId,
                EventType = quoteEvent.EventType,
                QuoteId = quoteEvent.QuoteId,
                Author = quoteEvent.Author,
                Text = quoteEvent.Text,
                DeliveryCount = deliveryCount,
                ReceivedAt = DateTimeOffset.UtcNow,
            }),
            args.CancellationToken);

        if (!processed)
        {
            logger.LogInformation(
                "{WorkerId}: duplicate delivery #{DeliveryCount} of message {MessageId} — already recorded, skipping.",
                WorkerId,
                deliveryCount,
                messageId);

            return;
        }

        logger.LogInformation(
            "{WorkerId}: recorded {EventType} for quote {QuoteId} (message {MessageId}, delivery #{DeliveryCount}).",
            WorkerId,
            quoteEvent.EventType,
            quoteEvent.QuoteId,
            messageId,
            deliveryCount);

        if (args.Message.ApplicationProperties.TryGetValue(
                QuoteEventPublisher.SimulateCrashProperty, out var simulate) &&
            simulate is true &&
            deliveryCount == 1)
        {
            logger.LogWarning(
                "{WorkerId}: simulating a crash after committing message {MessageId} but before " +
                "acknowledging it — expect a redelivery next.",
                WorkerId,
                messageId);

            throw new InvalidOperationException("Simulated crash after side effect, before ack.");
        }
    }

    private Task HandleErrorAsync(ProcessErrorEventArgs args)
    {
        logger.LogError(
            args.Exception,
            "{WorkerId}: processor error from {ErrorSource}.",
            WorkerId,
            args.ErrorSource);

        return Task.CompletedTask;
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_processor is not null)
        {
            await _processor.StopProcessingAsync(cancellationToken);
            await _processor.DisposeAsync();
        }

        await base.StopAsync(cancellationToken);
    }
}
