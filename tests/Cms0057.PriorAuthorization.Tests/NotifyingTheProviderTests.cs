using System.Net;

namespace Cms0057.PriorAuthorization.Tests;

/// <summary>
/// PAS subscriptions: a provider system asks to be told when a Pended request is decided, and the service POSTs the
/// decision to it.
/// </summary>
public sealed class NotifyingTheProviderTests : IDisposable
{
    private const string Endpoint = "http://provider.example.org/api/pas/notification";

    private readonly PriorAuthorizationApi _api = new();

    public void Dispose() => _api.Dispose();

    [Fact]
    public async Task A_provider_system_can_subscribe_to_decisions()
    {
        var response = await _api.Subscribe(RequestBundles.Subscription());

        Assert.Equal(HttpStatusCode.Created, response.Status);
        Assert.Equal("active", response.Subscription.Status);
    }

    [Fact]
    public async Task A_provider_system_can_look_its_subscription_up_by_endpoint_before_subscribing_again()
    {
        // br-provider searches Subscription?url=<its endpoint>&status=requested,active and only subscribes when
        // nothing comes back, so that a payer does not accumulate one subscription per page load.
        await _api.Subscribe(RequestBundles.Subscription(notificationUrl: Endpoint));

        var found = await _api.FindSubscriptions(Endpoint);
        var elsewhere = await _api.FindSubscriptions("http://other.example.org/api/pas/notification");

        Assert.Equal((HttpStatusCode.OK, "searchset", 1), (found.Status, found.BundleType, found.Total));
        Assert.Equal("active", found.Subscriptions.Single().Status);
        Assert.Equal(0, elsewhere.Total);
    }

    [Fact]
    public async Task A_subscription_must_say_which_sending_system_it_is_for()
    {
        var response = await _api.Subscribe(RequestBundles.Subscription(withFilter: false));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.Status);
        Assert.StartsWith("PAS subscription filter criteria must be 'org-identifier=", response.ErrorDiagnostics);
    }

    [Fact]
    public async Task The_subscribed_provider_is_told_when_its_request_is_approved()
    {
        await _api.Subscribe(RequestBundles.Subscription(notificationUrl: Endpoint));
        var pended = (await _api.Submit(RequestBundles.ResidentialTreatment())).ClaimResponseId;

        await _api.Approve(pended, authorizationEnds: "2026-12-06");

        var delivery = Assert.Single(_api.Notifications.Received);
        Assert.Equal(Endpoint, delivery.Url.ToString());
        Assert.Equal("A1", new PasResponse(HttpStatusCode.OK, delivery.Body).ReviewActionCode);
    }

    [Fact]
    public async Task The_subscribed_provider_is_told_when_its_request_is_denied()
    {
        await _api.Subscribe(RequestBundles.Subscription(notificationUrl: Endpoint));
        var pended = (await _api.Submit(RequestBundles.ResidentialTreatment())).ClaimResponseId;

        await _api.Deny(pended, denialReason: "The assessment does not show a need for a residential level of care.");

        var delivery = Assert.Single(_api.Notifications.Received);
        Assert.Equal("A2", new PasResponse(HttpStatusCode.OK, delivery.Body).ReviewActionCode);
    }

    [Fact]
    public async Task A_notification_is_a_history_bundle_led_by_the_subscription_status()
    {
        await _api.Subscribe(RequestBundles.Subscription(notificationUrl: Endpoint));
        var pended = (await _api.Submit(RequestBundles.ResidentialTreatment())).ClaimResponseId;

        await _api.Approve(pended, authorizationEnds: "2026-12-06");

        var notification = new PasResponse(HttpStatusCode.OK, _api.Notifications.Received.Single().Body);
        Assert.Equal("history", notification.BundleType);
        Assert.Equal(
            ("Parameters", "http://hl7.org/fhir/us/davinci-pas/SubscriptionTopic/PASSubscriptionTopic"),
            notification.NotificationStatus);
        Assert.Equal("application/fhir+json", _api.Notifications.Received.Single().ContentType);
    }

    [Fact]
    public async Task The_claim_response_a_notification_points_at_can_be_read()
    {
        // The notification's focus names the ClaimResponse's URL, and br-provider reads a ClaimResponse by URL
        // when it has no context for an inquiry.
        await _api.Subscribe(RequestBundles.Subscription(notificationUrl: Endpoint));
        var pended = (await _api.Submit(RequestBundles.ResidentialTreatment())).ClaimResponseId;
        await _api.Approve(pended, authorizationEnds: "2026-12-06");
        var focus = new PasResponse(HttpStatusCode.OK, _api.Notifications.Received.Single().Body).NotificationFocus;

        var read = await _api.Read(focus!);

        Assert.Equal((HttpStatusCode.OK, "ClaimResponse", "A1"), (read.Status, read.ResourceType, read.ReviewActionCode));
    }

    [Fact]
    public async Task Reading_a_claim_response_that_does_not_exist_is_answered_not_found()
    {
        var read = await _api.Read($"/fhir/ClaimResponse/{Guid.NewGuid()}");

        Assert.Equal((HttpStatusCode.NotFound, "OperationOutcome"), (read.Status, read.ResourceType));
    }

    [Fact]
    public async Task Nothing_is_sent_while_the_request_is_still_pended()
    {
        await _api.Subscribe(RequestBundles.Subscription(notificationUrl: Endpoint));

        await _api.Submit(RequestBundles.ResidentialTreatment());

        Assert.Empty(_api.Notifications.Received);
    }

    [Fact]
    public async Task A_provider_that_did_not_send_the_request_is_not_told()
    {
        await _api.Subscribe(RequestBundles.Subscription(orgIdentifier: "1112223338", notificationUrl: Endpoint));
        var pended = (await _api.Submit(RequestBundles.ResidentialTreatment())).ClaimResponseId;

        await _api.Approve(pended, authorizationEnds: "2026-12-06");

        Assert.Empty(_api.Notifications.Received);
    }

    [Fact]
    public async Task A_failed_delivery_does_not_undo_the_decision()
    {
        await _api.Subscribe(RequestBundles.Subscription(notificationUrl: Endpoint));
        var pended = (await _api.Submit(RequestBundles.ResidentialTreatment())).ClaimResponseId;
        _api.Notifications.RespondWith = HttpStatusCode.InternalServerError;

        var status = await _api.Approve(pended, authorizationEnds: "2026-12-06");

        Assert.Equal(HttpStatusCode.NoContent, status);
        Assert.Equal("A1", (await _api.Inquire(RequestBundles.Inquiry())).ReviewActionCode);
    }
}
