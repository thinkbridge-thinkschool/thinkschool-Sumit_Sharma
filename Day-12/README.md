# Day 12 — Task 1: Read Models + CQRS-lite

## Project

`Day-12/CqrsLiteDemo` — a small, standalone .NET 10 console app built on the
same EF Core + Sqlite stack as `Day-10/QueryTranslationDemo` and
`Day-11/QuotesApi.Profiling` (normalized `Author`/`Quote` shape, no
migrations, `EnsureCreated()`, console report capture). Nothing under
Day-1 through Day-11 was read or modified to build this.

This is **CQRS-lite**: one database, one `DbContext`, plain method calls (no
MediatR, no message bus). The only thing being separated is the *shape* of
the model each side uses — a normalized model for writes, a denormalized
projection for reads — and the code path each goes through.

## Write model

`Models/Write/Author.cs` and `Models/Write/Quote.cs` — normalized, exactly
the Day-11 shape:

```csharp
public class Author
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
}

public class Quote
{
    public int Id { get; set; }
    public int AuthorId { get; set; }   // plain FK column, no navigation property
    public string Text { get; set; } = "";
}
```

This is what `CqrsDbContext` persists and what commands validate against and
write through.

## Command path

`Command → validation → write model → SaveChanges()`

`Commands/AddQuoteCommand.cs`:

```csharp
public class AddQuoteCommand
{
    public int AuthorId { get; set; }
    public string Text { get; set; } = "";
}
```

`Commands/AddQuoteHandler.cs` (excerpt):

```csharp
public async Task<Quote> HandleAsync(AddQuoteCommand command)
{
    if (string.IsNullOrWhiteSpace(command.Text))
        throw new ArgumentException("Quote text is required.", nameof(command));

    if (command.Text.Length > 500)
        throw new ArgumentException("Quote text must be 500 characters or fewer.", nameof(command));

    var authorExists = await _db.Authors.AnyAsync(a => a.Id == command.AuthorId);
    if (!authorExists)
        throw new ArgumentException($"Author {command.AuthorId} does not exist.", nameof(command));

    var quote = new Quote { AuthorId = command.AuthorId, Text = command.Text.Trim() };
    _db.Quotes.Add(quote);
    await _db.SaveChangesAsync();

    return quote; // the write model, never the read model
}
```

The handler returns the normalized `Quote` write entity — not
`AuthorQuoteReadModel` — so the command path cannot be used as a shortcut
around the query path.

## Read model

`Models/Read/AuthorQuoteReadModel.cs` — denormalized, shaped for a "quotes by
author" screen, unrelated to how the write side stores the data:

```csharp
public class AuthorQuoteReadModel
{
    public int AuthorId { get; set; }
    public string AuthorName { get; set; } = "";
    public int QuoteId { get; set; }
    public string QuoteText { get; set; } = "";
}
```

## Query path

`Query → read model projection → response`

`Queries/GetAuthorQuotesQuery.cs`:

```csharp
public class GetAuthorQuotesQuery
{
    public int AuthorId { get; set; }
}
```

`Queries/GetAuthorQuotesHandler.cs`:

```csharp
public Task<List<AuthorQuoteReadModel>> HandleAsync(GetAuthorQuotesQuery query)
{
    return _db.Quotes
        .AsNoTracking()
        .Where(q => q.AuthorId == query.AuthorId)
        .Join(
            _db.Authors.AsNoTracking(),
            quote => quote.AuthorId,
            author => author.Id,
            (quote, author) => new AuthorQuoteReadModel
            {
                AuthorId = author.Id,
                AuthorName = author.Name,
                QuoteId = quote.Id,
                QuoteText = quote.Text
            })
        .ToListAsync();
}
```

The projection (`.Join(...).Select`-shaped constructor) is written directly
into the LINQ query, so EF Core translates the *shape*, including the column
list, into SQL — it does not load full `Author`/`Quote` entities and map them
in memory afterward. This method never calls `SaveChanges()` and never
touches the write model.

## Proof

Full console output (both paths, one run): `results/cqrs-output.txt`.

**Command path — validation, write, and persistence:**

