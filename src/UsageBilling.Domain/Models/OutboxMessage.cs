namespace UsageBilling.Domain.Models;

public sealed class OutboxMessage
{
    private OutboxMessage() { }
    private OutboxMessage(string type, string aggregateId, string payload)
    { Id = Guid.NewGuid(); Type = type; AggregateId = aggregateId; Payload = payload; Status = OutboxStatus.Pending; CreatedAt = DateTimeOffset.UtcNow; }
    public Guid Id { get; private set; }
    public string Type { get; private set; } = string.Empty;
    public string AggregateId { get; private set; } = string.Empty;
    public string Payload { get; private set; } = string.Empty;
    public OutboxStatus Status { get; private set; }
    public int Attempts { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? PublishedAt { get; private set; }
    public string? LastError { get; private set; }
    public static OutboxMessage Create(string type, string aggregateId, string payload) => new(type, aggregateId, payload);
    public void MarkPublished(DateTimeOffset at) { Status = OutboxStatus.Published; PublishedAt = at; Attempts++; LastError = null; }
    public void MarkFailed(string error) { Status = OutboxStatus.Failed; Attempts++; LastError = error; }
}
public enum OutboxStatus { Pending, Published, Failed }
