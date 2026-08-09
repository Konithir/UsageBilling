using UsageBilling.Domain.Models;

namespace UsageBilling.Domain.Tests;

public sealed class InvoiceTests
{
    [Fact]
    public void Invoice_recalculates_totals_and_moves_through_states()
    {
        var invoice = Invoice.Create("INV-1", Guid.NewGuid(), "acme", new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 31), "EUR");
        invoice.AddLine("base_fee", "Scale plan", 1m, 100m, 100m);
        invoice.AddLine("api_calls", "API calls", 20_000m, 0.001m, 20m);
        invoice.ApplyTax(0.23m);

        Assert.Equal(120m, invoice.Subtotal);
        Assert.Equal(27.60m, invoice.Tax);
        Assert.Equal(147.60m, invoice.Total);

        invoice.Issue("in_test_1", DateTimeOffset.UtcNow);
        Assert.Equal(InvoiceStatus.Open, invoice.Status);
        invoice.MarkPaid(DateTimeOffset.UtcNow);
        Assert.Equal(InvoiceStatus.Paid, invoice.Status);
    }

    [Fact]
    public void Issued_invoice_cannot_be_changed_or_issued_twice()
    {
        var invoice = Invoice.Create("INV-2", Guid.NewGuid(), "acme", new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 31), "EUR");
        invoice.AddLine("base_fee", "Scale plan", 1m, 100m, 100m);
        invoice.Issue("in_test_2", DateTimeOffset.UtcNow);

        Assert.Throws<InvalidOperationException>(() => invoice.AddLine("api_calls", "API calls", 1m, 1m, 1m));
        Assert.Throws<InvalidOperationException>(() => invoice.Issue("in_test_3", DateTimeOffset.UtcNow));
    }
}