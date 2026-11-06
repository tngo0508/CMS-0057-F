using System.Net;
using System.Net.Http.Json;
using System.Text;
using Cms0057.PriorAuthorization.Data;
using Cms0057.PriorAuthorization.Notifications;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;

namespace Cms0057.PriorAuthorization.Tests;

/// <summary>
/// The running service as a provider system and a Reviewer see it: over HTTP only. Each instance has its own
/// LocalDB database and its own clock, which starts at <see cref="Start"/>.
/// </summary>
public sealed class PriorAuthorizationApi : WebApplicationFactory<Program>
{
    public static readonly DateTimeOffset Start = new(2026, 11, 6, 18, 0, 0, TimeSpan.Zero);

    private readonly string _database = $"Cms0057_Tests_{Guid.NewGuid():N}";

    public FakeTimeProvider Clock { get; } = new(Start);

    /// <summary>Every notification the service sends to provider systems lands here.</summary>
    public NotificationRecorder Notifications { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:PriorAuthorization", ConnectionString);
        // The X12 886 reason codes are licensed, so the service ships none; these stand in for the plan's own list.
        builder.UseSetting("PriorAuthorization:DenialReasonCodes:0:Code", DenialReasonCodes.NotMedicallyNecessary);
        builder.UseSetting("PriorAuthorization:DenialReasonCodes:0:Display", "Not medically necessary (test code)");
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Clock);
            services.AddHttpClient(PasSubscriptions.HttpClientName)
                .ConfigurePrimaryHttpMessageHandler(() => Notifications);
        });
    }

    /// <summary>A provider system asks to be notified of decisions (PAS Subscription).</summary>
    public async Task<PasResponse> Subscribe(string subscription) => await Post("/fhir/Subscription", subscription);

    public async Task<PasResponse> Submit(string requestBundle) => await Post("/fhir/Claim/$submit", requestBundle);

    public async Task<PasResponse> Inquire(string inquiryBundle) => await Post("/fhir/Claim/$inquire", inquiryBundle);

    /// <summary>CDS Hooks discovery: the services this payer offers an EHR.</summary>
    public async Task<string> DiscoverServices()
    {
        using var client = CreateClient();
        return await client.GetStringAsync("/cds-services");
    }

    /// <summary>An EHR calls the order-sign service as a clinician signs an order.</summary>
    public async Task<HookResponse> OrderSign(string hookRequest)
    {
        using var client = CreateClient();
        using var content = new StringContent(hookRequest, Encoding.UTF8, "application/json");
        using var response = await client.PostAsync("/cds-services/order-sign-crd", content);
        return new HookResponse(response.StatusCode, await response.Content.ReadAsStringAsync());
    }

    public async Task<IReadOnlyList<PendedRequest>> PendedRequests()
    {
        using var client = CreateClient();
        return (await client.GetFromJsonAsync<List<PendedRequest>>("/reviewer/pended-requests"))!;
    }

    public async Task<PendedRequest> PendedRequest(string id) => (await PendedRequests()).Single(r => r.Id == id);

    public async Task<HttpStatusCode> Approve(string id, string? authorizationEnds)
    {
        using var client = CreateClient();
        using var response = await client.PostAsJsonAsync($"/reviewer/requests/{id}/approve", new { authorizationEnds });
        return response.StatusCode;
    }

    public async Task<HttpStatusCode> Deny(string id, string? denialReason, string? reasonCode = null)
    {
        using var client = CreateClient();
        using var response = await client.PostAsJsonAsync($"/reviewer/requests/{id}/deny", new { denialReason, reasonCode });
        return response.StatusCode;
    }

    /// <summary>The public prior authorization metrics for one calendar year.</summary>
    public async Task<MetricsReport> MetricsReport(int year)
    {
        using var client = CreateClient();
        return (await client.GetFromJsonAsync<MetricsReport>($"/reports/prior-authorization-metrics?year={year}"))!;
    }

    private async Task<PasResponse> Post(string path, string bundle)
    {
        using var client = CreateClient();
        using var content = new StringContent(bundle, Encoding.UTF8, "application/fhir+json");
        using var response = await client.PostAsync(path, content);
        return new PasResponse(response.StatusCode, await response.Content.ReadAsStringAsync());
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (!disposing || _dropped) return;

        // Test housekeeping only: drop this instance's throwaway database.
        _dropped = true;
        var options = new DbContextOptionsBuilder<PriorAuthorizationDbContext>().UseSqlServer(ConnectionString).Options;
        using var db = new PriorAuthorizationDbContext(options);
        db.Database.EnsureDeleted();
    }

    private bool _dropped;

    private string ConnectionString =>
        $@"Server=(localdb)\MSSQLLocalDB;Database={_database};Integrated Security=true;TrustServerCertificate=true";
}

/// <summary>The Denial Reason codes the test plan is configured with. Test values, not X12's.</summary>
public static class DenialReasonCodes
{
    public const string NotMedicallyNecessary = "TEST-NMN";
}

/// <summary>One year's prior authorization metrics as the plan would publish them.</summary>
public sealed record MetricsReport(
    int Year,
    string[] ServicesRequiringPriorAuthorization,
    int RequestsReceived,
    int Approved,
    int Denied,
    int Undecided,
    double? ApprovedPercent,
    double? DeniedPercent,
    double? AverageHoursToDecision,
    double? MedianHoursToDecision);

/// <summary>A Pended request as the Reviewer's list shows it; ServiceCodes are the ones awaiting the decision.</summary>
public sealed record PendedRequest(
    string Id,
    string MemberIdentifier,
    string[] ServiceCodes,
    bool Expedited,
    DateTimeOffset ReceivedAt,
    DateTimeOffset DecisionDeadline,
    bool Overdue);
