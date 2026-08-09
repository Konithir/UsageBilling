using Dapr.Workflow;
using UsageBilling.Domain.Services;

namespace UsageBilling.BillingWorker.Workflows;

public sealed class CreateInvoiceActivity(BillingApplicationService billing) : WorkflowActivity<MonthlyBillingInput, BillingActivityResult>
{
    public override async Task<BillingActivityResult> RunAsync(WorkflowActivityContext context, MonthlyBillingInput input)
    {
        var invoice = await billing.GenerateAsync(input.AccountId, input.PeriodStart, input.PeriodEnd, false, CancellationToken.None)
            ?? throw new InvalidOperationException($"Billing account '{input.AccountId}' does not exist.");
        return new BillingActivityResult(invoice.Id, invoice.Number, invoice.Status.ToString(), invoice.Total);
    }
}

public sealed class IssueInvoiceActivity(BillingApplicationService billing) : WorkflowActivity<Guid, BillingActivityResult>
{
    public override async Task<BillingActivityResult> RunAsync(WorkflowActivityContext context, Guid invoiceId)
    {
        var invoice = await billing.IssueAsync(invoiceId, CancellationToken.None)
            ?? throw new InvalidOperationException($"Invoice '{invoiceId}' does not exist.");
        return new BillingActivityResult(invoice.Id, invoice.Number, invoice.Status.ToString(), invoice.Total);
    }
}