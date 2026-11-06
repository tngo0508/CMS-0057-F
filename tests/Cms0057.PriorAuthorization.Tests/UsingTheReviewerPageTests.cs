using System.Net;

namespace Cms0057.PriorAuthorization.Tests;

/// <summary>
/// The Reviewer's web page sits on top of the Reviewer endpoints. These tests cover what the service serves to it;
/// what the page does with it is in the page itself (wwwroot/reviewer.html).
/// </summary>
public sealed class UsingTheReviewerPageTests : IDisposable
{
    private readonly PriorAuthorizationApi _api = new();

    public void Dispose() => _api.Dispose();

    [Fact]
    public async Task The_reviewer_page_is_served_at_reviewer()
    {
        var page = await _api.ReviewerPage();

        Assert.Equal(HttpStatusCode.OK, page.Status);
        Assert.StartsWith("text/html", page.ContentType);
        Assert.Contains("Pended", page.Body);
    }

    [Fact]
    public async Task The_page_can_ask_which_denial_reason_codes_the_plan_uses()
    {
        var codes = await _api.ConfiguredDenialReasonCodes();

        Assert.Equal(
            [new DenialReasonCodeOption(DenialReasonCodes.NotMedicallyNecessary, "Not medically necessary (test code)")],
            codes);
    }

    [Fact]
    public async Task A_plan_that_has_configured_no_codes_offers_none()
    {
        using var plan = new PriorAuthorizationApi(denialReasonCodes: false);

        Assert.Empty(await plan.ConfiguredDenialReasonCodes());
    }
}
