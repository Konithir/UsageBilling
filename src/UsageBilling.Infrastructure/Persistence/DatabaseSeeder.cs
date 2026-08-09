using Microsoft.EntityFrameworkCore;
using UsageBilling.Domain.Models;

namespace UsageBilling.Infrastructure.Persistence;

public sealed class DatabaseSeeder(BillingDbContext dbContext, TimeProvider timeProvider)
{
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (dbContext.Database.IsRelational())
            await dbContext.Database.MigrateAsync(cancellationToken);
        else
            await dbContext.Database.EnsureCreatedAsync(cancellationToken);
        if (await dbContext.Accounts.AnyAsync(cancellationToken)) return;

        var accounts = new[]
        {
            BillingAccount.Create("acme-labs", "Acme Labs", "Scale", "billing@acme.test", "EUR", 349m),
            BillingAccount.Create("northstar", "Northstar Analytics", "Growth", "finance@northstar.test", "EUR", 199m),
            BillingAccount.Create("papertrail", "Papertrail Studio", "Starter", "accounts@papertrail.test", "EUR", 79m)
        };
        var meters = new[]
        {
            MeterDefinition.Create("api_calls", "API calls", "requests", MeterAggregation.Sum, 100_000m, 400_000m, 0.0008m, 0.00055m),
            MeterDefinition.Create("storage_gb", "Object storage", "GB-month", MeterAggregation.Maximum, 20m, 180m, 0.14m, 0.09m),
            MeterDefinition.Create("export_rows", "Exported rows", "rows", MeterAggregation.Sum, 50_000m, 450_000m, 0.00035m, 0.0002m),
            MeterDefinition.Create("active_seats", "Active seats", "seats", MeterAggregation.Maximum, 3m, 22m, 7m, 5m)
        };
        dbContext.AddRange(accounts);
        dbContext.AddRange(meters);

        var now = timeProvider.GetUtcNow();
        var currentStart = new DateOnly(now.Year, now.Month, 1);
        var currentEnd = currentStart.AddMonths(1).AddDays(-1);
        var sample = new Dictionary<string, decimal[]>
        {
            ["acme-labs"] = [182_500m, 238_900m, 86m, 14m, 140_000m, 64m],
            ["northstar"] = [96_400m, 121_800m, 43m, 9m, 72_000m, 31m],
            ["papertrail"] = [34_200m, 27_600m, 18m, 5m, 21_000m, 12m]
        };
        var sequence = 1;
        foreach (var account in accounts)
        {
            var values = sample[account.ExternalId];
            var eventData = new[]
            {
                ("api_calls", values[0], now.AddHours(-2)),
                ("api_calls", values[1], now.AddDays(-2)),
                ("storage_gb", values[2], now.AddHours(-5)),
                ("active_seats", values[3], now.AddDays(-1)),
                ("export_rows", values[4], now.AddHours(-9)),
                ("storage_gb", values[5], now.AddDays(-4))
            };
            foreach (var (meterKey, quantity, occurredAt) in eventData)
            {
                var meter = meters.Single(x => x.Key == meterKey);
                var eventId = $"evt_demo_{sequence++:000}";
                var record = UsageRecord.Create(eventId, account.Id, account.ExternalId, meterKey, quantity, occurredAt, "{\"source\":\"seed\"}", occurredAt.AddSeconds(2));
                record.MarkProcessed(occurredAt.AddSeconds(3));
                var outbox = OutboxMessage.Create("usage.recorded.v1", eventId, $"{{\"eventId\":\"{eventId}\"}}");
                outbox.MarkPublished(occurredAt.AddSeconds(3));
                dbContext.Add(record);
                dbContext.Add(outbox);

                var aggregate = dbContext.ChangeTracker.Entries<UsageAggregate>()
                    .Select(x => x.Entity)
                    .SingleOrDefault(x => x.AccountId == account.Id && x.Meter == meterKey && x.PeriodStart == currentStart);
                if (aggregate is null)
                {
                    aggregate = UsageAggregate.Create(account.Id, account.ExternalId, meterKey, currentStart, currentEnd);
                    dbContext.Add(aggregate);
                }
                aggregate.Apply(quantity, meter.Aggregation, occurredAt.AddSeconds(3));
            }
        }

        for (var monthsAgo = 1; monthsAgo <= 5; monthsAgo++)
        {
            var start = currentStart.AddMonths(-monthsAgo);
            var end = start.AddMonths(1).AddDays(-1);
            for (var accountIndex = 0; accountIndex < accounts.Length; accountIndex++)
            {
                var account = accounts[accountIndex];
                var usageFactor = 1m + ((5 - monthsAgo) * 0.08m) + (accountIndex * 0.13m);
                var invoice = Invoice.Create($"INV-{start:yyyyMM}-{account.ExternalId.ToUpperInvariant()}", account.Id, account.ExternalId, start, end, account.Currency);
                invoice.AddLine("base_fee", $"{account.Plan} plan", 1m, account.BaseFee, account.BaseFee);
                invoice.AddLine("api_calls", "API calls", 180_000m * usageFactor, 0.00072m, 129.60m * usageFactor);
                invoice.AddLine("storage_gb", "Object storage", 72m * usageFactor, 0.12m, 8.64m * usageFactor);
                invoice.ApplyTax(0.23m);
                invoice.Issue($"in_demo_history_{monthsAgo}_{accountIndex}", now.AddMonths(-monthsAgo));
                if (monthsAgo > 1 || accountIndex != 2) invoice.MarkPaid(now.AddMonths(-monthsAgo).AddDays(4));
                dbContext.Add(invoice);
            }
        }

        var draftAccount = accounts[2];
        var draft = Invoice.Create($"INV-{currentStart:yyyyMM}-{draftAccount.ExternalId.ToUpperInvariant()}", draftAccount.Id, draftAccount.ExternalId, currentStart, currentEnd, draftAccount.Currency);
        draft.AddLine("base_fee", $"{draftAccount.Plan} plan", 1m, draftAccount.BaseFee, draftAccount.BaseFee);
        draft.AddLine("active_seats", "Active seats", 2m, 7m, 14m);
        draft.ApplyTax(0.23m);
        dbContext.Add(draft);

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}