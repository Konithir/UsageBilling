using UsageBilling.Domain.Models;

namespace UsageBilling.Domain;

public interface IBillingRepository
{
    Task<BillingAccount?> FindAccountAsync(string externalId, CancellationToken cancellationToken);
    Task<IReadOnlyList<BillingAccount>> ListAccountsAsync(CancellationToken cancellationToken);
    Task<MeterDefinition?> FindMeterAsync(string key, CancellationToken cancellationToken);
    Task<IReadOnlyList<MeterDefinition>> ListMetersAsync(CancellationToken cancellationToken);
    Task<bool> UsageEventExistsAsync(string eventId, CancellationToken cancellationToken);
    Task<UsageRecord?> FindUsageEventAsync(string eventId, CancellationToken cancellationToken);
    Task<IReadOnlyList<UsageRecord>> ListUsageEventsAsync(int take, CancellationToken cancellationToken);
    Task<UsageAggregate?> FindAggregateAsync(Guid accountId, string meter, DateOnly periodStart, CancellationToken cancellationToken);
    Task<IReadOnlyList<UsageAggregate>> ListAggregatesAsync(Guid? accountId, DateOnly periodStart, DateOnly periodEnd, CancellationToken cancellationToken);
    Task<IReadOnlyList<Invoice>> ListInvoicesAsync(int take, CancellationToken cancellationToken);
    Task<Invoice?> FindInvoiceAsync(Guid id, CancellationToken cancellationToken);
    Task<Invoice?> FindInvoiceAsync(Guid accountId, DateOnly periodStart, DateOnly periodEnd, CancellationToken cancellationToken);
    Task<IReadOnlyList<OutboxMessage>> ListOutboxAsync(int take, CancellationToken cancellationToken);
    Task AddUsageEventAsync(UsageRecord record, OutboxMessage outbox, CancellationToken cancellationToken);
    Task AddAggregateAsync(UsageAggregate aggregate, CancellationToken cancellationToken);
    Task AddInvoiceAsync(Invoice invoice, CancellationToken cancellationToken);
    Task SaveChangesAsync(CancellationToken cancellationToken);
}

public interface IUsageMessagePublisher
{
    string Mode { get; }
    string Endpoint { get; }
    Task PublishAsync(OutboxMessage message, CancellationToken cancellationToken);
}

public interface IPaymentGateway
{
    string Mode { get; }
    string Endpoint { get; }
    Task<string?> CreateInvoiceAsync(BillingAccount account, Invoice invoice, CancellationToken cancellationToken);
}

public sealed record UsageIngestionResult(bool Accepted, bool Duplicate, UsageRecord? Record, string Message);
