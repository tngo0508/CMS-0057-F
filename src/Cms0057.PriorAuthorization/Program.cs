using Cms0057.PriorAuthorization;
using Cms0057.PriorAuthorization.Authentication;
using Cms0057.PriorAuthorization.Crd;
using Cms0057.PriorAuthorization.Data;
using Cms0057.PriorAuthorization.Notifications;
using Cms0057.PriorAuthorization.Pas;
using Cms0057.PriorAuthorization.Reports;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddDbContext<PriorAuthorizationDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("PriorAuthorization")));
builder.Services.Configure<PriorAuthorizationOptions>(builder.Configuration.GetSection("PriorAuthorization"));
builder.Services.AddSingleton<IAuthorizationRules, ConfiguredAuthorizationRules>();
builder.Services.AddScoped<PriorAuthorizationRequests>();
builder.Services.AddSingleton<PasProfiles>();
builder.Services.AddSingleton<CoverageRequirements>();
builder.Services.AddScoped<PasSubscriptions>();
builder.Services.AddScoped<PriorAuthorizationMetrics>();
builder.Services.AddHttpClient(PasSubscriptions.HttpClientName, client => client.Timeout = TimeSpan.FromSeconds(10));
builder.Services.Configure<AuthenticationOptions>(builder.Configuration.GetSection(AuthenticationOptions.Section));
builder.Services.AddSingleton<ClientRegister>();
builder.Services.AddSingleton<AccessTokens>();
builder.Services.AddHttpClient(ClientRegister.HttpClientName, client => client.Timeout = TimeSpan.FromSeconds(10));
builder.Services.AddBearerTokens();
var authentication = builder.Configuration.GetSection(AuthenticationOptions.Section).Get<AuthenticationOptions>() ?? new();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<PriorAuthorizationDbContext>();
    await db.Database.MigrateAsync();
    if (app.Environment.IsDevelopment()) await SyntheticMembers.Seed(db);
}
// Fetch and compile the PAS profiles now rather than on the first provider request.
app.Services.GetRequiredService<PasProfiles>().WarmUp();

// SMART discovery: where a provider system or an EHR obtains an access token, and how.
app.MapGet("/.well-known/smart-configuration", (HttpRequest http) => Results.Json(SmartConfiguration.For(ServerRoot(http))));

// The token endpoint: a registered system trades its signed client assertion for a short-lived access token.
app.MapPost(SmartConfiguration.TokenPath, async (HttpRequest http, AccessTokens tokens) =>
{
    if (!http.HasFormContentType)
        return TokenError("invalid_request", "The token request must be a form (application/x-www-form-urlencoded)");
    var form = await http.ReadFormAsync();
    try
    {
        var issued = await tokens.Issue(
            new AccessTokens.Request(form["grant_type"], form["client_assertion_type"], form["client_assertion"], form["scope"]),
            ServerRoot(http) + SmartConfiguration.TokenPath);
        http.HttpContext.Response.Headers.CacheControl = "no-store";
        http.HttpContext.Response.Headers.Pragma = "no-cache";
        return Results.Json(new { access_token = issued.AccessToken, token_type = "bearer", expires_in = issued.ExpiresIn, scope = issued.Scope });
    }
    catch (AccessTokens.Refused refused)
    {
        return TokenError(refused.Error, refused.Message);
    }
});

// The PAS operations a provider system calls, each with the SMART system scope it needs.
Protected(app.MapPost("/fhir/Claim/$submit", async (HttpRequest http, PriorAuthorizationRequests requests, PasProfiles profiles) =>
{
    try
    {
        using var reader = new StreamReader(http.Body);
        var request = PasWire.ReadSubmission(await reader.ReadToEndAsync(), profiles) switch
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
        return Fhir(PasWire.Rejection(rejected), StatusCodes.Status400BadRequest);
    }
}), "system/Claim.write");

