using System.Text.Json;
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Options;
using QuotesApi.Data;
using QuotesApi.Models;

namespace QuotesApi.Messaging;

/// <summary>
/// The competing-consumer half of Day 19: starts
/// <see cref="ServiceBusOptions.DigestWorkerCount"/> independent
/// <see cref="ServiceBusProcessor"/> instances, all bound to the same
/// "digest-notifications" subscription. Service Bus hands each message to
/// exactly one of them — whichever asks first — so under load the
/// <see cref="DigestNotification.WorkerId"/> column fills in with a mix of
/// worker ids, proving more than one instance is genuinely competing for
/// work rather than one worker doing everything.
/// </summary>
public sealed class DigestConsumerPool(
    ServiceBusClient client,
    IOptions<ServiceBusOptions> options,
    IServiceScopeFactory scopeFactory,
    ILogger<DigestConsumerPool> logger) : BackgroundService
{
    private readonly ServiceBusOptions _options = options.Value;
    private readonly List<ServiceBusProcessor> _processors = [];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        for (var i = 1; i <= _options.DigestWorkerCount; i++)
        {
            var workerId = $"digest-worker-{i}";

            var processor = client.CreateProcessor(
                _options.TopicName,
                _options.DigestSubscriptionName,
                new ServiceBusProcessorOptions
                {
                    MaxConcurrentCalls = 1,
                    AutoCompleteMessages = true,
                });

            processor.ProcessMessageAsync += args => HandleMessageAsync(workerId, args);
            processor.ProcessErrorAsync += args => HandleErrorAsync(workerId, args);

            await processor.StartProcessingAsync(stoppingToken);

            _processors.Add(processor);
        }

        logger.LogInformation(
            "DigestConsumerPool started {Count} competing worker(s) on subscription '{Subscription}'.",
            _processors.Count,
            _options.DigestSubscriptionName);

        try
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown path.
        }
    }

    private async Task HandleMessageAsync(string workerId, ProcessMessageEventArgs args)
    {
        var messageId = args.Message.MessageId;
        var deliveryCount = args.Message.DeliveryCount;

        var quoteEvent = JsonSerializer.Deserialize<QuoteEventMessage>(args.Message.Body)
            ?? throw new InvalidOperationException($"Message {messageId} deserialized to null.");

        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var processed = await MessageIdempotency.TryProcessOnceAsync(
            db,
            _options.DigestSubscriptionName,
            messageId,
            addSideEffect: db => db.DigestNotifications.Add(new DigestNotification
            {
                MessageId = messageId,
                EventType = quoteEvent.EventType,
                QuoteId = quoteEvent.QuoteId,
                Author = quoteEvent.Author,
                Text = quoteEvent.Text,
                WorkerId = workerId,
                DeliveryCount = deliveryCount,
                ReceivedAt = DateTimeOffset.UtcNow,
            }),
            args.CancellationToken);

        if (!processed)
        {
            // Whichever worker instance picked up the redelivery, the
            // (subscription, MessageId) pair was already claimed — possibly
            // by a *different* worker than the one that saw delivery #1.
            logger.LogInformation(
                "{WorkerId}: duplicate delivery #{DeliveryCount} of message {MessageId} — already recorded, skipping.",
                workerId,
                deliveryCount,
                messageId);

            return;
        }

        logger.LogInformation(
            "{WorkerId}: recorded {EventType} for quote {QuoteId} (message {MessageId}, delivery #{DeliveryCount}).",
            workerId,
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
                "acknowledging it — expect a redelivery next, possibly to a different worker.",
                workerId,
                messageId);

            throw new InvalidOperationException("Simulated crash after side effect, before ack.");
        }
    }

    private Task HandleErrorAsync(string workerId, ProcessErrorEventArgs args)
    {
        logger.LogError(
            args.Exception,
            "{WorkerId}: processor error from {ErrorSource}.",
            workerId,
            args.ErrorSource);

        return Task.CompletedTask;
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        foreach (var processor in _processors)
        {
            await processor.StopProcessingAsync(cancellationToken);
            await processor.DisposeAsync();
        }

        await base.StopAsync(cancellationToken);
    }
}
