namespace UsageBilling.Contracts;

public sealed record OverviewDto(
    decimal MonthToDateRevenue,
    decimal ForecastRevenue,
    long EventsThisMonth,
    decimal ProcessedVolume,
    int ActiveAccounts,
    int DraftInvoices,
    decimal PipelineSuccessRate,
    IReadOnlyList<RevenuePointDto> Revenue,
    IReadOnlyList<MeterSummaryDto> Meters,
    IReadOnlyList<UsageEventDto> RecentEvents,
    IReadOnlyList<InvoiceDto> RecentInvoices);

public sealed record RevenuePointDto(string Label, decimal Actual, decimal Forecast);
public sealed record MeterSummaryDto(string Key, string Name, string Unit, decimal Quantity, decimal Charge, decimal ChangePercent, string Color);
public sealed record AccountDto(Guid Id, string ExternalId, string Name, string Plan, string Status, string BillingEmail, string Currency, decimal MonthToDate, long Events, DateTimeOffset CreatedAt);
public sealed record MeterDto(Guid Id, string Key, string Name, string Unit, string Aggregation, decimal IncludedQuantity, decimal FirstTierLimit, decimal FirstTierPrice, decimal OveragePrice, string Currency);
public sealed record UsageEventRequest(string EventId, string AccountId, string Meter, decimal Quantity, DateTimeOffset? OccurredAt, Dictionary<string, string>? Dimensions);
public sealed record UsageIngestionResultDto(bool Accepted, bool Duplicate, string EventId, string Status, string Message);
public sealed record UsageEventDto(string EventId, string AccountId, string AccountName, string Meter, decimal Quantity, string Unit, string Status, DateTimeOffset OccurredAt, DateTimeOffset ReceivedAt);
public sealed record UsageAggregateDto(string AccountId, string AccountName, string Meter, string MeterName, decimal Quantity, decimal IncludedQuantity, decimal BillableQuantity, decimal Charge, DateOnly PeriodStart, DateOnly PeriodEnd);
public sealed record InvoiceLineDto(string Meter, string Description, decimal Quantity, decimal UnitPrice, decimal Amount);
public sealed record InvoiceDto(Guid Id, string Number, string AccountId, string AccountName, DateOnly PeriodStart, DateOnly PeriodEnd, string Currency, decimal Subtotal, decimal Tax, decimal Total, string Status, DateTimeOffset CreatedAt, DateTimeOffset? IssuedAt, IReadOnlyList<InvoiceLineDto> Lines);
public sealed record BillingRunRequest(string AccountId, DateOnly PeriodStart, DateOnly PeriodEnd, bool IssueImmediately = false);
public sealed record OutboxDto(Guid Id, string Type, string AggregateId, string Status, int Attempts, DateTimeOffset CreatedAt, DateTimeOffset? PublishedAt);
public sealed record IntegrationDto(string Name, string Mode, string Status, string Endpoint);
