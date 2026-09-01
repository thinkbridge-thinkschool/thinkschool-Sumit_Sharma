using Azure.Identity;
using Azure.Messaging.ServiceBus;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using QuotesApi.Auth;
using QuotesApi.BackgroundJobs;
using QuotesApi.Data;
using QuotesApi.Messaging;
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

        services.Configure<JwtOptions>(
            configuration.GetSection("Jwt"));

        services.AddScoped<IQuoteRepository, QuoteRepository>();

        services.AddScoped<IAuthService, AuthService>();

        services.AddScoped<ICollectionRepository, CollectionRepository>();

        services.AddScoped<IAuthorizationHandler, CollectionOwnerAuthorizationHandler>();

        services.AddSingleton<IClock, SystemClock>();

        return services;
    }

    public static IServiceCollection AddBackgroundJobs(
        this IServiceCollection services)
    {
        services.AddSingleton<IBackgroundTaskQueue, BackgroundTaskQueue>();
        services.AddSingleton<IJobStatusStore, JobStatusStore>();

        services.AddHostedService<QueuedHostedService>();
        services.AddHostedService<PeriodicStatsHostedService>();

        services.AddScoped<HangfireRecurringJobs>();

        return services;
    }

    public static IServiceCollection AddServiceBusMessaging(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<ServiceBusOptions>(
            configuration.GetSection("ServiceBus"));

        services.AddSingleton(sp =>
        {
            var options = sp.GetRequiredService<IOptions<ServiceBusOptions>>().Value;

            // A real namespace + Managed Identity in Azure; the emulator's
            // dev connection string everywhere else (local docker-compose).
            return string.IsNullOrEmpty(options.FullyQualifiedNamespace)
                ? new ServiceBusClient(options.ConnectionString)
                : new ServiceBusClient(options.FullyQualifiedNamespace, new DefaultAzureCredential());
        });

        services.AddSingleton<IQuoteEventPublisher, QuoteEventPublisher>();

        services.AddHostedService<AuditLogSubscriptionWorker>();
        services.AddHostedService<DigestConsumerPool>();
        services.AddHostedService<DeadLetterMonitorWorker>();

        return services;
    }
}
