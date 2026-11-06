using System.Net;
using System.Text.Json;

namespace Cms0057.PriorAuthorization.Tests;

/// <summary>
/// SMART App Launch Backend Services: how a provider system (or an EHR) proves who it is before it may call the
/// Prior Authorization API. The system is registered with its public key; it signs a short-lived JWT with the
/// matching private key, trades it for an access token, and sends that token as a bearer token.
/// </summary>
public sealed class AuthenticatingAProviderSystemTests : IDisposable
{
    private readonly PriorAuthorizationApi _api = new();

    public void Dispose() => _api.Dispose();

    [Fact]
    public async Task The_service_advertises_where_and_how_to_obtain_a_token()
    {
        var configuration = JsonDocument.Parse(await _api.SmartConfiguration()).RootElement;

        Assert.Equal("http://localhost/auth/token", configuration.GetProperty("token_endpoint").GetString());
        Assert.Equal(["private_key_jwt"], Strings(configuration, "token_endpoint_auth_methods_supported"));
        Assert.Equal(["RS384", "ES384"], Strings(configuration, "token_endpoint_auth_signing_alg_values_supported"));
        Assert.Equal(["client-confidential-asymmetric"], Strings(configuration, "capabilities"));
        Assert.Contains("system/Claim.write", Strings(configuration, "scopes_supported"));
    }

    [Fact]
    public async Task A_registered_system_that_signs_with_its_key_is_given_an_access_token()
    {
        var token = await _api.RequestToken(_api.ProviderSystem.ClientAssertion(Now));

        Assert.Equal((HttpStatusCode.OK, "bearer"), (token.Status, token.TokenType?.ToLowerInvariant()));
        Assert.False(string.IsNullOrEmpty(token.AccessToken));
        Assert.InRange(token.ExpiresIn!.Value, 1, 300);
    }

    [Fact]
    public async Task A_system_that_publishes_its_keys_at_a_url_is_authenticated_from_there()
    {
        var token = await _api.RequestToken(_api.Ehr.ClientAssertion(Now), scope: _api.Ehr.Scopes);

        Assert.Equal(HttpStatusCode.OK, token.Status);
    }

    [Fact]
    public async Task An_assertion_signed_with_a_key_the_plan_does_not_hold_is_refused()
    {
        var token = await _api.RequestToken(_api.ProviderSystem.ClientAssertion(Now, signedWith: RegisteredSystem.UnknownKey()));

        Assert.Equal((HttpStatusCode.BadRequest, "invalid_client"), (token.Status, token.Error));
    }

    [Fact]
    public async Task A_system_the_plan_has_not_registered_is_refused()
    {
        var stranger = new RegisteredSystem("unregistered-system", "system/Claim.read");

        var token = await _api.RequestToken(stranger.ClientAssertion(Now));

        Assert.Equal((HttpStatusCode.BadRequest, "invalid_client"), (token.Status, token.Error));
    }

    [Fact]
    public async Task An_assertion_addressed_to_another_token_endpoint_is_refused()
    {
        var token = await _api.RequestToken(_api.ProviderSystem.ClientAssertion(Now, audience: "https://other-payer.example.test/auth/token"));

        Assert.Equal((HttpStatusCode.BadRequest, "invalid_client"), (token.Status, token.Error));
    }

    [Fact]
    public async Task An_expired_assertion_is_refused()
    {
        var token = await _api.RequestToken(_api.ProviderSystem.ClientAssertion(Now, lifetime: TimeSpan.FromMinutes(-1)));

        Assert.Equal((HttpStatusCode.BadRequest, "invalid_client"), (token.Status, token.Error));
    }

    [Fact]
    public async Task An_assertion_good_for_more_than_five_minutes_is_refused()
    {
        var token = await _api.RequestToken(_api.ProviderSystem.ClientAssertion(Now, lifetime: TimeSpan.FromMinutes(10)));

        Assert.Equal((HttpStatusCode.BadRequest, "invalid_client"), (token.Status, token.Error));
    }

    [Fact]
    public async Task An_assertion_cannot_be_presented_twice()
    {
        var assertion = _api.ProviderSystem.ClientAssertion(Now);
        await _api.RequestToken(assertion);

        var replay = await _api.RequestToken(assertion);

        Assert.Equal((HttpStatusCode.BadRequest, "invalid_client"), (replay.Status, replay.Error));
    }

    [Fact]
    public async Task An_assertion_whose_subject_is_not_its_issuer_is_refused()
    {
        var token = await _api.RequestToken(_api.ProviderSystem.ClientAssertion(Now, subject: "someone-else"));

        Assert.Equal((HttpStatusCode.BadRequest, "invalid_client"), (token.Status, token.Error));
    }

