namespace QuotesApi.BackgroundJobs;

/// <summary>
/// In-memory record of one queued/running/finished import job. Deliberately
/// a mutable class, not a record: the queued work item and the status-poll
/// endpoint both hold a reference to the SAME instance from
/// <see cref="IJobStatusStore"/>, so progress updates made by the
/// background worker are visible to GET /api/jobs/{id} without any extra
/// synchronization step. This is process-memory only — a restart loses
/// every job, which is exactly the tradeoff called out against Hangfire's
/// persistent storage in the Day-18 README.
/// </summary>
public sealed class QuoteImportJob
{
    public required Guid Id { get; init; }

    public required int RequestedCount { get; init; }

    public BackgroundJobStatus Status { get; set; } = BackgroundJobStatus.Queued;

    public int ImportedCount { get; set; }

    public string? Error { get; set; }

    public required DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset? StartedAt { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }
}
