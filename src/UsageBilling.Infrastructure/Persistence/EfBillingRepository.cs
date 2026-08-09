using Microsoft.EntityFrameworkCore;
using UsageBilling.Domain;
using UsageBilling.Domain.Models;

namespace UsageBilling.Infrastructure.Persistence;

public sealed class EfBillingRepository(BillingDbContext dbContext) : IBillingRepository
{
    public Task<BillingAccount?> FindAccountAsync(string externalId, CancellationToken cancellationToken) =>
        dbContext.Accounts.SingleOrDefaultAsync(x => x.ExternalId == externalId, cancellationToken);

    public async Task<IReadOnlyList<BillingAccount>> ListAccountsAsync(CancellationToken cancellationToken) =>
        await dbContext.Accounts.OrderBy(x => x.Name).ToListAsync(cancellationToken);

    public Task<MeterDefinition?> FindMeterAsync(string key, CancellationToken cancellationToken) =>
        dbContext.Meters.SingleOrDefaultAsync(x => x.Key == key, cancellationToken);

    public async Task<IReadOnlyList<MeterDefinition>> ListMetersAsync(CancellationToken cancellationToken) =>
        await dbContext.Meters.OrderBy(x => x.Name).ToListAsync(cancellationToken);

    public Task<bool> UsageEventExistsAsync(string eventId, CancellationToken cancellationToken) =>
        dbContext.UsageEvents.AnyAsync(x => x.EventId == eventId, cancellationToken);

    public Task<UsageRecord?> FindUsageEventAsync(string eventId, CancellationToken cancellationToken) =>
        dbContext.UsageEvents.SingleOrDefaultAsync(x => x.EventId == eventId, cancellationToken);

    public async Task<IReadOnlyList<UsageRecord>> ListUsageEventsAsync(int take, CancellationToken cancellationToken) =>
        await dbContext.UsageEvents.OrderByDescending(x => x.ReceivedAt).Take(take).ToListAsync(cancellationToken);

    public Task<UsageAggregate?> FindAggregateAsync(Guid accountId, string meter, DateOnly periodStart, CancellationToken cancellationToken) =>
        dbContext.UsageAggregates.SingleOrDefaultAsync(x => x.AccountId == accountId && x.Meter == meter && x.PeriodStart == periodStart, cancellationToken);

    public async Task<IReadOnlyList<UsageAggregate>> ListAggregatesAsync(Guid? accountId, DateOnly periodStart, DateOnly periodEnd, CancellationToken cancellationToken) =>
        await dbContext.UsageAggregates
            .Where(x => (!accountId.HasValue || x.AccountId == accountId.Value) && x.PeriodStart >= periodStart && x.PeriodEnd <= periodEnd)
            .OrderBy(x => x.AccountExternalId).ThenBy(x => x.Meter)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Invoice>> ListInvoicesAsync(int take, CancellationToken cancellationToken) =>
        await dbContext.Invoices.Include(x => x.Lines).OrderByDescending(x => x.CreatedAt).Take(take).ToListAsync(cancellationToken);

    public Task<Invoice?> FindInvoiceAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.Invoices.Include(x => x.Lines).SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

    public Task<Invoice?> FindInvoiceAsync(Guid accountId, DateOnly periodStart, DateOnly periodEnd, CancellationToken cancellationToken) =>
        dbContext.Invoices.Include(x => x.Lines).SingleOrDefaultAsync(x => x.AccountId == accountId && x.PeriodStart == periodStart && x.PeriodEnd == periodEnd, cancellationToken);

    public async Task<IReadOnlyList<OutboxMessage>> ListOutboxAsync(int take, CancellationToken cancellationToken) =>
        await dbContext.Outbox.OrderByDescending(x => x.CreatedAt).Take(take).ToListAsync(cancellationToken);

    public async Task AddUsageEventAsync(UsageRecord record, OutboxMessage outbox, CancellationToken cancellationToken)
    {
        await dbContext.UsageEvents.AddAsync(record, cancellationToken);
        await dbContext.Outbox.AddAsync(outbox, cancellationToken);
    }

    public Task AddAggregateAsync(UsageAggregate aggregate, CancellationToken cancellationToken) => dbContext.UsageAggregates.AddAsync(aggregate, cancellationToken).AsTask();
    public Task AddInvoiceAsync(Invoice invoice, CancellationToken cancellationToken) => dbContext.Invoices.AddAsync(invoice, cancellationToken).AsTask();
    public Task SaveChangesAsync(CancellationToken cancellationToken) => dbContext.SaveChangesAsync(cancellationToken);
}