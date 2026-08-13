using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using QuotesApi.Data;

namespace Quotes.Tests.Integration.Migrations;

/// <summary>
/// Used only by the `dotnet ef migrations` CLI to scaffold SQL-Server-native
/// migrations for the integration tests. Not used at test run time -
/// QuotesApiFactory configures the real Testcontainers connection string.
/// </summary>
public sealed class SqlServerDesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();

        optionsBuilder.UseSqlServer(
            "Server=localhost;Database=DesignTimeOnly;TrustServerCertificate=True;",
            options => options.MigrationsAssembly("Quotes.Tests.Integration"));

        return new AppDbContext(optionsBuilder.Options);
    }
}