```
Attempting an invalid command (empty Text) to demonstrate validation:
Rejected as expected: Quote text is required. (Parameter 'command')

Executing a valid AddQuoteCommand:
Command result (write model, NOT the read model): Quote.Id=3, Quote.AuthorId=1, Quote.Text="A ship in port is safe, but that's not what ships are built for."

Verifying persistence by re-querying the normalized write model (Quotes table):
Persisted row: Quote.Id=3, Quote.AuthorId=1, Quote.Text="A ship in port is safe, but that's not what ships are built for."
```

The first attempt is rejected by validation before anything touches the
database. The second succeeds, and re-querying the normalized `Quotes` table
directly afterward (not through the read model) confirms the row was really
persisted with the expected `AuthorId`/`Text`.

**Query path — read model returned:**

```
Read model rows returned: 2
  AuthorQuoteReadModel: AuthorId=1, AuthorName="Ada Lovelace", QuoteId=1, QuoteText="That brain of mine is something more than merely mortal."
  AuthorQuoteReadModel: AuthorId=1, AuthorName="Ada Lovelace", QuoteId=3, QuoteText="A ship in port is safe, but that's not what ships are built for."
```

Both rows are `AuthorQuoteReadModel` instances — the newly added quote
(`QuoteId=3`, from the command above) shows up immediately through the query
path, proving the two paths share the same underlying data while staying
structurally separate.

## SQL

EF Core SQL logging (`DbContextOptionsBuilder.LogTo(...)` with
`EnableSensitiveDataLogging()`) was enabled for the query context only. The
actual SQL executed by `GetAuthorQuotesHandler` for `AuthorId=1`
(`results/query-sql.txt`):

```
Executed DbCommand (0ms) [Parameters=[@query_AuthorId='1'], CommandType='Text', CommandTimeout='30']
SELECT "a"."Id" AS "AuthorId", "a"."Name" AS "AuthorName", "q"."Id" AS "QuoteId", "q"."Text" AS "QuoteText"
FROM "Quotes" AS "q"
INNER JOIN "Authors" AS "a" ON "q"."AuthorId" = "a"."Id"
WHERE "q"."AuthorId" = @query_AuthorId
```

The `SELECT` list is exactly the four columns `AuthorQuoteReadModel` needs
(`Id`/`Name` from `Authors`, `Id`/`Text` from `Quotes`, aliased to the read
model's property names) — not `SELECT *` on either table, and no separate
round trip to load the `Author` navigation afterward. `Quote.Text` and
`Author.Name` are the only "extra" columns beyond the join keys, and both are
actually used by the read model; nothing unused is fetched.

## Why CQRS-lite helped

Separating the two paths meant the command handler only had to reason about
one normalized shape (`Quote`/`Author`) and one invariant (does the author
exist, is the text non-empty and within length) — it never had to worry
about what a screen wants to display. The query handler, in turn, only had
to reason about the shape a caller actually needs and could project straight
into it, without first materializing full `Quote`/`Author` entities and
reshaping them in C#. Neither handler carries code that belongs to the
other's concern. No performance measurement was taken here (single-row demo
data), so no performance claim is made beyond "the generated SQL selects
only the columns the read model uses," which is shown directly above.

## Files

```
Day-12/
  README.md
  CqrsLiteDemo/
    CqrsLiteDemo.csproj
    Program.cs                        # seeds data, runs command path, runs query path
    .gitignore                        # excludes the generated day12.db
    Models/
      Write/Author.cs
      Write/Quote.cs
      Read/AuthorQuoteReadModel.cs
    Data/CqrsDbContext.cs
    Commands/
      AddQuoteCommand.cs
      AddQuoteHandler.cs
    Queries/
      GetAuthorQuotesQuery.cs
      GetAuthorQuotesHandler.cs
      GetAuthorQuotesDapperHandler.cs    # Task 2: Dapper version of the same read query
  DapperVsEfBenchmark/
    DapperVsEfBenchmark.csproj           # Task 2: references CqrsLiteDemo, no own logic duplicated
    Program.cs                           # Task 2: seeds larger dataset, times both implementations
    .gitignore                           # excludes the generated benchmark.db
  results/
    cqrs-output.txt                   # Task 1: full console output, both paths
    query-sql.txt                     # Task 1: actual SQL generated by the read-model projection
    dapper-vs-ef-output.txt           # Task 2: full benchmark console output
    ef-sql.txt                        # Task 2: actual EF Core SQL captured during the benchmark run
    dapper-sql.txt                    # Task 2: actual Dapper SQL captured during the benchmark run
```

## How to reproduce

