namespace ChangeTrackerBenchmark.Models;

// Day-10 benchmark-only entity. Shaped after QuotesApi's production Quote
// model but intentionally isolated: it lives in its own table
// (Day10BenchmarkQuotes) inside a Day-10-only SQLite database and is never
// shared with the Day-1..Day-9 schemas or data.
public class BenchmarkQuote
{
    public int Id { get; set; }

    public string Author { get; set; } = "";

    public string Text { get; set; } = "";
}
