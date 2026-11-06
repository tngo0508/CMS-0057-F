namespace Cms0057.PriorAuthorization.Tests;

/// <summary>
/// One Prior Authorization Request can ask for several services. Each item is answered on its own; the request is
/// Pended as a whole when any item needs review, and the Reviewer decides it as a whole.
/// </summary>
public sealed class RequestingSeveralServicesTests : IDisposable
{
    private readonly PriorAuthorizationApi _api = new();

    public void Dispose() => _api.Dispose();

    [Fact]
    public async Task Each_item_is_answered_on_its_own()
    {
        var response = await _api.Submit(RequestBundles.ForServices(RequestBundles.Residential, RequestBundles.Counseling));

        Assert.Equal(["A4", "A3"], response.ReviewActionCodes);
    }

    [Fact]
    public async Task A_request_is_pended_once_however_many_items_need_review()
    {
        await _api.Submit(RequestBundles.ForServices(RequestBundles.Residential, RequestBundles.Counseling));

        Assert.Single(await _api.PendedRequests());
    }

    [Fact]
    public async Task A_request_whose_items_all_need_no_review_is_not_required()
    {
        var response = await _api.Submit(RequestBundles.ForServices(RequestBundles.Counseling, RequestBundles.GroupCounseling));

        Assert.Equal(["A3", "A3"], response.ReviewActionCodes);
        Assert.Empty(await _api.PendedRequests());
    }

    [Fact]
    public async Task An_approval_certifies_the_reviewed_items_and_leaves_the_others_not_required()
    {
        var pended = (await _api.Submit(RequestBundles.ForServices(RequestBundles.Counseling, RequestBundles.Residential))).ClaimResponseId;

        await _api.Approve(pended, authorizationEnds: "2026-12-06");

        Assert.Equal(["A3", "A1"], (await _api.Inquire(RequestBundles.Inquiry())).ReviewActionCodes);
    }

    [Fact]
    public async Task The_reviewer_sees_which_services_need_the_decision()
    {
        var pended = (await _api.Submit(RequestBundles.ForServices(RequestBundles.Counseling, RequestBundles.Residential))).ClaimResponseId;

        Assert.Equal(["H0019"], (await _api.PendedRequest(pended)).ServiceCodes);
    }
}
