using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Prometheus;
using Serilog;
using UsageBilling.Api.Hubs;
using UsageBilling.Api.Workers;
using UsageBilling.Contracts;
using UsageBilling.Domain;
using UsageBilling.Domain.Services;
using UsageBilling.Infrastructure;
using UsageBilling.Infrastructure.Configuration;
using UsageBilling.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseSerilog((context, logger) => logger.ReadFrom.Configuration(context.Configuration).WriteTo.Console());
builder.Services.AddUsageBillingInfrastructure(builder.Configuration);
builder.Services.AddSignalR();
builder.Services.AddHttpClient();
builder.Services.AddHealthChecks();
builder.Services.AddOpenApi();
builder.Services.AddHostedService<OutboxPublisherWorker>();
builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy.AllowAnyHeader().AllowAnyMethod().AllowCredentials().SetIsOriginAllowed(_ => true)));

var otlpEndpoint = builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"];
var telemetry = builder.Services.AddOpenTelemetry().ConfigureResource(resource => resource.AddService("usage-billing-api"));
telemetry.WithTracing(tracing =>
{
    tracing.AddAspNetCoreInstrumentation().AddHttpClientInstrumentation();
    if (!string.IsNullOrWhiteSpace(otlpEndpoint)) tracing.AddOtlpExporter(options => options.Endpoint = new Uri(otlpEndpoint));
});
telemetry.WithMetrics(metrics =>
{
    metrics.AddAspNetCoreInstrumentation().AddHttpClientInstrumentation().AddRuntimeInstrumentation();
    if (!string.IsNullOrWhiteSpace(otlpEndpoint)) metrics.AddOtlpExporter(options => options.Endpoint = new Uri(otlpEndpoint));
});

var app = builder.Build();
app.UseSerilogRequestLogging();
app.UseCors();
app.UseHttpMetrics();
app.MapMetrics();
app.MapHealthChecks("/health");
app.MapOpenApi();
app.MapHub<BillingHub>("/hubs/billing");

await using (var scope = app.Services.CreateAsyncScope())
    await scope.ServiceProvider.GetRequiredService<DatabaseSeeder>().InitializeAsync();

var api = app.MapGroup("/api");
api.MapGet("/overview", (BillingReadService reads, CancellationToken ct) => reads.GetOverviewAsync(ct));
api.MapGet("/accounts", (BillingReadService reads, CancellationToken ct) => reads.GetAccountsAsync(ct));
api.MapGet("/meters", (BillingReadService reads, CancellationToken ct) => reads.GetMetersAsync(ct));
api.MapGet("/usage-events", (int? take, BillingReadService reads, CancellationToken ct) => reads.GetUsageEventsAsync(take ?? 50, ct));
api.MapGet("/usage-aggregates", (DateOnly? start, DateOnly? end, BillingReadService reads, TimeProvider clock, CancellationToken ct) =>
{
    var now = clock.GetUtcNow();
    var periodStart = start ?? new DateOnly(now.Year, now.Month, 1);
    return reads.GetAggregatesAsync(periodStart, end ?? periodStart.AddMonths(1).AddDays(-1), ct);
});
api.MapGet("/invoices", (int? take, BillingReadService reads, CancellationToken ct) => reads.GetInvoicesAsync(take ?? 50, ct));
api.MapGet("/outbox", (int? take, BillingReadService reads, CancellationToken ct) => reads.GetOutboxAsync(take ?? 50, ct));
api.MapGet("/integrations", (IUsageMessagePublisher messaging, IPaymentGateway payment, IOptions<BillingStorageOptions> storage, IOptions<RedisOptions> redis) =>
{
    var storageValue = storage.Value;
    var redisValue = redis.Value;
    return new[]
    {
        new IntegrationDto("Storage", storageValue.Provider, "Ready", string.Equals(storageValue.Provider, "InMemory", StringComparison.OrdinalIgnoreCase) ? "local process" : "PostgreSQL"),
        new IntegrationDto("Usage stream", messaging.Mode, "Ready", messaging.Endpoint),
        new IntegrationDto("Invoice provider", payment.Mode, "Ready", payment.Endpoint),
        new IntegrationDto("Dashboard cache", redisValue.Enabled ? "Redis" : "Disabled", redisValue.Enabled ? "Ready" : "Optional", redisValue.Enabled ? redisValue.ConnectionString : "not configured"),
        new IntegrationDto("Billing workflow", "Dapr Workflow", "Ready", "usage-billing-worker")
    };
});

