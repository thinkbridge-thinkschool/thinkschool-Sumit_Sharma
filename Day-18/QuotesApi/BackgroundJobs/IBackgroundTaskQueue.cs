namespace QuotesApi.BackgroundJobs;

/// <summary>
/// The queue that moves slow work off the request thread. A request
/// handler calls <see cref="QueueBackgroundWorkItemAsync"/> and returns
/// immediately (202 Accepted); <see cref="QueuedHostedService"/> is the only
/// caller of <see cref="DequeueAsync"/>, draining items one at a time on a
/// dedicated background loop.
/// </summary>
public interface IBackgroundTaskQueue
{
    ValueTask QueueBackgroundWorkItemAsync(Func<IServiceProvider, CancellationToken, ValueTask> workItem);

    ValueTask<Func<IServiceProvider, CancellationToken, ValueTask>> DequeueAsync(CancellationToken cancellationToken);

    /// <summary>Items sitting in the queue right now, not counting one already dequeued and running.</summary>
    int Count { get; }
}
