using System.Net;
using System.Net.Http.Json;
using System.Text;
using Cms0057.PriorAuthorization.Data;
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

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:PriorAuthorization", ConnectionString);
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Clock);
        });
    }

    public async Task<PasResponse> Submit(string requestBundle) => await Post("/fhir/Claim/$submit", requestBundle);

    public async Task<PasResponse> Inquire(string inquiryBundle) => await Post("/fhir/Claim/$inquire", inquiryBundle);

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

    public async Task<HttpStatusCode> Deny(string id, string? denialReason)
    {
        using var client = CreateClient();
        using var response = await client.PostAsJsonAsync($"/reviewer/requests/{id}/deny", new { denialReason });
        return response.StatusCode;
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

/// <summary>A Pended request as the Reviewer's list shows it.</summary>
public sealed record PendedRequest(
    string Id,
    string MemberIdentifier,
    string ServiceCode,
    DateTimeOffset ReceivedAt,
    DateTimeOffset DecisionDeadline,
    bool Overdue);
