namespace QuotesApi.Profiling.Models;

// AuthorId is a plain int column, deliberately not declared as a
// navigation/foreign key in ProfilingDbContext. EF Core only creates an
// index automatically for configured foreign keys, so this column has no
// index — matching the "missing index" side of the Day-11 Task 1 exercise.
public class Quote
{
    public int Id { get; set; }

    public int AuthorId { get; set; }

    public string Text { get; set; } = "";
}
