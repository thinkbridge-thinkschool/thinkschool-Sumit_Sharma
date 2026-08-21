using Microsoft.EntityFrameworkCore;
using QuotesApi.Profiling.Models;

namespace QuotesApi.Profiling.Data;

public class ProfilingDbContext : DbContext
{
    public ProfilingDbContext(DbContextOptions<ProfilingDbContext> options)
        : base(options)
    {
    }

    public DbSet<Author> Authors => Set<Author>();

    public DbSet<Quote> Quotes => Set<Quote>();

    // Day-11 Task 2, optimization 2: Quotes.AuthorId had no index (see
    // Quote.cs / Task 1 findings), so every per-author lookup was a full
    // table SCAN. This is the only schema change from Task 1 — AuthorId
    // stays a plain int column with no navigation property, it just gets
    // an index so SQLite can SEARCH it instead of scanning.
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Quote>()
            .HasIndex(q => q.AuthorId)
            .HasDatabaseName("IX_Quotes_AuthorId");
    }
}
