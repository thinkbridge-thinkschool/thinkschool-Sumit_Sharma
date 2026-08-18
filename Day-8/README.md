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
