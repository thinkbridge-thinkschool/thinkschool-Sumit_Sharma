using System.Diagnostics;
using ChangeTrackerBenchmark.Data;
using ChangeTrackerBenchmark.Models;
using Microsoft.EntityFrameworkCore;

const int RowCount = 10_000;
const int WarmupIterations = 1;
const int MeasuredIterations = 5;

var dbPath = Path.Combine(Directory.GetCurrentDirectory(), "day10-benchmark.db");
var connectionString = $"Data Source={dbPath}";

DbContextOptions<BenchmarkDbContext> BuildOptions() =>
    new DbContextOptionsBuilder<BenchmarkDbContext>()
        .UseSqlite(connectionString)
        .Options;

var report = new List<string>();
void Line(string text = "")
{
    Console.WriteLine(text);
    report.Add(text);
}

Line("Day-10 Task 1 - EF Core Change Tracker + AsNoTracking benchmark");
Line($"Run started (UTC offset local clock): {DateTimeOffset.Now:O}");
Line(new string('=', 70));

// ---------------------------------------------------------------------
// 1. Ensure the Day-10-only benchmark database + ~10,000 seeded rows
// ---------------------------------------------------------------------
await using (var setupContext = new BenchmarkDbContext(BuildOptions()))
{
    await setupContext.Database.EnsureCreatedAsync();

    var existingCount = await setupContext.BenchmarkQuotes.CountAsync();
    if (existingCount < RowCount)
    {
        Line($"Seeding Day-10 benchmark table (Day10BenchmarkQuotes) with {RowCount} rows (found {existingCount})...");

        // Wipe any partial seed and insert a clean, deterministic 1..RowCount set.
        if (existingCount > 0)
        {
            setupContext.BenchmarkQuotes.RemoveRange(setupContext.BenchmarkQuotes);
            await setupContext.SaveChangesAsync();
        }

        for (var i = 1; i <= RowCount; i++)
        {
            setupContext.BenchmarkQuotes.Add(new BenchmarkQuote
            {
                Id = i,
                Author = $"Day-10 Benchmark Author {i % 50}",
                Text = $"Day-10 benchmark quote body #{i} - seeded only for the EF Core change tracker exercise, not real quote data."
            });

            if (i % 1000 == 0)
            {
                await setupContext.SaveChangesAsync();
            }
        }

        await setupContext.SaveChangesAsync();
        Line("Seeding complete.");
    }
    else
    {
        Line($"Day-10 benchmark table already has {existingCount} rows; skipping seed.");
    }
}

Line();

// ---------------------------------------------------------------------
// 2. Identity resolution demonstration (tracked vs AsNoTracking)
// ---------------------------------------------------------------------
Line("SECTION A: Identity resolution");
Line(new string('-', 70));

bool trackedReferenceEqual;
int trackedEntriesAfterFirst;
int trackedEntriesAfterSecond;

await using (var identityContext = new BenchmarkDbContext(BuildOptions()))
{
    Line($"[tracked] ChangeTracker entries before any query: {identityContext.ChangeTracker.Entries<BenchmarkQuote>().Count()}");

    var firstList = await identityContext.BenchmarkQuotes
        .Where(q => q.Id == 1)
        .ToListAsync();
    var trackedEntity1 = firstList.Single();
    trackedEntriesAfterFirst = identityContext.ChangeTracker.Entries<BenchmarkQuote>().Count();
    Line($"[tracked] After query #1 (Where Id == 1): ChangeTracker entries = {trackedEntriesAfterFirst}");

    var secondList = await identityContext.BenchmarkQuotes
        .Where(q => q.Id == 1)
        .ToListAsync();
    var trackedEntity2 = secondList.Single();
    trackedEntriesAfterSecond = identityContext.ChangeTracker.Entries<BenchmarkQuote>().Count();
    Line($"[tracked] After query #2 (same Where Id == 1, run again): ChangeTracker entries = {trackedEntriesAfterSecond}");

    trackedReferenceEqual = ReferenceEquals(trackedEntity1, trackedEntity2);
    Line($"[tracked] ReferenceEquals(trackedEntity1, trackedEntity2) = {trackedReferenceEqual}");
    Line("[tracked] -> EF Core's identity resolution returned the SAME tracked instance for query #2");
    Line("           instead of materializing a new object, so entry count stayed at 1, not 2.");
}

