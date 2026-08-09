using UsageBilling.Domain.Models;
using UsageBilling.Domain.Services;

namespace UsageBilling.Domain.Tests;

public sealed class PricingTests
{
    private static readonly MeterDefinition Meter = MeterDefinition.Create("jobs", "Background jobs", "jobs", MeterAggregation.Sum, 100m, 400m, 0.01m, 0.005m);

    [Fact]
    public void Included_usage_has_no_charge()
    {
        var result = PriceCalculator.Calculate(Meter, 80m);
        Assert.Equal(0m, result.BillableQuantity);
        Assert.Equal(0m, result.Amount);
    }

    [Fact]
    public void Quantity_inside_first_tier_uses_first_price()
    {
        var result = PriceCalculator.Calculate(Meter, 350m);
        Assert.Equal(250m, result.BillableQuantity);
        Assert.Equal(2.50m, result.Amount);
    }

    [Fact]
    public void Overage_is_priced_separately()
    {
        var result = PriceCalculator.Calculate(Meter, 700m);
        Assert.Equal(400m, result.FirstTierQuantity);
        Assert.Equal(200m, result.OverageQuantity);
        Assert.Equal(5m, result.Amount);
    }
}