```
cd Day-12/CqrsLiteDemo
dotnet run
```

The app deletes/recreates `day12.db` on each run (fresh, reproducible seed
data: 2 authors, 2 quotes), then runs the command path followed by the query
path, printing and saving the exact output above.

## Task 2 — When to Reach for Dapper

### Query being compared

The comparison reuses the exact `GetAuthorQuotesQuery` / `AuthorQuoteReadModel`
read path from Task 1: given an `AuthorId`, join `Quotes` to `Authors` and
project straight into the four-column `AuthorQuoteReadModel`
(`AuthorId`/`AuthorName`/`QuoteId`/`QuoteText`). It's a good read-heavy
candidate because it's a single, fixed, parameterized `SELECT` with a join
and no writes, no change tracking needed, and no domain behavior — exactly
the shape of query where an ORM's extra machinery (entity materialization,
change tracker setup, LINQ-to-SQL translation) is pure overhead rather than
something the caller benefits from.

Both implementations run against the **same** Sqlite database file and the
**same** seeded dataset: 30 authors x 150 quotes each (4,500 quotes total),
querying `AuthorId = 1` (150 rows). This is a separate, larger dataset than
Task 1's 2-quote demo seed (kept untouched — Task 1's own db/output files
were not modified to build this), sized so the benchmark measures something
beyond query-plan noise.

### EF Core implementation

Unchanged from Task 1 — `Queries/GetAuthorQuotesHandler.cs`:

```csharp
public Task<List<AuthorQuoteReadModel>> HandleAsync(GetAuthorQuotesQuery query)
{
    return _db.Quotes
        .AsNoTracking()
        .Where(q => q.AuthorId == query.AuthorId)
        .Join(
            _db.Authors.AsNoTracking(),
            quote => quote.AuthorId,
            author => author.Id,
            (quote, author) => new AuthorQuoteReadModel
            {
                AuthorId = author.Id,
                AuthorName = author.Name,
                QuoteId = quote.Id,
                QuoteText = quote.Text
            })
        .ToListAsync();
}
```

Actual SQL generated and executed by EF Core for `AuthorId=1`
(`results/ef-sql.txt`):

```
Executed DbCommand (0ms) [Parameters=[@query_AuthorId='1'], CommandType='Text', CommandTimeout='30']
SELECT "a"."Id" AS "AuthorId", "a"."Name" AS "AuthorName", "q"."Id" AS "QuoteId", "q"."Text" AS "QuoteText"
FROM "Quotes" AS "q"
INNER JOIN "Authors" AS "a" ON "q"."AuthorId" = "a"."Id"
WHERE "q"."AuthorId" = @query_AuthorId
```

### Dapper implementation

New — `Queries/GetAuthorQuotesDapperHandler.cs`, added to the existing
`CqrsLiteDemo` project (`Dapper` and `Microsoft.Data.Sqlite` package
references added to `CqrsLiteDemo.csproj`):

```csharp
public class GetAuthorQuotesDapperHandler
{
    public const string Sql = """
        SELECT a.Id AS AuthorId, a.Name AS AuthorName, q.Id AS QuoteId, q.Text AS QuoteText
        FROM Quotes AS q
        INNER JOIN Authors AS a ON q.AuthorId = a.Id
        WHERE q.AuthorId = @AuthorId
        """;

    private readonly string _connectionString;

    public GetAuthorQuotesDapperHandler(string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task<List<AuthorQuoteReadModel>> HandleAsync(GetAuthorQuotesQuery query)
    {
        await using var connection = new SqliteConnection(_connectionString);
        var rows = await connection.QueryAsync<AuthorQuoteReadModel>(Sql, new { AuthorId = query.AuthorId });
        return rows.AsList();
    }
}
```

The SQL text is a compile-time constant, parameterized through Dapper's
anonymous-object binding (`@AuthorId` -> `new { AuthorId = query.AuthorId }`)
— no string concatenation of caller-supplied values. It targets the same
`Quotes`/`Authors` tables with the same join and the same four-column
projection as the EF Core version, so the two are logically equivalent
queries.

Actual SQL executed by Dapper for `AuthorId=1` (`results/dapper-sql.txt`,
the literal string passed to `connection.QueryAsync`, with the bound
parameter value noted):

