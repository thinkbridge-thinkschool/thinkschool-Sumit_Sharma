using QuotesApi.BackgroundJobs;
using QuotesApi.ExternalQuotes;
using QuotesApi.Models;
using QuotesApi.Repositories;

namespace QuotesApi.Extensions;

public static class BackgroundJobEndpointExtensions
{
    public static void MapBackgroundJobEndpoints(this WebApplication app)
    {
        app.MapPost("/api/quotes/import", async (
            ImportQuotesRequest? request,
            IBackgroundTaskQueue taskQueue,
            IJobStatusStore jobStore,
            ILoggerFactory loggerFactory) =>
        {
            var count = Math.Clamp(request?.Count ?? 3, 1, 10);
            var job = jobStore.Create(count);

            // The slow part — one external HTTP call plus a DB write per
            // quote — is queued and this handler returns immediately. The
            // request thread never waits for it.
            await taskQueue.QueueBackgroundWorkItemAsync(async (services, jobToken) =>
            {
                var logger = loggerFactory.CreateLogger("QuoteImportJob");

                job.Status = BackgroundJobStatus.Running;
                job.StartedAt = DateTimeOffset.UtcNow;

                try
                {
                    var externalClient = services.GetRequiredService<IExternalQuoteClient>();
                    var repository = services.GetRequiredService<IQuoteRepository>();

                    for (var i = 0; i < job.RequestedCount; i++)
                    {
                        jobToken.ThrowIfCancellationRequested();

                        var external = await externalClient.GetRandomQuoteAsync(jobToken);
                        var quote = Quote.Create(external.Author, external.Text);
                        await repository.AddAsync(quote, jobToken);

                        job.ImportedCount++;

                        // Stand-in for real per-item latency, so the queue
                        // drain — and a graceful shutdown mid-job — are
                        // actually observable instead of finishing before
                        // anyone can look.
                        await Task.Delay(TimeSpan.FromSeconds(1.5), jobToken);
                    }

                    job.Status = BackgroundJobStatus.Completed;
                }
                catch (OperationCanceledException)
                {
                    job.Status = BackgroundJobStatus.Failed;
                    job.Error =
                        $"Cancelled during graceful shutdown after importing {job.ImportedCount} of {job.RequestedCount} quote(s).";
                }
                catch (Exception ex)
                {
                    job.Status = BackgroundJobStatus.Failed;
                    job.Error = ex.Message;

                    logger.LogError(ex, "Quote import job {JobId} failed.", job.Id);
                }
                finally
                {
                    job.CompletedAt = DateTimeOffset.UtcNow;
                }
            });

            return Results.Accepted($"/api/jobs/{job.Id}", job);
        })
        .RequireAuthorization("CanEditQuotes");

        app.MapGet("/api/jobs/{id:guid}", (
            Guid id,
            IJobStatusStore jobStore) =>
        {
            var job = jobStore.Get(id);

            return job is null
                ? Results.NotFound()
                : Results.Ok(job);
        });
    }
}

public sealed record ImportQuotesRequest(int? Count);