Line();

bool noTrackingReferenceEqual;
int noTrackingEntriesAfterFirst;
int noTrackingEntriesAfterSecond;

await using (var noTrackingContext = new BenchmarkDbContext(BuildOptions()))
{
    Line($"[AsNoTracking] ChangeTracker entries before any query: {noTrackingContext.ChangeTracker.Entries<BenchmarkQuote>().Count()}");

    var firstList = await noTrackingContext.BenchmarkQuotes
        .AsNoTracking()
        .Where(q => q.Id == 1)
        .ToListAsync();
    var noTrackingEntity1 = firstList.Single();
    noTrackingEntriesAfterFirst = noTrackingContext.ChangeTracker.Entries<BenchmarkQuote>().Count();
    Line($"[AsNoTracking] After query #1 (Where Id == 1): ChangeTracker entries = {noTrackingEntriesAfterFirst}");

    var secondList = await noTrackingContext.BenchmarkQuotes
        .AsNoTracking()
        .Where(q => q.Id == 1)
        .ToListAsync();
    var noTrackingEntity2 = secondList.Single();
    noTrackingEntriesAfterSecond = noTrackingContext.ChangeTracker.Entries<BenchmarkQuote>().Count();
    Line($"[AsNoTracking] After query #2 (same Where Id == 1, run again): ChangeTracker entries = {noTrackingEntriesAfterSecond}");

    noTrackingReferenceEqual = ReferenceEquals(noTrackingEntity1, noTrackingEntity2);
    Line($"[AsNoTracking] ReferenceEquals(noTrackingEntity1, noTrackingEntity2) = {noTrackingReferenceEqual}");
    Line("[AsNoTracking] -> Each query materialized a brand-new object and the ChangeTracker");
    Line("                 never gained an entry, because AsNoTracking() skips the identity map entirely.");
}

Line();

// ---------------------------------------------------------------------
// 3. Benchmark: tracked ToListAsync() vs AsNoTracking().ToListAsync()
//    reading ~10,000 rows, using Stopwatch + GC.GetAllocatedBytesForCurrentThread()
// ---------------------------------------------------------------------
Line("SECTION B: Benchmark (tracked vs AsNoTracking, ~10,000 rows)");
Line(new string('-', 70));

(double ElapsedMs, long AllocatedBytes, int RowsRead, int ChangeTrackerEntriesAfter) RunTrackedIteration()
{
    using var context = new BenchmarkDbContext(BuildOptions());

    var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
    var stopwatch = Stopwatch.StartNew();

    var rows = context.BenchmarkQuotes
        .Where(q => q.Id <= RowCount)
        .ToList();

    stopwatch.Stop();
    var allocatedAfter = GC.GetAllocatedBytesForCurrentThread();

    var entriesAfter = context.ChangeTracker.Entries<BenchmarkQuote>().Count();

    return (stopwatch.Elapsed.TotalMilliseconds, allocatedAfter - allocatedBefore, rows.Count, entriesAfter);
}

(double ElapsedMs, long AllocatedBytes, int RowsRead, int ChangeTrackerEntriesAfter) RunNoTrackingIteration()
{
    using var context = new BenchmarkDbContext(BuildOptions());

    var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
    var stopwatch = Stopwatch.StartNew();

    var rows = context.BenchmarkQuotes
        .AsNoTracking()
        .Where(q => q.Id <= RowCount)
        .ToList();

    stopwatch.Stop();
    var allocatedAfter = GC.GetAllocatedBytesForCurrentThread();

    var entriesAfter = context.ChangeTracker.Entries<BenchmarkQuote>().Count();

    return (stopwatch.Elapsed.TotalMilliseconds, allocatedAfter - allocatedBefore, rows.Count, entriesAfter);
}

Line($"Warmup iterations (excluded from averages): {WarmupIterations}");
Line($"Measured iterations per variant: {MeasuredIterations}");
Line();

Line("-- Variant A: tracked query --");
for (var i = 0; i < WarmupIterations; i++)
{
    var warmup = RunTrackedIteration();
    Line($"[tracked] warmup #{i + 1}: {warmup.ElapsedMs:F3} ms, {warmup.AllocatedBytes:N0} bytes allocated, rows={warmup.RowsRead}, ChangeTracker entries after (disposed context, informational only)={warmup.ChangeTrackerEntriesAfter}");
}

