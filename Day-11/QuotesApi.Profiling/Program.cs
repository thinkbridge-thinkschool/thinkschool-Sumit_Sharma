using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using QuotesApi.Profiling.Data;
using QuotesApi.Profiling.Models;

// Day-11 Task 1: a deliberately slow, standalone profiling API built on the
// same EF Core + Sqlite stack as the Week-1 QuotesApi (Day-4/Day-5). It adds
// a normalized Author entity (the Week-1 Quote.Author is a plain string) so
// that a genuine authors -> quotes N+1 pattern can be reproduced and
// measured. Nothing under Day-1..Day-10 is modified.

const int AuthorCount = 300;

var builder = WebApplication.CreateBuilder(args);

// The default console logger provider also logs the
// Microsoft.EntityFrameworkCore.Database.Command category; silence it here
// so SQL only appears once, via the explicit LogTo sink configured below.
builder.Logging.AddFilter(
    "Microsoft.EntityFrameworkCore.Database.Command",
    LogLevel.Warning);

builder.Services.AddDbContext<ProfilingDbContext>(options =>
{
    options.UseSqlite("Data Source=day11.db");

    // EF Core SQL logging for the Day-11 exercise: every SQL statement sent
    // to Sqlite, plus parameter values, is written to the console so the
    // N+1 pattern (one Authors query followed by N Quotes queries with
    // different @__author_Id_0 values) can be captured from stdout.
    options.LogTo(Console.WriteLine, LogLevel.Information)
        .EnableSensitiveDataLogging();
});

builder.Services.AddHealthChecks();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ProfilingDbContext>();
    db.Database.EnsureCreated();

    if (!db.Authors.Any())
    {
        var authors = new List<Author>(AuthorCount);
        for (var i = 1; i <= AuthorCount; i++)
        {
            authors.Add(new Author { Name = $"Author {i}" });
        }

        db.Authors.AddRange(authors);
        db.SaveChanges();

        var quotes = new List<Quote>();
        foreach (var author in authors)
        {
            // Deterministic 5-14 quotes per author so the exercise is
            // reproducible across runs.
            var quoteCount = 5 + (author.Id % 10);
            for (var q = 1; q <= quoteCount; q++)
            {
                quotes.Add(new Quote
                {
                    AuthorId = author.Id,
                    Text = $"Quote {q} by {author.Name}"
                });
            }
        }

        db.Quotes.AddRange(quotes);
        db.SaveChanges();
    }
}

app.MapHealthChecks("/health");

// Deliberately slow Day-11 endpoint: 1 query for all authors, then N
// additional queries (one per author) for that author's quotes. This is
// the N+1 pattern described in the Day-11 Task 1 exercise.
app.MapGet("/api/day11/authors-with-quotes-slow", async (ProfilingDbContext db) =>
{
    var authors = await db.Authors.AsNoTracking().ToListAsync();

    var result = new List<object>(authors.Count);

    foreach (var author in authors)
    {
        var quotes = await db.Quotes
            .AsNoTracking()
            .Where(q => q.AuthorId == author.Id)
            .ToListAsync();

        result.Add(new
        {
            author.Id,
            author.Name,
            QuoteCount = quotes.Count,
            Quotes = quotes.Select(q => q.Text)
        });
    }

    return Results.Ok(result);
});

app.Run();

public partial class Program
{
}
