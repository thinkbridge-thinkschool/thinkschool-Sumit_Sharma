namespace QuotesApi.BackgroundJobs;

/// <summary>
/// Drains <see cref="IBackgroundTaskQueue"/> for as long as the host is
/// running. This is the "move slow work off the request thread" half of
/// Day 18: a request enqueues a work item and returns immediately; this
/// service is the only thing that ever calls <c>DequeueAsync</c>, so work
/// runs one item at a time, off the request thread, on its own lifetime.
///
/// Graceful shutdown: <see cref="ExecuteAsync"/> is handed <c>stoppingToken</c>
/// by the base class, which the host cancels the moment shutdown begins.
/// <c>DequeueAsync(stoppingToken)</c> throws as soon as that happens, so the
/// loop stops picking up NEW items immediately — but an item already
/// dequeued keeps running (its own delegate independently observes the same
/// token to decide whether to bail early). The host then awaits this
/// service's <c>ExecuteTask</c> for up to <c>HostOptions.ShutdownTimeout</c>
/// before giving up, which is what gives an in-flight item a real chance to
/// finish instead of being killed mid-write.
/// </summary>
public sealed class QueuedHostedService(
    IBackgroundTaskQueue taskQueue,
    IServiceScopeFactory scopeFactory,
    ILogger<QueuedHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("QueuedHostedService started; draining the background task queue.");

        while (!stoppingToken.IsCancellationRequested)
        {
            Func<IServiceProvider, CancellationToken, ValueTask> workItem;

            try
            {
                workItem = await taskQueue.DequeueAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                // Shutdown was requested while waiting for the next item —
                // nothing was dequeued, so there is nothing to finish.
                break;
            }

            try
            {
                using var scope = scopeFactory.CreateScope();
                await workItem(scope.ServiceProvider, stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "A background work item threw an unhandled exception.");
            }
        }

        if (taskQueue.Count > 0)
        {
            logger.LogWarning(
                "QueuedHostedService is stopping with {Remaining} item(s) still queued; " +
                "they were never dequeued and will NOT run (in-memory queue — nothing persists this).",
                taskQueue.Count);
        }

        logger.LogInformation("QueuedHostedService loop exited.");
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "Graceful shutdown requested for QueuedHostedService. " +
            "Letting any in-flight work item finish (queue currently holds {Remaining} pending item(s)).",
            taskQueue.Count);

        await base.StopAsync(cancellationToken);

        logger.LogInformation("QueuedHostedService stopped.");
    }
}
