using Microsoft.Extensions.Options;
using Stripe;
using UsageBilling.Domain;
using UsageBilling.Domain.Models;
using UsageBilling.Infrastructure.Configuration;

namespace UsageBilling.Infrastructure.Payments;

public sealed class StripePaymentGateway(IOptions<StripeOptions> stripeOptions) : IPaymentGateway
{
    private readonly StripeOptions options = stripeOptions.Value;
    public string Mode => options.Mode;
    public string Endpoint => string.Equals(options.Mode, "Demo", StringComparison.OrdinalIgnoreCase) ? "Stripe test adapter" : "api.stripe.com";

    public async Task<string?> CreateInvoiceAsync(BillingAccount account, UsageBilling.Domain.Models.Invoice invoice, CancellationToken cancellationToken)
    {
        if (string.Equals(options.Mode, "Demo", StringComparison.OrdinalIgnoreCase))
            return $"in_demo_{invoice.Id:N}";

        if (string.IsNullOrWhiteSpace(options.SecretKey) || !options.SecretKey.StartsWith("sk_test_", StringComparison.Ordinal))
            throw new InvalidOperationException("A Stripe test secret key is required when Stripe mode is enabled.");

        var client = new StripeClient(options.SecretKey);
        var customer = await new CustomerService(client).CreateAsync(new CustomerCreateOptions
        {
            Email = account.BillingEmail,
            Name = account.Name,
            Metadata = new Dictionary<string, string> { ["billing_account"] = account.ExternalId }
        }, cancellationToken: cancellationToken);

        await new InvoiceItemService(client).CreateAsync(new InvoiceItemCreateOptions
        {
            Customer = customer.Id,
            Amount = decimal.ToInt64(invoice.Total * 100m),
            Currency = invoice.Currency.ToLowerInvariant(),
            Description = $"Usage charges for {invoice.PeriodStart:yyyy-MM-dd} - {invoice.PeriodEnd:yyyy-MM-dd}"
        }, cancellationToken: cancellationToken);

        var stripeInvoice = await new InvoiceService(client).CreateAsync(new InvoiceCreateOptions
        {
            Customer = customer.Id,
            AutoAdvance = false,
            Description = invoice.Number,
            Metadata = new Dictionary<string, string> { ["usage_billing_invoice"] = invoice.Id.ToString() }
        }, cancellationToken: cancellationToken);
        return stripeInvoice.Id;
    }
}