namespace UsageBilling.Domain.Models;

public sealed class UsageAggregate
{
    private UsageAggregate() { }
    private UsageAggregate(Guid accountId, string accountExternalId, string meter, DateOnly periodStart, DateOnly periodEnd)
    { Id = Guid.NewGuid(); AccountId = accountId; AccountExternalId = accountExternalId; Meter = meter; PeriodStart = periodStart; PeriodEnd = periodEnd; }

    public Guid Id { get; private set; }
    public Guid AccountId { get; private set; }
    public string AccountExternalId { get; private set; } = string.Empty;
    public string Meter { get; private set; } = string.Empty;
    public DateOnly PeriodStart { get; private set; }
    public DateOnly PeriodEnd { get; private set; }
    public decimal Quantity { get; private set; }
    public long EventCount { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public static UsageAggregate Create(Guid accountId, string accountExternalId, string meter, DateOnly start, DateOnly end) => new(accountId, accountExternalId, meter, start, end);
    public void Apply(decimal quantity, MeterAggregation aggregation, DateTimeOffset at)
    {
        Quantity = aggregation == MeterAggregation.Maximum ? Math.Max(Quantity, quantity) : Quantity + quantity;
        EventCount++; UpdatedAt = at;
    }
}
