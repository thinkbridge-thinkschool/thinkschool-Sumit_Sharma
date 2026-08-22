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
  results/
    cqrs-output.txt                   # full console output, both paths
    query-sql.txt                     # actual SQL generated by the read-model projection
```

## How to reproduce

```
cd Day-12/CqrsLiteDemo
dotnet run
```

The app deletes/recreates `day12.db` on each run (fresh, reproducible seed
data: 2 authors, 2 quotes), then runs the command path followed by the query
path, printing and saving the exact output above.
