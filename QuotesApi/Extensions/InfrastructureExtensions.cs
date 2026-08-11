using Microsoft.EntityFrameworkCore;
using QuotesApi.Data;
using QuotesApi.Repositories;
using QuotesApi.Time;

namespace QuotesApi.Extensions;

public static class InfrastructureExtensions
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddDbContext<AppDbContext>(options =>
            options.UseSqlite(
                configuration.GetConnectionString(
                    "DefaultConnection")));

        services.AddScoped<IQuoteRepository, QuoteRepository>();

        services.AddScoped<ICollectionRepository, CollectionRepository>();

        services.AddSingleton<IClock, SystemClock>();

        return services;
    }
}
