# Day 11 — Task 1: Profile a Slow Endpoint

## Endpoint

- **Method / route:** `GET /api/day11/authors-with-quotes-slow`
- **Project:** `Day-11/QuotesApi.Profiling` — a small, standalone ASP.NET Core
  minimal API built on the same stack as the Week-1 Quotes API (Day-4/Day-5):
  .NET 10, EF Core, Sqlite.
- **What it does:** returns every author together with all of that author's
  quotes.
- **Why it is intentionally slow:** the handler queries all authors, then
  loops over them and issues a *separate* database round trip for each
  author's quotes:

  ```csharp
  var authors = await db.Authors.AsNoTracking().ToListAsync();

  foreach (var author in authors)
  {
      var quotes = await db.Quotes
          .AsNoTracking()
          .Where(q => q.AuthorId == author.Id)
          .ToListAsync();
      // ...
  }
  ```

  This is the classic N+1 pattern: 1 query for the authors + N queries for
  quotes, where N is the author count (300 in the seed data).

### Why a new project instead of reusing Day-4/Day-5 directly

The existing Week-1 `Quote` entity (`Day-4/Day-5/QuotesApi/Models/Quote.cs`)
stores `Author` as a plain denormalized `string`, not a related entity — so
there is no `Authors` table to reproduce a real authors → quotes N+1 against.
`Day-11/QuotesApi.Profiling` reuses the same EF Core + Sqlite approach and
coding style, and adds a normalized `Author` entity plus a `Quote.AuthorId`
column so the N+1 pattern described in the exercise can be reproduced
faithfully, without touching Day-1 through Day-10.

