using System.Net;
using System.Text.Json;

namespace Cms0057.PriorAuthorization.Tests;

/// <summary>
/// The Final Rule's Payer-to-Payer API (42 CFR 422.121(b), applied to Medicaid managed care through 438.242(b)(7)):
/// when a Member who has opted in moves to or also holds coverage with another Impacted Payer, that payer may
/// retrieve the Member's information here, for dates of service within five years, excluding denied prior
/// authorizations. Here the other payer finds the Member with HRex $member-match, sees the matched Members as a
/// PDex Member Match Group, and reads or exports their prior authorization information.
/// </summary>
public sealed class ServingThePayerToPayerApiTests : IAsyncLifetime
{
    private const string MemberMatchGroupProfile = "http://hl7.org/fhir/us/davinci-pdex/StructureDefinition/pdex-member-match-group";
    private static readonly string GroupId = "payer-" + PriorAuthorizationApi.OtherPayerIdentifier;
    private const string Member = RequestBundles.KnownMember;

    private readonly PriorAuthorizationApi _api = new();
    private string _pended = "";

    private Caller OtherPayer => Caller.As(_api.OtherPayer);

    /// <summary>Every test starts with one request for the known Member, who has opted in to Payer-to-Payer exchange.</summary>
    public async Task InitializeAsync()
    {
        _pended = (await _api.Submit(RequestBundles.ResidentialTreatment(bundleIdentifier: "treating-the-member"))).ClaimResponseId;
        Assert.Equal(HttpStatusCode.NoContent, await _api.SetPayerToPayerOptIn(Member, optedIn: true));
    }

    public Task DisposeAsync()
    {
        _api.Dispose();
        return Task.CompletedTask;
    }

    private Task<MemberMatchResponse> Match(string member = Member, string family = "EXAMPLE", string given = "ALEX", bool withConsent = true, string consentEnds = "2027-11-06") =>
        _api.MemberMatch(MemberMatchRequests.For(member, family, given, withConsent, consentEnds), OtherPayer);

    [Fact]
    public async Task A_payer_matches_an_opted_in_member_and_learns_the_member_identifier()
    {
        var match = await Match();

        Assert.Equal(HttpStatusCode.OK, match.Status);
        Assert.Equal("Parameters", match.ResourceType);
        Assert.Equal((Member, "UMB", "Patient/" + Member), (match.MemberIdentifier, match.MemberIdentifierType, match.MemberId));
    }

