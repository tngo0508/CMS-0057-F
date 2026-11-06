namespace Cms0057.PriorAuthorization;

public enum RequestState
{
    /// <summary>Received and awaiting a Reviewer's decision.</summary>
    Pended,
    Approved,
    Denied,
    /// <summary>The service is one the Behavioral Health Plan does not prior-authorize.</summary>
    NotRequired,
    /// <summary>Withdrawn by the provider. Never a Prior Authorization Decision, so never a denial.</summary>
    Cancelled,
}

/// <summary>
/// A provider's request that the Behavioral Health Plan approve a service for a Member before it is delivered,
/// together with whatever answer it has received so far.
/// </summary>
public sealed class PriorAuthorizationRequest
{
    private PriorAuthorizationRequest() { }

    public Guid Id { get; private set; }

    /// <summary>The provider system's identifier for the submission; a repeat of it is the same request.</summary>
    public string SubmissionIdentifier { get; private set; } = "";

    /// <summary>The Claim's own identifier (its trace number), which a later update or cancellation quotes.</summary>
    public string ClaimIdentifier { get; private set; } = "";

    public string MemberIdentifier { get; private set; } = "";

    /// <summary>The identifier (usually the NPI) of the organization that sent the request, when it gave one.</summary>
    public string? ProviderIdentifier { get; private set; }

    /// <summary>The services asked for, one per item of the request, in the order sent.</summary>
    public IReadOnlyList<RequestedItem> Items => _items;
    private readonly List<RequestedItem> _items = [];

    /// <summary>The billing codes of the items a Reviewer has to decide on.</summary>
    public IEnumerable<string> ServiceCodesUnderReview => _items.Where(i => i.RequiresReview).Select(i => i.ServiceCode);

    /// <summary>True when the provider asked for an expedited decision (Claim.priority "stat").</summary>
    public bool Expedited { get; private set; }

    public DateTimeOffset ReceivedAt { get; private set; }

    /// <summary>Fixed at receipt. Null when authorization is Not Required.</summary>
    public DateTimeOffset? DecisionDeadline { get; private set; }

    public RequestState State { get; private set; }
    public DateTimeOffset? DecidedAt { get; private set; }
    public string? AuthorizationNumber { get; private set; }
    public DateOnly? AuthorizationEnds { get; private set; }
    public string? DenialReason { get; private set; }

    /// <summary>The plan's coded Denial Reason (X12 886), when the Reviewer gave one, and its display text.</summary>
    public string? DenialReasonCode { get; private set; }
    public string? DenialReasonDisplay { get; private set; }

    /// <summary>The request Bundle exactly as the provider system sent it.</summary>
    public string RequestBundle { get; private set; } = "";

    /// <summary>The reference a provider quotes while the request is Pended.</summary>
    public string ReferenceNumber => "PEND-" + ShortId;

    private string ShortId => Id.ToString("N")[..10].ToUpperInvariant();

    /// <summary>
    /// Records a submission and gives it its immediate answer. Each requested service is judged by its own rule;
    /// the request is Pended when any of them needs review, with the earliest Decision Deadline among them (the
    /// expedited one when the provider asked for an expedited decision), and Not Required when none does.
    /// </summary>
    public static PriorAuthorizationRequest Receive(Submission submission, IAuthorizationRules rules, DateTimeOffset now)
    {
        var judged = submission.Services.Select(service => (Service: service, Rule: rules.For(service))).ToList();
        var deadlines = judged.Select(j => j.Rule)
            .OfType<AuthorizationRule.RequiresReview>()
            .Select(r => r.Timeframe.DeadlineFrom(now, submission.Expedited))
            .ToList();

        var request = new PriorAuthorizationRequest
        {
            Id = Guid.NewGuid(),
            SubmissionIdentifier = submission.SubmissionIdentifier,
            ClaimIdentifier = submission.ClaimIdentifier,
            MemberIdentifier = submission.MemberIdentifier,
            ProviderIdentifier = submission.ProviderIdentifier,
            Expedited = submission.Expedited,
            ReceivedAt = now,
            RequestBundle = submission.RequestBundle,
            State = deadlines.Count > 0 ? RequestState.Pended : RequestState.NotRequired,
            DecisionDeadline = deadlines.Count > 0 ? deadlines.Min() : null,
        };
        request._items.AddRange(judged.Select(j =>
            RequestedItem.Of(j.Service, requiresReview: j.Rule is AuthorizationRule.RequiresReview)));
        return request;
    }

