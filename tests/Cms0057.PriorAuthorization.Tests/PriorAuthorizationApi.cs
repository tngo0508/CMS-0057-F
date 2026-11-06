using System.Net;
using System.Net.Http.Json;
using System.Text;
using Cms0057.PriorAuthorization.Authentication;
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
/// <param name="denialReasonCodes">Off only to show the page without a code picker; the plan normally has codes.</param>
/// <param name="authentication">Off only to show the local-demo switch; every other test runs with it on.</param>
/// <param name="validateProfiles">Off only to show the setting works; every other test runs with it on.</param>
public sealed class PriorAuthorizationApi(
    bool denialReasonCodes = true,
    bool authentication = true,
    bool validateProfiles = true)
    : WebApplicationFactory<Program>
{
    public static readonly DateTimeOffset Start = new(2026, 11, 6, 18, 0, 0, TimeSpan.Zero);

    private readonly string _database = $"Cms0057_Tests_{Guid.NewGuid():N}";

    public FakeTimeProvider Clock { get; } = new(Start);

    /// <summary>Every notification the service sends to provider systems lands here.</summary>
    public NotificationRecorder Notifications { get; } = new();

    /// <summary>The provider system the tests act as, registered with the plan for every PAS scope.</summary>
    public RegisteredSystem ProviderSystem { get; } =
        new("synthetic-recovery-house", "system/Claim.read system/Claim.write system/Subscription.write");

    /// <summary>The EHR the tests act as for CRD; registered by the URL where it publishes its keys.</summary>
    public RegisteredSystem Ehr { get; } = new("synthetic-ehr", "system/Claim.read");

    public const string EhrJwksUrl = "https://ehr.example.test/.well-known/jwks.json";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:PriorAuthorization", ConnectionString);
        // Validation against the PAS profiles is on by default; a test switches it off to show the setting works.
        if (!validateProfiles) builder.UseSetting("PriorAuthorization:ValidateProfiles", "false");
        if (denialReasonCodes)
        {
            // The X12 886 reason codes are licensed, so the service ships none; these stand in for the plan's own list.
            builder.UseSetting("PriorAuthorization:DenialReasonCodes:0:Code", DenialReasonCodes.NotMedicallyNecessary);
            builder.UseSetting("PriorAuthorization:DenialReasonCodes:0:Display", "Not medically necessary (test code)");
        }
        builder.UseSetting("Authentication:Enabled", authentication ? "true" : "false");
        Register(builder, 0, ProviderSystem);
        builder.UseSetting("Authentication:Clients:1:ClientId", Ehr.ClientId);
        builder.UseSetting("Authentication:Clients:1:Scopes", Ehr.Scopes);
        builder.UseSetting("Authentication:Clients:1:JwksUri", EhrJwksUrl);
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Clock);
            services.AddHttpClient(PasSubscriptions.HttpClientName)
                .ConfigurePrimaryHttpMessageHandler(() => Notifications);
            services.AddHttpClient(ClientRegister.HttpClientName)
                .ConfigurePrimaryHttpMessageHandler(() => new JwksServer(Ehr));
        });
    }

    /// <summary>Puts a system on the plan's client register, replacing whatever appsettings.json has at that index.</summary>
    private static void Register(IWebHostBuilder builder, int index, RegisteredSystem system)
    {
        builder.UseSetting($"Authentication:Clients:{index}:ClientId", system.ClientId);
        builder.UseSetting($"Authentication:Clients:{index}:Scopes", system.Scopes);
        foreach (var (member, value) in system.PublicJwk)
            builder.UseSetting($"Authentication:Clients:{index}:Jwks:keys:0:{member}", value);
    }

    /// <summary>
    /// A system presents its signed client assertion at the token endpoint (SMART Backend Services). The other
    /// form fields default to what the profile prescribes; a test can send something else to see it refused.
    /// </summary>
    public async Task<TokenResponse> RequestToken(
        string clientAssertion,
        string scope = "system/Claim.read system/Claim.write system/Subscription.write",
        string grantType = "client_credentials",
        string clientAssertionType = "urn:ietf:params:oauth:client-assertion-type:jwt-bearer")
    {
        using var client = CreateClient();
        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = grantType,
            ["client_assertion_type"] = clientAssertionType,
            ["client_assertion"] = clientAssertion,
            ["scope"] = scope,
        });
        using var response = await client.PostAsync("/auth/token", form);
        return new TokenResponse(response.StatusCode, await response.Content.ReadAsStringAsync());
    }

    /// <summary>Something other than a form is sent to the token endpoint.</summary>
    public async Task<TokenResponse> RequestTokenWith(string contentType, string body)
    {
        using var client = CreateClient();
        using var content = new StringContent(body, Encoding.UTF8, contentType);
        using var response = await client.PostAsync("/auth/token", content);
        return new TokenResponse(response.StatusCode, await response.Content.ReadAsStringAsync());
    }

    /// <summary>A provider system asks to be notified of decisions (PAS Subscription).</summary>
    public async Task<PasResponse> Subscribe(string subscription, Caller? caller = null) =>
        await Post("/fhir/Subscription", subscription, caller ?? Caller.As(ProviderSystem));

    public async Task<PasResponse> Submit(string requestBundle, Caller? caller = null) =>
        await Post("/fhir/Claim/$submit", requestBundle, caller ?? Caller.As(ProviderSystem));

    public async Task<PasResponse> Inquire(string inquiryBundle, Caller? caller = null) =>
        await Post("/fhir/Claim/$inquire", inquiryBundle, caller ?? Caller.As(ProviderSystem));

    /// <summary>SMART discovery: where and how a provider system obtains an access token.</summary>
    public async Task<string> SmartConfiguration()
    {
        using var client = CreateClient();
        return await client.GetStringAsync("/.well-known/smart-configuration");
    }

    /// <summary>CDS Hooks discovery: the services this payer offers an EHR.</summary>
    public async Task<string> DiscoverServices()
    {
        using var client = ClientFor(Caller.As(Ehr));
        return await client.GetStringAsync("/cds-services");
    }

    /// <summary>An EHR calls the order-sign service as a clinician signs an order.</summary>
    public async Task<HookResponse> OrderSign(string hookRequest, Caller? caller = null)
    {
        using var client = ClientFor(caller ?? Caller.As(Ehr));
        using var content = new StringContent(hookRequest, Encoding.UTF8, "application/json");
        using var response = await client.PostAsync("/cds-services/order-sign-crd", content);
        return new HookResponse(response.StatusCode, await response.Content.ReadAsStringAsync());
    }

    /// <summary>The public metrics report, fetched without any credentials.</summary>
    public async Task<HttpStatusCode> MetricsReportStatus(int year)
    {
        using var client = CreateClient();
        using var response = await client.GetAsync($"/reports/prior-authorization-metrics?year={year}");
        return response.StatusCode;
    }

    private HttpClient ClientFor(Caller caller) => CreateDefaultClient(new Caller.Handler(caller, this));

    private readonly Dictionary<string, (string AccessToken, DateTimeOffset Expires)> _tokens = [];

    /// <summary>A registered system's current access token, fetched (again) when it has none that is still good.</summary>
    internal async Task<string> AccessTokenFor(
        RegisteredSystem system,
        Func<string, Task<(string AccessToken, int ExpiresIn)>> fetch)
    {
        var now = Clock.GetUtcNow();
        if (_tokens.TryGetValue(system.ClientId, out var held) && held.Expires > now + TimeSpan.FromSeconds(10))
            return held.AccessToken;
        var (accessToken, expiresIn) = await fetch(system.ClientAssertion(now));
        _tokens[system.ClientId] = (accessToken, now + TimeSpan.FromSeconds(expiresIn));
        return accessToken;
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

    /// <summary>The Reviewer opens the web page.</summary>
    public async Task<(HttpStatusCode Status, string? ContentType, string Body)> ReviewerPage()
    {
        using var client = CreateClient();
        using var response = await client.GetAsync("/reviewer");
        return (response.StatusCode, response.Content.Headers.ContentType?.ToString(), await response.Content.ReadAsStringAsync());
    }

    /// <summary>The coded Denial Reasons the page offers the Reviewer: the ones the plan has configured.</summary>
    public async Task<IReadOnlyList<DenialReasonCodeOption>> ConfiguredDenialReasonCodes()
    {
        using var client = CreateClient();
        return (await client.GetFromJsonAsync<List<DenialReasonCodeOption>>("/reviewer/denial-reason-codes"))!;
    }

    /// <summary>The public prior authorization metrics for one calendar year.</summary>
    public async Task<MetricsReport> MetricsReport(int year)
    {
        using var client = CreateClient();
        return (await client.GetFromJsonAsync<MetricsReport>($"/reports/prior-authorization-metrics?year={year}"))!;
    }

    private async Task<PasResponse> Post(string path, string bundle, Caller caller)
    {
        using var client = ClientFor(caller);
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
    int Cancelled,
    int Undecided,
    double? ApprovedPercent,
    double? DeniedPercent,
    double? AverageHoursToDecision,
    double? MedianHoursToDecision);

/// <summary>One coded Denial Reason as the page's picker shows it.</summary>
public sealed record DenialReasonCodeOption(string Code, string Display);

/// <summary>A Pended request as the Reviewer's list shows it; ServiceCodes are the ones awaiting the decision.</summary>
public sealed record PendedRequest(
    string Id,
    string MemberIdentifier,
    string[] ServiceCodes,
    bool Expedited,
    DateTimeOffset ReceivedAt,
    DateTimeOffset DecisionDeadline,
    bool Overdue);
