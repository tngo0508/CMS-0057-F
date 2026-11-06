using Cms0057.PriorAuthorization.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Cms0057.PriorAuthorization.Reports;

/// <summary>
/// The prior authorization metrics the final rule (42 CFR 438.210(f), via DHCS BHIN 26-008 for Behavioral Health
/// Plans) has the plan post publicly every year for the previous calendar year.
/// </summary>
/// <remarks>
/// Counts only requests that needed a Prior Authorization Decision: a Not Required answer is not a request for
/// prior authorization, and a rejected submission is an error that was never recorded, so neither is ever a denial.
/// The rule's further breakdowns (expedited against standard, approved after appeal, extended time frames) wait on
/// the service recording those things; see the open issues.
/// </remarks>
public sealed class PriorAuthorizationMetrics(PriorAuthorizationDbContext db, IOptions<PriorAuthorizationOptions> options)
{
    public sealed record Report(
        int Year,
        IReadOnlyList<string> ServicesRequiringPriorAuthorization,
        int RequestsReceived,
        int Approved,
        int Denied,
        int Undecided,
        double? ApprovedPercent,
        double? DeniedPercent,
        double? AverageHoursToDecision,
        double? MedianHoursToDecision);

    /// <summary>The metrics for the requests received in one calendar year (UTC).</summary>
    public async Task<Report> For(int year)
    {
        var from = new DateTimeOffset(year, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var to = from.AddYears(1);
        var requests = await db.Requests
            .Where(r => r.State != RequestState.NotRequired && r.ReceivedAt >= from && r.ReceivedAt < to)
            .Select(r => new { r.State, r.ReceivedAt, r.DecidedAt })
            .ToListAsync();

        var received = requests.Count;
        var approved = requests.Count(r => r.State == RequestState.Approved);
        var denied = requests.Count(r => r.State == RequestState.Denied);
        var hoursToDecision = requests
            .Where(r => r.DecidedAt is not null)
            .Select(r => (r.DecidedAt!.Value - r.ReceivedAt).TotalHours)
            .Order()
            .ToList();

        return new Report(
            Year: year,
            ServicesRequiringPriorAuthorization: Services(),
            RequestsReceived: received,
            Approved: approved,
            Denied: denied,
            Undecided: received - approved - denied,
            ApprovedPercent: Percent(approved, received),
            DeniedPercent: Percent(denied, received),
            AverageHoursToDecision: hoursToDecision.Count == 0 ? null : Math.Round(hoursToDecision.Average(), 1),
            MedianHoursToDecision: hoursToDecision.Count == 0 ? null : Math.Round(Median(hoursToDecision), 1));
    }

    private IReadOnlyList<string> Services() =>
    [
        .. options.Value.ResidentialServices.SelectMany(s =>
            s.Modifiers.Count == 0 ? [s.Code] : s.Modifiers.Select(m => $"{s.Code} {m}")),
    ];

    private static double? Percent(int part, int whole) =>
        whole == 0 ? null : Math.Round(100.0 * part / whole, 1);

    private static double Median(List<double> sorted) =>
        sorted.Count % 2 == 1
            ? sorted[sorted.Count / 2]
            : (sorted[sorted.Count / 2 - 1] + sorted[sorted.Count / 2]) / 2;
}