    [Fact]
    public async Task The_token_carries_the_scopes_asked_for()
    {
        var token = await _api.RequestToken(_api.ProviderSystem.ClientAssertion(Now), scope: "system/Claim.read");

        Assert.Equal("system/Claim.read", token.Scope);
    }

    [Fact]
    public async Task A_scope_the_system_is_not_registered_for_is_refused()
    {
        var token = await _api.RequestToken(_api.ProviderSystem.ClientAssertion(Now), scope: "system/Patient.read");

        Assert.Equal((HttpStatusCode.BadRequest, "invalid_scope"), (token.Status, token.Error));
    }

    [Fact]
    public async Task A_token_request_that_is_not_a_form_is_refused()
    {
        var token = await _api.RequestTokenWith("application/json", """{"grant_type":"client_credentials"}""");

        Assert.Equal((HttpStatusCode.BadRequest, "invalid_request"), (token.Status, token.Error));
    }

    [Fact]
    public async Task Only_the_client_credentials_grant_is_offered()
    {
        var token = await _api.RequestToken(_api.ProviderSystem.ClientAssertion(Now), grantType: "authorization_code");

        Assert.Equal((HttpStatusCode.BadRequest, "unsupported_grant_type"), (token.Status, token.Error));
    }

    [Fact]
    public async Task A_request_without_a_token_is_refused_with_an_operation_outcome()
    {
        var response = await _api.Submit(RequestBundles.ResidentialTreatment(), Caller.Anonymous);

        Assert.Equal((HttpStatusCode.Unauthorized, "OperationOutcome"), (response.Status, response.ResourceType));
    }

    [Fact]
    public async Task A_request_with_an_expired_token_is_refused()
    {
        var token = await _api.RequestToken(_api.ProviderSystem.ClientAssertion(Now));
        _api.Clock.Advance(TimeSpan.FromMinutes(6));

        var response = await _api.Submit(RequestBundles.ResidentialTreatment(), Caller.Bearing(token.AccessToken!));

        Assert.Equal(HttpStatusCode.Unauthorized, response.Status);
    }

    [Fact]
    public async Task A_token_the_service_did_not_issue_is_refused()
    {
        var forged = _api.ProviderSystem.ClientAssertion(Now, audience: "http://localhost/fhir");

        var response = await _api.Submit(RequestBundles.ResidentialTreatment(), Caller.Bearing(forged));

        Assert.Equal(HttpStatusCode.Unauthorized, response.Status);
    }

    [Fact]
    public async Task A_token_without_the_scope_to_submit_cannot_submit()
    {
        var token = await _api.RequestToken(_api.ProviderSystem.ClientAssertion(Now), scope: "system/Claim.read");

        var response = await _api.Submit(RequestBundles.ResidentialTreatment(), Caller.Bearing(token.AccessToken!));

        Assert.Equal((HttpStatusCode.Forbidden, "OperationOutcome"), (response.Status, response.ResourceType));
    }

    [Fact]
    public async Task A_token_with_the_scope_to_read_claims_can_inquire()
    {
        var token = await _api.RequestToken(_api.ProviderSystem.ClientAssertion(Now), scope: "system/Claim.read");

        var response = await _api.Inquire(RequestBundles.Inquiry(), Caller.Bearing(token.AccessToken!));

        Assert.Equal(HttpStatusCode.OK, response.Status);
    }

    [Fact]
    public async Task An_ehr_must_present_a_token_too()
    {
        var response = await _api.OrderSign(HookRequests.OrderSign(), Caller.Anonymous);

        Assert.Equal(HttpStatusCode.Unauthorized, response.Status);
    }

    [Fact]
    public async Task The_metrics_report_is_public()
    {
        Assert.Equal(HttpStatusCode.OK, await _api.MetricsReportStatus(2026));
    }

    [Fact]
    public async Task With_authentication_switched_off_for_a_local_demo_an_unauthenticated_call_is_answered()
    {
        using var demo = new PriorAuthorizationApi(authentication: false);

        var response = await demo.Submit(RequestBundles.ResidentialTreatment(), Caller.Anonymous);

        Assert.Equal((HttpStatusCode.OK, "A4"), (response.Status, response.ReviewActionCode));
    }

    private DateTimeOffset Now => _api.Clock.GetUtcNow();

    private static string[] Strings(JsonElement element, string property) =>
        [.. element.GetProperty(property).EnumerateArray().Select(e => e.GetString()!)];
}
