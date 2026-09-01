using System.Threading.Channels;

namespace QuotesApi.BackgroundJobs;

public sealed class BackgroundTaskQueue : IBackgroundTaskQueue
{
    private const int Capacity = 100;

    private readonly Channel<Func<IServiceProvider, CancellationToken, ValueTask>> queue =
        Channel.CreateBounded<Func<IServiceProvider, CancellationToken, ValueTask>>(
            new BoundedChannelOptions(Capacity)
            {
                // Backpressure, not data loss: a producer awaits until a slot
                // frees up rather than an item being silently dropped.
                FullMode = BoundedChannelFullMode.Wait
            });

    public int Count => queue.Reader.Count;

    public async ValueTask QueueBackgroundWorkItemAsync(Func<IServiceProvider, CancellationToken, ValueTask> workItem)
    {
        ArgumentNullException.ThrowIfNull(workItem);

        await queue.Writer.WriteAsync(workItem);
    }

    public async ValueTask<Func<IServiceProvider, CancellationToken, ValueTask>> DequeueAsync(
        CancellationToken cancellationToken)
    {
        return await queue.Reader.ReadAsync(cancellationToken);
    }
}
