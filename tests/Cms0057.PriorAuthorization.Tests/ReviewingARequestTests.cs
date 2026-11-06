using System.Net;

namespace Cms0057.PriorAuthorization.Tests;

public sealed class ReviewingARequestTests : IAsyncLifetime
{
    private readonly PriorAuthorizationApi _api = new();
    private string _pended = "";

    /// <summary>Every test starts with one residential treatment request Pended in front of the Reviewer.</summary>
    public async Task InitializeAsync() =>
        _pended = (await _api.Submit(RequestBundles.ResidentialTreatment(bundleIdentifier: "under-review"))).ClaimResponseId;

    public Task DisposeAsync()
    {
        _api.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task The_provider_can_ask_about_a_pended_request_and_learn_it_is_still_pended()
    {
        var answer = await _api.Inquire(RequestBundles.Inquiry());

        Assert.Equal("A4", answer.ReviewActionCode);
    }

    [Fact]
    public async Task Asking_about_a_member_with_no_requests_finds_nothing()
    {
        var answer = await _api.Inquire(RequestBundles.Inquiry(memberIdentifier: "90000002B"));

        Assert.False(answer.HasAnswer);
    }

    [Fact]
    public async Task A_provider_that_did_not_send_the_request_cannot_see_it()
    {
        var answer = await _api.Inquire(RequestBundles.Inquiry(providerNpi: "1112223334"));

        Assert.False(answer.HasAnswer);
    }

    [Fact]
    public async Task An_approved_request_is_reported_to_the_provider_as_certified()
    {
        await _api.Approve(_pended, authorizationEnds: "2026-12-06");

        var answer = await _api.Inquire(RequestBundles.Inquiry());
        Assert.Equal("A1", answer.ReviewActionCode);
    }

    [Fact]
    public async Task An_approval_tells_the_provider_when_the_authorization_ends()
    {
        await _api.Approve(_pended, authorizationEnds: "2026-12-06");

        var answer = await _api.Inquire(RequestBundles.Inquiry());
        Assert.Equal("2026-12-06", answer.AuthorizationEnds);
    }

    [Fact]
    public async Task An_approval_gives_the_provider_an_authorization_number()
    {
        await _api.Approve(_pended, authorizationEnds: "2026-12-06");

        var answer = await _api.Inquire(RequestBundles.Inquiry());
        Assert.False(string.IsNullOrWhiteSpace(answer.AuthorizationNumber));
    }

    [Fact]
    public async Task A_pended_request_has_no_authorization_number()
    {
        var answer = await _api.Inquire(RequestBundles.Inquiry());

        Assert.Null(answer.AuthorizationNumber);
    }

    [Fact]
    public async Task Approving_without_saying_when_the_authorization_ends_is_refused()
    {
        var status = await _api.Approve(_pended, authorizationEnds: null);

        Assert.Equal(HttpStatusCode.BadRequest, status);
    }

    [Fact]
    public async Task An_authorization_cannot_end_before_the_day_it_is_approved()
    {
        // The clock stands at 2026-11-06.
        var status = await _api.Approve(_pended, authorizationEnds: "2026-11-05");

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Equal("A4", (await _api.Inquire(RequestBundles.Inquiry())).ReviewActionCode);
    }

    [Fact]
    public async Task An_authorization_may_end_on_the_day_it_is_approved()
    {
        var status = await _api.Approve(_pended, authorizationEnds: "2026-11-06");

        Assert.Equal(HttpStatusCode.NoContent, status);
    }

    [Fact]
    public async Task A_refused_approval_leaves_the_request_pended()
    {
        await _api.Approve(_pended, authorizationEnds: null);

        var answer = await _api.Inquire(RequestBundles.Inquiry());
        Assert.Equal("A4", answer.ReviewActionCode);
    }

    [Fact]
    public async Task A_denied_request_is_reported_to_the_provider_as_not_certified()
    {
        await _api.Deny(_pended, denialReason: "The assessment does not show a need for a residential level of care.");

        var answer = await _api.Inquire(RequestBundles.Inquiry());
        Assert.Equal("A2", answer.ReviewActionCode);
    }

    [Fact]
    public async Task A_denial_tells_the_provider_the_reviewers_reason()
    {
        await _api.Deny(_pended, denialReason: "The assessment does not show a need for a residential level of care.");

        var answer = await _api.Inquire(RequestBundles.Inquiry());
        Assert.Equal("The assessment does not show a need for a residential level of care.", answer.DenialReason);
    }

    [Fact]
    public async Task A_denial_may_carry_one_of_the_plans_coded_reasons_alongside_the_text()
    {
        await _api.Deny(_pended, "The assessment does not show a need for a residential level of care.",
            reasonCode: DenialReasonCodes.NotMedicallyNecessary);

        var answer = await _api.Inquire(RequestBundles.Inquiry());
        Assert.Equal(
            (DenialReasonCodes.NotMedicallyNecessary, "The assessment does not show a need for a residential level of care."),
            answer.DenialReasonCode);
    }

    [Fact]
    public async Task A_denial_without_a_code_still_tells_the_provider_the_reason_in_words()
    {
        await _api.Deny(_pended, "The assessment does not show a need for a residential level of care.");

        var answer = await _api.Inquire(RequestBundles.Inquiry());
        Assert.Equal((null, "The assessment does not show a need for a residential level of care."), answer.DenialReasonCode);
    }

    [Fact]
    public async Task A_reason_code_the_plan_does_not_use_is_refused()
    {
        var status = await _api.Deny(_pended, "Some reason.", reasonCode: "NOT-A-CODE");

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Equal("A4", (await _api.Inquire(RequestBundles.Inquiry())).ReviewActionCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    public async Task Denying_without_a_reason_is_refused(string? noReason)
    {
        var status = await _api.Deny(_pended, denialReason: noReason);

        Assert.Equal(HttpStatusCode.BadRequest, status);
    }

    [Fact]
    public async Task A_decided_request_leaves_the_reviewers_list()
    {
        await _api.Approve(_pended, authorizationEnds: "2026-12-06");

        Assert.Empty(await _api.PendedRequests());
    }

    [Fact]
    public async Task A_decision_cannot_be_changed_by_deciding_again()
    {
        await _api.Approve(_pended, authorizationEnds: "2026-12-06");

        var status = await _api.Deny(_pended, denialReason: "Changed my mind.");

        Assert.Equal(HttpStatusCode.Conflict, status);
    }

    [Fact]
    public async Task Deciding_a_request_that_does_not_exist_is_not_found()
    {
        var status = await _api.Approve(Guid.NewGuid().ToString(), authorizationEnds: "2026-12-06");

        Assert.Equal(HttpStatusCode.NotFound, status);
    }

    [Fact]
    public async Task A_retry_of_a_decided_request_returns_the_decision()
    {
        await _api.Approve(_pended, authorizationEnds: "2026-12-06");

        var retry = await _api.Submit(RequestBundles.ResidentialTreatment(bundleIdentifier: "under-review"));

        Assert.Equal("A1", retry.ReviewActionCode);
    }

    [Fact]
    public async Task A_request_is_not_overdue_while_its_deadline_has_not_passed()
    {
        _api.Clock.Advance(TimeSpan.FromHours(24));

        Assert.False((await _api.PendedRequest(_pended)).Overdue);
    }

    [Fact]
    public async Task A_pended_request_past_its_deadline_is_marked_overdue()
    {
        _api.Clock.Advance(TimeSpan.FromHours(24) + TimeSpan.FromSeconds(1));

        Assert.True((await _api.PendedRequest(_pended)).Overdue);
    }

    [Fact]
    public async Task An_overdue_request_is_not_decided_automatically()
    {
        _api.Clock.Advance(TimeSpan.FromHours(48));

        var answer = await _api.Inquire(RequestBundles.Inquiry());
        Assert.Equal("A4", answer.ReviewActionCode);
    }
}
