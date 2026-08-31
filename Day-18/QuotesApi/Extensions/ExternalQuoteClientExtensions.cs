using Microsoft.Extensions.Http.Resilience;
using Polly;
using QuotesApi.ExternalQuotes;

namespace QuotesApi.Extensions;

public static class ExternalQuoteClientExtensions
{
    /// <summary>
    /// Registers the outbound HTTP client for the external quote service with
    /// a resilience pipeline: total timeout, retry with exponential
    /// backoff+jitter, and a circuit breaker. Order is outermost-to-innermost:
    /// Timeout wraps Retry wraps CircuitBreaker, so the 10s timeout budget
    /// covers every retry attempt combined.
    /// </summary>
    public static IServiceCollection AddExternalQuoteClient(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddHttpClient<IExternalQuoteClient, ExternalQuoteClient>(client =>
        {
            client.BaseAddress = new Uri(
                configuration["ExternalQuoteApi:BaseUrl"]
                    ?? "https://dummyjson.com/quotes/");

            // The resilience pipeline's own timeout strategy owns the time
            // budget; disable HttpClient's independent timeout so it can't
            // race the pipeline.
            client.Timeout = Timeout.InfiniteTimeSpan;
        })
        .AddResilienceHandler("external-quote-pipeline", (builder, context) =>
        {
            var logger = context.ServiceProvider
                .GetRequiredService<ILoggerFactory>()
                .CreateLogger("QuotesApi.ExternalQuotes.Resilience");

            builder
                .AddTimeout(TimeSpan.FromSeconds(10))
                .AddRetry(new HttpRetryStrategyOptions
                {
                    MaxRetryAttempts = 3,
                    BackoffType = DelayBackoffType.Exponential,
                    UseJitter = true,
                    Delay = TimeSpan.FromMilliseconds(200),
                    OnRetry = args =>
                    {
                        logger.LogWarning(
                            "External quote request retry {AttemptNumber} of {MaxAttempts} after {DelayMs}ms. Reason: {Reason}",
                            args.AttemptNumber + 1,
                            3,
                            args.RetryDelay.TotalMilliseconds,
                            args.Outcome.Exception is not null
                                ? args.Outcome.Exception.Message
                                : $"HTTP {(int)args.Outcome.Result!.StatusCode}");

                        return default;
                    }
                })
                .AddCircuitBreaker(new HttpCircuitBreakerStrategyOptions
                {
                    FailureRatio = 0.5,
                    SamplingDuration = TimeSpan.FromSeconds(30),
                    MinimumThroughput = 10,
                    BreakDuration = TimeSpan.FromSeconds(15),
                    OnOpened = args =>
                    {
                        logger.LogError(
                            "External quote circuit breaker opened for {BreakDurationSeconds}s after the failure-rate threshold was exceeded.",
                            args.BreakDuration.TotalSeconds);

                        return default;
                    },
                    OnClosed = _ =>
                    {
                        logger.LogInformation(
                            "External quote circuit breaker closed; requests are flowing normally again.");

                        return default;
                    }
                });
        });

        return services;
    }
}
