using System.Text.Json;
using UsageBilling.Domain.Models;

namespace UsageBilling.Domain.Services;

public sealed class UsageApplicationService(IBillingRepository repository, IUsageMessagePublisher publisher, TimeProvider timeProvider)
{
    public async Task<UsageIngestionResult> IngestAsync(string eventId, string accountExternalId, string meterKey, decimal quantity, DateTimeOffset occurredAt, IReadOnlyDictionary<string, string>? dimensions, CancellationToken cancellationToken)
    {
        if (await repository.UsageEventExistsAsync(eventId, cancellationToken))
            return new UsageIngestionResult(true, true, null, "Event already exists; no usage was added twice.");

        var account = await repository.FindAccountAsync(accountExternalId, cancellationToken);
        if (account is null) return new UsageIngestionResult(false, false, null, "Account was not found.");
        var meter = await repository.FindMeterAsync(meterKey, cancellationToken);
        if (meter is null) return new UsageIngestionResult(false, false, null, "Meter was not found.");

        var record = UsageRecord.Create(eventId, account.Id, account.ExternalId, meter.Key, quantity, occurredAt, JsonSerializer.Serialize(dimensions ?? new Dictionary<string, string>()), timeProvider.GetUtcNow());
        var outbox = OutboxMessage.Create("usage.recorded.v1", record.EventId, JsonSerializer.Serialize(new { record.EventId, AccountId = account.ExternalId, record.Meter, record.Quantity, record.OccurredAt }));
        await repository.AddUsageEventAsync(record, outbox, cancellationToken);

        var start = new DateOnly(occurredAt.Year, occurredAt.Month, 1);
        var end = start.AddMonths(1).AddDays(-1);
        var aggregate = await repository.FindAggregateAsync(account.Id, meter.Key, start, cancellationToken);
        if (aggregate is null) { aggregate = UsageAggregate.Create(account.Id, account.ExternalId, meter.Key, start, end); await repository.AddAggregateAsync(aggregate, cancellationToken); }
        aggregate.Apply(quantity, meter.Aggregation, timeProvider.GetUtcNow());
        record.MarkProcessed(timeProvider.GetUtcNow());

        await repository.SaveChangesAsync(cancellationToken);
        try { await publisher.PublishAsync(outbox, cancellationToken); outbox.MarkPublished(timeProvider.GetUtcNow()); }
        catch (Exception exception) { outbox.MarkFailed(exception.Message); }
        await repository.SaveChangesAsync(cancellationToken);
        return new UsageIngestionResult(true, false, record, "Usage accepted and aggregated.");
    }
}

public sealed class BillingApplicationService(IBillingRepository repository, IPaymentGateway paymentGateway, TimeProvider timeProvider)
{
    public async Task<Invoice?> GenerateAsync(string accountExternalId, DateOnly start, DateOnly end, bool issueImmediately, CancellationToken cancellationToken)
    {
        var account = await repository.FindAccountAsync(accountExternalId, cancellationToken);
        if (account is null) return null;
        var existing = await repository.FindInvoiceAsync(account.Id, start, end, cancellationToken);
        if (existing is not null) return existing;

        var meters = (await repository.ListMetersAsync(cancellationToken)).ToDictionary(x => x.Key);
        var aggregates = await repository.ListAggregatesAsync(account.Id, start, end, cancellationToken);
        var invoice = Invoice.Create($"INV-{start:yyyyMM}-{account.ExternalId.ToUpperInvariant()}", account.Id, account.ExternalId, start, end, account.Currency);
        if (account.BaseFee > 0) invoice.AddLine("base_fee", $"{account.Plan} plan", 1, account.BaseFee, account.BaseFee);
        foreach (var aggregate in aggregates)
        {
            if (!meters.TryGetValue(aggregate.Meter, out var meter)) continue;
            var charge = PriceCalculator.Calculate(meter, aggregate.Quantity);
            invoice.AddLine(meter.Key, meter.Name, charge.BillableQuantity, charge.EffectiveUnitPrice, charge.Amount);
        }
        invoice.ApplyTax(0.23m);
        await repository.AddInvoiceAsync(invoice, cancellationToken);
        if (issueImmediately)
        {
            var externalId = await paymentGateway.CreateInvoiceAsync(account, invoice, cancellationToken);
            invoice.Issue(externalId, timeProvider.GetUtcNow());
        }
        await repository.SaveChangesAsync(cancellationToken);
        return invoice;
    }

    public async Task<Invoice?> IssueAsync(Guid id, CancellationToken cancellationToken)
    {
        var invoice = await repository.FindInvoiceAsync(id, cancellationToken); if (invoice is null) return null;
        var account = (await repository.ListAccountsAsync(cancellationToken)).Single(x => x.Id == invoice.AccountId);
        var externalId = await paymentGateway.CreateInvoiceAsync(account, invoice, cancellationToken);
        invoice.Issue(externalId, timeProvider.GetUtcNow()); await repository.SaveChangesAsync(cancellationToken); return invoice;
    }

    public async Task<Invoice?> MarkPaidAsync(Guid id, CancellationToken cancellationToken)
    { var invoice = await repository.FindInvoiceAsync(id, cancellationToken); if (invoice is null) return null; invoice.MarkPaid(timeProvider.GetUtcNow()); await repository.SaveChangesAsync(cancellationToken); return invoice; }
}
