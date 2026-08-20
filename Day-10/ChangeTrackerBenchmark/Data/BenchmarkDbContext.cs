using ChangeTrackerBenchmark.Models;
using Microsoft.EntityFrameworkCore;

namespace ChangeTrackerBenchmark.Data;

public class BenchmarkDbContext : DbContext
{
    public BenchmarkDbContext(DbContextOptions<BenchmarkDbContext> options)
        : base(options)
    {
    }

    public DbSet<BenchmarkQuote> BenchmarkQuotes => Set<BenchmarkQuote>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<BenchmarkQuote>(entity =>
        {
            entity.ToTable("Day10BenchmarkQuotes");
            entity.HasKey(q => q.Id);
            entity.Property(q => q.Author).IsRequired().HasMaxLength(200);
            entity.Property(q => q.Text).IsRequired().HasMaxLength(1000);
        });
    }
}