```
Executed Dapper query [Parameters=[@AuthorId=1]]
SELECT a.Id AS AuthorId, a.Name AS AuthorName, q.Id AS QuoteId, q.Text AS QuoteText
FROM Quotes AS q
INNER JOIN Authors AS a ON q.AuthorId = a.Id
WHERE q.AuthorId = @AuthorId
```

### Benchmark method

New standalone console project, `Day-12/DapperVsEfBenchmark`, referencing
`CqrsLiteDemo` for the shared query/read-model types so both implementations
under test are the real Task 1/Task 2 code, not copies. It seeds its own
`benchmark.db` (30 authors x 150 quotes) so Task 1's demo dataset and output
files are untouched, then:

- **Warm-up:** 20 untimed iterations of each implementation first (fresh
  `DbContext` per EF Core call, fresh `SqliteConnection` per Dapper call,
  matching how each handler is actually used), to let JIT compilation, query
  plan caching, and file-system/page caching settle before anything is
  timed.
- **Measured iterations:** 300 timed iterations of each implementation,
  back-to-back (all 300 EF Core calls, then all 300 Dapper calls), each
  iteration opening its own `DbContext`/connection and running the full
  query end to end.
- **Timing method:** `System.Diagnostics.Stopwatch`, started immediately
  before the measured loop and stopped immediately after; per-implementation
  average = total elapsed / 300.
- **Allocations:** `GC.GetAllocatedBytesForCurrentThread()` sampled before
  and after each measured loop (with a forced `GC.Collect()` beforehand), as
  an approximate managed-allocation figure — not a precise profiler
  measurement, but useful as a directional signal.
- Both implementations query the same `benchmark.db` file, the same
  `AuthorId=1` target, and the same row count (150) per call, so the
  comparison is apples-to-apples.
- Before timing anything, the run also does a row-for-row parity check
  (sorted by `QuoteId`) confirming EF Core and Dapper return identical
  `AuthorQuoteReadModel` data from the same dataset; the benchmark aborts if
  they don't match.

Full raw console output: `results/dapper-vs-ef-output.txt`.

### Results

Measured on this machine, one run, 300 iterations each (see
`results/dapper-vs-ef-output.txt` for the untouched raw output):

| Metric | EF Core | Dapper |
|---|---:|---:|
| Average time | 1.9209 ms/iteration | 0.6863 ms/iteration |
| Total elapsed (300 iterations) | 576.256 ms | 205.899 ms |
| Rows returned | 150 | 150 |
| Allocations (approx, GC-sampled) | 132,138 bytes/iteration | 36,668 bytes/iteration |

### Comparison

In this run, Dapper's average per-call time was about **2.80x faster** than
EF Core's for the same join-and-project read query (0.69 ms vs 1.92 ms per
call), and allocated roughly a third as many managed bytes per call. Both
implementations returned identical result sets (150 rows, verified
row-for-row), so the gap is attributable to EF Core's per-call overhead —
`DbContext` construction, change-tracker setup (even with `AsNoTracking()`
still applied per query), and LINQ-to-SQL translation/materialization — not
to any difference in the SQL itself, since both handlers execute the same
join with the same `WHERE` clause against the same data. This is a single
machine, single run, one specific query shape (a two-table join returning
150 rows); it shows a real, measured difference for *this* query on *this*
setup, not a general claim about EF Core versus Dapper performance across
all queries or workloads.

### When would I use Dapper?

Use EF Core by default: change tracking, navigation properties, LINQ
composition, and migrations are worth the overhead for most of an
application's data access, and they're exactly what `AddQuoteHandler` (the
write side of this same project) relies on. Reach for Dapper on specific,
already-identified read-heavy hot paths — like `GetAuthorQuotesQuery` here —
where the query is a fixed, well-understood projection, you want direct
control over the exact SQL and parameters, and profiling (as done above) has
actually shown EF Core's per-call overhead is measurable rather than
negligible. Introducing Dapper everywhere "for performance" without
measuring first would trade away EF Core's ergonomics for a speedup that,
for most queries, is too small to matter.

### How to reproduce (Task 2)

```
cd Day-12/DapperVsEfBenchmark
dotnet run
```

Deletes/recreates its own `benchmark.db` on each run (30 authors, 4,500
quotes), runs the parity check, then the warm-up and measured benchmark
loops, printing and saving the exact output above. Does not touch
`CqrsLiteDemo/day12.db` or Task 1's result files.
