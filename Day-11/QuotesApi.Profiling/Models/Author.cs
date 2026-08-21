namespace QuotesApi.Profiling.Models;

// Day-11 profiling exercise: a normalized Author table so that
// GET /api/day11/authors-with-quotes-slow can demonstrate a real
// authors -> quotes N+1 query pattern. The Week-1 QuotesApi (Day-4/Day-5)
// stores Author as a plain string on Quote and is left untouched.
public class Author
{
    public int Id { get; set; }

    public string Name { get; set; } = "";
}
