using Dapr.Workflow;

namespace UsageBilling.BillingWorker.Workflows;

public sealed record MonthlyBillingInput(string AccountId, DateOnly PeriodStart, DateOnly PeriodEnd, bool IssueInvoice);
public sealed record BillingActivityResult(Guid InvoiceId, string Number, string Status, decimal Total);
public sealed record MonthlyBillingResult(Guid InvoiceId, string Number, string Status, decimal Total, DateTime CompletedAt);

public sealed class MonthlyBillingWorkflow : Workflow<MonthlyBillingInput, MonthlyBillingResult>
{
    public override async Task<MonthlyBillingResult> RunAsync(WorkflowContext context, MonthlyBillingInput input)
    {
        context.SetCustomStatus(new { Step = "calculating", input.AccountId, input.PeriodStart, input.PeriodEnd });
        var retry = new WorkflowTaskOptions(new WorkflowRetryPolicy(4, TimeSpan.FromSeconds(2), 2d, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5)));
        var invoice = await context.CallActivityAsync<BillingActivityResult>(nameof(CreateInvoiceActivity), input, retry);

        if (input.IssueInvoice && string.Equals(invoice.Status, "Draft", StringComparison.OrdinalIgnoreCase))
        {
            context.SetCustomStatus(new { Step = "issuing", InvoiceId = invoice.InvoiceId });
            invoice = await context.CallActivityAsync<BillingActivityResult>(nameof(IssueInvoiceActivity), invoice.InvoiceId, retry);
        }

        context.SetCustomStatus(new { Step = "completed", InvoiceId = invoice.InvoiceId, invoice.Status });
        return new MonthlyBillingResult(invoice.InvoiceId, invoice.Number, invoice.Status, invoice.Total, context.CurrentUtcDateTime);
    }
}