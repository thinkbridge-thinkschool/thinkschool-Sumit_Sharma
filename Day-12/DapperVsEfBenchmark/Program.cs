using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using CqrsLiteDemo.Data;
using CqrsLiteDemo.Models.Read;
using CqrsLiteDemo.Models.Write;
using CqrsLiteDemo.Queries;

const int AuthorCount = 30;
const int QuotesPerAuthor = 150;
const int TargetAuthorId = 1;
const int WarmupIterations = 20;
const int MeasuredIterations = 300;

var dbPath = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "benchmark.db"));
var connectionString = $"Data Source={dbPath}";

var mainReport = new List<string>();
var efSqlReport = new List<string>();
var dapperSqlReport = new List<string>();

void Line(string text = "")
{
    Console.WriteLine(text);
    mainReport.Add(text);
}

DbContextOptions<CqrsDbContext> PlainOptions() =>
    new DbContextOptionsBuilder<CqrsDbContext>()
        .UseSqlite(connectionString)
        .Options;

DbContextOptions<CqrsDbContext> LoggingOptions(Action<string> sink) =>
    new DbContextOptionsBuilder<CqrsDbContext>()
        .UseSqlite(connectionString)
        .LogTo(sink, new[] { DbLoggerCategory.Database.Command.Name }, LogLevel.Information)
        .EnableSensitiveDataLogging()
        .Options;

Line("Day 12 - Task 2: EF Core vs Dapper read benchmark");
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

    var authors = Enumerable.Range(1, AuthorCount)
        .Select(i => new Author { Name = $"Author {i}" })
        .ToList();
    setupDb.Authors.AddRange(authors);
    await setupDb.SaveChangesAsync();

    var quotes = new List<Quote>();
    foreach (var author in authors)
    {
        for (var q = 1; q <= QuotesPerAuthor; q++)
        {
            quotes.Add(new Quote { AuthorId = author.Id, Text = $"Quote {q} from {author.Name}" });
        }
    }
    setupDb.Quotes.AddRange(quotes);
    await setupDb.SaveChangesAsync();

    Line($"Seeded {await setupDb.Authors.CountAsync()} authors and {await setupDb.Quotes.CountAsync()} quotes.");
    Line($"Target query: GetAuthorQuotesQuery {{ AuthorId = {TargetAuthorId} }} " +
         $"({QuotesPerAuthor} quotes expected).");
}

Line();
Line(new string('=', 70));
Line("SQL CAPTURE");
Line(new string('-', 70));

List<AuthorQuoteReadModel> efRows;
await using (var db = new CqrsDbContext(LoggingOptions(msg =>
{
    Console.WriteLine(msg);
    mainReport.Add(msg);
    efSqlReport.Add(msg);
})))
{
    var handler = new GetAuthorQuotesHandler(db);
    efRows = await handler.HandleAsync(new GetAuthorQuotesQuery { AuthorId = TargetAuthorId });
}

Line();
var dapperHandler = new GetAuthorQuotesDapperHandler(connectionString);
var dapperRows = await dapperHandler.HandleAsync(new GetAuthorQuotesQuery { AuthorId = TargetAuthorId });

var dapperSqlLog =
    $"Executed Dapper query [Parameters=[@AuthorId={TargetAuthorId}]]{Environment.NewLine}" +
    GetAuthorQuotesDapperHandler.Sql;
Line(dapperSqlLog);
dapperSqlReport.Add(dapperSqlLog);

Line();
Line(new string('=', 70));
Line("RESULT PARITY CHECK");
Line(new string('-', 70));
Line($"EF Core rows: {efRows.Count}, Dapper rows: {dapperRows.Count}");

var efShaped = efRows
    .OrderBy(r => r.QuoteId)
    .Select(r => (r.AuthorId, r.AuthorName, r.QuoteId, r.QuoteText))
    .ToList();
var dapperShaped = dapperRows
    .OrderBy(r => r.QuoteId)
    .Select(r => (r.AuthorId, r.AuthorName, r.QuoteId, r.QuoteText))
    .ToList();
var rowsMatch = efShaped.SequenceEqual(dapperShaped);
Line($"Row-for-row match between EF Core and Dapper results: {rowsMatch}");

if (!rowsMatch)
{
    throw new InvalidOperationException("EF Core and Dapper returned different results; benchmark aborted.");
}

