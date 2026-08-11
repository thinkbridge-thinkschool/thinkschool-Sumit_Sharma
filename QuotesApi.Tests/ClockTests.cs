using Microsoft.VisualStudio.TestTools.UnitTesting;
using QuotesApi.Models;
using QuotesApi.Time;

namespace QuotesApi.Tests;

[TestClass]
public class ClockTests
{
    [TestMethod]
    public void AddItem_UsesTimeFromInjectedClock()
    {
        var expectedTime = new DateTimeOffset(
            2026,
            8,
            10,
            16,
            0,
            0,
            TimeSpan.Zero);

        var clock = new FakeClock(expectedTime);

        var collection = new Collection(
            "My Quotes",
            1);

        collection.AddItem(
            1,
            clock);

        Assert.AreEqual(
            expectedTime,
            collection.Items.Single().AddedAt);
    }

    private sealed class FakeClock : IClock
    {
        public FakeClock(DateTimeOffset utcNow)
        {
            UtcNow = utcNow;
        }

        public DateTimeOffset UtcNow { get; }
    }
}
