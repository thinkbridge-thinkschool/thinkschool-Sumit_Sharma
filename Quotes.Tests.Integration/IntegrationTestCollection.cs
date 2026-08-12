namespace Quotes.Tests.Integration;

[CollectionDefinition(Name)]
public sealed class IntegrationTestCollection : ICollectionFixture<MsSqlContainerFixture>
{
    public const string Name = "Integration tests";
}
