namespace UsageBilling.Domain.Models;

public sealed class Invoice
{
    private readonly List<InvoiceLine> _lines = [];
    private Invoice() { }
    private Invoice(string number, Guid accountId, string accountExternalId, DateOnly start, DateOnly end, string currency)
    { Id = Guid.NewGuid(); Number = number; AccountId = accountId; AccountExternalId = accountExternalId; PeriodStart = start; PeriodEnd = end; Currency = currency; Status = InvoiceStatus.Draft; CreatedAt = DateTimeOffset.UtcNow; }

    public Guid Id { get; private set; }
    public string Number { get; private set; } = string.Empty;
    public Guid AccountId { get; private set; }
    public string AccountExternalId { get; private set; } = string.Empty;
    public DateOnly PeriodStart { get; private set; }
    public DateOnly PeriodEnd { get; private set; }
    public string Currency { get; private set; } = "EUR";
    public decimal Subtotal { get; private set; }
    public decimal Tax { get; private set; }
    public decimal Total { get; private set; }
    public InvoiceStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? IssuedAt { get; private set; }
    public DateTimeOffset? PaidAt { get; private set; }
    public string? ExternalInvoiceId { get; private set; }
    public IReadOnlyCollection<InvoiceLine> Lines => _lines;

    public static Invoice Create(string number, Guid accountId, string accountExternalId, DateOnly start, DateOnly end, string currency) => new(number, accountId, accountExternalId, start, end, currency);
    public void AddLine(string meter, string description, decimal quantity, decimal unitPrice, decimal amount)
    {
        if (Status != InvoiceStatus.Draft) throw new InvalidOperationException("Only draft invoices can be changed.");
        _lines.Add(InvoiceLine.Create(Id, meter, description, quantity, unitPrice, amount));
        Recalculate();
    }
    public void ApplyTax(decimal rate) { if (Status != InvoiceStatus.Draft) throw new InvalidOperationException("Only draft invoices can be changed."); Tax = decimal.Round(Subtotal * rate, 2, MidpointRounding.AwayFromZero); Total = Subtotal + Tax; }
    public void Issue(string? externalId, DateTimeOffset at) { if (Status != InvoiceStatus.Draft) throw new InvalidOperationException("Only a draft invoice can be issued."); if (!_lines.Any()) throw new InvalidOperationException("Cannot issue an empty invoice."); Status = InvoiceStatus.Open; ExternalInvoiceId = externalId; IssuedAt = at; }
    public void MarkPaid(DateTimeOffset at) { if (Status != InvoiceStatus.Open) throw new InvalidOperationException("Only an open invoice can be paid."); Status = InvoiceStatus.Paid; PaidAt = at; }
    private void Recalculate() { Subtotal = decimal.Round(_lines.Sum(x => x.Amount), 2, MidpointRounding.AwayFromZero); Total = Subtotal + Tax; }
}

public sealed class InvoiceLine
{
    private InvoiceLine() { }
    private InvoiceLine(Guid invoiceId, string meter, string description, decimal quantity, decimal unitPrice, decimal amount)
    { Id = Guid.NewGuid(); InvoiceId = invoiceId; Meter = meter; Description = description; Quantity = quantity; UnitPrice = unitPrice; Amount = decimal.Round(amount, 2, MidpointRounding.AwayFromZero); }
    public Guid Id { get; private set; }
    public Guid InvoiceId { get; private set; }
    public string Meter { get; private set; } = string.Empty;
    public string Description { get; private set; } = string.Empty;
    public decimal Quantity { get; private set; }
    public decimal UnitPrice { get; private set; }
    public decimal Amount { get; private set; }
    internal static InvoiceLine Create(Guid invoiceId, string meter, string description, decimal quantity, decimal unitPrice, decimal amount) => new(invoiceId, meter, description, quantity, unitPrice, amount);
}

public enum InvoiceStatus { Draft, Open, Paid, Void }
