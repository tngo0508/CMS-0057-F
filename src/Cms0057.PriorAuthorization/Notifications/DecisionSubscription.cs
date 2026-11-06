namespace Cms0057.PriorAuthorization.Notifications;

/// <summary>
/// A provider system's standing request to be told when the requests it sent are decided. One per PAS
/// Subscription the service accepted.
/// </summary>
public sealed class DecisionSubscription
{
    private DecisionSubscription() { }

    public Guid Id { get; private set; }

    /// <summary>The identifier (usually the NPI) of the organization whose requests the subscriber wants to hear about.</summary>
    public string OrganizationIdentifier { get; private set; } = "";

    /// <summary>Where notifications are POSTed.</summary>
    public string Endpoint { get; private set; } = "";

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>How many notifications have been sent on this subscription; numbers the next one.</summary>
    public int EventsSent { get; private set; }

    public static DecisionSubscription Create(string organizationIdentifier, string endpoint, DateTimeOffset now) =>
        new() { Id = Guid.NewGuid(), OrganizationIdentifier = organizationIdentifier, Endpoint = endpoint, CreatedAt = now };

    /// <summary>Takes the next event number.</summary>
    public int NextEvent() => ++EventsSent;
}
