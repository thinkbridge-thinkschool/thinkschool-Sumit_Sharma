namespace QuotesApi.BackgroundJobs;

public interface IJobStatusStore
{
    QuoteImportJob Create(int requestedCount);

    QuoteImportJob? Get(Guid id);
}