Line();
Line(new string('=', 70));
Line("BENCHMARK");
Line(new string('-', 70));
Line($"Warm-up iterations (untimed): {WarmupIterations}");
Line($"Measured iterations: {MeasuredIterations}");
Line("Timing method: System.Diagnostics.Stopwatch, one query per iteration, fresh " +
     "DbContext/SqliteConnection per iteration for both implementations.");
Line();

for (var i = 0; i < WarmupIterations; i++)
{
    await using var db = new CqrsDbContext(PlainOptions());
    var handler = new GetAuthorQuotesHandler(db);
    await handler.HandleAsync(new GetAuthorQuotesQuery { AuthorId = TargetAuthorId });
}
for (var i = 0; i < WarmupIterations; i++)
{
    await dapperHandler.HandleAsync(new GetAuthorQuotesQuery { AuthorId = TargetAuthorId });
}

GC.Collect();
GC.WaitForPendingFinalizers();
GC.Collect();
var efAllocStart = GC.GetAllocatedBytesForCurrentThread();
var efStopwatch = Stopwatch.StartNew();
var efRowCounts = new List<int>(MeasuredIterations);
for (var i = 0; i < MeasuredIterations; i++)
{
    await using var db = new CqrsDbContext(PlainOptions());
    var handler = new GetAuthorQuotesHandler(db);
    var rows = await handler.HandleAsync(new GetAuthorQuotesQuery { AuthorId = TargetAuthorId });
    efRowCounts.Add(rows.Count);
}
efStopwatch.Stop();
var efAllocBytes = GC.GetAllocatedBytesForCurrentThread() - efAllocStart;

GC.Collect();
GC.WaitForPendingFinalizers();
GC.Collect();
var dapperAllocStart = GC.GetAllocatedBytesForCurrentThread();
var dapperStopwatch = Stopwatch.StartNew();
var dapperRowCounts = new List<int>(MeasuredIterations);
for (var i = 0; i < MeasuredIterations; i++)
{
    var rows = await dapperHandler.HandleAsync(new GetAuthorQuotesQuery { AuthorId = TargetAuthorId });
    dapperRowCounts.Add(rows.Count);
}
dapperStopwatch.Stop();
var dapperAllocBytes = GC.GetAllocatedBytesForCurrentThread() - dapperAllocStart;

var efAvgMs = efStopwatch.Elapsed.TotalMilliseconds / MeasuredIterations;
var dapperAvgMs = dapperStopwatch.Elapsed.TotalMilliseconds / MeasuredIterations;
var efAvgAllocBytes = efAllocBytes / (double)MeasuredIterations;
var dapperAvgAllocBytes = dapperAllocBytes / (double)MeasuredIterations;

Line("EF Core:");
Line($"  Total elapsed:   {efStopwatch.Elapsed.TotalMilliseconds:F3} ms");
Line($"  Average elapsed: {efAvgMs:F4} ms/iteration");
Line($"  Rows returned per iteration: {efRowCounts.Distinct().Single()}");
Line($"  Allocated (approx): {efAllocBytes:N0} bytes total, {efAvgAllocBytes:F1} bytes/iteration");
Line();
Line("Dapper:");
Line($"  Total elapsed:   {dapperStopwatch.Elapsed.TotalMilliseconds:F3} ms");
Line($"  Average elapsed: {dapperAvgMs:F4} ms/iteration");
Line($"  Rows returned per iteration: {dapperRowCounts.Distinct().Single()}");
Line($"  Allocated (approx): {dapperAllocBytes:N0} bytes total, {dapperAvgAllocBytes:F1} bytes/iteration");
Line();

var ratio = efAvgMs / dapperAvgMs;
Line(new string('-', 70));
Line($"EF Core average / Dapper average = {ratio:F2}x");
Line(new string('=', 70));
Line("Run complete.");

var resultsDir = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "..", "results"));
Directory.CreateDirectory(resultsDir);

await File.WriteAllLinesAsync(Path.Combine(resultsDir, "dapper-vs-ef-output.txt"), mainReport);
await File.WriteAllLinesAsync(Path.Combine(resultsDir, "ef-sql.txt"), efSqlReport);
await File.WriteAllLinesAsync(Path.Combine(resultsDir, "dapper-sql.txt"), dapperSqlReport);

Console.WriteLine();
Console.WriteLine($"Full report written to: {Path.Combine(resultsDir, "dapper-vs-ef-output.txt")}");
Console.WriteLine($"EF Core SQL written to: {Path.Combine(resultsDir, "ef-sql.txt")}");
Console.WriteLine($"Dapper SQL written to: {Path.Combine(resultsDir, "dapper-sql.txt")}");
