using Testcontainers.MsSql;

namespace Quotes.Tests.Integration;

public sealed class MsSqlContainerFixture : IAsyncLifetime
{
    private readonly MsSqlContainer container = new MsSqlBuilder(
        "mcr.microsoft.com/mssql/server:2022-latest")
        .Build();

    public string ConnectionString => container.GetConnectionString();

    public Task InitializeAsync() => container.StartAsync();

    public Task DisposeAsync() => container.DisposeAsync().AsTask();
}
