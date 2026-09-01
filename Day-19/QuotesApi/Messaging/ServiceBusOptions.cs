namespace QuotesApi.Messaging;

public sealed class ServiceBusOptions
{
    /// <summary>
    /// Used for local development against the Service Bus emulator
    /// (docker-compose). Ignored when <see cref="FullyQualifiedNamespace"/>
    /// is set.
    /// </summary>
    public string ConnectionString { get; set; } = "";

    /// <summary>
    /// A real Azure Service Bus namespace host, e.g.
    /// "sb-thinkschool-day19.servicebus.windows.net". When set, the app
    /// authenticates with <c>DefaultAzureCredential</c> (a Container App's
    /// system-assigned Managed Identity in Azure) instead of a connection
    /// string — no shared access key is ever configured.
    /// </summary>
    public string FullyQualifiedNamespace { get; set; } = "";

    public string TopicName { get; set; } = "quotes.events";

    public string AuditSubscriptionName { get; set; } = "audit-log";

    public string DigestSubscriptionName { get; set; } = "digest-notifications";

    /// <summary>
    /// How many independent <see cref="ServiceBusProcessor"/> instances
    /// <c>DigestConsumerPool</c> starts against the same subscription — the
    /// competing-consumer fan-out.
    /// </summary>
    public int DigestWorkerCount { get; set; } = 3;
}
