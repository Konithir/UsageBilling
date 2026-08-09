namespace UsageBilling.Domain.Models;

public sealed class BillingAccount
{
    private BillingAccount() { }
    private BillingAccount(string externalId, string name, string plan, string billingEmail, string currency, decimal baseFee)
    {
        Id = Guid.NewGuid(); ExternalId = externalId; Name = name; Plan = plan; BillingEmail = billingEmail;
        Currency = currency; BaseFee = baseFee; Status = AccountStatus.Active; CreatedAt = DateTimeOffset.UtcNow;
    }

    public Guid Id { get; private set; }
    public string ExternalId { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public string Plan { get; private set; } = string.Empty;
    public string BillingEmail { get; private set; } = string.Empty;
    public string Currency { get; private set; } = "EUR";
    public decimal BaseFee { get; private set; }
    public AccountStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public static BillingAccount Create(string externalId, string name, string plan, string billingEmail, string currency, decimal baseFee) => new(externalId, name, plan, billingEmail, currency, baseFee);
}

public enum AccountStatus { Active, Trial, Suspended }
