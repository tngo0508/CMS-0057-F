namespace Cms0057.PriorAuthorization;

public enum RequestState
{
    /// <summary>Received and awaiting a Reviewer's decision.</summary>
    Pended,
    Approved,
    Denied,
    /// <summary>The service is one the Behavioral Health Plan does not prior-authorize.</summary>
    NotRequired,
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

    public string MemberIdentifier { get; private set; } = "";
    public string ServiceCode { get; private set; } = "";
    public DateTimeOffset ReceivedAt { get; private set; }

    /// <summary>Fixed at receipt. Null when authorization is Not Required.</summary>
    public DateTimeOffset? DecisionDeadline { get; private set; }

    public RequestState State { get; private set; }
    public DateTimeOffset? DecidedAt { get; private set; }
    public string? AuthorizationNumber { get; private set; }
    public DateOnly? AuthorizationEnds { get; private set; }
    public string? DenialReason { get; private set; }

    /// <summary>The request Bundle exactly as the provider system sent it.</summary>
    public string RequestBundle { get; private set; } = "";

    /// <summary>The reference a provider quotes while the request is Pended.</summary>
    public string ReferenceNumber => "PEND-" + ShortId;

    private string ShortId => Id.ToString("N")[..10].ToUpperInvariant();

    public static PriorAuthorizationRequest Receive(Submission submission, AuthorizationRule rule, DateTimeOffset now) =>
        new()
        {
            Id = Guid.NewGuid(),
            SubmissionIdentifier = submission.SubmissionIdentifier,
            MemberIdentifier = submission.MemberIdentifier,
            ServiceCode = submission.Service.Code,
            ReceivedAt = now,
            RequestBundle = submission.RequestBundle,
            State = rule is AuthorizationRule.RequiresReview ? RequestState.Pended : RequestState.NotRequired,
            DecisionDeadline = rule is AuthorizationRule.RequiresReview review ? now + review.DecideWithin : null,
        };

    public bool IsOverdue(DateTimeOffset now) => State == RequestState.Pended && now > DecisionDeadline;

    /// <summary>A Reviewer approves the request. The approval must say when the authorization ends.</summary>
    public void Approve(DateOnly? authorizationEnds, DateTimeOffset now)
    {
        RequirePended();
        AuthorizationEnds = authorizationEnds
            ?? throw new DecisionRefused("An approval must state the date the authorization ends");
        AuthorizationNumber = "AUTH-" + ShortId;
        State = RequestState.Approved;
        DecidedAt = now;
    }

    /// <summary>A Reviewer denies the request. The denial must give a specific, plain-language reason.</summary>
    public void Deny(string? denialReason, DateTimeOffset now)
    {
        RequirePended();
        if (string.IsNullOrWhiteSpace(denialReason))
            throw new DecisionRefused("A denial must give a specific reason");
        DenialReason = denialReason.Trim();
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
public sealed class RequestRejected(string reason) : Exception(reason);

/// <summary>What the service takes from a submitted request Bundle.</summary>
public sealed record Submission(
    string SubmissionIdentifier,
    string MemberIdentifier,
    RequestedService Service,
    string RequestBundle);

public sealed record RequestedService(string Code, IReadOnlyList<string> Modifiers);

/// <summary>
/// A provider system asking what has become of a Member's requests, optionally narrowed to the reference or
/// authorization numbers it was given.
/// </summary>
public sealed record Inquiry(string MemberIdentifier, IReadOnlyList<string> Numbers);
