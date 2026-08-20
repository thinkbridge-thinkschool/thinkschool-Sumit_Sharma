using ChangeTrackerBenchmark.Data;
using ChangeTrackerBenchmark.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using QueryTranslationDemo;

// Reuses the Day-10 Task 1 model/DbContext (BenchmarkQuote / BenchmarkDbContext)
// and its SQLite database (day10-benchmark.db) rather than creating a new schema.
var dbPath = Path.GetFullPath(Path.Combine(
    Directory.GetCurrentDirectory(), "..", "ChangeTrackerBenchmark", "day10-benchmark.db"));
var connectionString = $"Data Source={dbPath}";

var report = new List<string>();
void Line(string text = "")
{
    Console.WriteLine(text);
    report.Add(text);
}

void SqlLog(string message)
{
    Console.WriteLine(message);
    report.Add(message);
}

DbContextOptions<BenchmarkDbContext> BuildOptions() =>
    new DbContextOptionsBuilder<BenchmarkDbContext>()
        .UseSqlite(connectionString)
        .LogTo(SqlLog, new[] { DbLoggerCategory.Database.Command.Name }, LogLevel.Information)
        .Options;

Line("Day-10 Task 2 - EF Core Query Translation + Projections");
Line($"Run started (UTC offset local clock): {DateTimeOffset.Now:O}");
Line(new string('=', 70));
Line();

// ---------------------------------------------------------------------
// 0. Reuse (or, if never seeded, minimally seed) the Task-1 database/table.
//    This does not touch or resize the Task-1 10,000-row seed if present.
// ---------------------------------------------------------------------
await using (var setupContext = new BenchmarkDbContext(
    new DbContextOptionsBuilder<BenchmarkDbContext>().UseSqlite(connectionString).Options))
{
    await setupContext.Database.EnsureCreatedAsync();

    var existingCount = await setupContext.BenchmarkQuotes.CountAsync();
    if (existingCount == 0)
    {
        Line("Day10BenchmarkQuotes is empty (Task 1 has not been run in this environment yet).");
        Line("Seeding 200 deterministic rows for the Task 2 query-translation exercise only.");

        for (var i = 1; i <= 200; i++)
        {
            setupContext.BenchmarkQuotes.Add(new BenchmarkQuote
            {
                Id = i,
                Author = $"Day-10 Benchmark Author {i % 50}",
                Text = i % 3 == 0
                    ? $"Day-10 benchmark quote body #{i} - a longer seeded quote used only for the EF Core query translation exercise, not real quote data."
                    : $"Short quote #{i}."
            });
        }

        await setupContext.SaveChangesAsync();
        Line("Seed complete.");
    }
    else
    {
        Line($"Reusing existing Day10BenchmarkQuotes table ({existingCount} rows already present, e.g. from Task 1).");
    }
}

Line();

// ---------------------------------------------------------------------
// SECTION 1: Whole-entity query
// ---------------------------------------------------------------------
Line(new string('=', 70));
Line("SECTION 1: Whole-entity query");
Line(new string('-', 70));

List<BenchmarkQuote> wholeEntityResults;
string wholeEntitySql;

await using (var context = new BenchmarkDbContext(BuildOptions()))
{
    var wholeEntityQuery = context.BenchmarkQuotes
        .Where(q => q.Id <= 100);

    wholeEntitySql = wholeEntityQuery.ToQueryString();

    Line("LINQ:");
    Line("  context.BenchmarkQuotes.Where(q => q.Id <= 100)");
    Line();
    Line("Generated SQL (ToQueryString()):");
    Line(wholeEntitySql);
    Line();
    Line("Executing (EF Core command logging below)...");

    wholeEntityResults = await wholeEntityQuery.ToListAsync();
}

Line();
Line($"Row count returned: {wholeEntityResults.Count}");
Line($"Sample row[0]: Id={wholeEntityResults[0].Id}, Author=\"{wholeEntityResults[0].Author}\", Text.Length={wholeEntityResults[0].Text.Length}");
Line();

// ---------------------------------------------------------------------
// SECTION 2: Projection query (.Select)
// ---------------------------------------------------------------------
Line(new string('=', 70));
Line("SECTION 2: Projection query (.Select) - same filter, fewer columns");
Line(new string('-', 70));

List<object> projectionResultsUntyped;
string projectionSql;

