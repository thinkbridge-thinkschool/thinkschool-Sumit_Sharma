using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using QuotesApi.Auth;
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

        services.AddScoped<IAuthService, AuthService>();

        services.AddScoped<ICollectionRepository, CollectionRepository>();

        services.AddScoped<IAuthorizationHandler, CollectionOwnerAuthorizationHandler>();

        services.AddSingleton<IClock, SystemClock>();

        return services;
    }
}
