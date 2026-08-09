namespace UsageBilling.Domain.Models;

public sealed class UsageRecord
{
    private UsageRecord() { }
    private UsageRecord(string eventId, Guid accountId, string accountExternalId, string meter, decimal quantity, DateTimeOffset occurredAt, DateTimeOffset receivedAt, string dimensionsJson)
    {
        Id = Guid.NewGuid(); EventId = eventId; AccountId = accountId; AccountExternalId = accountExternalId;
        Meter = meter; Quantity = quantity; OccurredAt = occurredAt; ReceivedAt = receivedAt;
        DimensionsJson = dimensionsJson; Status = UsageRecordStatus.Received;
    }

    public Guid Id { get; private set; }
    public string EventId { get; private set; } = string.Empty;
    public Guid AccountId { get; private set; }
    public string AccountExternalId { get; private set; } = string.Empty;
    public string Meter { get; private set; } = string.Empty;
    public decimal Quantity { get; private set; }
    public DateTimeOffset OccurredAt { get; private set; }
    public DateTimeOffset ReceivedAt { get; private set; }
    public string DimensionsJson { get; private set; } = "{}";
    public UsageRecordStatus Status { get; private set; }
    public DateTimeOffset? ProcessedAt { get; private set; }

    public static UsageRecord Create(string eventId, Guid accountId, string accountExternalId, string meter, decimal quantity, DateTimeOffset occurredAt, string dimensionsJson, DateTimeOffset? receivedAt = null)
    {
        if (string.IsNullOrWhiteSpace(eventId)) throw new ArgumentException("Event id is required.", nameof(eventId));
        if (quantity <= 0) throw new ArgumentOutOfRangeException(nameof(quantity), "Quantity must be positive.");
        return new UsageRecord(eventId, accountId, accountExternalId, meter, quantity, occurredAt, receivedAt ?? DateTimeOffset.UtcNow, dimensionsJson);
    }

    public void MarkProcessed(DateTimeOffset at) { Status = UsageRecordStatus.Processed; ProcessedAt = at; }
    public void MarkRejected() => Status = UsageRecordStatus.Rejected;
}

public enum UsageRecordStatus { Received, Processed, Rejected }