await using (var context = new BenchmarkDbContext(BuildOptions()))
{
    var projectionQuery = context.BenchmarkQuotes
        .Where(q => q.Id <= 100)
        .Select(q => new { q.Id, q.Author });

    projectionSql = projectionQuery.ToQueryString();

    Line("LINQ:");
    Line("  context.BenchmarkQuotes");
    Line("      .Where(q => q.Id <= 100)");
    Line("      .Select(q => new { q.Id, q.Author })");
    Line();
    Line("Generated SQL (ToQueryString()):");
    Line(projectionSql);
    Line();
    Line("Executing (EF Core command logging below)...");

    var projectionResults = await projectionQuery.ToListAsync();
    projectionResultsUntyped = projectionResults.Cast<object>().ToList();

    Line();
    Line($"Row count returned: {projectionResults.Count}");
    Line($"Sample row[0]: Id={projectionResults[0].Id}, Author=\"{projectionResults[0].Author}\"");
}

Line();

// ---------------------------------------------------------------------
// SECTION 3: Before/after column comparison
// ---------------------------------------------------------------------
Line(new string('=', 70));
Line("SECTION 3: Column comparison (same 100 rows, same predicate)");
Line(new string('-', 70));
Line("Whole entity  SELECT columns: Id, Author, Text");
Line("Projection    SELECT columns: Id, Author");
Line("Column dropped by the projection: Text");
Line($"Row counts equal (both variants): {wholeEntityResults.Count == projectionResultsUntyped.Count} " +
     $"({wholeEntityResults.Count} vs {projectionResultsUntyped.Count})");
Line();

// ---------------------------------------------------------------------
// SECTION 4: Non-translatable expression (accidental client-side risk)
// ---------------------------------------------------------------------
Line(new string('=', 70));
Line("SECTION 4: Non-translatable expression attempt");
Line(new string('-', 70));

await using (var context = new BenchmarkDbContext(BuildOptions()))
{
    Line("LINQ:");
    Line("  context.BenchmarkQuotes.Where(q => NonTranslatableFilters.IsLongQuote(q.Text))");
    Line("  where: public static bool IsLongQuote(string text) => text.Length > 30;");
    Line();

    try
    {
        var badResults = await context.BenchmarkQuotes
            .Where(q => NonTranslatableFilters.IsLongQuote(q.Text))
            .ToListAsync();

        Line($"UNEXPECTED: query succeeded without throwing and returned {badResults.Count} rows.");
    }
    catch (Exception ex)
    {
        Line($"EF Core threw: {ex.GetType().FullName}");
        Line($"Message: {ex.Message}");
        Line();
        Line("Explanation: this EF Core version does NOT silently run the whole filter");
        Line("client-side. IsLongQuote() is a plain C# method with no known SQL translation,");
        Line("so EF Core's query translator refuses to build the query at all and throws");
        Line("InvalidOperationException at ToListAsync() time, before any rows are read.");
    }
}

Line();

// ---------------------------------------------------------------------
// SECTION 5: Fixed, SQL-translatable expression
// ---------------------------------------------------------------------
Line(new string('=', 70));
Line("SECTION 5: Fixed, SQL-translatable expression");
Line(new string('-', 70));

List<BenchmarkQuote> fixedResults;
string fixedSql;

await using (var context = new BenchmarkDbContext(BuildOptions()))
{
    var fixedQuery = context.BenchmarkQuotes
        .Where(q => q.Text.Length > 30);

    fixedSql = fixedQuery.ToQueryString();

    Line("LINQ:");
    Line("  context.BenchmarkQuotes.Where(q => q.Text.Length > 30)");
    Line();
    Line("Generated SQL (ToQueryString()):");
    Line(fixedSql);
    Line();
    Line("Executing (EF Core command logging below)...");

    fixedResults = await fixedQuery.ToListAsync();
}

Line();
Line($"Row count returned: {fixedResults.Count}");
Line("Confirms the length filter now runs inside SQLite (LENGTH(\"Text\") > 30 in the SQL above)");
Line("instead of failing translation or being evaluated row-by-row in .NET.");
Line();

Line(new string('=', 70));
Line("Run complete.");

var resultsDir = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "..", "results"));
Directory.CreateDirectory(resultsDir);
var resultsPath = Path.Combine(resultsDir, "query-translation-output.txt");
await File.WriteAllLinesAsync(resultsPath, report);

Console.WriteLine();
Console.WriteLine($"Full report written to: {resultsPath}");
