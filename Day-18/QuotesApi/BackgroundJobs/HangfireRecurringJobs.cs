using Microsoft.EntityFrameworkCore;
using QuotesApi.Data;

namespace QuotesApi.BackgroundJobs;

/// <summary>
/// Contrast case #2: scheduled work handed to Hangfire instead of a
/// hand-rolled timer. Registered as a recurring job in Program.cs with
/// <c>RecurringJob.AddOrUpdate</c>; Hangfire's own server polls its job
/// storage for due jobs, instantiates this class per run via the DI
/// container, and records success/failure/duration/history in the
/// dashboard at <c>/hangfire</c> — none of which
/// <see cref="PeriodicStatsHostedService"/> gets without writing it by hand.
/// The one thing this demo does NOT get from that comparison: the demo
/// uses Hangfire's in-memory storage, so like the queue above, the
/// schedule itself does not survive a restart either. Point Hangfire at
/// SQL Server/Redis/PostgreSQL storage instead and it would.
/// </summary>
public sealed class HangfireRecurringJobs(
    AppDbContext db,
    ILogger<HangfireRecurringJobs> logger)
{
    public async Task LogQuoteDigestAsync()
    {
        var total = await db.Quotes.CountAsync();
        var deleted = await db.Quotes.CountAsync(q => q.IsDeleted);

        logger.LogInformation(
            "[Hangfire recurring job \"quote-digest\"] {Total} total quote(s), {Active} active, {Deleted} soft-deleted.",
            total,
            total - deleted,
            deleted);
    }
}
