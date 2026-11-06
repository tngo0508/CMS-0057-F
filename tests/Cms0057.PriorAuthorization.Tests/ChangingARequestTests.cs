using System.Net;

namespace Cms0057.PriorAuthorization.Tests;

/// <summary>
/// What a provider system can do to a request after sending it: update it, cancel it, or send a renewal. The
/// shapes follow PAS as br-payer reads them: Claim.related names the prior request; certificationType 3 on the
/// Claim makes it a cancellation; certificationType R on an item makes the request a renewal.
/// </summary>
public sealed class ChangingARequestTests : IDisposable
{
    private readonly PriorAuthorizationApi _api = new();

    public void Dispose() => _api.Dispose();

    // ----- Cancellations -----

    [Fact]
    public async Task A_provider_can_cancel_a_pended_request()
    {
        await _api.Submit(RequestBundles.ResidentialTreatment(bundleIdentifier: "to-cancel"));

        var response = await _api.Submit(RequestBundles.Cancel("to-cancel"));

        Assert.Equal(HttpStatusCode.OK, response.Status);
        Assert.Equal("A2", response.ReviewActionCode);
        Assert.Empty(await _api.PendedRequests());
    }

    [Fact]
    public async Task Cancelling_an_approved_request_withdraws_the_authorization()
    {
        var approved = (await _api.Submit(RequestBundles.ResidentialTreatment(bundleIdentifier: "approved"))).ClaimResponseId;
        await _api.Approve(approved, authorizationEnds: "2026-12-06");

        await _api.Submit(RequestBundles.Cancel("approved"));

        var answer = await _api.Inquire(RequestBundles.Inquiry());
        Assert.Equal("A2", answer.ReviewActionCode);
        Assert.Null(answer.AuthorizationNumber);
    }

    [Fact]
    public async Task A_cancellation_is_not_a_denial()
    {
        await _api.Submit(RequestBundles.ResidentialTreatment(bundleIdentifier: "to-cancel"));

        await _api.Submit(RequestBundles.Cancel("to-cancel"));

        var report = await _api.MetricsReport(2026);
        Assert.Equal((0, 1, 0), (report.Denied, report.Cancelled, report.Undecided));
    }

    [Fact]
    public async Task A_denied_request_cannot_be_cancelled()
    {
        var denied = (await _api.Submit(RequestBundles.ResidentialTreatment(bundleIdentifier: "denied"))).ClaimResponseId;
        await _api.Deny(denied, denialReason: "The assessment does not show a need for a residential level of care.");

        var response = await _api.Submit(RequestBundles.Cancel("denied"));

        Assert.Equal(HttpStatusCode.BadRequest, response.Status);
        Assert.Equal("Cannot cancel a denied prior authorization (review action A2)", response.ErrorDiagnostics);
    }

    [Fact]
    public async Task Cancelling_a_request_the_plan_never_received_is_an_error()
    {
        var response = await _api.Submit(RequestBundles.Cancel("never-sent"));

        Assert.Equal(HttpStatusCode.BadRequest, response.Status);
        Assert.Equal("No stored authorization found matching the prior Claim identifier", response.ErrorDiagnostics);
    }

    // ----- Updates -----

    [Fact]
    public async Task An_update_replaces_the_services_of_a_pended_request()
    {
        var pended = (await _api.Submit(RequestBundles.ResidentialTreatment(bundleIdentifier: "to-update"))).ClaimResponseId;

        var response = await _api.Submit(RequestBundles.Update("to-update",
            new RequestBundles.ItemChange(RequestBundles.Residential),
            new RequestBundles.ItemChange(RequestBundles.TherapeuticBehavioralServices, Changed: true)));

        Assert.Equal(pended, response.ClaimResponseId);
        Assert.Equal(["A4", "A4"], response.ReviewActionCodes);
        Assert.Equal(["H0019", "H2019"], (await _api.PendedRequest(pended)).ServiceCodes);
    }

    [Fact]
    public async Task An_update_does_not_restart_the_decision_deadline()
    {
        var pended = (await _api.Submit(RequestBundles.ResidentialTreatment(bundleIdentifier: "to-update"))).ClaimResponseId;
        _api.Clock.Advance(TimeSpan.FromHours(5));

        await _api.Submit(RequestBundles.Update("to-update",
            new RequestBundles.ItemChange(RequestBundles.Residential),
            new RequestBundles.ItemChange(RequestBundles.TherapeuticBehavioralServices, Changed: true)));

        Assert.Equal(new DateTimeOffset(2026, 11, 7, 18, 0, 0, TimeSpan.Zero), (await _api.PendedRequest(pended)).DecisionDeadline);
    }

    [Fact]
    public async Task Withdrawing_every_service_under_review_leaves_the_reviewer_nothing_to_decide()
    {
        await _api.Submit(RequestBundles.ResidentialTreatment(bundleIdentifier: "to-update"));

        var response = await _api.Submit(RequestBundles.Update("to-update",
            new RequestBundles.ItemChange(RequestBundles.Residential, Cancelled: true)));

        Assert.Equal(["A2"], response.ReviewActionCodes);
        Assert.Empty(await _api.PendedRequests());
    }

    [Fact]
    public async Task A_denied_request_cannot_be_updated()
    {
        var denied = (await _api.Submit(RequestBundles.ResidentialTreatment(bundleIdentifier: "denied"))).ClaimResponseId;
        await _api.Deny(denied, denialReason: "The assessment does not show a need for a residential level of care.");

        var response = await _api.Submit(RequestBundles.Update("denied",
            new RequestBundles.ItemChange(RequestBundles.Residential, Changed: true)));

        Assert.Equal(HttpStatusCode.BadRequest, response.Status);
        Assert.Equal("Cannot update a denied prior authorization (review action A2)", response.ErrorDiagnostics);
    }

    [Fact]
    public async Task An_approved_request_cannot_be_updated_so_the_provider_sends_a_new_one()
    {
        var approved = (await _api.Submit(RequestBundles.ResidentialTreatment(bundleIdentifier: "approved"))).ClaimResponseId;
        await _api.Approve(approved, authorizationEnds: "2026-12-06");

        var response = await _api.Submit(RequestBundles.Update("approved",
            new RequestBundles.ItemChange(RequestBundles.Residential, Changed: true)));

        Assert.Equal(HttpStatusCode.BadRequest, response.Status);
        Assert.StartsWith("Cannot update an approved prior authorization", response.ErrorDiagnostics);
    }

    // ----- Renewals -----

    [Fact]
    public async Task A_renewal_is_a_new_request_with_its_own_deadline()
    {
        var original = (await _api.Submit(RequestBundles.ResidentialTreatment())).ClaimResponseId;
        await _api.Approve(original, authorizationEnds: "2026-12-06");
        _api.Clock.Advance(TimeSpan.FromDays(20));

        var renewal = await _api.Submit(RequestBundles.Renewal(RequestBundles.Residential));

        Assert.Equal("A4", renewal.ReviewActionCode);
        Assert.NotEqual(original, renewal.ClaimResponseId);
        Assert.Equal(new DateTimeOffset(2026, 11, 27, 18, 0, 0, TimeSpan.Zero), (await _api.PendedRequest(renewal.ClaimResponseId)).DecisionDeadline);
    }
}
