using System.Net;
using System.Text.Json;

namespace Cms0057.PriorAuthorization.Tests;

/// <summary>
/// CRD: an EHR asks, as a clinician signs an order, whether the Behavioral Health Plan requires prior authorization.
/// </summary>
public sealed class CoverageRequirementsTests : IDisposable
{
    private readonly PriorAuthorizationApi _api = new();

    public void Dispose() => _api.Dispose();

    [Fact]
    public async Task The_payer_advertises_an_order_sign_service()
    {
        var discovery = JsonDocument.Parse(await _api.DiscoverServices()).RootElement;

        var service = discovery.GetProperty("services").EnumerateArray().Single();
        Assert.Equal(("order-sign-crd", "order-sign"),
            (service.GetProperty("id").GetString(), service.GetProperty("hook").GetString()));
    }

    [Fact]
    public async Task Signing_a_residential_treatment_order_says_prior_authorization_is_needed()
    {
        var response = await _api.OrderSign(HookRequests.OrderSign("H0019", "U1"));

        Assert.Equal("auth-needed", response.PriorAuthorizationNeeded);
    }

    [Fact]
    public async Task Signing_an_outpatient_counseling_order_says_no_authorization_is_needed()
    {
        var response = await _api.OrderSign(HookRequests.OrderSign("H0004", "U7"));

        Assert.Equal("no-auth", response.PriorAuthorizationNeeded);
    }

    [Fact]
    public async Task The_answer_comes_back_as_coverage_information_on_the_signed_order()
    {
        var response = await _api.OrderSign(HookRequests.OrderSign());

        Assert.Equal(("update", "ServiceRequest", "ResidentialOrder"),
            (response.SystemActionType, response.UpdatedResourceType, response.UpdatedResourceId));
    }

    [Fact]
    public async Task The_coverage_information_names_the_coverage_it_applies_to()
    {
        var response = await _api.OrderSign(HookRequests.OrderSign());

        Assert.Equal("Coverage/MemberCoverage", response.CoverageReference);
    }

    [Fact]
    public async Task The_service_is_covered_either_way()
    {
        var response = await _api.OrderSign(HookRequests.OrderSign("H0004", "U7"));

        Assert.Equal("covered", response.Covered);
    }

    [Fact]
    public async Task Coverage_information_is_not_repeated_as_a_card()
    {
        var response = await _api.OrderSign(HookRequests.OrderSign());

        Assert.Equal(0, response.CardCount);
    }

    [Fact]
    public async Task A_request_without_draft_orders_is_rejected()
    {
        var response = await _api.OrderSign(HookRequests.OrderSignWithoutDraftOrders());

        Assert.Equal(
            (HttpStatusCode.BadRequest, "Missing required context field 'draftOrders' for hook 'order-sign'."),
            (response.Status, response.ErrorMessage));
    }

    [Fact]
    public async Task A_request_without_the_members_coverage_is_rejected()
    {
        var response = await _api.OrderSign(HookRequests.OrderSignWithoutCoverage());

        Assert.Equal(HttpStatusCode.BadRequest, response.Status);
    }

    [Fact]
    public async Task A_request_for_a_different_hook_is_rejected()
    {
        var response = await _api.OrderSign(HookRequests.OrderSign(hook: "order-select"));

        Assert.Equal(
            (HttpStatusCode.BadRequest, "Mismatched hook in request. Expected: order-sign, Actual: order-select"),
            (response.Status, response.ErrorMessage));
    }
}
