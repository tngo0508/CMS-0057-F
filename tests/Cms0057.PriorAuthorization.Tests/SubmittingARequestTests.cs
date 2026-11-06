using System.Net;

namespace Cms0057.PriorAuthorization.Tests;

public sealed class SubmittingARequestTests : IDisposable
{
    private readonly PriorAuthorizationApi _api = new();

    public void Dispose() => _api.Dispose();

    [Fact]
    public async Task A_residential_treatment_request_is_pended()
    {
        var response = await _api.Submit(RequestBundles.ResidentialTreatment());

        Assert.Equal("A4", response.ReviewActionCode);
    }

    [Fact]
    public async Task The_response_carries_a_tracking_identifier_that_stays_the_same_for_the_request()
    {
        // br-provider files its copy of a ClaimResponse under identifier[0].value, and keeps none without one;
        // as in br-payer the identifier is the payer's, under the trace number system, and it never changes.
        var submitted = await _api.Submit(RequestBundles.ResidentialTreatment());
        var inquired = await _api.Inquire(RequestBundles.Inquiry());

        Assert.Equal("http://example.org/PATIENT_EVENT_TRACE_NUMBER", submitted.TrackingIdentifier.System);
        Assert.False(string.IsNullOrEmpty(submitted.TrackingIdentifier.Value));
        Assert.Equal(submitted.TrackingIdentifier, inquired.TrackingIdentifier);
    }

    [Fact]
    public async Task A_pended_residential_request_must_be_decided_within_24_hours_of_receipt()
    {
        var response = await _api.Submit(RequestBundles.ResidentialTreatment());

        var pended = await _api.PendedRequest(response.ClaimResponseId);
        Assert.Equal(new DateTimeOffset(2026, 11, 7, 18, 0, 0, TimeSpan.Zero), pended.DecisionDeadline);
    }

    [Fact]
    public async Task A_service_the_plan_does_not_prior_authorize_is_answered_not_required()
    {
        var response = await _api.Submit(RequestBundles.OutpatientCounseling());

        Assert.Equal("A3", response.ReviewActionCode);
    }

    [Fact]
    public async Task A_not_required_request_never_reaches_the_reviewer()
    {
        await _api.Submit(RequestBundles.OutpatientCounseling());

        Assert.Empty(await _api.PendedRequests());
    }

    [Fact]
    public async Task Outpatient_counseling_carrying_a_residential_modifier_is_still_not_required()
    {
        var response = await _api.Submit(RequestBundles.OutpatientCounseling(modifier: "U1"));

        Assert.Equal("A3", response.ReviewActionCode);
    }

    [Fact]
    public async Task A_request_for_someone_who_is_not_a_member_is_rejected_as_an_error()
    {
        var response = await _api.Submit(RequestBundles.ResidentialTreatment(memberIdentifier: "00000000Z"));

        Assert.Equal(HttpStatusCode.BadRequest, response.Status);
        Assert.Equal("OperationOutcome", response.ResourceType);
    }

    [Fact]
    public async Task A_rejected_request_is_not_recorded_for_review()
    {
        await _api.Submit(RequestBundles.ResidentialTreatment(memberIdentifier: "00000000Z"));

        Assert.Empty(await _api.PendedRequests());
    }

    [Fact]
    public async Task A_bundle_that_is_not_a_prior_authorization_request_is_rejected_as_an_error()
    {
        var response = await _api.Submit(RequestBundles.ResidentialTreatment(claimUse: "claim"));

        Assert.Equal(HttpStatusCode.BadRequest, response.Status);
        Assert.Equal("Claim.use must be 'preauthorization', got: claim", response.ErrorDiagnostics);
    }

    [Fact]
    public async Task A_body_that_is_not_fhir_is_rejected_as_an_error()
    {
        var response = await _api.Submit("""{ "hello": "world" }""");

        Assert.Equal(HttpStatusCode.BadRequest, response.Status);
        Assert.Equal("OperationOutcome", response.ResourceType);
    }

    [Fact]
    public async Task Sending_the_same_request_twice_returns_the_original_answer()
    {
        var bundle = RequestBundles.ResidentialTreatment(bundleIdentifier: "retry-1");

        var first = await _api.Submit(bundle);
        var second = await _api.Submit(bundle);

        Assert.Equal(first.ClaimResponseId, second.ClaimResponseId);
    }

    [Fact]
    public async Task Sending_the_same_request_twice_puts_one_item_in_front_of_the_reviewer()
    {
        var bundle = RequestBundles.ResidentialTreatment(bundleIdentifier: "retry-2");

        await _api.Submit(bundle);
        await _api.Submit(bundle);

        Assert.Single(await _api.PendedRequests());
    }

    [Fact]
    public async Task A_retry_does_not_restart_the_decision_deadline()
    {
        var bundle = RequestBundles.ResidentialTreatment(bundleIdentifier: "retry-3");
        var first = await _api.Submit(bundle);
        _api.Clock.Advance(TimeSpan.FromHours(5));

        await _api.Submit(bundle);

        var pended = await _api.PendedRequest(first.ClaimResponseId);
        Assert.Equal(new DateTimeOffset(2026, 11, 7, 18, 0, 0, TimeSpan.Zero), pended.DecisionDeadline);
    }
}
