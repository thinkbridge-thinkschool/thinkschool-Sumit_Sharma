using Microsoft.EntityFrameworkCore;
using CqrsLiteDemo.Data;
using CqrsLiteDemo.Models.Write;

namespace CqrsLiteDemo.Commands;

public class AddQuoteHandler
{
    private readonly CqrsDbContext _db;

    public AddQuoteHandler(CqrsDbContext db)
    {
        _db = db;
    }

    public async Task<Quote> HandleAsync(AddQuoteCommand command)
    {
        if (string.IsNullOrWhiteSpace(command.Text))
        {
            throw new ArgumentException("Quote text is required.", nameof(command));
        }

        if (command.Text.Length > 500)
        {
            throw new ArgumentException("Quote text must be 500 characters or fewer.", nameof(command));
        }

        var authorExists = await _db.Authors.AnyAsync(a => a.Id == command.AuthorId);
        if (!authorExists)
        {
            throw new ArgumentException($"Author {command.AuthorId} does not exist.", nameof(command));
        }

        var quote = new Quote
        {
            AuthorId = command.AuthorId,
            Text = command.Text.Trim()
        };

        _db.Quotes.Add(quote);
        await _db.SaveChangesAsync();

        return quote;
    }
}