    [Fact]
    public async Task A_member_who_has_not_opted_in_is_not_matched()
    {
        var match = await Match("90000002B", "SAMPLE", "JORDAN");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, match.Status);
        Assert.Contains("opted in", match.ErrorDiagnostics);
    }

    [Fact]
    public async Task A_match_request_without_the_members_consent_is_refused()
    {
        var match = await Match(withConsent: false);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, match.Status);
        Assert.Contains("Consent", match.ErrorDiagnostics);
    }

    [Fact]
    public async Task A_consent_whose_period_has_ended_is_refused()
    {
        var match = await Match(consentEnds: "2026-11-01");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, match.Status);
        Assert.Contains("period has ended", match.ErrorDiagnostics);
    }

    [Fact]
    public async Task Nobody_the_plan_covers_is_no_match()
    {
        var match = await Match("NOBODY", "EXAMPLE", "ALEX");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, match.Status);
        Assert.Contains("No Member matches", match.ErrorDiagnostics);
    }

    [Fact]
    public async Task A_name_that_does_not_go_with_the_identifier_is_no_match()
    {
        var match = await Match(Member, "OTHER", "PERSON");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, match.Status);
    }

    [Fact]
    public async Task A_provider_system_cannot_match_members()
    {
        var match = await _api.MemberMatch(MemberMatchRequests.For(Member, "EXAMPLE", "ALEX"), Caller.As(_api.ProviderSystem));

        Assert.Equal(HttpStatusCode.Forbidden, match.Status);
    }

    [Fact]
    public async Task A_payer_sees_the_members_it_has_matched_as_a_group()
    {
        await Match();

        var groups = await _api.Groups(OtherPayer);

        Assert.Equal(1, groups.Total);
        var group = groups.Single;
        Assert.Equal((GroupId, MemberMatchGroupProfile, "match"), (group.Id, group.Profile, group.Code));
        Assert.Equal(["Patient/" + Member], group.Members);
        Assert.Equal(["Patient/" + Member], (await _api.Group(GroupId, OtherPayer)).Single.Members);
    }

    [Fact]
    public async Task A_payer_that_has_matched_nobody_sees_no_group_and_no_member()
    {
        Assert.Equal(0, (await _api.Groups(OtherPayer)).Total);
        Assert.Equal(HttpStatusCode.NotFound, (await _api.Group(GroupId, OtherPayer)).Status);
        Assert.Equal(HttpStatusCode.Forbidden, (await _api.PriorAuthorizations("patient=" + Member, OtherPayer)).Status);
    }

    [Fact]
    public async Task A_payer_reads_a_matched_members_prior_authorization_information()
    {
        await Match();

        var found = await _api.PriorAuthorizations("patient=" + Member, OtherPayer);

        Assert.Equal(HttpStatusCode.OK, found.Status);
        Assert.Equal(_pended, found.Single.Id);
        Assert.Equal(HttpStatusCode.OK, (await _api.PriorAuthorization(_pended, OtherPayer)).Status);
    }

    [Fact]
    public async Task Denied_prior_authorizations_are_not_shared_with_another_payer()
    {
        await _api.Deny(_pended, "Not medically necessary at this level of care");
        await Match();

        Assert.Equal(0, (await _api.PriorAuthorizations("patient=" + Member, OtherPayer)).Total);
        Assert.Equal(HttpStatusCode.Forbidden, (await _api.PriorAuthorization(_pended, OtherPayer)).Status);
    }

    [Fact]
    public async Task Prior_authorizations_no_longer_active_whose_status_last_changed_over_a_year_ago_are_not_shared()
    {
        await _api.Approve(_pended, authorizationEnds: "2026-12-06");
        _api.Clock.Advance(TimeSpan.FromDays(400));
        var recent = (await _api.Submit(RequestBundles.ResidentialTreatment(bundleIdentifier: "a-year-later"))).ClaimResponseId;
        await Match(consentEnds: "2028-12-31");

        var shared = await _api.PriorAuthorizations("patient=" + Member, OtherPayer);

        Assert.Equal([recent], shared.PriorAuthorizations.Select(p => p.Id));
    }

    [Fact]
    public async Task A_member_who_withdraws_the_opt_in_leaves_the_group_and_is_no_longer_shared()
    {
        await Match();

        Assert.Equal(HttpStatusCode.NoContent, await _api.SetPayerToPayerOptIn(Member, optedIn: false));

        Assert.Empty((await _api.Group(GroupId, OtherPayer)).Single.Members);
        Assert.Equal(HttpStatusCode.Forbidden, (await _api.PriorAuthorizations("patient=" + Member, OtherPayer)).Status);
    }

    [Fact]
    public async Task An_export_of_the_group_delivers_the_matched_members_prior_authorizations()
    {
        await Match();

        var started = await _api.StartExport(GroupId, caller: OtherPayer);
        Assert.Equal(HttpStatusCode.Accepted, started.Status);
        var manifest = await _api.ExportStatus(started.ContentLocation!, OtherPayer);
        var (type, url, count) = manifest.Output.Single();
        var file = await _api.ExportFile(url, OtherPayer);

        Assert.Equal(("ExplanationOfBenefit", 1), (type, count));
        Assert.Equal(_pended, JsonDocument.Parse(file.Body.TrimEnd()).RootElement.GetProperty("id").GetString());
    }

    [Fact]
    public async Task Everything_about_a_matched_member_is_the_member_and_their_prior_authorization_information()
    {
        await Match();

        var everything = await _api.Everything(Member, OtherPayer);

        Assert.Equal(HttpStatusCode.OK, everything.Status);
        Assert.Equal("searchset", everything.BundleType);
        Assert.Equal(["Patient", "ExplanationOfBenefit"], everything.ResourceTypes);
        Assert.Equal(_pended, everything.Single.Id);
    }

    [Fact]
    public async Task Everything_about_a_member_the_payer_has_not_matched_is_forbidden()
    {
        Assert.Equal(HttpStatusCode.Forbidden, (await _api.Everything(Member, OtherPayer)).Status);
    }
}
