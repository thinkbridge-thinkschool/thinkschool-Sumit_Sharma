# Day 8 — Task 1: Azure SQL Indexing Benchmark

Real benchmark run against a live Azure SQL Database (`day7quotesdb` on
`thinkschool-day7-sqlsrv.database.windows.net`, the same server/db created
for Day-7), not simulated. Day-1 through Day-7 were not modified.

## Environment / method

- **Database**: `day7quotesdb`, Azure SQL Database (serverless, free-tier),
  server `thinkschool-day7-sqlsrv.database.windows.net`.
- **Client**: no `sqlcmd`/`mssql-cli`/SSMS on this Linux dev box. Used
  `pymssql` in a throwaway Python venv (outside the repo, under `/tmp`) to
  run the `.sql` files in `azure-sql/` and capture results.
- **Credential handling**: the admin password was written only to an
  untracked file under `/tmp/.../scratchpad/`, never printed, never
  committed, never placed under this repository. Nothing in this directory
  or its `results/*.json` files contains the password or connection string.
- **STATISTICS IO/TIME caveat**: pymssql (via FreeTDS) does not surface
  SQL Server's textual "info message" stream, which is how `SET STATISTICS
  IO/TIME ON` normally reports. Instead, `SET STATISTICS XML ON` was used
  for every query — this returns the **actual** execution plan (not
  estimated) as an XML result set, and SQL Server embeds the exact same
  counters STATISTICS IO would print (`ActualLogicalReads`,
  `ActualPhysicalReads`, `ActualElapsedms`, `ActualCPUms`) directly on each
  plan operator. All read/IO numbers below are pulled from that XML, so
  they are the real per-operator runtime counters, not estimates.
- Raw captured data (full plan XML + parsed summaries) is under
  `azure-sql/results/`: `before.json`/`after.json` (full plan XML per
  query), `before-summary.json`/`after-summary.json` (parsed operator/IO
  summary), `write-cost-results.json` (write-side trials).

## Dataset

`dbo.QuoteEvents`, seeded via `azure-sql/seed.sql`:

| check | value |
|---|---|
| total rows | 100,000 |
| distinct `AuthorId` | 50 (2,000 rows each) |
| distinct `EventType` | 4 (25,000 rows each) |
| rows with `AuthorId = 23` | 2,000 |
| rows in `CreatedAt` range `2025-06-01`–`2025-07-01` | 2,880 (~2.9%) |
| `CreatedAt` span | 2023-01-01 → 2025-11-07 (~2.85 years) |

All verified against the live table after seeding, matching the design in
`azure-sql/schema.sql`/`seed.sql`.

## Baseline (heap, no indexes) — `azure-sql/schema.sql` + `seed.sql`

| Query | Plan | Actual logical reads | Actual elapsed (ms) | Rows returned |
|---|---|---|---|---|
| Q1: `WHERE Id = 55555` | Table Scan (Heap) | 3,571 | 21 | 1 |
| Q2: `WHERE AuthorId = 23` | Table Scan (Heap) | 3,571 | 21 | 2,000 |
| Q3: `WHERE CreatedAt BETWEEN ... ORDER BY CreatedAt` | Table Scan (Heap) + explicit **Sort** | 3,571 | 24 | 2,880 |

Every query does a full scan of the heap (3,571 logical reads = every page
of the 100k-row table), regardless of how selective the predicate is,
because there is no index to seek on. Q3 additionally pays for an explicit
Sort operator since nothing returns rows in `CreatedAt` order.

## After indexing — `azure-sql/indexes.sql`

Added: `PK_QuoteEvents` (clustered, on `Id`), `IX_QuoteEvents_AuthorId`
(nonclustered, on `AuthorId`), `IX_QuoteEvents_CreatedAt` (nonclustered,
covering, on `CreatedAt` INCLUDE `AuthorId, EventType`). All three
confirmed present via `sys.indexes` after running the script.

| Query | Plan | Actual logical reads | Actual elapsed (ms) | Rows returned |
|---|---|---|---|---|
| Q1: `WHERE Id = 55555` | **Clustered Index Seek** on `PK_QuoteEvents` | 3 | 0 | 1 |
| Q2: `WHERE AuthorId = 23` (4-col SELECT) | **Index Scan** on `IX_QuoteEvents_CreatedAt` | 423 | 8 | 2,000 |
| Q3: date range + `ORDER BY` | **Index Seek** on `IX_QuoteEvents_CreatedAt` (dynamic-range-seek shape, no Sort) | 15 | 1 | 2,880 |

Q1: 3,571 → 3 logical reads (a ~1,190x reduction) — the clustered index
turns the lookup into a direct B-tree seek.

Q3: 3,571 + a Sort → 15 logical reads, no Sort — `IX_QuoteEvents_CreatedAt`
is covering (INCLUDE `AuthorId, EventType`) and already key-ordered by
`CreatedAt`, so the query is answered entirely from the index with no key
lookup and no separate sort operator, exactly as designed.

### Execution-plan finding: Q2 did *not* use the index built for it

The query as written (`SELECT Id, AuthorId, EventType, CreatedAt WHERE
AuthorId = 23`) was expected to use `IX_QuoteEvents_AuthorId` (seek + key
lookup). The actual plan instead **scans** `IX_QuoteEvents_CreatedAt` —
because that index is covering for this exact column list (`AuthorId`,
`EventType` are INCLUDE columns; `Id` rides along as the clustering key),
so SQL Server can satisfy the whole query from one narrower index with no
key lookups at all (423 logical reads total). Seeking
`IX_QuoteEvents_AuthorId` would have been a cheap seek but then required
2,000 key lookups back into the clustered index — one per matching row —
which the optimizer correctly judged more expensive than a full scan of
the narrower covering index.

To confirm `IX_QuoteEvents_AuthorId` is not dead weight, the same
predicate was re-run with a column list it *does* cover
(`SELECT Id, AuthorId ... WHERE AuthorId = 23`):

| Query | Plan | Actual logical reads |
|---|---|---|
| `SELECT Id, AuthorId WHERE AuthorId = 23` | **Index Seek** on `IX_QuoteEvents_AuthorId` | 6 |

Confirms the index is real and effective for the access pattern it was
designed for (an `AuthorId`-only projection); it's simply outcompeted by
the wider covering index once `EventType`/`CreatedAt` are also selected.
This is a genuine "which index wins" trade-off, not a modeling error in
`indexes.sql`.

## Write-side cost — `azure-sql/write-cost.sql`

Same deterministic 5,000-row batch insert run 5 times each into a
heap (`WriteCost_NoIndex`) and an identically-shaped table carrying the
same 3 indexes as `QuoteEvents` (`WriteCost_Indexed`), both tables dropped
and recreated between runs. Wall-clock timed in Python around each
`INSERT`; plan operators captured via `SET STATISTICS XML ON` on the same
statement.

| Table | Write plan operators | Trial 1 (ms) | Trial 2 | Trial 3 | Trial 4 | Trial 5 | Steady-state avg (trials 3–5) |
|---|---|---|---|---|---|---|---|
| `WriteCost_NoIndex` | 1× Table Insert (Heap) | 308.1 | 260.1 | 114.3 | 125.8 | 113.0 | **117.7 ms** |
| `WriteCost_Indexed` | 1× Clustered Index Insert + Sort, 2× Nonclustered Index Insert (each with its own Sort) | 2168.5 | 207.7 | 235.8 | 229.1 | 253.0 | **239.3 ms** |

Trial 1 in both cases (and trial 2 for the heap) is inflated by
plan-compilation/cache warm-up on first execution — this is called out
rather than hidden; trials 3–5 are the stable, repeatable numbers.

Maintaining 3 index structures instead of 1 makes the same 5,000-row
insert **~2x slower** (117.7ms → 239.3ms), and the plan shows why: instead
of one `Table Insert`, the indexed table pays for a `Clustered Index
Insert` plus two more `Index Insert` operators — one per nonclustered
index — each preceded by its own `Sort` to get rows into that index's key
order before insertion. This is the concrete "indexes are a tax on
writes" cost that justifies not indexing low-selectivity columns like
`EventType`/`Status` (per the reasoning already in `indexes.sql`).

## Verification performed

- Row counts and value distributions checked against the seed script's
  design (50 authors × 2,000, 4 event types × 25,000, ~2.9% in the June
  2025 window).
- All 3 indexes confirmed present via `sys.indexes` after running
  `indexes.sql`.
- Every plan above is the **actual** (post-execution) plan, not an
  estimated one — `RetrievedFromCache`/`ActualRows`/`ActualLogicalReads`
  attributes are populated from real execution, not the optimizer's
  estimates.
- Write-cost throwaway tables (`WriteCost_NoIndex`, `WriteCost_Indexed`)
  were dropped after capture, per `write-cost.sql`'s own design — nothing
  extra was left in the database.
- Final state of `day7quotesdb`: `dbo.QuoteEvents` with 100,000 rows, one
  clustered PK and two nonclustered indexes as designed. No Day-7 objects
  (`Quotes`, `QuotesTimeline`, etc.) were touched.

## Task 2 — Covering indexes + included columns

Real before/after benchmark against the same `dbo.QuoteEvents` table and
the same live database, run after Task 1's indexes were already in
place. Full SQL in `azure-sql/task2-covering-index.sql`; full captured
evidence (raw plan XML + parsed operator summaries) in
`azure-sql/results/task2-before.json`, `task2-after.json`,
`task2-summary.json`, and the trimmed `azure-sql/task2-results.json`.

### Problem / query

Task 1's `IX_QuoteEvents_CreatedAt` (key `CreatedAt`, INCLUDE `AuthorId,
EventType`) covers most of Task 1's Q3, but not a query that also needs
`Status` — a column in neither non-clustered index. Filtering
`AuthorId = 23` alone (2% selective) turned out **not** to reproduce a
Key Lookup: at that selectivity, 2,000 individual lookups cost more than
one Clustered Index Scan, so the optimizer just scans (verified
empirically, not assumed). A **narrow, highly selective** predicate was
needed instead — a single day of `CreatedAt` (~96 of 100,000 rows,
~0.1%) — where per-row lookups are cheap enough that the optimizer
prefers seek+lookup over a scan:

```sql
SELECT Id, AuthorId, EventType, Status, CreatedAt
FROM dbo.QuoteEvents
WHERE CreatedAt >= '2025-06-01' AND CreatedAt < '2025-06-02';
```

This exact query — same predicate, same columns, same 96 rows — was run
twice: once against Task 1's index set (BEFORE), once after adding one
new covering index (AFTER). Nothing else about the data or database
changed between the two runs.

### BEFORE — index state (Task 1's indexes, untouched)

`PK_QuoteEvents` (clustered, `Id`), `IX_QuoteEvents_AuthorId`
(nonclustered, `AuthorId`), `IX_QuoteEvents_CreatedAt` (nonclustered, key
`CreatedAt`, INCLUDE `AuthorId, EventType`).

**Actual plan:**

```
Nested Loops (Inner Join)
 ├─ Index Seek on IX_QuoteEvents_CreatedAt        -> 3 logical reads, 96 rows
 └─ Clustered Index Seek on PK_QuoteEvents         -> 258 logical reads, 96 rows
    (SeekPredicates: Id = <outer row's Id> — this is the Key Lookup)
```

**BEFORE logical reads: 261** (3 + 258), 96 rows returned. Verified
directly from the `SeekPredicates` XML: the Clustered Index Seek's seek
key is `Id`, correlated to the outer Nested Loops input row — the exact
mechanism SSMS's graphical plan shows as a "Key Lookup (Clustered)" icon
(the raw Showplan XML has no separate "Key Lookup" operator name; it is
always this Nested-Loops-to-Clustered-Index-Seek shape).

### Covering index DDL

```sql
CREATE NONCLUSTERED INDEX IX_QuoteEvents_CreatedAt_Covering
    ON dbo.QuoteEvents (CreatedAt)
    INCLUDE (AuthorId, EventType, Status);
```

Created as a **new, separately-named** index rather than widening Task
1's `IX_QuoteEvents_CreatedAt` in place, so Task 1's already-committed
index description and numbers stay exactly reproducible throughout this
exercise — Task 1's three indexes were not dropped, altered, or
recreated at any point. Confirmed present with the right key/include
split via `sys.indexes`/`sys.index_columns` after creation.

### AFTER — same query, one new index added

**Actual plan:**

```
Nested Loops (Inner Join)
 └─ Index Seek on IX_QuoteEvents_CreatedAt_Covering -> 3 logical reads, 96 rows
```

(The Nested Loops/Merge Interval/Concatenation operators around it are
SQL Server's dynamic-range-seek handling for the parameterized date
literals — the same artifact seen in Task 1's Q3 — not a join to another
table.) **There is no Clustered Index Seek anywhere in this plan.**

**AFTER logical reads: 3**, 96 rows returned (same rows as BEFORE).

### Before/after comparison

| | BEFORE | AFTER |
|---|---|---|
| Plan shape | Index Seek → **Key Lookup** (Clustered Index Seek via Nested Loops) | Index Seek only, no lookup |
| Logical reads | 261 | 3 |
| Rows returned | 96 | 96 |
| Index used | `IX_QuoteEvents_CreatedAt` (non-covering) | `IX_QuoteEvents_CreatedAt_Covering` (covering) |

**261 → 3 logical reads — an 87x reduction — and the Key Lookup operator
is completely gone from the actual plan**, not merely cheaper. This is
confirmed directly from the actual (post-execution) plan XML, not an
estimated plan.

### Why INCLUDE columns eliminate the lookup

A non-clustered index's leaf level stores its key column(s) plus the
clustering key (here, `Id`) for every row. Any column requested by a
query that is *not* in that key or INCLUDE list forces SQL Server back to
the clustered index — once per matching row — to fetch it (a Key
Lookup). `Status` was outside both existing non-clustered indexes, so
every one of the 96 matching rows needed a separate clustered-index
round trip. Adding `Status` as an INCLUDE column puts a copy of that
value directly on the non-clustered index's leaf pages, so the engine
never needs to visit the clustered index at all — the index becomes
"covering" for this exact SELECT list. INCLUDE (rather than adding
`Status` to the key) was the right choice because the query doesn't
filter or sort on `Status` — it only needs the value returned, which is
exactly what INCLUDE columns are for (no B-tree ordering cost).

### What was learned

- Whether a Key Lookup appears at all depends on predicate selectivity,
  not just on whether an index is missing a column — the same "missing
  column" query returned a full scan (no lookup) at 2% selectivity and a
  genuine Key Lookup at 0.1% selectivity. This had to be verified
  empirically; it isn't something the schema alone determines.
- The raw Showplan XML never contains an operator literally named "Key
  Lookup" — it's a Nested Loops joining a non-clustered index access to a
  Clustered Index Seek keyed on the clustering column. Detecting it
  programmatically means checking for that shape, not a label.
- `ActualLogicalReads` on plan operators (from `SET STATISTICS XML ON`)
  gave exact, real per-operator read counts without needing
  `STATISTICS IO`'s text output, which this driver can't capture.

### What could break this optimization

- **Widening the SELECT list again.** Adding any column back that isn't
  in the index's key or INCLUDE list (e.g. `Payload`) immediately
  reintroduces a Key Lookup for that column.
- **A less selective predicate.** As shown above, if the date window
  were widened enough (approaching the ~2% mark from Task 1's Q2), the
  optimizer could switch back to scanning the base table instead of
  seeking this index at all — covering only helps once the optimizer
  chooses to use the index.
- **Two indexes on the same key column.** `IX_QuoteEvents_CreatedAt` and
  `IX_QuoteEvents_CreatedAt_Covering` now both key on `CreatedAt`. That
  was a deliberate choice here to keep Task 1 untouched, but in a real
  system this is redundant and doubles the write-maintenance cost (per
  Task 1's write-cost findings) for every insert/update touching
  `CreatedAt` — a production fix would consolidate to one covering index
  rather than keep both.
- **Column data-type/width growth**, e.g. if `Status` grew from
  `NVARCHAR(20)` to something much wider, would grow every leaf page of
  the covering index, increasing its own logical reads and eventually
  eroding the benefit versus a lookup-based plan.

### Key Lookup outcome

**The Key Lookup disappeared from the actual execution plan.** Confirmed
directly: the AFTER plan (`azure-sql/results/task2-after.json`) contains
no `Clustered Index Seek` operator and no Nested-Loops-to-base-table
pattern anywhere — the entire query is answered by one `Index Seek` on
`IX_QuoteEvents_CreatedAt_Covering`.
