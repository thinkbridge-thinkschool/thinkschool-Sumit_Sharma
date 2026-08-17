# Day 7 — SQL Fundamentals: Joins, CTEs, Window Functions & Set Operations

Day 7 goes a level deeper than basic CRUD SQL. It covers three core relational
concepts — multi-table reporting with CTEs and joins, analytic window
functions, and set-level operators — implemented first against a local
SQLite database and then re-verified against a real **Azure SQL Database**
to confirm the same query patterns hold on SQL Server T-SQL.

Both implementations sit side by side in this folder:

- [`Day-7/sql/`](sql/) — the original SQLite implementation (schema, seed data, and the three task queries).
- [`Day-7/azure-sql/`](azure-sql/) — a T-SQL port of the same schema, seed data, and queries, executed against Azure SQL.

The SQLite files were not modified to produce the Azure SQL port; the
`azure-sql/` scripts are separate files with the type/identity conversions
called out inline (e.g. `INTEGER PRIMARY KEY AUTOINCREMENT` → `INT
IDENTITY(1,1) PRIMARY KEY`, `TEXT` → `NVARCHAR`, `julianday()` arithmetic →
`DATEDIFF`).

## Azure SQL environment

| | |
|---|---|
| **Subscription** | Azure for Students |
| **Resource group** | `thinkschool-rg` |
| **Logical server** | `thinkschool-day7-sqlsrv` |
| **Database** | `day7quotesdb` |
| **Region** | Central India |
| **Tier** | General Purpose, Serverless (Gen5, 2 vCore), free-limit, auto-pause enabled |
| **Firewall** | A single rule (`AllowMyClientIP`) scoping access to the dev machine's public IP |

`thinkschool-rg` also holds the unrelated Day-5 Container Apps resources
(ACR, Container Apps environment, Application Insights); none of those were
touched while setting up or querying `day7quotesdb`.

