namespace UsageBilling.Domain.Models;

public sealed class MeterDefinition
{
    private MeterDefinition() { }
    private MeterDefinition(string key, string name, string unit, MeterAggregation aggregation, decimal includedQuantity, decimal firstTierLimit, decimal firstTierPrice, decimal overagePrice, string currency)
    {
        Id = Guid.NewGuid(); Key = key; Name = name; Unit = unit; Aggregation = aggregation;
        IncludedQuantity = includedQuantity; FirstTierLimit = firstTierLimit; FirstTierPrice = firstTierPrice;
        OveragePrice = overagePrice; Currency = currency;
    }

    public Guid Id { get; private set; }
    public string Key { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public string Unit { get; private set; } = string.Empty;
    public MeterAggregation Aggregation { get; private set; }
    public decimal IncludedQuantity { get; private set; }
    public decimal FirstTierLimit { get; private set; }
    public decimal FirstTierPrice { get; private set; }
    public decimal OveragePrice { get; private set; }
    public string Currency { get; private set; } = "EUR";

    public static MeterDefinition Create(string key, string name, string unit, MeterAggregation aggregation, decimal includedQuantity, decimal firstTierLimit, decimal firstTierPrice, decimal overagePrice, string currency = "EUR") =>
        new(key, name, unit, aggregation, includedQuantity, firstTierLimit, firstTierPrice, overagePrice, currency);
}

public enum MeterAggregation { Sum, Maximum }
