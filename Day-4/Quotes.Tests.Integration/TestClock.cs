using QuotesApi.Time;

namespace Quotes.Tests.Integration;

public sealed class TestClock : IClock
{
    public TestClock(DateTimeOffset utcNow)
    {
        UtcNow = utcNow;
    }

    public DateTimeOffset UtcNow { get; set; }
}
