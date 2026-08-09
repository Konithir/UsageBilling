using Dapr.Workflow;
using UsageBilling.BillingWorker.Workflows;
using UsageBilling.Infrastructure;
using UsageBilling.Infrastructure.Persistence;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddUsageBillingInfrastructure(builder.Configuration);
builder.Services.AddDaprWorkflow(options =>
{
    options.RegisterWorkflow<MonthlyBillingWorkflow>();
    options.RegisterActivity<CreateInvoiceActivity>();
    options.RegisterActivity<IssueInvoiceActivity>();
});

var host = builder.Build();
await using (var scope = host.Services.CreateAsyncScope())
    await scope.ServiceProvider.GetRequiredService<DatabaseSeeder>().InitializeAsync();
await host.RunAsync();