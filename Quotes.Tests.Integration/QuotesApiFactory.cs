using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using QuotesApi.Data;
using QuotesApi.Time;

namespace Quotes.Tests.Integration;

public sealed class QuotesApiFactory : WebApplicationFactory<Program>
{
    private readonly string connectionString;

    public TestClock Clock { get; } = new(
        new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));

    public QuotesApiFactory(string containerConnectionString)
    {
        var builder = new SqlConnectionStringBuilder(containerConnectionString)
        {
            InitialCatalog = $"quotes_test_{Guid.NewGuid():N}"
        };

        connectionString = builder.ConnectionString;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<AppDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<AppDbContext>>();

            services.AddDbContext<AppDbContext>(options =>
                options.UseSqlServer(
                    connectionString,
                    sqlServer => sqlServer.MigrationsAssembly(
                        "Quotes.Tests.Integration")));

            services.RemoveAll<IClock>();

            services.AddSingleton<IClock>(Clock);
        });
    }
}