Protected(app.MapPost("/fhir/Claim/$inquire", async (HttpRequest http, PriorAuthorizationRequests requests, PasProfiles profiles) =>
{
    try
    {
        using var reader = new StreamReader(http.Body);
        var inquiry = PasWire.ReadInquiry(await reader.ReadToEndAsync(), profiles);
        var found = await requests.Find(inquiry);
        return Fhir(PasWire.InquiryResponse(found, ServerBase(http)));
    }
    catch (RequestRejected rejected)
    {
        return Fhir(PasWire.Rejection(rejected), StatusCodes.Status400BadRequest);
    }
}), "system/Claim.read");

// A provider system reads a ClaimResponse by the id the response gave it, which is also what a notification's
// focus names; br-provider reads one this way when it has no context for an inquiry.
Protected(app.MapGet("/fhir/ClaimResponse/{id:guid}", async (Guid id, HttpRequest http, PriorAuthorizationRequests requests) =>
    await requests.Find(id) is { } request
        ? Fhir(PasWire.ClaimResponseOf(request, ServerBase(http)))
        : Fhir(PasWire.NotFound($"No ClaimResponse with id {id}"), StatusCodes.Status404NotFound)), "system/ClaimResponse.read");

// A provider system asks to be told when its requests are decided (PAS subscription).
Protected(app.MapPost("/fhir/Subscription", async (HttpRequest http, PasSubscriptions subscriptions) =>
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
}), "system/Subscription.write");

// A provider system looks for the subscription it already holds before it subscribes again, as br-provider does.
Protected(app.MapGet("/fhir/Subscription", async (HttpRequest http, PasSubscriptions subscriptions) =>
    Fhir(await subscriptions.Find(QueryValue(http, "url"), QueryValue(http, "status"), ServerBase(http)))), "system/Subscription.read");

// CRD over CDS Hooks: what an EHR calls as a clinician signs an order. The EHR authenticates like a provider
// system, as a registered client; no scope is needed beyond that.
Protected(app.MapGet("/cds-services", () => Results.Json(CoverageRequirements.Discovery())));

Protected(app.MapPost($"/cds-services/{CoverageRequirements.ServiceId}", async (HttpRequest http, CoverageRequirements crd) =>
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
}));

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

// The Reviewer's web page, and the coded Denial Reasons it offers (the ones the plan has configured).
app.MapGet("/reviewer", (IWebHostEnvironment env) =>
    Results.File(Path.Combine(env.WebRootPath, "reviewer.html"), "text/html; charset=utf-8"));

app.MapGet("/reviewer/denial-reason-codes", (IOptions<PriorAuthorizationOptions> options) =>
    options.Value.DenialReasonCodes.Select(c => new { c.Code, c.Display }));

// The public prior authorization metrics for a calendar year (the rule has the plan post last year's by March 31).
app.MapGet("/reports/prior-authorization-metrics", async (int? year, PriorAuthorizationMetrics metrics, TimeProvider clock) =>
    Results.Json(await metrics.For(year ?? clock.GetUtcNow().Year - 1)));

app.Run();

// Demands a token this service issued, with the given scope, unless Authentication:Enabled is off for a local demo.
RouteHandlerBuilder Protected(RouteHandlerBuilder endpoint, string? scope = null) =>
    !authentication.Enabled ? endpoint
    : scope is null ? endpoint.RequireAuthorization()
    : endpoint.RequireAuthorization(scope);

/// RFC 6749 section 5.2: a refused token request.
static IResult TokenError(string error, string description) =>
    Results.Json(new { error, error_description = description }, statusCode: StatusCodes.Status400BadRequest);

static IResult Fhir(Hl7.Fhir.Model.Resource resource, int status = StatusCodes.Status200OK) =>
    Results.Text(PasWire.Write(resource), "application/fhir+json", statusCode: status);

static string ServerRoot(HttpRequest http) => $"{http.Scheme}://{http.Host}";

static string ServerBase(HttpRequest http) => ServerRoot(http) + "/fhir";

/// <summary>A search parameter's value, or null when the request does not carry it.</summary>
static string? QueryValue(HttpRequest http, string name) =>
    http.Query.TryGetValue(name, out var values) ? values.ToString() : null;

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
