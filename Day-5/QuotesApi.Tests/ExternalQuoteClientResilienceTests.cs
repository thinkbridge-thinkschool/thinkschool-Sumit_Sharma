using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using QuotesApi.Extensions;
using QuotesApi.ExternalQuotes;

namespace QuotesApi.Tests;

[TestClass]
public class ExternalQuoteClientResilienceTests
{
    [TestMethod]
    public async Task GetRandomQuoteAsync_WhenTransientFailuresPrecedeSuccess_RetriesAndEventuallySucceeds()
    {
        var handler = new SequencedHttpMessageHandler(
            () => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable),
            () => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable),
            () => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(
                    new ExternalQuote("Resilient Author", "Retries work."))
            });

        var logSink = new CapturingLoggerProvider();

        using var provider = BuildProvider(handler, logSink);
        var client = provider.GetRequiredService<IExternalQuoteClient>();

        var quote = await client.GetRandomQuoteAsync(CancellationToken.None);

        Assert.AreEqual("Resilient Author", quote.Author);
        Assert.AreEqual("Retries work.", quote.Text);

        // 1 initial attempt + 2 retries before the 3rd call succeeds.
        Assert.AreEqual(3, handler.CallCount);

        var retryLogCount = logSink.Entries.Count(
            e => e.Message.Contains(
                "External quote request retry",
                StringComparison.OrdinalIgnoreCase));

        Assert.AreEqual(
            2,
            retryLogCount,
            "Expected one retry log entry per retried attempt.");
    }

    [TestMethod]
    public async Task GetRandomQuoteAsync_WhenFailuresPersistBeyondMaxRetries_PropagatesFailureAfterExhaustingRetries()
    {
        var handler = new SequencedHttpMessageHandler(
            Enumerable.Repeat(
                (Func<HttpResponseMessage>)(() =>
                    new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)),
                10).ToArray());

        var logSink = new CapturingLoggerProvider();

        using var provider = BuildProvider(handler, logSink);
        var client = provider.GetRequiredService<IExternalQuoteClient>();

        await Assert.ThrowsExactlyAsync<HttpRequestException>(
            () => client.GetRandomQuoteAsync(CancellationToken.None));

        // 1 initial attempt + 3 retries = 4 total attempts, then the
        // exhausted failure is propagated instead of being swallowed.
        Assert.AreEqual(4, handler.CallCount);

        var retryLogCount = logSink.Entries.Count(
            e => e.Message.Contains(
                "External quote request retry",
                StringComparison.OrdinalIgnoreCase));

        Assert.AreEqual(3, retryLogCount);

        var errorLogged = logSink.Entries.Any(
            e => e.Level == LogLevel.Error
                && e.Message.Contains(
                    "after all retries were exhausted",
                    StringComparison.OrdinalIgnoreCase));

        Assert.IsTrue(
            errorLogged,
            "Expected the exhausted-retry failure to be logged, not silently swallowed.");
    }

    private static ServiceProvider BuildProvider(
        HttpMessageHandler handler,
        CapturingLoggerProvider logSink)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ExternalQuoteApi:BaseUrl"] = "https://external-quotes.test/"
            })
            .Build();

        var services = new ServiceCollection();

        services.AddLogging(builder => builder.AddProvider(logSink));
        services.AddExternalQuoteClient(configuration);

        services.AddHttpClient<IExternalQuoteClient, ExternalQuoteClient>()
            .ConfigurePrimaryHttpMessageHandler(() => handler);

        return services.BuildServiceProvider();
    }

    private sealed class SequencedHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpResponseMessage>[] _responses;

        public SequencedHttpMessageHandler(
            params Func<HttpResponseMessage>[] responses)
        {
            _responses = responses;
        }

        public int CallCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var index = Math.Min(CallCount, _responses.Length - 1);
            CallCount++;

            return Task.FromResult(_responses[index]());
        }
    }

    private sealed record LogEntry(LogLevel Level, string Message);

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        private readonly List<LogEntry> _entries = new();

        public IReadOnlyList<LogEntry> Entries => _entries;

        public ILogger CreateLogger(string categoryName) =>
            new CapturingLogger(this);

        public void Dispose()
        {
        }

        private sealed class CapturingLogger(CapturingLoggerProvider owner) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                lock (owner._entries)
                {
                    owner._entries.Add(
                        new LogEntry(logLevel, formatter(state, exception)));
                }
            }
        }
    }
}
