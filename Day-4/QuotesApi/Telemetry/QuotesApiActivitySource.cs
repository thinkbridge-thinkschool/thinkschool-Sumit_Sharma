using System.Diagnostics;

namespace QuotesApi.Telemetry;

public static class QuotesApiActivitySource
{
    public const string Name = "QuotesApi";

    public static readonly ActivitySource Instance = new(Name);
}