api.MapPost("/usage-events", async (UsageEventRequest request, UsageApplicationService service, BillingReadService reads, IHubContext<BillingHub> hub, TimeProvider clock, CancellationToken ct) =>
{
    var errors = new Dictionary<string, string[]>();
    if (string.IsNullOrWhiteSpace(request.EventId)) errors["eventId"] = ["Event id is required."];
    if (string.IsNullOrWhiteSpace(request.AccountId)) errors["accountId"] = ["Account id is required."];
    if (string.IsNullOrWhiteSpace(request.Meter)) errors["meter"] = ["Meter is required."];
    if (request.Quantity <= 0) errors["quantity"] = ["Quantity must be greater than zero."];
    if (errors.Count > 0) return Results.ValidationProblem(errors);

    var result = await service.IngestAsync(request.EventId, request.AccountId, request.Meter, request.Quantity, request.OccurredAt ?? clock.GetUtcNow(), request.Dimensions, ct);
    if (!result.Accepted) return Results.BadRequest(new UsageIngestionResultDto(false, false, request.EventId, "Rejected", result.Message));
    await reads.ClearCacheAsync(ct);
    var response = new UsageIngestionResultDto(true, result.Duplicate, request.EventId, result.Duplicate ? "Duplicate" : "Processed", result.Message);
    if (!result.Duplicate) await hub.Clients.All.SendAsync("usageRecorded", response, ct);
    return Results.Ok(response);
});

api.MapPost("/billing-workflows", async (BillingRunRequest request, IHttpClientFactory clients, IConfiguration configuration, CancellationToken ct) =>
{
    if (request.PeriodEnd < request.PeriodStart) return Results.BadRequest(new { message = "Period end must be on or after period start." });
    var instanceId = $"billing-{request.AccountId}-{request.PeriodStart:yyyyMM}";
    var daprEndpoint = configuration["DAPR_HTTP_ENDPOINT"] ?? $"http://127.0.0.1:{configuration["DAPR_HTTP_PORT"] ?? "3500"}";
    var url = $"{daprEndpoint}/v1.0/workflows/dapr/MonthlyBillingWorkflow/start?instanceID={Uri.EscapeDataString(instanceId)}";
    var response = await clients.CreateClient().PostAsJsonAsync(url, new
    {
        request.AccountId,
        request.PeriodStart,
        request.PeriodEnd,
        IssueInvoice = request.IssueImmediately
    }, ct);
    if (!response.IsSuccessStatusCode)
        return Results.Problem(await response.Content.ReadAsStringAsync(ct), statusCode: (int)response.StatusCode, title: "Dapr rejected the workflow request.");
    return Results.Accepted($"/api/billing-workflows/{instanceId}", new { instanceId, status = "Scheduled" });
});
api.MapPost("/billing-runs", async (BillingRunRequest request, BillingApplicationService service, IBillingRepository repository, BillingReadService reads, IHubContext<BillingHub> hub, CancellationToken ct) =>
{
    if (request.PeriodEnd < request.PeriodStart) return Results.BadRequest(new { message = "Period end must be on or after period start." });
    var invoice = await service.GenerateAsync(request.AccountId, request.PeriodStart, request.PeriodEnd, request.IssueImmediately, ct);
    if (invoice is null) return Results.NotFound(new { message = "Account was not found." });
    var account = await repository.FindAccountAsync(invoice.AccountExternalId, ct);
    await reads.ClearCacheAsync(ct);
    var response = BillingReadService.ToInvoiceDto(invoice, account?.Name ?? invoice.AccountExternalId);
    await hub.Clients.All.SendAsync("invoiceChanged", response, ct);
    return Results.Ok(response);
});

api.MapPost("/invoices/{id:guid}/issue", async (Guid id, BillingApplicationService service, IBillingRepository repository, BillingReadService reads, IHubContext<BillingHub> hub, CancellationToken ct) =>
{
    try
    {
        var invoice = await service.IssueAsync(id, ct);
        if (invoice is null) return Results.NotFound();
        var account = await repository.FindAccountAsync(invoice.AccountExternalId, ct);
        await reads.ClearCacheAsync(ct);
        var response = BillingReadService.ToInvoiceDto(invoice, account?.Name ?? invoice.AccountExternalId);
        await hub.Clients.All.SendAsync("invoiceChanged", response, ct);
        return Results.Ok(response);
    }
    catch (InvalidOperationException exception) { return Results.Conflict(new { message = exception.Message }); }
});

api.MapPost("/invoices/{id:guid}/paid", async (Guid id, BillingApplicationService service, IBillingRepository repository, BillingReadService reads, IHubContext<BillingHub> hub, CancellationToken ct) =>
{
    try
    {
        var invoice = await service.MarkPaidAsync(id, ct);
        if (invoice is null) return Results.NotFound();
        var account = await repository.FindAccountAsync(invoice.AccountExternalId, ct);
        await reads.ClearCacheAsync(ct);
        var response = BillingReadService.ToInvoiceDto(invoice, account?.Name ?? invoice.AccountExternalId);
        await hub.Clients.All.SendAsync("invoiceChanged", response, ct);
        return Results.Ok(response);
    }
    catch (InvalidOperationException exception) { return Results.Conflict(new { message = exception.Message }); }
});

app.Run();

public partial class Program;