`Quote.AuthorId` is deliberately declared as a plain `int` column with no
navigation property and no Fluent API relationship configuration in
`ProfilingDbContext`. EF Core only creates an index automatically for a
configured foreign key, so this column ends up with **no index** — the
"missing index" half of the exercise, reproduced the same way the example
pseudocode does it (`Where(q => q.AuthorId == author.Id)` against a plain
column EF Core doesn't know is a relationship).

Seed data: 300 authors, 5–14 quotes each (deterministic:
`5 + (author.Id % 10)`), 2,850 quotes total.

## Load test

Tool: `ab` (Apache Bench), already installed on this machine. `bombardier`
and `k6` were checked and are not installed; no Python was used anywhere in
this exercise.

Command:

```
ab -n 200 -c 10 http://localhost:5211/api/day11/authors-with-quotes-slow
```

The test was run twice back to back to rule out a cold-start artifact
(raw output: `results/load-test-run1.txt`, `results/load-test-run2.txt`;
`results/load-test.txt` is a copy of run 2, used as the reported baseline
below).

| Metric | Run 1 | Run 2 (reported) |
|---|---|---|
| Requests | 200 | 200 |
| Concurrency | 10 | 10 |
| Duration | 5.810 s | 4.520 s |
| Throughput | 34.42 req/s | 44.25 req/s |
| p50 | 273 ms | **217 ms** |
| p99 | 420 ms | **384 ms** |
| Failed requests | 0 | 0 |

Both runs are consistent (no timeouts, no failures, similar shape), so run 2
is reported as the baseline: **p50 = 217 ms, p99 = 384 ms**, ~44 req/s at
concurrency 10, 0 errors.

## SQL emitted

EF Core SQL logging was enabled for this project only (`Program.cs`, via
`DbContextOptionsBuilder.LogTo(...)` with `EnableSensitiveDataLogging()`),
scoped to the `Development` environment. A single `curl` request to the
endpoint was captured to a log file; the full raw capture is in
`results/sql-output-full.txt`, and a representative trim is in
`results/sql-output.txt`.

**One HTTP request to `GET /api/day11/authors-with-quotes-slow` generated
exactly 301 SQL statements**, verified by counting `Executed DbCommand`
entries in the request-scoped portion of the log:

1. One query for all authors:

   ```sql
   SELECT "a"."Id", "a"."Name"
   FROM "Authors" AS "a"
   ```

2. Then 300 queries — one per author — each identical in shape but with a
   different parameter value:

   ```sql
   -- @author_Id='1'
   SELECT "q"."Id", "q"."AuthorId", "q"."Text"
   FROM "Quotes" AS "q"
   WHERE "q"."AuthorId" = @author_Id

   -- @author_Id='2'
   SELECT "q"."Id", "q"."AuthorId", "q"."Text"
   FROM "Quotes" AS "q"
   WHERE "q"."AuthorId" = @author_Id

   -- ... repeated for @author_Id = 3, 4, 5, ... up to 300
   ```

This is direct evidence of the N+1 behavior: 1 + 300 = 301 round trips to
the database for a single API call.

## Execution plan

Database: **SQLite** (`day11.db`), inspected with the `sqlite3` CLI's
`EXPLAIN QUERY PLAN`. This is SQLite's own plan format, not a SQL Server
plan. Full output: `results/execution-plan.txt`.

- `sqlite_master` confirms **no index exists on `Quotes`** at all (not even
  on the primary key beyond the implicit rowid, and none on `AuthorId`).
- The authors query plan:

  ```
  QUERY PLAN
  `--SCAN a
  ```

  A full scan of `Authors` (300 rows) — fine, since it runs once.

- The per-author quotes query plan (run 300 times per request, against a
  2,850-row `Quotes` table):

  ```
  QUERY PLAN
  `--SCAN q
  ```

  `SCAN q` means SQLite reads every row of `Quotes` and filters `AuthorId`
  in memory — there is no index it could `SEARCH` with instead. Run 300
  times per request, that is roughly 300 × 2,850 ≈ 855,000 row comparisons
  for the quote lookups alone.

- For comparison, `results/execution-plan.txt` also shows the plan on a
  scratch copy of the database with `CREATE INDEX ... ON Quotes(AuthorId)`
  applied (not applied to the real `day11.db`, per the "do not fix yet"
  instruction for this task):

  ```
  QUERY PLAN
  `--SEARCH q USING INDEX IX_Quotes_AuthorId_SCRATCH (AuthorId=?)
  ```

  `SEARCH ... USING INDEX` is an indexed point lookup instead of a full
  scan — this is what an index on `AuthorId` would change, but it was not
  applied to the baseline being measured.

## Two biggest problems

1. **N+1 query pattern (authors → quotes).**
   - *Evidence:* `results/sql-output.txt` / `sql-output-full.txt` show
     exactly 301 `Executed DbCommand` entries for one HTTP request — 1
     authors query plus 300 sequential, awaited quote queries, one per
     author.
   - *Impact:* the request pays 300 extra network/IPC round trips to the
     database instead of 1. This is the dominant cost: even with SQLite
     in-process (no network latency), per-query overhead compounds across
     300 sequential `await`ed calls, driving p50 to 217 ms and p99 to
     384 ms for a dataset of only 2,850 quote rows. This scales linearly
     with author count — 10x the authors means roughly 10x the queries and
     latency.

2. **Missing index on `Quotes.AuthorId`.**
   - *Evidence:* `sqlite_master` shows zero indexes on `Quotes`;
     `EXPLAIN QUERY PLAN` on the per-author lookup returns `SCAN q` (full
     table scan) instead of a `SEARCH ... USING INDEX`.
   - *Impact:* each of the 300 per-author queries scans the entire
     `Quotes` table (2,850 rows) instead of seeking directly to the
     matching rows. This multiplies the cost of problem #1 — it is not
     just 300 round trips, but 300 full scans. At larger data volumes this
     problem grows worse than the N+1 round-trip cost alone, since each
     individual query gets more expensive as the table grows, independent
     of author count.

These are the two biggest problems by the evidence actually collected. The
query shape and row counts here are small (2,850 rows), so a third
candidate — serialized `await` calls in the loop (no batching or
parallel fan-out) — is really a restatement of problem #1 and was folded
into it rather than counted separately.

## Conclusion

The endpoint's code is short and looks unremarkable — a `ToListAsync()`
followed by a `foreach` loop with another `ToListAsync()` inside it. Nothing
in the C# source signals how expensive that loop actually is. Profiling
was necessary to see the real cost: enabling SQL logging revealed the loop
executes 300 separate database round trips per request, not the "one query"
it might appear to be at a glance; the execution plan then revealed that
each of those 300 queries is *also* a full table scan rather than an
indexed lookup, because the column being filtered on was never configured
as an indexed foreign key. Neither fact is visible from reading the
endpoint code alone — only from actually running it, capturing the SQL,
and inspecting the query plan.

## Files

```
Day-11/
  README.md
  QuotesApi.Profiling/            # new standalone ASP.NET Core + EF Core + Sqlite project
    Program.cs
    Models/Author.cs
    Models/Quote.cs
    Data/ProfilingDbContext.cs
    Properties/launchSettings.json
    .gitignore                    # excludes the generated day11.db
  results/
    load-test-run1.txt            # ab run 1 (raw)
    load-test-run2.txt            # ab run 2 (raw)
    load-test.txt                 # copy of run 2, reported baseline
    sql-output-full.txt           # full raw EF Core SQL log for one request (301 commands)
    sql-output.txt                # trimmed, annotated version of the above
    execution-plan.txt            # sqlite3 EXPLAIN QUERY PLAN evidence
```

## How to reproduce

```
cd Day-11/QuotesApi.Profiling
dotnet run --urls http://localhost:5211
# in another shell:
curl http://localhost:5211/api/day11/authors-with-quotes-slow
ab -n 200 -c 10 http://localhost:5211/api/day11/authors-with-quotes-slow
sqlite3 day11.db "EXPLAIN QUERY PLAN SELECT * FROM Quotes WHERE AuthorId = 150;"
```
