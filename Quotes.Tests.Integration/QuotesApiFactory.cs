using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using QuotesApi.Data;
using QuotesApi.Time;

namespace Quotes.Tests.Integration;

public sealed class QuotesApiFactory : WebApplicationFactory<Program>
{
    private readonly SqliteConnection connection = new("DataSource=:memory:");

    public TestClock Clock { get; } = new(
        new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        connection.Open();

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<AppDbContext>>();

            services.AddDbContext<AppDbContext>(options =>
                options.UseSqlite(connection));

            services.RemoveAll<IClock>();

            services.AddSingleton<IClock>(Clock);
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        connection.Dispose();
    }
}
