using Microsoft.EntityFrameworkCore;
using QuotesApi.Data;
using QuotesApi.Models;

namespace QuotesApi.Repositories;

public class CollectionRepository : ICollectionRepository
{
    private readonly AppDbContext db;
    private readonly ILogger logger;

    public CollectionRepository(
        AppDbContext db,
        ILogger<CollectionRepository> logger)
    {
        this.db = db;
        this.logger = logger;
    }

    public async Task<Collection?> GetByIdAsync(
        int id,
        CancellationToken cancellationToken)
    {
        return await db.Collections
            .Include(collection => collection.Items)
            .FirstOrDefaultAsync(
                collection => collection.Id == id,
                cancellationToken);
    }

    public async Task<Collection> AddAsync(
        Collection collection,
        CancellationToken cancellationToken)
    {
        db.Collections.Add(collection);

        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Created collection {CollectionId}",
            collection.Id);

        return collection;
    }

    public async Task UpdateAsync(
        Collection collection,
        CancellationToken cancellationToken)
    {
        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Updated collection {CollectionId}",
            collection.Id);
    }

    public async Task<bool> DeleteAsync(
        int id,
        CancellationToken cancellationToken)
    {
        var collection = await db.Collections
            .FirstOrDefaultAsync(
                collection => collection.Id == id,
                cancellationToken);

        if (collection is null)
            return false;

        db.Collections.Remove(collection);

        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Deleted collection {CollectionId}",
            id);

        return true;
    }
}
