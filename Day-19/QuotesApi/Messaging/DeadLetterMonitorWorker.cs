using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Options;
using QuotesApi.Data;
using QuotesApi.Models;

namespace QuotesApi.Messaging;

/// <summary>
/// Polls both subscriptions' dead-letter subqueues and turns whatever
/// Service Bus has moved there into a durable, queryable
/// <see cref="DeadLetteredMessage"/> row — the observable proof that a
/// poison message (one whose handler throws on every delivery attempt) was
/// caught after <c>MaxDeliveryCount</c> attempts instead of being retried
/// forever or silently dropped.
/// </summary>
public sealed class DeadLetterMonitorWorker(
    ServiceBusClient client,
    IOptions<ServiceBusOptions> options,
    IServiceScopeFactory scopeFactory,
    ILogger<DeadLetterMonitorWorker> logger) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);

    private readonly ServiceBusOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await using var auditDlq = client.CreateReceiver(
            _options.TopicName,
            _options.AuditSubscriptionName,
            new ServiceBusReceiverOptions { SubQueue = SubQueue.DeadLetter });

        await using var digestDlq = client.CreateReceiver(
            _options.TopicName,
            _options.DigestSubscriptionName,
            new ServiceBusReceiverOptions { SubQueue = SubQueue.DeadLetter });

        logger.LogInformation("DeadLetterMonitorWorker started, polling both subscriptions' dead-letter subqueues.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await DrainAsync(_options.AuditSubscriptionName, auditDlq, stoppingToken);
                await DrainAsync(_options.DigestSubscriptionName, digestDlq, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                // A transient fault here (a brief auth/network hiccup, RBAC
                // not yet propagated) must not take down the whole host - an
                // unhandled exception from a BackgroundService.ExecuteAsync
                // stops the entire app by default. Log and retry next poll.
                logger.LogError(ex, "Dead-letter monitor: error polling dead-letter subqueues; will retry.");
            }

            try
            {
                await Task.Delay(PollInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task DrainAsync(
        string subscription,
        ServiceBusReceiver receiver,
        CancellationToken stoppingToken)
    {
        IReadOnlyList<ServiceBusReceivedMessage> messages;

        try
        {
            messages = await receiver.ReceiveMessagesAsync(
                maxMessages: 10,
                maxWaitTime: TimeSpan.FromMilliseconds(500),
                cancellationToken: stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        foreach (var message in messages)
        {
            var bodyPreview = message.Body.ToString();
            if (bodyPreview.Length > 200)
            {
                bodyPreview = bodyPreview[..200];
            }

            logger.LogWarning(
                "Dead-letter monitor: subscription '{Subscription}' dead-lettered message {MessageId} " +
                "after {DeliveryCount} delivery attempt(s) — reason: {Reason} ({Description}).",
                subscription,
                message.MessageId,
                message.DeliveryCount,
                message.DeadLetterReason,
                message.DeadLetterErrorDescription);

            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            db.DeadLetteredMessages.Add(new DeadLetteredMessage
            {
                Subscription = subscription,
                MessageId = message.MessageId,
                DeadLetterReason = message.DeadLetterReason ?? "",
                DeadLetterErrorDescription = message.DeadLetterErrorDescription ?? "",
                BodyPreview = bodyPreview,
                DeliveryCount = message.DeliveryCount,
                DeadLetteredAt = DateTimeOffset.UtcNow,
            });

            await db.SaveChangesAsync(stoppingToken);

            // Captured durably above - remove it from the DLQ so the same
            // poison message isn't logged again on the next poll.
            await receiver.CompleteMessageAsync(message, stoppingToken);
        }
    }
}