    /// <summary>
    /// The provider changes what it asked for while the request is still Pended. The items are replaced and judged
    /// afresh; a withdrawn item is answered as cancelled. The Decision Deadline does not move: the clock started at
    /// receipt and nothing pauses it (42 CFR 438.210(d)). If nothing is left to review, the request has nothing to
    /// decide. A request already decided cannot be updated: the provider sends a new one.
    /// </summary>
    /// <exception cref="RequestRejected">The request is Denied, Approved or Cancelled.</exception>
    public void Update(IReadOnlyList<ItemUpdate> items, IAuthorizationRules rules, string requestBundle)
    {
        switch (State)
        {
            case RequestState.Denied:
                throw new RequestRejected("Cannot update a denied prior authorization (review action A2)");
            case RequestState.Approved:
                throw new RequestRejected("Cannot update an approved prior authorization; send a new request");
            case RequestState.Cancelled:
                throw new RequestRejected("Cannot update a cancelled prior authorization; send a new request");
        }

        _items.Clear();
        _items.AddRange(items.Select(i => i.Cancelled
            ? RequestedItem.Of(i.Service, requiresReview: false, cancelled: true)
            : RequestedItem.Of(i.Service, requiresReview: rules.For(i.Service) is AuthorizationRule.RequiresReview)));
        RequestBundle = requestBundle;
        State = _items.Any(i => i.RequiresReview) ? RequestState.Pended : RequestState.NotRequired;
    }

    /// <summary>
    /// The provider withdraws the whole request. A Pended request leaves the Reviewer's list; an approved one loses
    /// its authorization. Cancelling twice changes nothing.
    /// </summary>
    /// <exception cref="RequestRejected">The request is Denied; as in PAS, a denial cannot be cancelled.</exception>
    public void Cancel(DateTimeOffset now)
    {
        if (State == RequestState.Denied)
            throw new RequestRejected("Cannot cancel a denied prior authorization (review action A2)");
        if (State == RequestState.Cancelled) return;
        State = RequestState.Cancelled;
        DecidedAt = now;
        AuthorizationNumber = null;
        AuthorizationEnds = null;
    }

    /// <summary>Whether the item with this sequence number is one the Reviewer decides on.</summary>
    public bool RequiresReview(int sequence) => _items.FirstOrDefault(i => i.Sequence == sequence)?.RequiresReview ?? false;

    /// <summary>
    /// The answer for one item: cancelled items, and every item of a cancelled request, are Cancelled; an item
    /// under review follows the request; any other is Not Required.
    /// </summary>
    public RequestState StateOf(int sequence)
    {
        if (State == RequestState.Cancelled) return RequestState.Cancelled;
        var item = _items.FirstOrDefault(i => i.Sequence == sequence);
        if (item is { Cancelled: true }) return RequestState.Cancelled;
        return item is { RequiresReview: true } ? State : RequestState.NotRequired;
    }

    public bool IsOverdue(DateTimeOffset now) => State == RequestState.Pended && now > DecisionDeadline;

    /// <summary>
    /// A Reviewer approves the request. The approval must say when the authorization ends, and that cannot be
    /// before the day of the decision.
    /// </summary>
    public void Approve(DateOnly? authorizationEnds, DateTimeOffset now)
    {
        RequirePended();
        var ends = authorizationEnds
            ?? throw new DecisionRefused("An approval must state the date the authorization ends");
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        if (ends < today)
            throw new DecisionRefused($"The authorization cannot end before the day it is approved ({today:yyyy-MM-dd})");
        AuthorizationEnds = ends;
        AuthorizationNumber = "AUTH-" + ShortId;
        State = RequestState.Approved;
        DecidedAt = now;
    }

    /// <summary>
    /// A Reviewer denies the request. The denial must give a specific, plain-language reason; a coded reason from
    /// the plan's list may go with it.
    /// </summary>
    public void Deny(string? denialReason, DenialReasonCode? reasonCode, DateTimeOffset now)
    {
        RequirePended();
        if (string.IsNullOrWhiteSpace(denialReason))
            throw new DecisionRefused("A denial must give a specific reason");
        DenialReason = denialReason.Trim();
        DenialReasonCode = reasonCode?.Code;
        DenialReasonDisplay = reasonCode?.Display;
        State = RequestState.Denied;
        DecidedAt = now;
    }

