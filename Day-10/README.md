# Day 10 — Task 1: EF Core Change Tracker + AsNoTracking

This task demonstrates how EF Core's Change Tracker and identity resolution
work for tracked queries, contrasts that with `AsNoTracking()`, and measures
the real time/allocation cost of tracking ~10,000 rows versus reading the
same rows untracked.

Everything here is new, self-contained C#/.NET code. No Python was used at
any point (no scripting, no timing, no result collection). Day-1 through
Day-9 were not touched.

## What was tested

A console project, [`ChangeTrackerBenchmark/`](ChangeTrackerBenchmark/),
built directly against EF Core (the same EF Core version and SQLite
provider family used by the Day-5 `QuotesApi` project). It:

1. Seeds a **Day-10-only** SQLite database and table
   (`Day10BenchmarkQuotes`, `BenchmarkQuote` entity) with exactly 10,000
   deterministic rows, clearly labelled as benchmark data
   (`Author = "Day-10 Benchmark Author {n % 50}"`,
   `Text = "Day-10 benchmark quote body #{n} - seeded only for the EF Core
   change tracker exercise, not real quote data."`).
2. Demonstrates identity resolution and Change Tracker behaviour for a
   tracked query vs. an `AsNoTracking()` query.
3. Benchmarks a tracked `ToList()` read of all 10,000 rows against an
   `AsNoTracking().ToList()` read of the same rows, over 1 warm-up +
   5 measured iterations each, using `Stopwatch` and
   `GC.GetAllocatedBytesForCurrentThread()`.

### Why a new isolated model instead of reusing Day-5's `Quote`

The existing `QuotesApi` (`Day-5/QuotesApi/Models/Quote.cs`,
`Data/AppDbContext.cs`) has nowhere near 10,000 quote rows, and the task
instructions require keeping all new work inside `Day-10/` without
modifying or depending on the Day-5 app. `BenchmarkQuote` mirrors the shape
of the production `Quote` entity (`Id`, `Author`, `Text`) but lives in its
own project, its own `BenchmarkDbContext`, and its own SQLite file
(`day10-benchmark.db`, gitignored, generated on first run) — nothing from
Day-1–Day-9 was read, imported, or altered to build this.

## EF Core Change Tracker — what it is

Every `DbContext` owns a **Change Tracker**: an in-memory map from
`(entity type, primary key)` to the exact tracked instance, plus each
instance's original vs. current property values and its `EntityState`
(`Unchanged`, `Modified`, `Added`, `Deleted`, `Detached`). By default,
`ToList()`/`ToListAsync()` on a query:

- Materializes a row into a CLR object.
- **Checks the Change Tracker first** for an entry with the same primary
  key. If one already exists, EF Core returns *that same instance*
  instead of creating a new one — this is **identity resolution**.
- Otherwise creates the object and adds it to the tracker as `Unchanged`.

This is what makes `SaveChanges()` possible without manually diffing state:
EF Core already knows, from the tracker, what changed. The cost is that
every tracked row needs a snapshot of its original values and a tracker
entry, which is extra allocation and bookkeeping work done on every
tracked query — whether or not you ever call `SaveChanges()`.

`AsNoTracking()` skips all of this: rows are still materialized into
objects, but they are never registered with the Change Tracker, no
identity-resolution lookup happens, and no original-values snapshot is
kept.

## Identity resolution demonstration

Two independent queries against the **same tracked `DbContext`**, both
filtering for the same primary key (`Id == 1`) — a shape where the same
underlying database row genuinely gets queried twice:

```csharp
var firstList = await identityContext.BenchmarkQuotes
    .Where(q => q.Id == 1)
    .ToListAsync();
var trackedEntity1 = firstList.Single();

var secondList = await identityContext.BenchmarkQuotes
    .Where(q => q.Id == 1)
    .ToListAsync();
var trackedEntity2 = secondList.Single();

ReferenceEquals(trackedEntity1, trackedEntity2); // == true
```

The same shape repeated on a context using `AsNoTracking()`:

