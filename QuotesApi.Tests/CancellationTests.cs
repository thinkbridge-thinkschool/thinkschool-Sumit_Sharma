using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace QuotesApi.Tests;

[TestClass]
public class CancellationTests
{
    [TestMethod]
    public async Task RequestCanBeCancelled()
    {
        using var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateClient();

        using var cancellationSource =
            new CancellationTokenSource();

        cancellationSource.Cancel();

        try
        {
            await client.GetAsync(
                "/api/quotes?page=1&size=10",
                cancellationSource.Token);

            Assert.Fail("Expected the request to be cancelled.");
        }
        catch (OperationCanceledException)
        {
            // Expected: cancellation was respected.
        }
    }
}
