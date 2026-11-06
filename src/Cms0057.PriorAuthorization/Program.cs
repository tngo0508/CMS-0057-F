using Cms0057.PriorAuthorization;
using Cms0057.PriorAuthorization.Crd;
using Cms0057.PriorAuthorization.Data;
using Cms0057.PriorAuthorization.Notifications;
using Cms0057.PriorAuthorization.Pas;
using Cms0057.PriorAuthorization.Reports;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddDbContext<PriorAuthorizationDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("PriorAuthorization")));
builder.Services.Configure<PriorAuthorizationOptions>(builder.Configuration.GetSection("PriorAuthorization"));
builder.Services.AddSingleton<IAuthorizationRules, ConfiguredAuthorizationRules>();
builder.Services.AddScoped<PriorAuthorizationRequests>();
builder.Services.AddSingleton<CoverageRequirements>();
builder.Services.AddScoped<PasSubscriptions>();
builder.Services.AddScoped<PriorAuthorizationMetrics>();
builder.Services.AddHttpClient(PasSubscriptions.HttpClientName, client => client.Timeout = TimeSpan.FromSeconds(10));

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<PriorAuthorizationDbContext>();
    await db.Database.MigrateAsync();
    if (app.Environment.IsDevelopment()) await SyntheticMembers.Seed(db);
}

// The PAS operations a provider system calls.
app.MapPost("/fhir/Claim/$submit", async (HttpRequest http, PriorAuthorizationRequests requests) =>
{
    try
    {
        using var reader = new StreamReader(http.Body);
        var request = PasWire.ReadSubmission(await reader.ReadToEndAsync()) switch
        {
            Submission submission => await requests.Receive(submission),
            RequestUpdate update => await requests.Update(update),
            RequestCancellation cancellation => await requests.Cancel(cancellation),
            var other => throw new InvalidOperationException($"Unknown submission {other.GetType().Name}"),
        };
        return Fhir(PasWire.Response(request, ServerBase(http)));
    }
    catch (RequestRejected rejected)
    {
        return Fhir(PasWire.Rejection(rejected.Message), StatusCodes.Status400BadRequest);
    }
});

app.MapPost("/fhir/Claim/$inquire", async (HttpRequest http, PriorAuthorizationRequests requests) =>
{
    try
    {
        using var reader = new StreamReader(http.Body);
        var inquiry = PasWire.ReadInquiry(await reader.ReadToEndAsync());
        var found = await requests.Find(inquiry);
        return Fhir(PasWire.InquiryResponse(found, ServerBase(http)));
    }
    catch (RequestRejected rejected)
    {
        return Fhir(PasWire.Rejection(rejected.Message), StatusCodes.Status400BadRequest);
    }
});

// A provider system asks to be told when its requests are decided (PAS subscription).
app.MapPost("/fhir/Subscription", async (HttpRequest http, PasSubscriptions subscriptions) =>
{
    try
    {
        using var reader = new StreamReader(http.Body);
        var accepted = await subscriptions.Subscribe(await reader.ReadToEndAsync());
        return Fhir(accepted, StatusCodes.Status201Created);
    }
    catch (RequestRejected rejected)
    {
        return Fhir(PasWire.Rejection(rejected.Message), StatusCodes.Status422UnprocessableEntity);
    }
});

// CRD over CDS Hooks: what an EHR calls as a clinician signs an order.
app.MapGet("/cds-services", () => Results.Json(CoverageRequirements.Discovery()));

app.MapPost($"/cds-services/{CoverageRequirements.ServiceId}", async (HttpRequest http, CoverageRequirements crd) =>
{
    try
    {
        using var reader = new StreamReader(http.Body);
        return Results.Json(crd.OrderSign(await reader.ReadToEndAsync()));
    }
    catch (RequestRejected rejected)
    {
        return Results.Problem(rejected.Message, statusCode: StatusCodes.Status400BadRequest);
    }
});

// What a Reviewer uses. Internal, not FHIR. Each decision is sent on to the subscribed provider system.
app.MapPost("/reviewer/requests/{id:guid}/approve",
    (Guid id, Approval approval, HttpRequest http, PriorAuthorizationRequests requests, PasSubscriptions subscriptions) =>
        Decision(() => requests.Approve(id, approval.AuthorizationEnds), subscriptions, ServerBase(http)));

app.MapPost("/reviewer/requests/{id:guid}/deny",
    (Guid id, Denial denial, HttpRequest http, PriorAuthorizationRequests requests, PasSubscriptions subscriptions) =>
        Decision(() => requests.Deny(id, denial.DenialReason, denial.ReasonCode), subscriptions, ServerBase(http)));

app.MapGet("/reviewer/pended-requests", async (PriorAuthorizationRequests requests, TimeProvider clock) =>
{
    var now = clock.GetUtcNow();
    return (await requests.Pended()).Select(r => new
    {
        r.Id,
        r.MemberIdentifier,
        ServiceCodes = r.ServiceCodesUnderReview,
        r.Expedited,
        r.ReceivedAt,
        r.DecisionDeadline,
        Overdue = r.IsOverdue(now),
    });
});

// The public prior authorization metrics for a calendar year (the rule has the plan post last year's by March 31).
app.MapGet("/reports/prior-authorization-metrics", async (int? year, PriorAuthorizationMetrics metrics, TimeProvider clock) =>
    Results.Json(await metrics.For(year ?? clock.GetUtcNow().Year - 1)));

app.Run();

static IResult Fhir(Hl7.Fhir.Model.Resource resource, int status = StatusCodes.Status200OK) =>
    Results.Text(PasWire.Write(resource), "application/fhir+json", statusCode: status);

static string ServerBase(HttpRequest http) => $"{http.Scheme}://{http.Host}/fhir";

static async Task<IResult> Decision(
    Func<Task<PriorAuthorizationRequest?>> decide,
    PasSubscriptions subscriptions,
    string serverBase)
{
    try
    {
        var decided = await decide();
        if (decided is null) return Results.NotFound();
        await subscriptions.NotifyDecided(decided, serverBase);
        return Results.NoContent();
    }
    catch (DecisionRefused refused)
    {
        return Results.Problem(refused.Message, statusCode: StatusCodes.Status400BadRequest);
    }
    catch (AlreadyAnswered answered)
    {
        return Results.Problem(answered.Message, statusCode: StatusCodes.Status409Conflict);
    }
}

public sealed record Approval(DateOnly? AuthorizationEnds);

/// <param name="DenialReason">The plain-language reason; required.</param>
/// <param name="ReasonCode">One of the plan's configured X12 886 codes; optional.</param>
public sealed record Denial(string? DenialReason, string? ReasonCode = null);

public partial class Program;
