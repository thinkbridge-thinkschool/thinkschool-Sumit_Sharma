using Microsoft.EntityFrameworkCore;
using QuotesApi.Data;
using QuotesApi.Models;
using QuotesApi.Telemetry;

namespace QuotesApi.Repositories;

public class QuoteRepository : IQuoteRepository
{
    private readonly AppDbContext db;
    private readonly ILogger<QuoteRepository> logger;

    public QuoteRepository(
        AppDbContext db,
        ILogger<QuoteRepository> logger)
    {
        this.db = db;
        this.logger = logger;
    }

    public async Task<List<Quote>> GetAllAsync(
        int page,
        int size,
        CancellationToken cancellationToken)
    {
        return await db.Quotes
            .AsNoTracking()
            .OrderBy(q => q.Id)
            .Skip((page - 1) * size)
            .Take(size)
            .ToListAsync(cancellationToken);
    }

    public async Task<Quote?> GetByIdAsync(
        int id,
        CancellationToken cancellationToken)
    {
        using var activity = QuotesApiActivitySource.Instance.StartActivity(
            "QuoteRepository.GetByIdAsync");

        activity?.SetTag("quote.id", id);

        return await db.Quotes
            .AsNoTracking()
            .FirstOrDefaultAsync(
                q => q.Id == id,
                cancellationToken);
    }

    public async Task<Quote> AddAsync(
        Quote quote,
        CancellationToken cancellationToken)
    {
        db.Quotes.Add(quote);

        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Created quote {QuoteId} by {Author}",
            quote.Id,
            quote.Author);

        return quote;
    }

    public async Task<bool> DeleteAsync(
        int id,
        CancellationToken cancellationToken)
    {
        var quote = await db.Quotes
            .FirstOrDefaultAsync(
                q => q.Id == id,
                cancellationToken);

        if (quote is null)
        {
            logger.LogWarning(
                "Quote {QuoteId} was not found for deletion",
                id);

            return false;
        }

             quote.MarkDeleted();

             await db.SaveChangesAsync(cancellationToken);

            logger.LogInformation(
            "Soft-deleted quote {QuoteId}",
            id);

        return true;
    }
}