    private void RequirePended()
    {
        if (State != RequestState.Pended) throw new AlreadyAnswered(State);
    }
}

/// <summary>A Reviewer's decision was incomplete, so nothing changed.</summary>
public sealed class DecisionRefused(string reason) : Exception(reason);

/// <summary>The request is not Pended, so there is nothing for a Reviewer to decide.</summary>
public sealed class AlreadyAnswered(RequestState state) : Exception($"The request is already {state}");

/// <summary>
/// A request the service cannot process: malformed, or for someone who is not a Member. It is an error to the
/// sender, never a Prior Authorization Decision, and nothing is recorded.
/// </summary>
public class RequestRejected(string reason) : Exception(reason);

/// <summary>One service a Prior Authorization Request asks for, as recorded with the request.</summary>
public sealed class RequestedItem
{
    private RequestedItem() { }

    /// <summary>The item's sequence number in the request Bundle.</summary>
    public int Sequence { get; private set; }

    public string ServiceCode { get; private set; } = "";

    /// <summary>Billing modifiers, space-separated.</summary>
    public string Modifiers { get; private set; } = "";

    /// <summary>True when this item is what the request is Pended for; false when it is Not Required.</summary>
    public bool RequiresReview { get; private set; }

    /// <summary>True when the provider withdrew this item in an update.</summary>
    public bool Cancelled { get; private set; }

    public static RequestedItem Of(RequestedService service, bool requiresReview, bool cancelled = false) => new()
    {
        Sequence = service.Sequence,
        ServiceCode = service.Code,
        Modifiers = string.Join(' ', service.Modifiers),
        RequiresReview = requiresReview,
        Cancelled = cancelled,
    };
}

/// <summary>What a provider system sends to Claim/$submit: a new request, an update, or a cancellation.</summary>
public abstract record PasSubmission
{
    private protected PasSubmission() { }
}

/// <summary>A new Prior Authorization Request (a renewal is one too), as read from its Bundle.</summary>
public sealed record Submission(
    string SubmissionIdentifier,
    string ClaimIdentifier,
    string MemberIdentifier,
    string? ProviderIdentifier,
    bool Expedited,
    IReadOnlyList<RequestedService> Services,
    string RequestBundle) : PasSubmission;

/// <summary>An update to the request whose Claim carried <paramref name="PriorClaimIdentifier"/>.</summary>
public sealed record RequestUpdate(
    string PriorClaimIdentifier,
    IReadOnlyList<ItemUpdate> Items,
    string RequestBundle) : PasSubmission;

/// <summary>A cancellation of the whole request whose Claim carried <paramref name="PriorClaimIdentifier"/>.</summary>
public sealed record RequestCancellation(string PriorClaimIdentifier) : PasSubmission;

public sealed record ItemUpdate(RequestedService Service, bool Cancelled);

public sealed record RequestedService(int Sequence, string Code, IReadOnlyList<string> Modifiers)
{
    /// <summary>
    /// The service a draft order asks for: the order's code, with any HCPCS modifiers the EHR put in orderDetail.
    /// That is this project's convention; ServiceRequest has no modifier element. An order is one service, item 1.
    /// </summary>
    /// <exception cref="RequestRejected">The order has no code.</exception>
    public static RequestedService OfOrder(Hl7.Fhir.Model.ServiceRequest order)
    {
        var code = order.Code?.Coding.FirstOrDefault()?.Code
                   ?? throw new RequestRejected($"ServiceRequest/{order.Id} has no code");
        return new RequestedService(1, code, [.. order.OrderDetail.SelectMany(d => d.Coding).Select(c => c.Code).OfType<string>()]);
    }
}

/// <summary>
/// A provider system asking what has become of the requests its organization sent for a Member, optionally narrowed
/// to the reference or authorization numbers it was given.
/// </summary>
public sealed record Inquiry(string MemberIdentifier, string? ProviderIdentifier, IReadOnlyList<string> Numbers);
