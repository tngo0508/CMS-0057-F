using System.Net;

namespace Cms0057.PriorAuthorization.Tests;

/// <summary>
/// After br-payer's structural checks, a request is validated against the Da Vinci PAS 2.2.1 profiles and the
/// profiles they build on (US Core among them). A Bundle that breaks one is rejected with the validator's findings.
/// </summary>
public sealed class ConformingToThePasProfilesTests : IDisposable
{
    private readonly PriorAuthorizationApi _api = new();

    public void Dispose() => _api.Dispose();

    [Fact]
    public async Task A_request_that_breaks_a_profile_constraint_is_rejected_with_the_validators_finding()
    {
        // PAS's beneficiary is a US Core Patient, which must state a gender; br-payer's checks never look at it.
        var response = await _api.Submit(RequestBundles.ResidentialTreatment(withPatientGender: false));

        Assert.Equal(HttpStatusCode.BadRequest, response.Status);
        Assert.Equal("OperationOutcome", response.ResourceType);
        Assert.Contains(response.ErrorIssues, issue => issue.Contains("gender"));
    }

    [Fact]
    public async Task A_rejected_request_is_not_recorded_for_review()
    {
        await _api.Submit(RequestBundles.ResidentialTreatment(withPatientGender: false));

        Assert.Empty(await _api.PendedRequests());
    }

    [Fact]
    public async Task An_inquiry_that_breaks_a_profile_constraint_is_rejected_with_the_validators_finding()
    {
        var response = await _api.Inquire(RequestBundles.Inquiry(withPatientGender: false));

        Assert.Equal(HttpStatusCode.BadRequest, response.Status);
        Assert.Contains(response.ErrorIssues, issue => issue.Contains("gender"));
    }

    [Fact]
    public async Task The_structural_checks_still_answer_first_in_br_payers_words()
    {
        var response = await _api.Submit(RequestBundles.ResidentialTreatment(claimUse: "claim", withPatientGender: false));

        Assert.Equal("Claim.use must be 'preauthorization', got: claim", response.ErrorDiagnostics);
    }

    [Fact]
    public async Task Profile_validation_can_be_switched_off()
    {
        using var api = new PriorAuthorizationApi(validateProfiles: false);

        var response = await api.Submit(RequestBundles.ResidentialTreatment(withPatientGender: false));

        Assert.Equal("A4", response.ReviewActionCode);
    }
}
