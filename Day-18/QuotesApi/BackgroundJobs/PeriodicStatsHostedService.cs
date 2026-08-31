using Microsoft.EntityFrameworkCore;
using QuotesApi.Data;

namespace QuotesApi.BackgroundJobs;

/// <summary>
/// Contrast case: a plain <see cref="IHostedService"/> doing simple
/// scheduled work, logging a quote count every <see cref="interval"/>.
/// Unlike <see cref="QueuedHostedService"/> (a <c>BackgroundService</c>),
/// nothing here is provided for free — this class owns its own
/// <see cref="CancellationTokenSource"/>, its own background <see cref="Task"/>
/// field, and has to implement the "wait for the loop to finish, bounded by
/// the shutdown token" dance in <see cref="StopAsync"/> by hand. That is the
/// entire practical difference between the two interfaces: BackgroundService
/// is IHostedService plus that boilerplate written once, correctly, in the
/// framework.
/// </summary>
public sealed class PeriodicStatsHostedService(
    IServiceScopeFactory scopeFactory,
    ILogger<PeriodicStatsHostedService> logger) : IHostedService, IDisposable
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);

    private CancellationTokenSource? stoppingCts;
    private Task? executingTask;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        stoppingCts = new CancellationTokenSource();
        executingTask = RunAsync(stoppingCts.Token);

        logger.LogInformation(
            "PeriodicStatsHostedService started; logging a quote digest every {IntervalSeconds}s.",
            Interval.TotalSeconds);

        return Task.CompletedTask;
    }

    private async Task RunAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                using var scope = scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                var total = await db.Quotes.CountAsync(stoppingToken);
                var active = await db.Quotes.CountAsync(q => !q.IsDeleted, stoppingToken);

                logger.LogInformation(
                    "[PeriodicStatsHostedService] {Total} quote(s) stored, {Active} active.",
                    total,
                    active);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected: StopAsync cancelled stoppingCts.
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (stoppingCts is null || executingTask is null)
        {
            return;
        }

        logger.LogInformation("Graceful shutdown requested for PeriodicStatsHostedService.");

        stoppingCts.Cancel();

        // Manually bounded by the host's shutdown token — BackgroundService
        // gives you exactly this line for free.
        await Task.WhenAny(executingTask, Task.Delay(Timeout.Infinite, cancellationToken));

        logger.LogInformation("PeriodicStatsHostedService stopped.");
    }

    public void Dispose() => stoppingCts?.Cancel();
}
