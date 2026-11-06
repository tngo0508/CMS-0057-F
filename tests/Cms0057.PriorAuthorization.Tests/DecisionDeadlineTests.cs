namespace Cms0057.PriorAuthorization.Tests;

/// <summary>
/// How the Decision Deadline is set. The clock starts on Friday 2026-11-06 at 18:00Z. Residential substance use
/// treatment is 24 hours regardless (DHCS BHIN 24-001); the specialty mental health services under DHCS BHIN 26-030
/// get 5 business days, at most 7 calendar days, or 72 hours when expedited (42 CFR 438.210(d)). Where two rules
/// apply, the stricter sets the deadline.
/// </summary>
public sealed class DecisionDeadlineTests : IDisposable
{
    private readonly PriorAuthorizationApi _api = new();

    public void Dispose() => _api.Dispose();

    [Fact]
    public async Task A_mental_health_service_is_decided_within_five_business_days()
    {
        _api.Clock.Advance(TimeSpan.FromDays(1)); // Saturday 2026-11-07

        var deadline = await DeadlineOf(RequestBundles.For(RequestBundles.TherapeuticBehavioralServices));

        Assert.Equal(new DateTimeOffset(2026, 11, 13, 18, 0, 0, TimeSpan.Zero), deadline); // Friday
    }

    [Fact]
    public async Task Five_business_days_never_stretch_past_seven_calendar_days()
    {
        // Friday: the fifth business day is next Friday, exactly seven days on.
        var deadline = await DeadlineOf(RequestBundles.For(RequestBundles.TherapeuticBehavioralServices));

        Assert.Equal(new DateTimeOffset(2026, 11, 13, 18, 0, 0, TimeSpan.Zero), deadline);
    }

    [Fact]
    public async Task An_expedited_mental_health_request_is_decided_within_72_hours()
    {
        var deadline = await DeadlineOf(RequestBundles.For(RequestBundles.TherapeuticBehavioralServices, priority: "stat"));

        Assert.Equal(new DateTimeOffset(2026, 11, 9, 18, 0, 0, TimeSpan.Zero), deadline);
    }

    [Fact]
    public async Task Residential_treatment_keeps_its_24_hours_even_when_expedited()
    {
        var deadline = await DeadlineOf(RequestBundles.ResidentialTreatment(priority: "stat"));

        Assert.Equal(new DateTimeOffset(2026, 11, 7, 18, 0, 0, TimeSpan.Zero), deadline);
    }

    [Fact]
    public async Task The_stricter_rule_sets_the_deadline_when_a_request_mixes_services()
    {
        var deadline = await DeadlineOf(RequestBundles.ForServices(
            RequestBundles.TherapeuticBehavioralServices, RequestBundles.Residential));

        Assert.Equal(new DateTimeOffset(2026, 11, 7, 18, 0, 0, TimeSpan.Zero), deadline);
    }

    [Fact]
    public async Task The_reviewer_can_see_that_a_request_is_expedited()
    {
        var id = (await _api.Submit(RequestBundles.For(RequestBundles.TherapeuticBehavioralServices, priority: "stat"))).ClaimResponseId;

        Assert.True((await _api.PendedRequest(id)).Expedited);
    }

    private async Task<DateTimeOffset> DeadlineOf(string requestBundle)
    {
        var id = (await _api.Submit(requestBundle)).ClaimResponseId;
        return (await _api.PendedRequest(id)).DecisionDeadline;
    }
}
