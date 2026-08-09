using UsageBilling.Domain.Models;

namespace UsageBilling.Domain.Services;

public static class PriceCalculator
{
    public static ChargeBreakdown Calculate(MeterDefinition meter, decimal quantity)
    {
        var billable = Math.Max(0, quantity - meter.IncludedQuantity);
        var firstTier = Math.Min(billable, meter.FirstTierLimit);
        var overage = Math.Max(0, billable - firstTier);
        var amount = decimal.Round(firstTier * meter.FirstTierPrice + overage * meter.OveragePrice, 2, MidpointRounding.AwayFromZero);
        var effectivePrice = billable == 0 ? 0 : amount / billable;
        return new ChargeBreakdown(quantity, billable, firstTier, overage, effectivePrice, amount);
    }
}

public sealed record ChargeBreakdown(decimal Quantity, decimal BillableQuantity, decimal FirstTierQuantity, decimal OverageQuantity, decimal EffectiveUnitPrice, decimal Amount);
