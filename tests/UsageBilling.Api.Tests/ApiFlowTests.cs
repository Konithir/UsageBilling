using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using UsageBilling.Contracts;

namespace UsageBilling.Api.Tests;

public sealed class ApiFlowTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient client = factory.CreateClient();

    [Fact]
    public async Task Overview_contains_seeded_usage()
    {
        var response = await client.GetAsync("/api/overview");
        response.EnsureSuccessStatusCode();
        var overview = await response.Content.ReadFromJsonAsync<OverviewDto>();

        Assert.NotNull(overview);
        Assert.True(overview.ActiveAccounts >= 3);
        Assert.True(overview.EventsThisMonth > 0);
        Assert.NotEmpty(overview.Meters);
        Assert.NotEmpty(overview.RecentInvoices);
    }

    [Fact]
    public async Task Usage_event_is_idempotent()
    {
        var eventId = $"evt_test_{Guid.NewGuid():N}";
        var request = new UsageEventRequest(eventId, "acme-labs", "api_calls", 2500m, DateTimeOffset.UtcNow, new Dictionary<string, string> { ["region"] = "eu" });

        var first = await client.PostAsJsonAsync("/api/usage-events", request);
        var second = await client.PostAsJsonAsync("/api/usage-events", request);
        first.EnsureSuccessStatusCode();
        second.EnsureSuccessStatusCode();

        var firstResult = await first.Content.ReadFromJsonAsync<UsageIngestionResultDto>();
        var secondResult = await second.Content.ReadFromJsonAsync<UsageIngestionResultDto>();
        Assert.False(firstResult!.Duplicate);
        Assert.True(secondResult!.Duplicate);
    }

    [Fact]
    public async Task Billing_run_returns_existing_or_new_invoice()
    {
        var now = DateTimeOffset.UtcNow;
        var start = new DateOnly(now.Year, now.Month, 1);
        var request = new BillingRunRequest("acme-labs", start, start.AddMonths(1).AddDays(-1), false);

        var response = await client.PostAsJsonAsync("/api/billing-runs", request);
        response.EnsureSuccessStatusCode();
        var invoice = await response.Content.ReadFromJsonAsync<InvoiceDto>();

        Assert.NotNull(invoice);
        Assert.Equal("acme-labs", invoice.AccountId);
        Assert.True(invoice.Total > 0m);
        Assert.Equal("Draft", invoice.Status);
    }

    [Fact]
    public async Task Invalid_usage_is_rejected()
    {
        var request = new UsageEventRequest("evt_invalid", "missing", "api_calls", -1m, null, null);
        var response = await client.PostAsJsonAsync("/api/usage-events", request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}