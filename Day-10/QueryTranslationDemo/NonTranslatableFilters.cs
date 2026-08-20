namespace QueryTranslationDemo;

// A plain C# method with no known SQL translation. Referencing it inside an
// EF Core Where() predicate is the accidental "non-translatable expression"
// mistake this exercise demonstrates and then fixes.
public static class NonTranslatableFilters
{
    public static bool IsLongQuote(string text) => text.Length > 30;
}
