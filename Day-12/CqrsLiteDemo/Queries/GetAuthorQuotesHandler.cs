using Microsoft.EntityFrameworkCore;
using CqrsLiteDemo.Data;
using CqrsLiteDemo.Models.Read;

namespace CqrsLiteDemo.Queries;

public class GetAuthorQuotesHandler
{
    private readonly CqrsDbContext _db;

    public GetAuthorQuotesHandler(CqrsDbContext db)
    {
        _db = db;
    }

    public Task<List<AuthorQuoteReadModel>> HandleAsync(GetAuthorQuotesQuery query)
    {
        return _db.Quotes
            .AsNoTracking()
            .Where(q => q.AuthorId == query.AuthorId)
            .Join(
                _db.Authors.AsNoTracking(),
                quote => quote.AuthorId,
                author => author.Id,
                (quote, author) => new AuthorQuoteReadModel
                {
                    AuthorId = author.Id,
                    AuthorName = author.Name,
                    QuoteId = quote.Id,
                    QuoteText = quote.Text
                })
            .ToListAsync();
    }
}
