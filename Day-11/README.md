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
    load-test-run1.txt            # Task 1: ab run 1 (raw)
    load-test-run2.txt            # Task 1: ab run 2 (raw)
    load-test.txt                 # Task 1: copy of run 2, reported baseline
    sql-output-full.txt           # Task 1: full raw EF Core SQL log for one request (301 commands)
    sql-output.txt                # Task 1: trimmed, annotated version of the above
    execution-plan.txt            # Task 1: sqlite3 EXPLAIN QUERY PLAN evidence
    execution-plan-before.txt     # Task 2: same "before" finding as execution-plan.txt, Task 2 naming
    execution-plan-after.txt      # Task 2: EXPLAIN QUERY PLAN after the index was added
    sql-output-after.txt          # Task 2: EF Core SQL log for one request after the fix (2 commands)
    load-test-after-run1.txt      # Task 2: ab run 1 after the fix (raw)
    load-test-after-run2.txt      # Task 2: ab run 2 after the fix (raw)
    load-test-after.txt           # Task 2: copy of run 2, reported "after" result
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

## Task 2 — Drop p99 by 10×

Same endpoint (`GET /api/day11/authors-with-quotes-slow`), same database
(`day11.db`, same seeded 300 authors / 2,850 quotes — the file was not
regenerated, only altered), same load test command as Task 1. Task 1's
original evidence files (`load-test*.txt`, `sql-output*.txt`,
`execution-plan.txt`) are unchanged.

### Before (Task 1 baseline, unchanged)

- p50 = 217 ms
- p99 = 384 ms
- Throughput ≈ 44.25 req/s
- SQL commands per request: **301** (1 authors query + 300 per-author quote queries)
- Execution plan for the per-author quote lookup: `SCAN q` (full table scan of `Quotes`, no index) — `results/execution-plan-before.txt`
- Problems: (1) N+1 query pattern — one DB round trip per author instead of one for all quotes; (2) no index on `Quotes.AuthorId`, so every one of those 300 queries scans all 2,850 rows.

### Changes

1. **Removed the N+1.** `Program.cs`'s handler for
   `/api/day11/authors-with-quotes-slow` no longer loops over authors
   issuing one `db.Quotes.Where(q => q.AuthorId == author.Id)` query per
   author. Instead it issues exactly one query for all authors and one
   query for all quotes (`await db.Quotes.AsNoTracking().ToListAsync()`,
   no `WHERE`), then groups the quotes by `AuthorId` into a
   `Dictionary<int, List<string>>` in memory and matches them to each
   author from that dictionary. This is the "projection that fetches
   authors and their quotes efficiently, avoiding one query per author"
   approach — `Include`/`AsSplitQuery` was not used because the existing
   model (`Quote.AuthorId` as a plain `int`, no navigation property, kept
   deliberately that way since Task 1) has no EF-configured relationship
   for `Include` to walk; batching the two queries directly and joining
   in memory works with that model as-is.
2. **Added an index on `Quotes.AuthorId`.** `Data/ProfilingDbContext.cs`
   now configures it via Fluent API in `OnModelCreating`:

   ```csharp
   modelBuilder.Entity<Quote>()
       .HasIndex(q => q.AuthorId)
       .HasDatabaseName("IX_Quotes_AuthorId");
   ```

   This project has no EF Core migrations (schema is created via
   `db.Database.EnsureCreated()` in `Program.cs`), and `EnsureCreated()`
   only applies model configuration to a database file that doesn't exist
   yet — it does not alter an existing one. Since `day11.db` already
   existed (seeded by Task 1) and reseeding would mean losing the exact
   dataset Task 1 measured against, the index was applied directly to
   the existing file with the SQLite CLI, the "appropriate SQLite schema
   mechanism" for this standalone, migration-less project:

   ```sql
   CREATE INDEX IX_Quotes_AuthorId ON Quotes(AuthorId);
   ```

   Verified present via `sqlite_master`:

   ```
   sqlite3 day11.db "SELECT name, sql FROM sqlite_master WHERE type='index' AND tbl_name='Quotes';"
   IX_Quotes_AuthorId|CREATE INDEX IX_Quotes_AuthorId ON Quotes(AuthorId)
   ```

3. **Why these fix the two problems:** the N+1 fix eliminates 299 of the
   300 extra database round trips per request (301 → 2 total commands,
   regardless of author count going forward). The index fix means that
   *if* a per-author `WHERE AuthorId = ?` query is ever issued again
   (e.g. by a future single-author endpoint), SQLite can seek directly to
   the matching rows instead of scanning all of `Quotes`. Together they
   address both problems identified in Task 1: too many round trips, and
   each round trip being needlessly expensive.

