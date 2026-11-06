using System.Text;
using System.Text.RegularExpressions;
using Cms0057.PriorAuthorization.Data;
using Cms0057.PriorAuthorization.Pas;
using Hl7.Fhir.Model;
using Microsoft.EntityFrameworkCore;
using Task = System.Threading.Tasks.Task;

namespace Cms0057.PriorAuthorization.Notifications;

/// <summary>
/// PAS subscriptions: provider systems ask to be told when the requests they sent are decided, and the service
/// POSTs each decision to them. The wire shapes follow Da Vinci's br-payer and br-provider (see docs/adr/0001).
/// </summary>
public sealed partial class PasSubscriptions(
    PriorAuthorizationDbContext db,
    IHttpClientFactory httpClients,
    TimeProvider clock,
    ILogger<PasSubscriptions> log)
{
    /// <summary>The named HttpClient notifications go out on.</summary>
    public const string HttpClientName = "pas-notifications";

    public const string Topic = "http://hl7.org/fhir/us/davinci-pas/SubscriptionTopic/PASSubscriptionTopic";

    private const string Backport = "http://hl7.org/fhir/uv/subscriptions-backport/StructureDefinition/";
    private const string FilterCriteriaUrl = Backport + "backport-filter-criteria";
    private const string SubscriptionStatusProfile = Backport + "backport-subscription-status";
    private const string NotificationBundleProfile = Backport + "backport-subscription-notification";

    /// <summary>The only filter br-payer accepts: the sending organization's identifier.</summary>
    [GeneratedRegex(@"^(Bundle\?)?org-identifier=([^&?]+)$")]
    private static partial Regex FilterCriteria();

    /// <summary>Accepts a provider system's Subscription and returns it as activated.</summary>
    /// <exception cref="RequestRejected">The Subscription is not one this service can honour.</exception>
    public async Task<Subscription> Subscribe(string subscriptionJson)
    {
        var subscription = FhirJson.Read<Subscription>(subscriptionJson);
        Require(subscription.Criteria == Topic, $"Subscription.criteria must be '{Topic}'");

        var filter = subscription.CriteriaElement?.Extension
            .FirstOrDefault(e => e.Url == FilterCriteriaUrl)?.Value as FhirString;
        var match = filter?.Value is { } criteria ? FilterCriteria().Match(criteria) : Match.Empty;
        Require(match.Success, "PAS subscription filter criteria must be 'org-identifier=<identifier>'");

        Require(subscription.Channel?.Type == Subscription.SubscriptionChannelType.RestHook,
            "Subscription.channel.type must be 'rest-hook'");
        Require(Uri.TryCreate(subscription.Channel.Endpoint, UriKind.Absolute, out var endpoint)
                && endpoint.Scheme is "http" or "https",
            "Subscription.channel.endpoint must be an absolute http(s) URL");

        var stored = DecisionSubscription.Create(match.Groups[2].Value, endpoint.ToString(), clock.GetUtcNow());
        db.Subscriptions.Add(stored);
        await db.SaveChangesAsync();

        subscription.Id = stored.Id.ToString();
        subscription.Status = Subscription.SubscriptionStatus.Active;
        return subscription;
    }

    /// <summary>
    /// Tells every provider system subscribed for the request's sending organization that the request has been
    /// decided. A delivery that fails is logged and dropped; the decision stands regardless.
    /// </summary>
    public async Task NotifyDecided(PriorAuthorizationRequest request, string serverBase)
    {
        if (request.ProviderIdentifier is null) return;
        var subscribed = await db.Subscriptions
            .Where(s => s.OrganizationIdentifier == request.ProviderIdentifier)
            .ToListAsync();
        if (subscribed.Count == 0) return;

        var response = PasWire.Response(request, serverBase);
        var client = httpClients.CreateClient(HttpClientName);
        foreach (var subscription in subscribed)
        {
            var notification = Notification(subscription, subscription.NextEvent(), request, response, serverBase);
            await Deliver(client, subscription, notification);
        }
        await db.SaveChangesAsync();
    }

    private async Task Deliver(HttpClient client, DecisionSubscription subscription, Bundle notification)
    {
        try
        {
            using var content = new StringContent(FhirJson.Write(notification), Encoding.UTF8, "application/fhir+json");
            using var reply = await client.PostAsync(subscription.Endpoint, content);
            if (!reply.IsSuccessStatusCode)
                log.LogWarning("Notification to {Endpoint} was answered with {Status}", subscription.Endpoint, (int)reply.StatusCode);
        }
        catch (Exception failure) when (failure is HttpRequestException or TaskCanceledException)
        {
            log.LogWarning(failure, "Notification to {Endpoint} could not be delivered", subscription.Endpoint);
        }
    }

    /// <summary>
    /// A Subscriptions Backport notification as br-payer sends it: a history Bundle whose first entry is the
    /// subscription status and whose second is the full PAS response Bundle.
    /// </summary>
    private Bundle Notification(
        DecisionSubscription subscription,
        int eventNumber,
        PriorAuthorizationRequest request,
        Bundle response,
        string serverBase)
    {
        var now = clock.GetUtcNow();
        var subscriptionUrl = $"{serverBase}/Subscription/{subscription.Id}";

        var status = new Parameters { Id = Guid.NewGuid().ToString(), Meta = new Meta { Profile = [SubscriptionStatusProfile] } };
        status.Add("subscription", new ResourceReference(subscriptionUrl));
        status.Add("topic", new Canonical(Topic));
        status.Add("status", new Code("active"));
        status.Add("type", new Code("event-notification"));
        status.Add("events-since-subscription-start", new FhirString(eventNumber.ToString()));
        var notificationEvent = new Parameters.ParameterComponent { Name = "notification-event" };
        notificationEvent.Part.Add(new Parameters.ParameterComponent { Name = "event-number", Value = new FhirString(eventNumber.ToString()) });
        notificationEvent.Part.Add(new Parameters.ParameterComponent { Name = "timestamp", Value = new Instant(now) });
        notificationEvent.Part.Add(new Parameters.ParameterComponent { Name = "focus", Value = new ResourceReference($"{serverBase}/ClaimResponse/{request.Id}") });
        status.Parameter.Add(notificationEvent);

        response.Id = Guid.NewGuid().ToString();
        return new Bundle
        {
            Meta = new Meta { Profile = [NotificationBundleProfile] },
            Type = Bundle.BundleType.History,
            Timestamp = now,
            Entry =
            [
                new Bundle.EntryComponent
                {
                    FullUrl = "urn:uuid:" + status.Id,
                    Resource = status,
                    Request = new Bundle.RequestComponent { Method = Bundle.HTTPVerb.GET, Url = subscriptionUrl + "/$status" },
                    Response = new Bundle.ResponseComponent { Status = "200" },
                },
                new Bundle.EntryComponent
                {
                    FullUrl = $"{serverBase}/Bundle/{response.Id}",
                    Resource = response,
                    Request = new Bundle.RequestComponent { Method = Bundle.HTTPVerb.GET, Url = $"{serverBase}/Bundle/{response.Id}" },
                    Response = new Bundle.ResponseComponent { Status = "200" },
                },
            ],
        };
    }

    private static void Require(bool condition, string otherwise)
    {
        if (!condition) throw new RequestRejected(otherwise);
    }
}
