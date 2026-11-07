using System.Net;

namespace Cms0057.PriorAuthorization.Tests;

/// <summary>
/// The Final Rule has the Patient Access API show a Member their prior authorization information: the status, the
/// date approved or denied, when it ends, the services approved and the Denial Reason (42 CFR 422.119(b)(1)(iv),
/// which 438.242(b)(5) applies to Medicaid managed care). The agency's Patient Access API reads it from here as
/// Da Vinci PDex Prior Authorization ExplanationOfBenefit resources, one per Prior Authorization Request.
/// </summary>
public sealed class ServingThePatientAccessApiTests : IAsyncLifetime
{
    private const string PriorAuthorizationProfile = "http://hl7.org/fhir/us/davinci-pdex/StructureDefinition/pdex-priorauthorization";

    private readonly PriorAuthorizationApi _api = new();
    private string _pended = "";

    /// <summary>Every test starts with one residential treatment request Pended for the known Member.</summary>
    public async Task InitializeAsync() =>
        _pended = (await _api.Submit(RequestBundles.ResidentialTreatment(bundleIdentifier: "for-the-member"))).ClaimResponseId;

    public Task DisposeAsync()
    {
        _api.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task A_members_pended_request_is_shown_as_a_prior_authorization_awaiting_decision()
    {
        var answer = await _api.PriorAuthorizations("patient=" + RequestBundles.KnownMember);

        Assert.Equal(HttpStatusCode.OK, answer.Status);
        Assert.Equal("searchset", answer.BundleType);
        Assert.Equal(1, answer.Total);
        var information = answer.Single;
        Assert.Equal(PriorAuthorizationProfile, information.Profile);
        Assert.Equal("preauthorization", information.Use);
        Assert.Equal("Patient/" + RequestBundles.KnownMember, information.Patient);
        Assert.Equal("A4", information.ReviewActionCode);
        Assert.Equal("queued", information.Outcome);
        Assert.Equal(["H0019 U1"], information.Services);
    }

    [Fact]
    public async Task An_approval_shows_the_date_decided_the_authorization_number_and_when_it_ends()
    {
        _api.Clock.Advance(TimeSpan.FromHours(2));
        await _api.Approve(_pended, authorizationEnds: "2026-12-06");

        var information = (await _api.PriorAuthorizations("patient=" + RequestBundles.KnownMember)).Single;
        Assert.Equal("A1", information.ReviewActionCode);
        Assert.Equal("complete", information.Outcome);
        Assert.False(string.IsNullOrWhiteSpace(information.AuthorizationNumber));
        Assert.StartsWith("2026-11-06T20:00:00", information.DecidedAt);
        Assert.Equal(("2026-11-06", "2026-12-06"), information.AuthorizationPeriod);
    }

    [Fact]
    public async Task A_denial_shows_the_specific_reason_in_words_and_the_plans_code_for_it()
    {
        await _api.Deny(_pended, "No ASAM Criteria assessment supports level 3.1", DenialReasonCodes.NotMedicallyNecessary);

        var information = (await _api.PriorAuthorizations("patient=" + RequestBundles.KnownMember)).Single;
        Assert.Equal("A2", information.ReviewActionCode);
        Assert.Equal((DenialReasonCodes.NotMedicallyNecessary, "No ASAM Criteria assessment supports level 3.1"), information.DenialReason);
        Assert.Contains("No ASAM Criteria assessment supports level 3.1", information.ProcessNotes);
    }

    [Fact]
    public async Task A_request_the_provider_withdrew_is_shown_as_cancelled()
    {
        await _api.Submit(RequestBundles.Cancel("for-the-member"));

        var information = (await _api.PriorAuthorizations("patient=" + RequestBundles.KnownMember)).Single;
        Assert.Equal("cancelled", information.ResourceStatus);
        Assert.Equal("A2", information.ReviewActionCode);
    }

    [Fact]
    public async Task A_service_that_needed_no_authorization_is_shown_as_not_required()
    {
        await _api.Submit(RequestBundles.OutpatientCounseling());

        var answer = await _api.PriorAuthorizations("patient=" + RequestBundles.KnownMember);
        var counseling = answer.PriorAuthorizations.Single(i => i.Services.Contains("H0004 U7"));
        Assert.Equal("A3", counseling.ReviewActionCode);
        Assert.Equal("complete", counseling.Outcome);
    }

    [Fact]
    public async Task Each_service_of_a_request_shows_its_own_answer()
    {
        await _api.Submit(RequestBundles.ForServices(RequestBundles.Residential, RequestBundles.Counseling));

        var answer = await _api.PriorAuthorizations("patient=" + RequestBundles.KnownMember);
        var both = answer.PriorAuthorizations.Single(i => i.Services.Count == 2);
        Assert.Equal(["A4", "A3"], both.ItemReviewActionCodes);
    }

    [Fact]
    public async Task A_member_sees_only_their_own_requests()
    {
        var answer = await _api.PriorAuthorizations("patient=90000002B");

        Assert.Equal(HttpStatusCode.OK, answer.Status);
        Assert.Equal(0, answer.Total);
    }

    [Fact]
    public async Task The_search_must_say_which_member()
    {
        var answer = await _api.PriorAuthorizations("use=preauthorization");

        Assert.Equal(HttpStatusCode.BadRequest, answer.Status);
        Assert.Contains("patient", answer.ErrorDiagnostics);
    }

    [Fact]
    public async Task One_prior_authorization_can_be_read_by_its_id()
    {
        var id = (await _api.PriorAuthorizations("patient=" + RequestBundles.KnownMember)).Single.Id;

        var read = await _api.PriorAuthorization(id);
        Assert.Equal(HttpStatusCode.OK, read.Status);
        Assert.Equal(id, read.Single.Id);
        Assert.Equal(PriorAuthorizationProfile, read.Single.Profile);
    }

    [Fact]
    public async Task Reading_an_id_the_plan_does_not_hold_is_not_found()
    {
        var read = await _api.PriorAuthorization(Guid.NewGuid().ToString());

        Assert.Equal(HttpStatusCode.NotFound, read.Status);
    }

    [Fact]
    public async Task A_provider_the_member_is_not_attributed_to_cannot_read_their_prior_authorizations()
    {
        var answer = await _api.PriorAuthorizations("patient=" + RequestBundles.KnownMember, Caller.As(_api.OtherProviderSystem));

        Assert.Equal(HttpStatusCode.Forbidden, answer.Status);
    }

    [Fact]
    public async Task The_information_is_stamped_with_its_last_status_change()
    {
        var received = (await _api.PriorAuthorizations("patient=" + RequestBundles.KnownMember)).Single.LastUpdated;
        _api.Clock.Advance(TimeSpan.FromHours(2));
        await _api.Approve(_pended, authorizationEnds: "2026-12-06");

        var decided = (await _api.PriorAuthorizations("patient=" + RequestBundles.KnownMember)).Single.LastUpdated;
        Assert.StartsWith("2026-11-06T18:00:00", received);
        Assert.StartsWith("2026-11-06T20:00:00", decided);
    }

    [Fact]
    public async Task The_patient_access_api_can_ask_only_for_what_changed_since_it_last_looked()
    {
        await _api.Submit(RequestBundles.OutpatientCounseling());
        _api.Clock.Advance(TimeSpan.FromHours(2));
        await _api.Approve(_pended, authorizationEnds: "2026-12-06");

        var changed = await _api.PriorAuthorizations($"patient={RequestBundles.KnownMember}&_lastUpdated=ge2026-11-06T19:00:00Z");
        Assert.Equal(1, changed.Total);
        Assert.Equal("A1", changed.Single.ReviewActionCode);
    }
}
