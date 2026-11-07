using System.Net;
using System.Text.Json;

namespace Cms0057.PriorAuthorization.Tests;

/// <summary>
/// The Final Rule's Provider Access API (42 CFR 422.121(a), applied to Medicaid managed care through 438.242(b)(7)):
/// an in-network provider the Member is attributed to may retrieve the Member's prior authorization information,
/// within one business day of asking, unless the Member has opted out. Here a provider system sees its Attribution
/// List as a PDex Provider Group, reads one Member's prior authorizations, or exports the whole list's with Bulk Data.
/// </summary>
public sealed class ServingTheProviderAccessApiTests : IAsyncLifetime
{
    private const string ProviderGroupProfile = "http://hl7.org/fhir/us/davinci-pdex/StructureDefinition/pdex-provider-group";
    private static readonly string ListId = "provider-" + RequestBundles.TreatmentProviderNpi;

    private readonly PriorAuthorizationApi _api = new();
    private string _pended = "";

    /// <summary>Every test starts with the treatment provider having sent one request for the known Member.</summary>
    public async Task InitializeAsync() =>
        _pended = (await _api.Submit(RequestBundles.ResidentialTreatment(bundleIdentifier: "treating-the-member"))).ClaimResponseId;

    public Task DisposeAsync()
    {
        _api.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task A_provider_that_sent_a_request_for_a_member_finds_the_member_on_its_attribution_list()
    {
        var lists = await _api.Groups();

        Assert.Equal(HttpStatusCode.OK, lists.Status);
        Assert.Equal(1, lists.Total);
        var list = lists.Single;
        Assert.Equal(ListId, list.Id);
        Assert.Equal(ProviderGroupProfile, list.Profile);
        Assert.Equal(("person", true, RequestBundles.TreatmentProviderNpi, "pdexprovidergroup"), (list.Type, list.Actual, list.Npi, list.Code));
        Assert.Equal(["Patient/" + RequestBundles.KnownMember], list.Members);
    }

    [Fact]
    public async Task A_member_who_opts_out_of_provider_access_leaves_the_list_and_returns_on_opting_back_in()
    {
        Assert.Equal(HttpStatusCode.NoContent, await _api.SetProviderAccessOptOut(RequestBundles.KnownMember, optedOut: true));
        Assert.Empty((await _api.Group(ListId)).Single.Members);

        Assert.Equal(HttpStatusCode.NoContent, await _api.SetProviderAccessOptOut(RequestBundles.KnownMember, optedOut: false));
        Assert.Equal(["Patient/" + RequestBundles.KnownMember], (await _api.Group(ListId)).Single.Members);
    }

    [Fact]
    public async Task The_plan_can_attribute_a_member_to_a_provider_itself()
    {
        Assert.Equal(HttpStatusCode.NoContent, await _api.Attribute("90000002B", RequestBundles.TreatmentProviderNpi));

        Assert.Equal(["Patient/" + RequestBundles.KnownMember, "Patient/90000002B"], (await _api.Group(ListId)).Single.Members);
    }

    [Fact]
    public async Task Attributing_someone_who_is_not_a_member_is_refused()
    {
        Assert.Equal(HttpStatusCode.NotFound, await _api.Attribute("NOBODY", RequestBundles.TreatmentProviderNpi));
    }

    [Fact]
    public async Task A_provider_sees_no_list_but_its_own()
    {
        var other = Caller.As(_api.OtherProviderSystem);

        Assert.Equal(0, (await _api.Groups(other)).Total);
        Assert.Equal(HttpStatusCode.NotFound, (await _api.Group(ListId, other)).Status);
    }

    [Fact]
    public async Task An_export_of_the_list_delivers_its_members_prior_authorizations()
    {
        var started = await _api.StartExport(ListId);
        Assert.Equal(HttpStatusCode.Accepted, started.Status);
        Assert.NotNull(started.ContentLocation);

        var manifest = await _api.ExportStatus(started.ContentLocation!);
        Assert.Equal(HttpStatusCode.OK, manifest.Status);
        Assert.True(manifest.RequiresAccessToken);
        var (type, url, count) = Assert.Single(manifest.Output);
        Assert.Equal(("ExplanationOfBenefit", 1), (type, count));

        var (status, contentType, body) = await _api.ExportFile(url);
        Assert.Equal((HttpStatusCode.OK, "application/fhir+ndjson"), (status, contentType));
        var information = new PriorAuthorizationInformation(JsonDocument.Parse(Assert.Single(Lines(body))).RootElement);
        Assert.Equal((_pended, "Patient/" + RequestBundles.KnownMember, "A4"), (information.Id, information.Patient, information.ReviewActionCode));
    }

    [Fact]
    public async Task An_export_since_a_time_delivers_only_what_changed_since()
    {
        await _api.Submit(RequestBundles.OutpatientCounseling());
        _api.Clock.Advance(TimeSpan.FromHours(2));
        await _api.Approve(_pended, authorizationEnds: "2026-12-06");

        var started = await _api.StartExport(ListId, "_since=2026-11-06T19:00:00Z");
        var manifest = await _api.ExportStatus(started.ContentLocation!);
        var (_, url, count) = Assert.Single(manifest.Output);
        var (_, _, body) = await _api.ExportFile(url);
        Assert.Equal(1, count);
        Assert.Equal("A1", new PriorAuthorizationInformation(JsonDocument.Parse(Assert.Single(Lines(body))).RootElement).ReviewActionCode);
    }

    [Fact]
    public async Task An_opted_out_members_prior_authorizations_are_not_exported()
    {
        await _api.SetProviderAccessOptOut(RequestBundles.KnownMember, optedOut: true);

        var started = await _api.StartExport(ListId);
        var manifest = await _api.ExportStatus(started.ContentLocation!);
        Assert.Equal(0, manifest.Output.Sum(o => o.Count));
    }

    [Fact]
    public async Task Another_provider_cannot_export_a_list_that_is_not_its_own()
    {
        var started = await _api.StartExport(ListId, caller: Caller.As(_api.OtherProviderSystem));

        Assert.Equal(HttpStatusCode.NotFound, started.Status);
    }

    [Fact]
    public async Task An_export_in_a_format_the_service_does_not_write_is_refused()
    {
        var started = await _api.StartExport(ListId, "_outputFormat=application/fhir%2Bxml");

        Assert.Equal(HttpStatusCode.BadRequest, started.Status);
    }

    [Fact]
    public async Task A_provider_may_read_the_prior_authorizations_of_a_member_attributed_to_it()
    {
        var answer = await _api.PriorAuthorizations("patient=" + RequestBundles.KnownMember, Caller.As(_api.ProviderSystem));

        Assert.Equal(HttpStatusCode.OK, answer.Status);
        Assert.Equal(1, answer.Total);
    }

    [Fact]
    public async Task A_provider_may_not_read_those_of_a_member_not_attributed_to_it()
    {
        var answer = await _api.PriorAuthorizations("patient=" + RequestBundles.KnownMember, Caller.As(_api.OtherProviderSystem));

        Assert.Equal(HttpStatusCode.Forbidden, answer.Status);
    }

    [Fact]
    public async Task A_provider_may_not_read_those_of_a_member_who_opted_out_but_the_patient_access_api_still_may()
    {
        await _api.SetProviderAccessOptOut(RequestBundles.KnownMember, optedOut: true);

        Assert.Equal(HttpStatusCode.Forbidden, (await _api.PriorAuthorizations("patient=" + RequestBundles.KnownMember, Caller.As(_api.ProviderSystem))).Status);
        Assert.Equal(1, (await _api.PriorAuthorizations("patient=" + RequestBundles.KnownMember, Caller.As(_api.PatientAccessApi))).Total);
    }

    private static string[] Lines(string ndjson) => ndjson.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
