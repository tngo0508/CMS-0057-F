using System.Net;
using System.Text.Json;

namespace Cms0057.PriorAuthorization.Tests;

/// <summary>
/// The FHIR server describes itself: a CapabilityStatement at the FHIR base, which every FHIR server has and which
/// a client such as Da Vinci's br-provider reads to see whether the payer is there and what it offers.
/// </summary>
public sealed class DescribingTheServerTests : IDisposable
{
    private readonly PriorAuthorizationApi _api = new();

    public void Dispose() => _api.Dispose();

    [Fact]
    public async Task The_capability_statement_is_public_and_names_the_PAS_operations()
    {
        var (status, contentType, body) = await _api.CapabilityStatement();

        var statement = JsonDocument.Parse(body).RootElement;
        var claim = Resources(statement).Single(r => r.GetProperty("type").GetString() == "Claim");
        Assert.Equal((HttpStatusCode.OK, "application/fhir+json"), (status, contentType));
        Assert.Equal(("CapabilityStatement", "4.0.1"),
            (statement.GetProperty("resourceType").GetString(), statement.GetProperty("fhirVersion").GetString()));
        Assert.Equal(["submit", "inquire"],
            claim.GetProperty("operation").EnumerateArray().Select(o => o.GetProperty("name").GetString()));
    }

    [Fact]
    public async Task The_capability_statement_says_the_server_is_SMART_secured_and_where_the_token_endpoint_is()
    {
        var (_, _, body) = await _api.CapabilityStatement();

        var security = JsonDocument.Parse(body).RootElement.GetProperty("rest")[0].GetProperty("security");
        var service = security.GetProperty("service")[0].GetProperty("coding")[0].GetProperty("code").GetString();
        var oauthUris = security.GetProperty("extension").EnumerateArray()
            .Single(e => e.GetProperty("url").GetString() == "http://fhir-registry.smarthealthit.org/StructureDefinition/oauth-uris");
        var token = oauthUris.GetProperty("extension").EnumerateArray()
            .Single(e => e.GetProperty("url").GetString() == "token").GetProperty("valueUri").GetString();
        Assert.Equal(("SMART-on-FHIR", "http://localhost/auth/token"), (service, token));
    }

    [Fact]
    public async Task The_capability_statement_names_the_prior_authorization_information_a_patient_access_api_reads()
    {
        var (_, _, body) = await _api.CapabilityStatement();

        var eob = Resources(JsonDocument.Parse(body).RootElement).Single(r => r.GetProperty("type").GetString() == "ExplanationOfBenefit");
        Assert.Equal(["http://hl7.org/fhir/us/davinci-pdex/StructureDefinition/pdex-priorauthorization"],
            eob.GetProperty("supportedProfile").EnumerateArray().Select(p => p.GetString()));
        Assert.Equal(["read", "search-type"], eob.GetProperty("interaction").EnumerateArray().Select(i => i.GetProperty("code").GetString()));
        Assert.Equal(["patient", "_lastUpdated"], eob.GetProperty("searchParam").EnumerateArray().Select(p => p.GetProperty("name").GetString()));
    }

    private static IEnumerable<JsonElement> Resources(JsonElement statement) =>
        statement.GetProperty("rest")[0].GetProperty("resource").EnumerateArray();
}