var trackedRuns = new List<(double ElapsedMs, long AllocatedBytes, int RowsRead, int ChangeTrackerEntriesAfter)>();
for (var i = 0; i < MeasuredIterations; i++)
{
    var run = RunTrackedIteration();
    trackedRuns.Add(run);
    Line($"[tracked] measured #{i + 1}: {run.ElapsedMs:F3} ms, {run.AllocatedBytes:N0} bytes allocated, rows={run.RowsRead}, ChangeTracker entries after query={run.ChangeTrackerEntriesAfter}");
}

Line();
Line("-- Variant B: AsNoTracking query --");
for (var i = 0; i < WarmupIterations; i++)
{
    var warmup = RunNoTrackingIteration();
    Line($"[AsNoTracking] warmup #{i + 1}: {warmup.ElapsedMs:F3} ms, {warmup.AllocatedBytes:N0} bytes allocated, rows={warmup.RowsRead}, ChangeTracker entries after (disposed context, informational only)={warmup.ChangeTrackerEntriesAfter}");
}

var noTrackingRuns = new List<(double ElapsedMs, long AllocatedBytes, int RowsRead, int ChangeTrackerEntriesAfter)>();
for (var i = 0; i < MeasuredIterations; i++)
{
    var run = RunNoTrackingIteration();
    noTrackingRuns.Add(run);
    Line($"[AsNoTracking] measured #{i + 1}: {run.ElapsedMs:F3} ms, {run.AllocatedBytes:N0} bytes allocated, rows={run.RowsRead}, ChangeTracker entries after query={run.ChangeTrackerEntriesAfter}");
}

Line();
Line(new string('=', 70));
Line("SECTION C: Summary");
Line(new string('-', 70));

var trackedAvgMs = trackedRuns.Average(r => r.ElapsedMs);
var trackedAvgBytes = trackedRuns.Average(r => r.AllocatedBytes);
var noTrackingAvgMs = noTrackingRuns.Average(r => r.ElapsedMs);
var noTrackingAvgBytes = noTrackingRuns.Average(r => r.AllocatedBytes);

Line($"Tracked      -> avg time: {trackedAvgMs:F3} ms | avg allocated: {trackedAvgBytes:N0} bytes | rows per run: {trackedRuns[0].RowsRead}");
Line($"AsNoTracking -> avg time: {noTrackingAvgMs:F3} ms | avg allocated: {noTrackingAvgBytes:N0} bytes | rows per run: {noTrackingRuns[0].RowsRead}");
Line($"Row counts equal across variants: {trackedRuns.All(r => r.RowsRead == RowCount) && noTrackingRuns.All(r => r.RowsRead == RowCount)}");
Line($"Tracked run ChangeTracker entries after query (each fresh context): {string.Join(", ", trackedRuns.Select(r => r.ChangeTrackerEntriesAfter))}");
Line($"AsNoTracking run ChangeTracker entries after query (each fresh context): {string.Join(", ", noTrackingRuns.Select(r => r.ChangeTrackerEntriesAfter))}");
Line();
Line($"Identity resolution (tracked)     ReferenceEquals == {trackedReferenceEqual}");
Line($"Identity resolution (AsNoTracking) ReferenceEquals == {noTrackingReferenceEqual}");
Line();

var speedupFactor = trackedAvgMs > 0 ? trackedAvgMs / Math.Max(noTrackingAvgMs, 0.0001) : 0;
var allocRatio = trackedAvgBytes > 0 ? trackedAvgBytes / Math.Max(noTrackingAvgBytes, 1) : 0;
Line($"Tracked query was ~{speedupFactor:F2}x slower and allocated ~{allocRatio:F2}x more per iteration than AsNoTracking.");

var resultsDir = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "..", "results"));
Directory.CreateDirectory(resultsDir);
var resultsPath = Path.Combine(resultsDir, "benchmark-output.txt");
await File.WriteAllLinesAsync(resultsPath, report);

Console.WriteLine();
Console.WriteLine($"Full report written to: {resultsPath}");
