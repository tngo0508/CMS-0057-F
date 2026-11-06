using Cms0057.PriorAuthorization;
using Cms0057.PriorAuthorization.Data;
using Cms0057.PriorAuthorization.Pas;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddDbContext<PriorAuthorizationDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("PriorAuthorization")));
builder.Services.Configure<PriorAuthorizationOptions>(builder.Configuration.GetSection("PriorAuthorization"));
builder.Services.AddSingleton<IAuthorizationRules, ResidentialTreatmentRules>();
builder.Services.AddScoped<PriorAuthorizationRequests>();

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
        var submission = PasWire.ReadSubmission(await reader.ReadToEndAsync());
        var request = await requests.Receive(submission);
        return Fhir(PasWire.Response(request, $"{http.Scheme}://{http.Host}/fhir"));
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
        return Fhir(PasWire.InquiryResponse(found, $"{http.Scheme}://{http.Host}/fhir"));
    }
    catch (RequestRejected rejected)
    {
        return Fhir(PasWire.Rejection(rejected.Message), StatusCodes.Status400BadRequest);
    }
});

// What a Reviewer uses. Internal, not FHIR.
app.MapPost("/reviewer/requests/{id:guid}/approve", (Guid id, Approval approval, PriorAuthorizationRequests requests) =>
    Decision(() => requests.Approve(id, approval.AuthorizationEnds)));

app.MapPost("/reviewer/requests/{id:guid}/deny", (Guid id, Denial denial, PriorAuthorizationRequests requests) =>
    Decision(() => requests.Deny(id, denial.DenialReason)));

app.MapGet("/reviewer/pended-requests", async (PriorAuthorizationRequests requests, TimeProvider clock) =>
{
    var now = clock.GetUtcNow();
    return (await requests.Pended()).Select(r => new
    {
        r.Id,
        r.MemberIdentifier,
        r.ServiceCode,
        r.ReceivedAt,
        r.DecisionDeadline,
        Overdue = r.IsOverdue(now),
    });
});

app.Run();

static IResult Fhir(Hl7.Fhir.Model.Resource resource, int status = StatusCodes.Status200OK) =>
    Results.Text(PasWire.Write(resource), "application/fhir+json", statusCode: status);

static async Task<IResult> Decision(Func<Task<bool>> decide)
{
    try
    {
        return await decide() ? Results.NoContent() : Results.NotFound();
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

public sealed record Denial(string? DenialReason);

public partial class Program;