### After

- p50 = **21 ms** (run 2, reported)
- p99 = **42 ms** (run 2, reported)
- Throughput ≈ **444.01 req/s** (run 2)
- Failed requests: **0** (both runs)
- SQL commands per request: **2** (1 authors query + 1 all-quotes query) — down from 301
- Execution plan for a `WHERE AuthorId = ?` lookup: `SEARCH q USING INDEX IX_Quotes_AuthorId (AuthorId=?)` — `results/execution-plan-after.txt`

The load test was run twice, exactly as in Task 1
(`ab -n 200 -c 10 http://localhost:5211/api/day11/authors-with-quotes-slow`):

| Metric | Run 1 (`load-test-after-run1.txt`) | Run 2 (`load-test-after-run2.txt`, reported) |
|---|---|---|
| Requests | 200 | 200 |
| Concurrency | 10 | 10 |
| Duration | 1.045 s | 0.450 s |
| Throughput | 191.42 req/s | 444.01 req/s |
| p50 | 41 ms | **21 ms** |
| p99 | 108 ms | **42 ms** |
| Failed requests | 0 | 0 |

As in Task 1, run 1 is slower than run 2 (JIT warm-up / first-hit
connection overhead right after `dotnet run`, not a benchmark artifact
introduced here); run 2 is reported as the after result, matching the
methodology used for the Task 1 baseline. Run 1 is kept as raw evidence,
unedited.

### Before vs After

| Metric | Before | After | Improvement |
|---|---|---|---|
| p50 | 217 ms | 21 ms | ~10.3× faster |
| p99 | 384 ms | 42 ms | ~9.14× faster (≈89.1% reduction) |
| Throughput | 44.25 req/s | 444.01 req/s | ~10.0× higher |
| SQL commands/request | 301 | 2 | 299 fewer round trips (150.5× fewer commands) |

p99 improvement factor = 384 / 42 ≈ **9.14×**.
Percentage improvement = (384 − 42) / 384 × 100 ≈ **89.06%**.

### Plan comparison

- **Before** (`results/execution-plan-before.txt`, same finding as Task 1's
  `execution-plan.txt`): `SCAN q` — SQLite reads all 2,850 rows of
  `Quotes` and filters `AuthorId` in memory, because no index exists.
  This ran once per author, 300 times per request.
- **After** (`results/execution-plan-after.txt`): `SEARCH q USING INDEX
  IX_Quotes_AuthorId (AuthorId=?)` — SQLite seeks directly to the
  matching rows via the new B-tree index; the full scan is gone. In the
  optimized endpoint this per-author `WHERE` query is not issued at all
  any more (Optimization 1 replaced it with a single unfiltered
  `SELECT ... FROM Quotes`), so the two fixes are complementary: the
  index fix shows what any future per-author lookup would cost now, and
  the N+1 fix means the endpoint doesn't pay that cost 300 times over in
  the first place.

### Result

**p99 dropped from 384 ms to 42 ms, a 9.14× improvement (≈89.1% reduction)
— close to, but just under, the 10× target (384 / 10 = 38.4 ms).**

What remains, based on the evidence collected:

- The dominant cost in Task 1 (299 extra SQL round trips, each a full
  table scan) is gone — SQL commands per request dropped from 301 to 2,
  and the per-author-lookup plan is now an indexed `SEARCH`, not a
  `SCAN`.
- The ~42 ms p99 that remains is spent on things the N+1/index fixes
  don't touch: serializing the full response (300 authors × their
  quotes, ~85 KB of JSON per response, unchanged before/after), the
  in-memory `GroupBy`/dictionary construction over 2,850 quote rows on
  every request, and fixed per-request overhead (Kestrel request
  pipeline, SQLite connection/command setup for the 2 remaining queries).
  None of these scale with author count the way the N+1 pattern did, but
  they are not zero-cost either.
- Run-to-run variance (108 ms vs 42 ms p99 between the two after-runs)
  suggests some of the remaining tail latency is JIT/warm-up and GC
  noise rather than a stable floor — a longer-running or pre-warmed
  process would likely show a tighter, possibly lower, p99, but that
  was not measured here since the load test parameters were kept
  identical to Task 1's, as instructed.
- No further code changes were made to chase the remaining ~3.6 ms
  needed to cross the 38.4 ms line, since doing so without new profiling
  evidence would risk exactly the kind of unmeasured, unverified change
  this exercise is about avoiding.