```csharp
var firstList = await noTrackingContext.BenchmarkQuotes
    .AsNoTracking()
    .Where(q => q.Id == 1)
    .ToListAsync();
var noTrackingEntity1 = firstList.Single();

var secondList = await noTrackingContext.BenchmarkQuotes
    .AsNoTracking()
    .Where(q => q.Id == 1)
    .ToListAsync();
var noTrackingEntity2 = secondList.Single();

ReferenceEquals(noTrackingEntity1, noTrackingEntity2); // == false
```

### Actual identity-resolution results (from `results/benchmark-output.txt`)

| Variant | Query #1 tracker entries | Query #2 tracker entries | `ReferenceEquals(entity1, entity2)` |
|---|---|---|---|
| Tracked | 1 | **1** (not 2) | **True** |
| `AsNoTracking()` | 0 | 0 | **False** |

Both queries in the tracked case logically returned "a row with `Id == 1`,"
but the second call never allocated a new `BenchmarkQuote` object — EF Core
found the existing tracker entry for `Id == 1` and handed back the same
instance. That's why the tracker's entry count stayed at 1 instead of
growing to 2. In the `AsNoTracking()` case the tracker never had an entry
to find, so each call materialized its own independent object.

## Tracked query (benchmark)

```csharp
using var context = new BenchmarkDbContext(BuildOptions());

var rows = context.BenchmarkQuotes
    .Where(q => q.Id <= RowCount) // RowCount = 10_000
    .ToList();
```

## AsNoTracking query (benchmark)

```csharp
using var context = new BenchmarkDbContext(BuildOptions());

var rows = context.BenchmarkQuotes
    .AsNoTracking()
    .Where(q => q.Id <= RowCount) // RowCount = 10_000
    .ToList();
```

Both variants run the identical predicate against the identical
10,000-row table and both returned exactly 10,000 rows in every iteration
(`Row counts equal across variants: True` in the summary output).

## Benchmark methodology

- **Provider:** SQLite (`Microsoft.EntityFrameworkCore.Sqlite`), local file
  `day10-benchmark.db`, generated by the program on first run.
