using Microsoft.EntityFrameworkCore;
using UsageBilling.Contracts;
using UsageBilling.Domain.Services;

namespace UsageBilling.Infrastructure.Persistence;

public sealed class BillingReadService(BillingDbContext dbContext, IOverviewCache cache, TimeProvider timeProvider)
{
    public async Task<OverviewDto> GetOverviewAsync(CancellationToken cancellationToken)
    {
        var cached = await cache.GetAsync(cancellationToken);
        if (cached is not null) return cached;

        var now = timeProvider.GetUtcNow();
        var start = new DateOnly(now.Year, now.Month, 1);
        var end = start.AddMonths(1).AddDays(-1);
        var accounts = await dbContext.Accounts.AsNoTracking().ToListAsync(cancellationToken);
        var meters = await dbContext.Meters.AsNoTracking().ToListAsync(cancellationToken);
        var aggregates = await dbContext.UsageAggregates.AsNoTracking().Where(x => x.PeriodStart == start).ToListAsync(cancellationToken);
        var invoices = await dbContext.Invoices.AsNoTracking().Include(x => x.Lines).OrderByDescending(x => x.PeriodStart).ThenByDescending(x => x.CreatedAt).Take(40).ToListAsync(cancellationToken);
        var periodStartUtc = new DateTimeOffset(start.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var periodEndUtc = new DateTimeOffset(end.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var periodEvents = dbContext.UsageEvents.AsNoTracking().Where(x => x.OccurredAt >= periodStartUtc && x.OccurredAt < periodEndUtc);
        var eventCount = await periodEvents.LongCountAsync(cancellationToken);
        var processedCount = await periodEvents.LongCountAsync(x => x.Status == Domain.Models.UsageRecordStatus.Processed, cancellationToken);
        var events = await periodEvents.OrderByDescending(x => x.OccurredAt).Take(8).ToListAsync(cancellationToken);
        var accountNames = accounts.ToDictionary(x => x.Id, x => x.Name);
        var meterMap = meters.ToDictionary(x => x.Key);

        var usageCharges = aggregates.Sum(x => meterMap.TryGetValue(x.Meter, out var meter) ? PriceCalculator.Calculate(meter, x.Quantity).Amount : 0m);
        var monthToDate = accounts.Sum(x => x.BaseFee) + usageCharges;
        var elapsed = Math.Max(1, now.Day);
        var forecast = decimal.Round(monthToDate * end.Day / elapsed, 2);
        var colors = new[] { "#4777ff", "#22a06b", "#d97706", "#8b5cf6" };
        var meterSummaries = meters.Select((meter, index) =>
        {
            var quantity = aggregates.Where(x => x.Meter == meter.Key).Sum(x => x.Quantity);
            var charge = aggregates.Where(x => x.Meter == meter.Key).Sum(x => PriceCalculator.Calculate(meter, x.Quantity).Amount);
            var change = meter.Key switch { "api_calls" => 12.8m, "storage_gb" => 4.2m, "export_rows" => -3.1m, _ => 8.5m };
            return new MeterSummaryDto(meter.Key, meter.Name, meter.Unit, quantity, charge, change, colors[index % colors.Length]);
        }).ToList();

        var revenue = Enumerable.Range(0, 6).Select(offset => start.AddMonths(offset - 5)).Select(month =>
        {
            var actual = invoices.Where(x => x.PeriodStart == month && x.Status != Domain.Models.InvoiceStatus.Draft).Sum(x => x.Total);
            var isCurrent = month == start;
            return new RevenuePointDto(month.ToString("MMM"), isCurrent ? monthToDate : actual, isCurrent ? forecast : actual);
        }).ToList();

        var recentEvents = events.Select(x => new UsageEventDto(x.EventId, x.AccountExternalId, accountNames.GetValueOrDefault(x.AccountId, x.AccountExternalId), x.Meter, x.Quantity, meterMap.TryGetValue(x.Meter, out var meter) ? meter.Unit : string.Empty, x.Status.ToString(), x.OccurredAt, x.ReceivedAt)).ToList();
        var recentInvoices = invoices.Take(7).Select(x => ToInvoiceDto(x, accountNames.GetValueOrDefault(x.AccountId, x.AccountExternalId))).ToList();
        var processed = eventCount == 0 ? 100m : decimal.Round(100m * processedCount / eventCount, 1);

        var result = new OverviewDto(monthToDate, forecast, eventCount, aggregates.Sum(x => x.Quantity), accounts.Count, invoices.Count(x => x.Status == Domain.Models.InvoiceStatus.Draft), processed, revenue, meterSummaries, recentEvents, recentInvoices);
        await cache.SetAsync(result, cancellationToken);
        return result;
    }

    public async Task<IReadOnlyList<AccountDto>> GetAccountsAsync(CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var start = new DateOnly(now.Year, now.Month, 1);
        var accounts = await dbContext.Accounts.AsNoTracking().OrderBy(x => x.Name).ToListAsync(cancellationToken);
        var aggregates = await dbContext.UsageAggregates.AsNoTracking().Where(x => x.PeriodStart == start).ToListAsync(cancellationToken);
        var meters = (await dbContext.Meters.AsNoTracking().ToListAsync(cancellationToken)).ToDictionary(x => x.Key);
        return accounts.Select(account =>
        {
            var own = aggregates.Where(x => x.AccountId == account.Id).ToList();
            var charge = account.BaseFee + own.Sum(x => meters.TryGetValue(x.Meter, out var meter) ? PriceCalculator.Calculate(meter, x.Quantity).Amount : 0m);
            return new AccountDto(account.Id, account.ExternalId, account.Name, account.Plan, account.Status.ToString(), account.BillingEmail, account.Currency, charge, own.Sum(x => x.EventCount), account.CreatedAt);
        }).ToList();
    }

    public async Task<IReadOnlyList<MeterDto>> GetMetersAsync(CancellationToken cancellationToken) =>
        await dbContext.Meters.AsNoTracking().OrderBy(x => x.Name).Select(x => new MeterDto(x.Id, x.Key, x.Name, x.Unit, x.Aggregation.ToString(), x.IncludedQuantity, x.FirstTierLimit, x.FirstTierPrice, x.OveragePrice, x.Currency)).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<UsageEventDto>> GetUsageEventsAsync(int take, CancellationToken cancellationToken)
    {
        var names = await dbContext.Accounts.AsNoTracking().ToDictionaryAsync(x => x.Id, x => x.Name, cancellationToken);
        var units = await dbContext.Meters.AsNoTracking().ToDictionaryAsync(x => x.Key, x => x.Unit, cancellationToken);
        var events = await dbContext.UsageEvents.AsNoTracking().OrderByDescending(x => x.OccurredAt).Take(Math.Clamp(take, 1, 200)).ToListAsync(cancellationToken);
        return events.Select(x => new UsageEventDto(x.EventId, x.AccountExternalId, names.GetValueOrDefault(x.AccountId, x.AccountExternalId), x.Meter, x.Quantity, units.GetValueOrDefault(x.Meter, string.Empty), x.Status.ToString(), x.OccurredAt, x.ReceivedAt)).ToList();
    }

    public async Task<IReadOnlyList<UsageAggregateDto>> GetAggregatesAsync(DateOnly start, DateOnly end, CancellationToken cancellationToken)
    {
        var names = await dbContext.Accounts.AsNoTracking().ToDictionaryAsync(x => x.Id, x => x.Name, cancellationToken);
        var meters = await dbContext.Meters.AsNoTracking().ToDictionaryAsync(x => x.Key, cancellationToken);
        var rows = await dbContext.UsageAggregates.AsNoTracking().Where(x => x.PeriodStart >= start && x.PeriodEnd <= end).OrderByDescending(x => x.PeriodStart).ToListAsync(cancellationToken);
        return rows.Select(x =>
        {
            var meter = meters[x.Meter]; var charge = PriceCalculator.Calculate(meter, x.Quantity);
            return new UsageAggregateDto(x.AccountExternalId, names.GetValueOrDefault(x.AccountId, x.AccountExternalId), x.Meter, meter.Name, x.Quantity, meter.IncludedQuantity, charge.BillableQuantity, charge.Amount, x.PeriodStart, x.PeriodEnd);
        }).ToList();
    }

    public async Task<IReadOnlyList<InvoiceDto>> GetInvoicesAsync(int take, CancellationToken cancellationToken)
    {
        var names = await dbContext.Accounts.AsNoTracking().ToDictionaryAsync(x => x.Id, x => x.Name, cancellationToken);
        var invoices = await dbContext.Invoices.AsNoTracking().Include(x => x.Lines).OrderByDescending(x => x.PeriodStart).ThenByDescending(x => x.CreatedAt).Take(Math.Clamp(take, 1, 200)).ToListAsync(cancellationToken);
        return invoices.Select(x => ToInvoiceDto(x, names.GetValueOrDefault(x.AccountId, x.AccountExternalId))).ToList();
    }

    public async Task<IReadOnlyList<OutboxDto>> GetOutboxAsync(int take, CancellationToken cancellationToken) =>
        await dbContext.Outbox.AsNoTracking().OrderByDescending(x => x.CreatedAt).Take(Math.Clamp(take, 1, 200)).Select(x => new OutboxDto(x.Id, x.Type, x.AggregateId, x.Status.ToString(), x.Attempts, x.CreatedAt, x.PublishedAt)).ToListAsync(cancellationToken);

    public Task ClearCacheAsync(CancellationToken cancellationToken) => cache.ClearAsync(cancellationToken);

    public static InvoiceDto ToInvoiceDto(Domain.Models.Invoice invoice, string accountName) =>
        new(invoice.Id, invoice.Number, invoice.AccountExternalId, accountName, invoice.PeriodStart, invoice.PeriodEnd, invoice.Currency, invoice.Subtotal, invoice.Tax, invoice.Total, invoice.Status.ToString(), invoice.CreatedAt, invoice.IssuedAt, invoice.Lines.Select(x => new InvoiceLineDto(x.Meter, x.Description, x.Quantity, x.UnitPrice, x.Amount)).ToList());
}