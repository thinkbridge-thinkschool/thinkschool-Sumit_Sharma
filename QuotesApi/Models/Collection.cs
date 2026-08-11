using QuotesApi.Time;

namespace QuotesApi.Models;

public class Collection
{
    private readonly List<CollectionItem> items = new();

    public int Id { get; private set; }

    public string Name { get; private set; } = "";

    public int OwnerId { get; private set; }

    public IReadOnlyCollection<CollectionItem> Items =>
        items.AsReadOnly();

    private Collection()
    {
    }

    public Collection(
        string name,
        int ownerId)
    {
        ValidateName(name);

        if (ownerId <= 0)
            throw new ArgumentException(
                "OwnerId must be greater than zero.");

        Name = name;
        OwnerId = ownerId;
    }

    public void AddItem(
        int quoteId,
        IClock clock)
    {
        if (items.Count >= 50)
            throw new InvalidOperationException(
                "A collection cannot contain more than 50 items.");

        if (items.Any(item => item.QuoteId == quoteId))
            throw new InvalidOperationException(
                "This quote is already in the collection.");

        items.Add(
            new CollectionItem(
                quoteId,
                clock.UtcNow));
    }

    public void RemoveItem(int quoteId)
    {
        var item = items.FirstOrDefault(
            item => item.QuoteId == quoteId);

        if (item is null)
            throw new InvalidOperationException(
                "This quote is not in the collection.");

        items.Remove(item);
    }

    public void Rename(string name)
    {
        ValidateName(name);
        Name = name;
    }

    private static void ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) ||
            name.Length < 3 ||
            name.Length > 80)
        {
            throw new ArgumentException(
                "Collection name must be between 3 and 80 characters.");
        }
    }
}