The database's schema and seed data were created **specifically for this
Day 7 exercise** — they are not a copy of any production database. The
`Quotes` table structure mirrors the real `QuotesApi` schema (see
[Schema](#schema) below), but the multi-author seed rows and the
`QuotesTimeline` / `Tags` / `QuoteTags` / `AuthorCategories` tables exist
only to give the three tasks something meaningful to query.

No admin password, connection string, or access token is included anywhere
in this repository or this document.

## Task 1 — Joins and CTEs at Depth

**Requirement:** in a single SQL statement, return each author's quote
count and their most recent quote — without a correlated subquery.

**Why a CTE:** the per-author aggregation (count, latest quote id) and the
row-level lookup of that quote's text are two different levels of
granularity. A CTE (`AuthorStats`) computes the aggregation once, and the
outer query then joins back to `Quotes` to pull the text for the winning
row. This keeps the whole thing as one statement while avoiding a
correlated subquery (e.g. `(SELECT Text FROM Quotes WHERE ...)` evaluated
once per outer row).

**How `AuthorStats` works:**
- `COUNT(*)` per `Author`, grouped by `Author` → `QuoteCount`.
- `MAX(Id)` per `Author` → `LatestQuoteId`. Since the real `Quotes` table
  has no timestamp column, the autoincrement `Id` is the only recency
  signal that actually exists in the data — the highest `Id` for an author
  is their most recently inserted quote.

**Joining back to `Quotes`:** the outer query does a plain `INNER JOIN`
from `AuthorStats` to `Quotes` on `latest.Id = stats.LatestQuoteId`. This is
appropriate (rather than `LEFT JOIN`) because every row in `AuthorStats` is
guaranteed to have a matching `Quotes` row — `LatestQuoteId` was derived
from `Quotes` itself in the CTE, so the join can never fail to match.

**Soft-delete handling:** `WHERE IsDeleted = 0` in the CTE excludes
soft-deleted quotes from both the count and the "most recent" calculation,
matching `Quote.MarkDeleted()` semantics in the real `QuotesApi` domain
model.

```sql
-- Day-7/azure-sql/task1-cte-joins.sql
WITH AuthorStats AS (
    SELECT
        Author,
        COUNT(*) AS QuoteCount,
        MAX(Id)  AS LatestQuoteId
    FROM Quotes
    WHERE IsDeleted = 0
    GROUP BY Author
)
SELECT TOP 10
    stats.Author       AS Author,
    stats.QuoteCount    AS QuoteCount,
    latest.Text         AS MostRecentQuote,
    latest.Id           AS MostRecentQuoteId
FROM AuthorStats AS stats
INNER JOIN Quotes AS latest
    ON latest.Id = stats.LatestQuoteId
ORDER BY stats.Author;
```

### Result

Verified against `day7quotesdb` (Azure SQL):

| Author | QuoteCount | MostRecentQuote | MostRecentQuoteId |
|---|---|---|---|
| Ada Lovelace | 1 | That which is not exact is not knowledge. | 9 |
| Albert Einstein | 3 | The important thing is not to stop questioning. | 4 |
| Day5 Author | 1 | Day 5 sample quote after fix | 1 |
| Marie Curie | 2 | I was taught that the way of progress was neither swift nor easy. | 11 |
| Mark Twain | 2 | Kindness is the language which the deaf can hear and the blind can see. | 8 |
| Maya Angelou | 2 | I've learned that people will forget what you said, people will forget what you did, but people will never forget how you made them feel. | 6 |
| Nelson Mandela | 3 | A good head and a good heart are always a formidable combination. | 14 |

**Observation:** the soft-deleted Mark Twain row (`Id 15`, "This quote was
retracted...") does not appear anywhere — Mark Twain's `QuoteCount` is 2
(not 3) and his `MostRecentQuoteId` is 8 (not 15), confirming
`WHERE IsDeleted = 0` correctly excludes it from both the aggregate and the
"most recent" lookup.

### What I learned

A CTE is the cleanest way to separate "aggregate per group" from "fetch a
detail row for the winner of that group" without resorting to a correlated
subquery — the aggregation runs once, and the join back to the base table
is a simple equality join on the value the aggregation already computed.

## Task 2 — Window Functions

**Requirement:** per author, show each quote's position in that author's
timeline, a running quote count, the previous quote, and the gap in days
since the previous quote.

**Why a separate timeline table:** the real `Quotes` table has no date/time
column at all (verified against the actual `QuotesApi` schema), so there is
no `CreatedAt` to partition and order by. Rather than inventing a column on
the real table, this task adds a Day‑7‑only `QuotesTimeline` table that
references each real `Quotes.Id` and attaches a synthetic `CreatedAt` date
purely for this exercise (see [Schema](#schema)).

**Functions demonstrated, all `PARTITION BY Author`:**
- `ROW_NUMBER() OVER (PARTITION BY Author ORDER BY CreatedAt, QuoteId)` — a strict, never-tying 1, 2, 3… sequence per author.
- `RANK() OVER (PARTITION BY Author ORDER BY CreatedAt)` — same idea, but rows tied on `CreatedAt` share a rank, and the next rank is skipped.
- `LAG(Text)` / `LAG(CreatedAt)` — pull the previous quote's text and date onto the current row (`NULL` for an author's first quote).
- `SUM(1) OVER (... ROWS BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW)` — a running count of quotes seen so far for that author.
- `DATEDIFF(DAY, LAG(CreatedAt) OVER (...), CreatedAt)` as `GapInDays` — the T-SQL equivalent of the SQLite `julianday()` subtraction used in the local version, giving the actual number of calendar days since the author's previous quote.

```sql
-- Day-7/azure-sql/task2-window-functions.sql
SELECT
    Author,
    Text AS Quote,
    QuoteId,
    CreatedAt,
    ROW_NUMBER() OVER (
        PARTITION BY Author
        ORDER BY CreatedAt, QuoteId
    ) AS RowNumber,
    RANK() OVER (
        PARTITION BY Author
        ORDER BY CreatedAt
    ) AS [Rank],
    SUM(1) OVER (
        PARTITION BY Author
        ORDER BY CreatedAt, QuoteId
        ROWS BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW
    ) AS RunningQuoteCount,
    LAG(Text) OVER (
        PARTITION BY Author
        ORDER BY CreatedAt, QuoteId
    ) AS PreviousQuote,
    LAG(CreatedAt) OVER (
        PARTITION BY Author
        ORDER BY CreatedAt, QuoteId
    ) AS PreviousQuoteDate,
    DATEDIFF(
        DAY,
        LAG(CreatedAt) OVER (
            PARTITION BY Author
            ORDER BY CreatedAt, QuoteId
        ),
        CreatedAt
    ) AS GapInDays
FROM QuotesTimeline
ORDER BY Author, CreatedAt, QuoteId;
```

### Result

Full result set, verified against `day7quotesdb` (Azure SQL), 14 rows
(`QuotesTimeline` excludes the soft-deleted quote, matching Task 1):

| Author | QuoteId | CreatedAt | RowNumber | Rank | RunningQuoteCount | PreviousQuoteDate | GapInDays |
|---|---|---|---|---|---|---|---|
| Ada Lovelace | 9 | 2024-01-15 | 1 | 1 | 1 | — | — |
| Albert Einstein | 2 | 2024-01-05 | 1 | 1 | 1 | — | — |
| Albert Einstein | 3 | 2024-01-05 | 2 | 1 | 2 | 2024-01-05 | 0 |
| Albert Einstein | 4 | 2024-02-20 | 3 | 3 | 3 | 2024-01-05 | 46 |
| Day5 Author | 1 | 2024-01-01 | 1 | 1 | 1 | — | — |
| Marie Curie | 10 | 2024-01-20 | 1 | 1 | 1 | — | — |
| Marie Curie | 11 | 2024-01-22 | 2 | 2 | 2 | 2024-01-20 | 2 |
| Mark Twain | 7 | 2024-01-12 | 1 | 1 | 1 | — | — |
| Mark Twain | 8 | 2024-03-01 | 2 | 2 | 2 | 2024-01-12 | 49 |
| Maya Angelou | 5 | 2024-01-10 | 1 | 1 | 1 | — | — |
| Maya Angelou | 6 | 2024-01-25 | 2 | 2 | 2 | 2024-01-10 | 15 |
| Nelson Mandela | 12 | 2024-01-01 | 1 | 1 | 1 | — | — |
| Nelson Mandela | 13 | 2024-01-10 | 2 | 2 | 2 | 2024-01-01 | 9 |
| Nelson Mandela | 14 | 2024-01-30 | 3 | 3 | 3 | 2024-01-10 | 20 |

**ROW_NUMBER vs. RANK, verified on the Einstein tie:** Albert Einstein has
two quotes on the *same* synthetic date, `2024-01-05` (`QuoteId 2`,
"Imagination is more important than knowledge." and `QuoteId 3`, "Life is
like riding a bicycle..."):

- `ROW_NUMBER` gives them **1** and **2** — sequential, tie-blind.
- `RANK` gives them **both 1** — a tied rank — and Einstein's third quote
  (`QuoteId 4`, `2024-02-20`) gets `Rank = 3`, not `2`. `RANK` skips the
  rank that the tie "used up," which is exactly the documented difference
  between the two functions.
- `GapInDays` for the tied pair is `0` (same calendar date), and jumps to
  `46` for Einstein's third quote — the actual day count between
  `2024-01-05` and `2024-02-20`.

Rows with no previous quote for that author (`RowNumber = 1`) correctly
show `NULL` for `PreviousQuote`, `PreviousQuoteDate`, and `GapInDays`,
since `LAG()` has nothing to look back to.

## Task 3 — Set Operations

Three independent business questions, each solved with the set operator
that actually matches the semantics required. Data source: the real
`Quotes` table plus the Day‑7‑only `Tags`, `QuoteTags`, and
`AuthorCategories` tables (see [Schema](#schema)).

### 1. EXCEPT — authors with quotes but no tagged quotes

**Business question:** which authors have at least one quote, but none of
their quotes have ever been tagged?

**Why EXCEPT:** this is a straight set difference — everything in "all
authors with a quote" minus "authors with at least one tagged quote."
`EXCEPT` computes exactly that, with no need to express the negation as a
`NOT IN` / `NOT EXISTS`.

```sql
SELECT DISTINCT Author
FROM Quotes
WHERE IsDeleted = 0

EXCEPT

SELECT DISTINCT q.Author
FROM Quotes q
INNER JOIN QuoteTags qt ON qt.QuoteId = q.Id
WHERE q.IsDeleted = 0

ORDER BY Author;
```

**Result** (Azure SQL):

| Author |
|---|
| Ada Lovelace |
| Day5 Author |

Both authors were deliberately left untagged in the seed data (`QuoteId 1`
and `QuoteId 9`) specifically so this query would return a genuine,
non-empty answer rather than an empty set.

### 2. INTERSECT — authors in both the classic and modern categories

**Business question:** which authors are classified as *both* "classic"
and "modern" in `AuthorCategories`?

**Why INTERSECT:** the question is literally "which authors are common to
both category lists" — the definition of a set intersection.

```sql
SELECT Author
FROM AuthorCategories
WHERE Category = 'classic'

INTERSECT

SELECT Author
FROM AuthorCategories
WHERE Category = 'modern'

ORDER BY Author;
```

**Result** (Azure SQL):

| Author |
|---|
| Ada Lovelace |
| Nelson Mandela |

Both authors were deliberately seeded into *both* category rows, so
`INTERSECT` has a genuine, non-empty answer to return.

### 3. UNION — combined distinct tags across the classic and modern categories

**Business question:** across all quotes by "classic" authors and all
quotes by "modern" authors, what is the combined, de-duplicated list of
tags in use?

**Why UNION (not UNION ALL):** the requirement is a *distinct* tag list.
Nelson Mandela appears in both the classic and modern category rows, so his
tags (`courage`, `education`) are produced by both halves of the query.
`UNION ALL` would return those tags twice; plain `UNION` discards the
duplicate.

```sql
SELECT t.Name
FROM Tags t
INNER JOIN QuoteTags qt ON qt.TagId = t.Id
INNER JOIN Quotes q ON q.Id = qt.QuoteId
INNER JOIN AuthorCategories ac ON ac.Author = q.Author AND ac.Category = 'classic'
WHERE q.IsDeleted = 0

UNION

SELECT t.Name
FROM Tags t
INNER JOIN QuoteTags qt ON qt.TagId = t.Id
INNER JOIN Quotes q ON q.Id = qt.QuoteId
INNER JOIN AuthorCategories ac ON ac.Author = q.Author AND ac.Category = 'modern'
WHERE q.IsDeleted = 0

ORDER BY Name;
```

**Result** (Azure SQL):

| Name |
|---|
| courage |
| education |
| inspiration |
| kindness |
| knowledge |
| perseverance |
| science |

Seven distinct tags, no duplicates — including `courage` and `education`,
which are contributed by Nelson Mandela's quotes under *both* branches of
the `UNION` and correctly appear only once.

## Schema

All three tasks run against `day7quotesdb`, defined by
[`Day-7/azure-sql/schema.sql`](azure-sql/schema.sql) and seeded by
[`Day-7/azure-sql/seed.sql`](azure-sql/seed.sql) (T-SQL ports of
[`Day-7/sql/schema.sql`](sql/schema.sql) and the local SQLite seed files).

**Real application schema:**
- `Quotes` — mirrors the actual `QuotesApi` `Quotes` table (`Id`, `Author`, `Text`, `IsDeleted`). Seeded with the one real row from the app plus additional multi-author quotes added specifically for this exercise, clearly labeled as such in the seed scripts.

**Day-7 exercise-only schema** (do not exist in the production `QuotesApi` database):
- `QuotesTimeline` — pairs each real `Quotes.Id` with a synthetic `CreatedAt` date, added because `Quotes` has no timestamp column. Used by Task 2.
- `Tags` / `QuoteTags` — a small tag vocabulary and a many-to-many join to `Quotes`. Used by Task 3.
- `AuthorCategories` — an arbitrary "classic"/"modern" classification per author (an author can be in one, both, or neither). Not a factual claim about any author's era — exists purely to give Task 3's `INTERSECT` query two real, overlapping sets. Used by Task 3.

## Verification

- All three Azure SQL task queries (`task1-cte-joins.sql`, `task2-window-functions.sql`, `task3-set-operations.sql`) were executed directly against the live `day7quotesdb` Azure SQL database and returned the result sets documented above.
- SQL syntax for all three queries was validated implicitly by successful execution against SQL Server (no parse or runtime errors).
- `git status` was checked before and after this documentation work; `Day-1` through `Day-6` were not touched by it.
- No credentials, connection strings, or tokens were written to this repository.
- No build or automated test suite was run as part of this documentation task — only the SQL queries above were executed.

## Day-7 Structure

```
Day-7/
├── README.md                    # this file
├── .gitignore                   # excludes the local SQLite database file
├── quotes-day7.db                # local SQLite database (git-ignored, not tracked)
├── sql/                          # SQLite implementation
│   ├── schema.sql                 # Quotes table (real app schema)
│   ├── seed.sql                   # Quotes seed data (Task 1)
│   ├── author-quote-report.sql    # Task 1: CTE + JOIN
│   ├── window-functions-seed.sql  # QuotesTimeline table + seed (Task 2)
│   ├── window-functions.sql       # Task 2: window functions
│   ├── set-operations-seed.sql    # Tags / QuoteTags / AuthorCategories + seed (Task 3)
│   └── set-operations.sql         # Task 3: EXCEPT / INTERSECT / UNION
└── azure-sql/                    # Azure SQL (T-SQL) port, same tasks
    ├── schema.sql                 # all four tables (Quotes, QuotesTimeline, Tags, QuoteTags, AuthorCategories)
    ├── seed.sql                   # combined seed data for all four tables
    ├── task1-cte-joins.sql        # Task 1, ported to T-SQL
    ├── task2-window-functions.sql # Task 2, ported to T-SQL
    └── task3-set-operations.sql   # Task 3, ported to T-SQL
```
