using Microsoft.EntityFrameworkCore;
using CqrsLiteDemo.Models.Write;

namespace CqrsLiteDemo.Data;

public class CqrsDbContext : DbContext
{
    public CqrsDbContext(DbContextOptions<CqrsDbContext> options)
        : base(options)
    {
    }

    public DbSet<Author> Authors => Set<Author>();

    public DbSet<Quote> Quotes => Set<Quote>();
}