- **Fresh `DbContext` per iteration.** Each measured call creates a brand
  new `BenchmarkDbContext` and disposes it at the end of the iteration.
  This is deliberate: reusing one tracked context across iterations would
  let identity resolution short-circuit every iteration after the first
  (query #2 would just hand back the objects already in the tracker from
  query #1, understating the real cost of tracking), and it mirrors how
  contexts are actually used in an app (short-lived, one per
  request/operation).
- **Synchronous `ToList()`, not `ToListAsync()`, inside the measured
  region.** `GC.GetAllocatedBytesForCurrentThread()` is a *per-thread*
  counter. An `await` can resume on a different thread-pool thread, which
  would silently corrupt an allocation measurement taken as
  "before-thread-A, after-thread-B." Using the synchronous EF Core API for
  the timed section keeps the whole query on one thread so the allocation
  delta is trustworthy. (The identity-resolution section above isn't
  timed, so it uses the normal async API.)
- **Warm-up:** 1 untimed iteration per variant first, to absorb JIT
  compilation, SQLite connection setup, and EF Core's internal model
  building, none of which should count toward steady-state query cost.
  This warm-up iteration is excluded from all averages below.
- **Measured:** 5 timed iterations per variant, each wrapping
  `Stopwatch.StartNew()` / `GC.GetAllocatedBytesForCurrentThread()`
  immediately around the `ToList()` call, matching the pattern specified
  for this task.
- **Equivalence check:** every iteration, both variants, asserts
  `rows.Count == 10_000`.

## Actual results

Captured verbatim from a real run — [`results/benchmark-output.txt`](results/benchmark-output.txt)
is the full console output of that run, unedited.

### Timing (5 measured iterations, 1 warm-up excluded)

| Iteration | Tracked (ms) | AsNoTracking (ms) |
|---|---|---|
| Warm-up (excluded) | 116.084 | 34.651 |
| 1 | 150.334 | 39.187 |
| 2 | 173.313 | 32.309 |
| 3 | 152.017 | 25.583 |
| 4 | 90.820 | 34.559 |
| 5 | 61.634 | 31.864 |
| **Average (measured only)** | **125.624 ms** | **32.700 ms** |

### Allocations (5 measured iterations, 1 warm-up excluded)

| Iteration | Tracked (bytes) | AsNoTracking (bytes) |
|---|---|---|
| Warm-up (excluded) | 11,827,400 | 5,954,192 |
| 1 | 11,703,928 | 5,825,696 |
| 2 | 11,703,928 | 5,820,848 |
| 3 | 11,707,872 | 5,820,848 |
| 4 | 11,654,808 | 5,820,848 |
| 5 | 11,383,928 | 5,820,848 |
| **Average (measured only)** | **11,630,893 bytes (~11.1 MB)** | **5,821,818 bytes (~5.6 MB)** |

### ChangeTracker counts (per iteration, fresh context each time)

| Variant | `ChangeTracker.Entries<BenchmarkQuote>().Count()` after query |
|---|---|
| Tracked | 10000, 10000, 10000, 10000, 10000 |
| AsNoTracking | 0, 0, 0, 0, 0 |

### Identity resolution results

| Variant | `ReferenceEquals(entity1, entity2)` |
|---|---|
| Tracked | **True** |
| AsNoTracking | **False** |

### Summary line from the run

> Tracked query was ~3.84x slower and allocated ~2.00x more per iteration
> than AsNoTracking.

(Run-to-run wall-clock ratios vary with machine load — see
[Notes on variance](#notes-on-variance) below — but the allocation ratio
(~2x) is stable across runs, since it depends on EF Core's internal
bookkeeping, not scheduling.)

## Why AsNoTracking is faster / lighter

For every one of the 10,000 rows, a **tracked** query additionally has to:

1. Look the primary key up in the Change Tracker's identity map.
2. Allocate an `InternalEntityEntry` (plus its state/relationship
   bookkeeping) for the new row.
3. Snapshot the row's original property values, so `DetectChanges()` /
   `SaveChanges()` can later diff current vs. original state.
4. Wire the new entry into the tracker's internal indexes.

None of that is optional overhead you can configure away in a tracked
query — it's the mechanism that makes `SaveChanges()` work at all.
`AsNoTracking()` materializes the same 10,000 `BenchmarkQuote` objects but
stops there: no identity-map lookup, no entry object, no original-values
snapshot, no indexing. That is directly reflected in the numbers above —
roughly half the allocated bytes per iteration for `AsNoTracking()`, and a
meaningfully lower average time, because there is simply less work being
done per row.

## When AsNoTracking should NOT be used

**Do not use `AsNoTracking()` when the entities returned need to be
modified and saved back through the same `DbContext`.** Because
`AsNoTracking()` entities are never registered with the Change Tracker,
EF Core has no original-values snapshot to diff against and no entry to
mark `Modified` — mutating a property on a no-tracking entity and calling
`SaveChanges()` does **nothing**, silently. (The demonstration above shows
this directly: `ChangeTracker.Entries<BenchmarkQuote>().Count()` stayed at
`0` after every `AsNoTracking()` query, so there is nothing for
`SaveChanges()` to act on.) If you need to update or delete an entity, you
must either query it with tracking (the default) or explicitly re-attach
it (`Attach()` / `Update()`) and set its state before saving.
`AsNoTracking()` is the right choice only for read-only paths — reporting,
list/detail views, exports — where the results are never fed back into
`SaveChanges()` on that context.

## Files

```
Day-10/
├── README.md
├── ChangeTrackerBenchmark/
│   ├── ChangeTrackerBenchmark.csproj
│   ├── Program.cs
│   ├── .gitignore              (excludes the generated day10-benchmark.db)
│   ├── Data/
│   │   └── BenchmarkDbContext.cs
│   └── Models/
│       └── BenchmarkQuote.cs
└── results/
    └── benchmark-output.txt    (full, unedited console output of the run quoted above)
```

## How to run it

```bash
cd Day-10/ChangeTrackerBenchmark
dotnet run -c Release
```

This is idempotent: if `day10-benchmark.db` already has 10,000 rows, the
program skips reseeding and goes straight to the identity-resolution
demonstration and benchmark. Deleting `day10-benchmark.db` forces a fresh
seed on the next run.

## Notes on variance

The SQLite database file lives on disk and the measured region includes
query translation, connection open, and row materialization — actual
wall-clock numbers will vary somewhat between machines and between runs
depending on OS disk cache state and background load. The allocation
numbers are far more stable since they count only .NET heap bytes
attributed to the current thread. Re-running the benchmark consistently
shows tracked allocations at roughly double `AsNoTracking()` allocations,
and tracked timing consistently at or above `AsNoTracking()` timing.

## Verification performed

- `dotnet build` — succeeded, 0 warnings, 0 errors.
- `dotnet run -c Release` — succeeded; seeded and confirmed exactly 10,000
  rows in `Day10BenchmarkQuotes` (verified independently with
  `sqlite3 day10-benchmark.db "SELECT COUNT(*) FROM Day10BenchmarkQuotes;"` → `10000`).
- Re-ran a second time to confirm seeding is skipped once 10,000 rows
  exist (idempotency).
- Confirmed both query variants returned exactly 10,000 rows in every
  iteration.
- Confirmed Change Tracker entry counts: 10,000 after each tracked
  iteration, 0 after each `AsNoTracking()` iteration.
- Confirmed identity resolution: `True` for tracked, `False` for
  `AsNoTracking()`.
- No Python used anywhere in this task — all seeding, querying,
  timing (`Stopwatch`), and allocation measurement
  (`GC.GetAllocatedBytesForCurrentThread()`) are plain C#/.NET.
- `git status --short` confirms only `Day-10/` is new; no file under
  `Day-1/`–`Day-9/` was modified by this task.

---

## Task 2 — Query Translation + Projections

This task demonstrates exactly what SQL EF Core generates for a
whole-entity query versus a `.Select()` projection, and separately
demonstrates the actual (not assumed) behavior of this EF Core version
when a LINQ predicate calls a plain C# method that cannot be translated
to SQL.

New, self-contained C#/.NET code only. No Python was used. Day-1 through
Day-9 were not touched, and Task 1's files/behavior were not modified.

### What was built

A second console project,
[`QueryTranslationDemo/`](QueryTranslationDemo/), that **reuses Task 1's
model and database** instead of creating new ones:

- References `ChangeTrackerBenchmark.csproj` as a `ProjectReference` and
  reuses its `BenchmarkDbContext` / `BenchmarkQuote` entity as-is (no
  changes to either file).
- Opens the same SQLite file, `ChangeTrackerBenchmark/day10-benchmark.db`.
  If Task 1 has already been run, this reuses its 10,000-row seed
  unchanged (confirmed in the actual run below: `10000 rows already
  present`). If the table is empty, it seeds 200 rows on its own so the
  exercise is self-contained — it never touches Task 1's row count or
  file once rows already exist.
- Enables EF Core SQL logging scoped to the `Database.Command` category
  via `LogTo(..., new[] { DbLoggerCategory.Database.Command.Name },
  LogLevel.Information)`, so the actual `Executed DbCommand` SQL is
  printed for every query. `EnableSensitiveDataLogging()` was **not**
  enabled — not needed here, and this project logs parameter placeholders
  only, never parameter values.
- Captures SQL with `.ToQueryString()` **and** separately executes each
  query with `.ToListAsync()`, so both the generated SQL and proof the
  query actually runs are captured independently.
- Writes the full run to
  [`results/query-translation-output.txt`](results/query-translation-output.txt)
  (unedited console output), alongside — not replacing —
  Task 1's `results/benchmark-output.txt`.

### 1–2. Whole-entity query and its generated SQL

```csharp
var wholeEntityQuery = context.BenchmarkQuotes
    .Where(q => q.Id <= 100);
```

Actual SQL captured from `wholeEntityQuery.ToQueryString()` (confirmed
identical to the `Executed DbCommand` log at run time):

```sql
SELECT "d"."Id", "d"."Author", "d"."Text"
FROM "Day10BenchmarkQuotes" AS "d"
WHERE "d"."Id" <= 100
```

`BenchmarkQuote` has exactly three mapped columns (`Id`, `Author`,
`Text`), and EF Core's SELECT list here is exactly those three columns —
every mapped property, because a plain `Where()` with no `Select()`
returns whole `BenchmarkQuote` entities and EF Core must fetch every
column needed to materialize one.

Executed: **100 rows returned** (matches `Id <= 100` against the seeded
table).

### 3–4. Projection query and its generated SQL

The task instructions' example projects `Id`, `Author`, and `Text` (all
three mapped columns), which would not change the SELECT list at all. To
actually demonstrate a reduced column set, this exercise projects only
the two columns a caller who just needs "quote id + author" would
plausibly need:

```csharp
var projectionQuery = context.BenchmarkQuotes
    .Where(q => q.Id <= 100)
    .Select(q => new { q.Id, q.Author });
```

Actual SQL captured from `projectionQuery.ToQueryString()` (confirmed
identical to the `Executed DbCommand` log at run time):

```sql
SELECT "d"."Id", "d"."Author"
FROM "Day10BenchmarkQuotes" AS "d"
WHERE "d"."Id" <= 100
```

Executed: **100 rows returned** — the same 100 rows (same `Id <= 100`
filter, same underlying table), only reshaped to an anonymous type with
two properties instead of a full `BenchmarkQuote`.

### 5. Before/after column comparison

| | SELECT columns | Row count (Id ≤ 100) |
|---|---|---|
| Whole entity | `Id`, `Author`, `Text` | 100 |
| Projection | `Id`, `Author` | 100 |
| **Column dropped** | **`Text`** | — |

Both queries filter on the identical predicate (`q.Id <= 100`) against
the identical table and return the identical 100 logical rows — only the
shape of each returned row differs.

### 6. Why the projection can reduce data transferred/materialized

Loading the whole entity forces EF Core to select every column needed to
build a valid `BenchmarkQuote` object, including `Text` — in this
dataset, quote bodies are full sentences (over 100 characters each in
the real seed), so `Text` is by far the largest column. The projection's
`Select(q => new { q.Id, q.Author })` tells EF Core the query never
needs `Text` at all, so the compiled SQL never asks SQLite for that
column: less data crosses the SQLite driver boundary, and there is no
`Text` string to allocate or materialize for any of the 100 rows. This
is the same mechanism as Task 1's `AsNoTracking()` savings, but it acts
on the SQL SELECT list itself rather than on Change Tracker bookkeeping —
the two techniques are independent and compose (a projection is also
never tracked, since anonymous types have no entity identity).

### 7–10. Non-translatable expression, actual EF Core behavior, and the fix

A method call to a plain C# helper inside a `Where()` predicate is a
realistic mistake — it looks like ordinary C#, but EF Core has to be
able to translate every part of the expression tree to SQL:

```csharp
public static class NonTranslatableFilters
{
    public static bool IsLongQuote(string text) => text.Length > 30;
}
...
var badResults = await context.BenchmarkQuotes
    .Where(q => NonTranslatableFilters.IsLongQuote(q.Text))
    .ToListAsync();
```

**Actual observed behavior** (this is EF Core 10.0.11 on the SQLite
provider — captured verbatim, not assumed): this throws immediately, at
`ToListAsync()`, before any row is read. It does **not** silently fall
back to pulling every row and filtering in .NET.

```
System.InvalidOperationException
The LINQ expression 'DbSet<BenchmarkQuote>()
    .Where(b => NonTranslatableFilters.IsLongQuote(b.Text))' could not be translated.
Additional information: Translation of method
'QueryTranslationDemo.NonTranslatableFilters.IsLongQuote' failed. If this
method can be mapped to your custom function, see
https://go.microsoft.com/fwlink/?linkid=2132413 for more information.
Either rewrite the query in a form that can be translated, or switch to
client evaluation explicitly by inserting a call to 'AsEnumerable',
'AsAsyncEnumerable', 'ToList', or 'ToListAsync'. See
https://go.microsoft.com/fwlink/?linkid=2101038 for more information.
```

This confirms the instructions' expectation: modern EF Core does not
quietly run an untranslatable predicate client-side — it refuses to
build the query and throws, forcing the mistake to be caught immediately
rather than discovered later as a silent full-table-scan-then-filter.

**Fix** — replace the custom method with `string.Length`, which EF
Core's SQLite provider translates to the SQL `length()` function:

```csharp
var fixedQuery = context.BenchmarkQuotes
    .Where(q => q.Text.Length > 30);
```

Actual SQL captured from `fixedQuery.ToQueryString()` (confirmed
identical to the `Executed DbCommand` log at run time):

```sql
SELECT "d"."Id", "d"."Author", "d"."Text"
FROM "Day10BenchmarkQuotes" AS "d"
WHERE length("d"."Text") > 30
```

Executed successfully: **10,000 rows returned** out of the 10,000-row
Task 1 seed, because every seeded quote body in that dataset is a full
sentence well over 30 characters — a real, unmodified result from the
actual data, not a fabricated number. The filter now runs entirely
inside SQLite via `length("Text") > 30`, with zero rows ever evaluated
in .NET.

### 11. Verification performed

- `dotnet build Day-10/QueryTranslationDemo/QueryTranslationDemo.csproj` —
  succeeded, 0 warnings, 0 errors.
- `dotnet build Day-10/ChangeTrackerBenchmark/ChangeTrackerBenchmark.csproj` —
  rebuilt clean afterward, 0 warnings, 0 errors, confirming Task 1 was
  not broken by this work.
- `dotnet run` against `QueryTranslationDemo` — succeeded; console output
  and [`results/query-translation-output.txt`](results/query-translation-output.txt)
  match exactly.
- Confirmed the run reused Task 1's existing 10,000-row
  `Day10BenchmarkQuotes` table (`Reusing existing Day10BenchmarkQuotes
  table (10000 rows already present, e.g. from Task 1).`) rather than
  reseeding or destroying it.
- Confirmed whole-entity query returned exactly 100 rows for
  `Id <= 100`.
- Confirmed projection query returned exactly 100 rows for the same
  predicate, each row containing only `Id` and `Author`.
- Confirmed the non-translatable `IsLongQuote()` predicate threw
  `System.InvalidOperationException` at `ToListAsync()` with the message
  captured above, and did **not** return any rows.
- Confirmed the corrected `q.Text.Length > 30` query succeeded and
  returned 10,000 rows against the current dataset.
- `git status --short` confirms only new files under `Day-10/`
  (`QueryTranslationDemo/` and `results/query-translation-output.txt`);
  no file under `Day-1/`–`Day-9/` was modified.
- No Python used anywhere in this task.
- Day 10 Task 3 was **not** started.
- Nothing was committed or pushed.

### 12. Conclusion

Loading the whole entity causes EF Core to select every mapped column
needed to materialize that entity (`Id`, `Author`, `Text` for
`BenchmarkQuote`). A projection changes the SQL SELECT list so only the
requested columns are returned (`Id`, `Author`), dropping `Text`
entirely from the generated SQL while returning the exact same filtered
rows. Separately, this EF Core version does not silently evaluate a
non-translatable predicate on the client — it throws
`InvalidOperationException` at query-execution time — and swapping the
custom method for a translatable expression (`string.Length`) turns the
same logical filter into a plain SQL `WHERE length(...) > 30` clause
executed entirely by SQLite.

### Files (Task 2)

```
Day-10/
├── README.md                              (this file, Task 2 section added)
├── QueryTranslationDemo/
│   ├── QueryTranslationDemo.csproj         (ProjectReference to ChangeTrackerBenchmark)
│   ├── Program.cs
│   └── NonTranslatableFilters.cs
└── results/
    └── query-translation-output.txt        (full, unedited console output of the Task 2 run)
```

### How to run it

```bash
cd Day-10/QueryTranslationDemo
dotnet run
```

Reuses `../ChangeTrackerBenchmark/day10-benchmark.db` if it already
exists (e.g. after running Task 1), otherwise seeds 200 rows on its own.
