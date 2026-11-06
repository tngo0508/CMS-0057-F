namespace Cms0057.PriorAuthorization.Tests;

/// <summary>
/// The prior authorization metrics the final rule has an Impacted Payer post every year, produced from what the
/// service already records. Only requests that needed a decision count; Not Required answers are not requests for
/// prior authorization, and rejected submissions were never recorded at all.
/// </summary>
public sealed class ReportingMetricsTests : IDisposable
{
    private readonly PriorAuthorizationApi _api = new();

    public void Dispose() => _api.Dispose();

    [Fact]
    public async Task The_report_counts_approvals_denials_and_what_is_still_undecided()
    {
        await _api.Approve(await Pended(), authorizationEnds: "2026-12-06");
        await _api.Approve(await Pended(), authorizationEnds: "2026-12-06");
        await _api.Deny(await Pended(), denialReason: "The assessment does not show a need for a residential level of care.");
        await Pended();

        var report = await _api.MetricsReport(2026);

        Assert.Equal((4, 2, 1, 1), (report.RequestsReceived, report.Approved, report.Denied, report.Undecided));
        Assert.Equal((50.0, 25.0), (report.ApprovedPercent, report.DeniedPercent));
    }

    [Fact]
    public async Task A_service_that_needs_no_prior_authorization_is_not_a_request_for_one()
    {
        await _api.Submit(RequestBundles.OutpatientCounseling());

        var report = await _api.MetricsReport(2026);

        Assert.Equal(0, report.RequestsReceived);
    }

    [Fact]
    public async Task The_report_gives_the_average_and_median_time_to_a_decision()
    {
        var first = await Pended();
        var second = await Pended();
        var third = await Pended();
        _api.Clock.Advance(TimeSpan.FromHours(2));
        await _api.Approve(first, authorizationEnds: "2026-12-06");
        _api.Clock.Advance(TimeSpan.FromHours(2));
        await _api.Approve(second, authorizationEnds: "2026-12-06");
        _api.Clock.Advance(TimeSpan.FromHours(8));
        await _api.Deny(third, denialReason: "The assessment does not show a need for a residential level of care.");

        var report = await _api.MetricsReport(2026);

        Assert.Equal((6.0, 4.0), (report.AverageHoursToDecision, report.MedianHoursToDecision));
    }

    [Fact]
    public async Task The_report_covers_one_calendar_year_of_requests()
    {
        await Pended();
        _api.Clock.Advance(TimeSpan.FromDays(60)); // into January 2027
        await Pended();

        Assert.Equal(1, (await _api.MetricsReport(2026)).RequestsReceived);
        Assert.Equal(1, (await _api.MetricsReport(2027)).RequestsReceived);
    }

    [Fact]
    public async Task The_report_lists_the_services_that_need_prior_authorization()
    {
        var report = await _api.MetricsReport(2026);

        Assert.Equal(["H0019 U1", "H0019 U2", "H0019 U3", "H0040", "H2012", "H2019", "S5145"], report.ServicesRequiringPriorAuthorization);
    }

    private async Task<string> Pended() => (await _api.Submit(RequestBundles.ResidentialTreatment())).ClaimResponseId;
}
