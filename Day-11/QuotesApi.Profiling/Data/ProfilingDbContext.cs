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
}
