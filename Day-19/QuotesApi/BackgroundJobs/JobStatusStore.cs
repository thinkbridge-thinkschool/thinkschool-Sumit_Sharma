using System.Collections.Concurrent;

namespace QuotesApi.BackgroundJobs;

/// <summary>
/// Singleton, process-memory job tracker. Fine for a Week-1-style demo of
/// "what BackgroundService gives you for free"; a real system would persist
/// this (which is exactly what Hangfire's job storage does instead).
/// </summary>
public sealed class JobStatusStore : IJobStatusStore
{
    private readonly ConcurrentDictionary<Guid, QuoteImportJob> jobs = new();

    public QuoteImportJob Create(int requestedCount)
    {
        var job = new QuoteImportJob
        {
            Id = Guid.NewGuid(),
            RequestedCount = requestedCount,
            CreatedAt = DateTimeOffset.UtcNow
        };

        jobs[job.Id] = job;

        return job;
    }

    public QuoteImportJob? Get(Guid id) =>
        jobs.TryGetValue(id, out var job) ? job : null;
}
