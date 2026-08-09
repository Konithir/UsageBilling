namespace UsageBilling.Infrastructure.Configuration;

public sealed class BillingStorageOptions
{
    public const string SectionName = "Storage";
    public string Provider { get; set; } = "InMemory";
    public string ConnectionString { get; set; } = string.Empty;
}

public sealed class MessagingOptions
{
    public const string SectionName = "Messaging";
    public string Provider { get; set; } = "Demo";
    public string BootstrapServers { get; set; } = "localhost:9092";
    public string Topic { get; set; } = "usage-events";
    public string? SaslUsername { get; set; }
    public string? SaslPassword { get; set; }
}

public sealed class StripeOptions
{
    public const string SectionName = "Stripe";
    public string Mode { get; set; } = "Demo";
    public string SecretKey { get; set; } = string.Empty;
}

public sealed class RedisOptions
{
    public const string SectionName = "Redis";
    public bool Enabled { get; set; }
    public string ConnectionString { get; set; } = "localhost:6379";
}