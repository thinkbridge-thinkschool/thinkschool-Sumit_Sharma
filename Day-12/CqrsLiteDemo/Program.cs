using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using CqrsLiteDemo.Commands;
using CqrsLiteDemo.Data;
using CqrsLiteDemo.Models.Write;
using CqrsLiteDemo.Queries;

var dbPath = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "day12.db"));
var connectionString = $"Data Source={dbPath}";

var mainReport = new List<string>();
var sqlReport = new List<string>();

void Line(string text = "")
{
    Console.WriteLine(text);
    mainReport.Add(text);
}

void SqlLog(string message)
{
    Console.WriteLine(message);
    mainReport.Add(message);
    sqlReport.Add(message);
}

DbContextOptions<CqrsDbContext> PlainOptions() =>
    new DbContextOptionsBuilder<CqrsDbContext>()
        .UseSqlite(connectionString)
        .Options;

DbContextOptions<CqrsDbContext> LoggingOptions() =>
    new DbContextOptionsBuilder<CqrsDbContext>()
        .UseSqlite(connectionString)
        .LogTo(SqlLog, new[] { DbLoggerCategory.Database.Command.Name }, LogLevel.Information)
        .EnableSensitiveDataLogging()
        .Options;

Line("Day 12 - Task 1: Read Models + CQRS-lite");
Line($"Run started (local clock): {DateTimeOffset.Now:O}");
Line(new string('=', 70));
Line();

if (File.Exists(dbPath))
{
    File.Delete(dbPath);
}

await using (var setupDb = new CqrsDbContext(PlainOptions()))
{
    await setupDb.Database.EnsureCreatedAsync();

    var author1 = new Author { Name = "Ada Lovelace" };
    var author2 = new Author { Name = "Grace Hopper" };
    setupDb.Authors.AddRange(author1, author2);
    await setupDb.SaveChangesAsync();

    setupDb.Quotes.AddRange(
        new Quote { AuthorId = author1.Id, Text = "That brain of mine is something more than merely mortal." },
        new Quote { AuthorId = author2.Id, Text = "The most dangerous phrase is: we've always done it this way." });
    await setupDb.SaveChangesAsync();

    Line($"Seeded {await setupDb.Authors.CountAsync()} authors and {await setupDb.Quotes.CountAsync()} quotes.");
    Line($"Command path below will target AuthorId={author1.Id} ({author1.Name}).");
}

Line();

Line(new string('=', 70));
Line("COMMAND PATH");
Line(new string('-', 70));
Line("AddQuoteCommand");
Line("  -> AddQuoteHandler.HandleAsync validates AuthorId/Text");
Line("  -> writes AuthorId/Text to the normalized Quote write model");
Line("  -> SaveChanges()");
Line();

int targetAuthorId;
await using (var db = new CqrsDbContext(PlainOptions()))
{
    targetAuthorId = await db.Authors.Select(a => a.Id).FirstAsync();
}

Line("Attempting an invalid command (empty Text) to demonstrate validation:");
await using (var db = new CqrsDbContext(PlainOptions()))
{
    var handler = new AddQuoteHandler(db);
    try
    {
        await handler.HandleAsync(new AddQuoteCommand { AuthorId = targetAuthorId, Text = "   " });
        Line("UNEXPECTED: invalid command was accepted.");
    }
    catch (ArgumentException ex)
    {
        Line($"Rejected as expected: {ex.Message}");
    }
}

Line();

Line("Executing a valid AddQuoteCommand:");
Quote createdQuote;
await using (var db = new CqrsDbContext(PlainOptions()))
{
    var handler = new AddQuoteHandler(db);
    var command = new AddQuoteCommand
    {
        AuthorId = targetAuthorId,
        Text = "A ship in port is safe, but that's not what ships are built for."
    };

    createdQuote = await handler.HandleAsync(command);
}

Line($"Command result (write model, NOT the read model): Quote.Id={createdQuote.Id}, " +
     $"Quote.AuthorId={createdQuote.AuthorId}, Quote.Text=\"{createdQuote.Text}\"");
Line();

Line("Verifying persistence by re-querying the normalized write model (Quotes table):");
await using (var db = new CqrsDbContext(PlainOptions()))
{
    var persisted = await db.Quotes.AsNoTracking().FirstAsync(q => q.Id == createdQuote.Id);
    Line($"Persisted row: Quote.Id={persisted.Id}, Quote.AuthorId={persisted.AuthorId}, " +
         $"Quote.Text=\"{persisted.Text}\"");
}

Line();

Line(new string('=', 70));
Line("QUERY PATH");
Line(new string('-', 70));
Line("GetAuthorQuotesQuery");
Line("  -> GetAuthorQuotesHandler.HandleAsync joins Authors/Quotes");
Line("  -> projects directly into AuthorQuoteReadModel (.Select-shaped Join)");
Line("  -> returns the read model - never touches SaveChanges()");
Line();

List<CqrsLiteDemo.Models.Read.AuthorQuoteReadModel> readModelResults;
await using (var db = new CqrsDbContext(PlainOptions()))
{
    var projectionQuery = db.Quotes
        .AsNoTracking()
        .Where(q => q.AuthorId == targetAuthorId)
        .Join(
            db.Authors.AsNoTracking(),
            quote => quote.AuthorId,
            author => author.Id,
            (quote, author) => new CqrsLiteDemo.Models.Read.AuthorQuoteReadModel
            {
                AuthorId = author.Id,
                AuthorName = author.Name,
                QuoteId = quote.Id,
                QuoteText = quote.Text
            });

    Line("Generated SQL (ToQueryString(), before execution):");
    Line(projectionQuery.ToQueryString());
    Line();
}

Line("Executing the query handler (EF Core SQL logging below shows the actual executed SQL):");
Line(new string('-', 70));
await using (var db = new CqrsDbContext(LoggingOptions()))
{
    var handler = new GetAuthorQuotesHandler(db);
    readModelResults = await handler.HandleAsync(new GetAuthorQuotesQuery { AuthorId = targetAuthorId });
}

Line(new string('-', 70));
Line();
Line($"Read model rows returned: {readModelResults.Count}");
foreach (var row in readModelResults)
{
    Line($"  AuthorQuoteReadModel: AuthorId={row.AuthorId}, AuthorName=\"{row.AuthorName}\", " +
         $"QuoteId={row.QuoteId}, QuoteText=\"{row.QuoteText}\"");
}

Line();
Line(new string('=', 70));
Line("Run complete. Command path wrote through the normalized write model;");
Line("query path read through a denormalized projection. Neither path touched the other's model.");

var resultsDir = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "..", "results"));
Directory.CreateDirectory(resultsDir);

await File.WriteAllLinesAsync(Path.Combine(resultsDir, "cqrs-output.txt"), mainReport);
await File.WriteAllLinesAsync(Path.Combine(resultsDir, "query-sql.txt"), sqlReport);

Console.WriteLine();
Console.WriteLine($"Full report written to: {Path.Combine(resultsDir, "cqrs-output.txt")}");
Console.WriteLine($"Query SQL written to: {Path.Combine(resultsDir, "query-sql.txt")}");
