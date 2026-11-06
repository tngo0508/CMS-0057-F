using Cms0057.PriorAuthorization.Data;
using Microsoft.EntityFrameworkCore;

namespace Cms0057.PriorAuthorization;

/// <summary>
/// Everything the Behavioral Health Plan does with Prior Authorization Requests: receiving them from provider
/// systems and putting the Pended ones in front of a Reviewer.
/// </summary>
public sealed class PriorAuthorizationRequests(
    PriorAuthorizationDbContext db,
    IAuthorizationRules rules,
    TimeProvider clock)
{
    /// <summary>
    /// Records a new request and gives it its immediate answer: Pended or Not Required. A submission seen before
    /// returns the request it created the first time.
    /// </summary>
    /// <exception cref="RequestRejected">The request is for someone who is not a Member.</exception>
    public async Task<PriorAuthorizationRequest> Receive(Submission submission)
    {
        if (await Submitted(submission) is { } original) return original;

        if (!await db.Members.AnyAsync(m => m.Identifier == submission.MemberIdentifier))
            throw new RequestRejected($"{submission.MemberIdentifier} is not a Member of this plan");

        var request = PriorAuthorizationRequest.Receive(submission, rules.For(submission.Service), clock.GetUtcNow());
        db.Requests.Add(request);
        try
        {
            await db.SaveChangesAsync();
            return request;
        }
        catch (DbUpdateException) when (IsRetryThatRacedUs())
        {
            db.Entry(request).State = EntityState.Detached;
            return (await Submitted(submission))!;
        }

        bool IsRetryThatRacedUs() =>
            db.Requests.AsNoTracking().Any(r => r.SubmissionIdentifier == submission.SubmissionIdentifier);
    }

    private Task<PriorAuthorizationRequest?> Submitted(Submission submission) =>
        db.Requests.SingleOrDefaultAsync(r => r.SubmissionIdentifier == submission.SubmissionIdentifier);

    /// <summary>The Member's requests an inquiry asks about, oldest first.</summary>
    public async Task<IReadOnlyList<PriorAuthorizationRequest>> Find(Inquiry inquiry)
    {
        var ofMember = await db.Requests
            .Where(r => r.MemberIdentifier == inquiry.MemberIdentifier)
            .OrderBy(r => r.ReceivedAt)
            .ToListAsync();

        return inquiry.Numbers.Count == 0
            ? ofMember
            : [.. ofMember.Where(r =>
                inquiry.Numbers.Contains(r.ReferenceNumber)
                || (r.AuthorizationNumber is not null && inquiry.Numbers.Contains(r.AuthorizationNumber)))];
    }

    /// <returns>The approved request, or null when there is no such request.</returns>
    /// <exception cref="DecisionRefused">No end date was given.</exception>
    /// <exception cref="AlreadyAnswered">The request is not Pended.</exception>
    public Task<PriorAuthorizationRequest?> Approve(Guid id, DateOnly? authorizationEnds) =>
        Decide(id, request => request.Approve(authorizationEnds, clock.GetUtcNow()));

    /// <returns>The denied request, or null when there is no such request.</returns>
    /// <exception cref="DecisionRefused">No Denial Reason was given.</exception>
    /// <exception cref="AlreadyAnswered">The request is not Pended.</exception>
    public Task<PriorAuthorizationRequest?> Deny(Guid id, string? denialReason) =>
        Decide(id, request => request.Deny(denialReason, clock.GetUtcNow()));

    private async Task<PriorAuthorizationRequest?> Decide(Guid id, Action<PriorAuthorizationRequest> decision)
    {
        var request = await db.Requests.FindAsync(id);
        if (request is null) return null;

        decision(request);
        await db.SaveChangesAsync();
        return request;
    }

    /// <summary>Pended requests, the one with the nearest Decision Deadline first.</summary>
    public async Task<IReadOnlyList<PriorAuthorizationRequest>> Pended() =>
        await db.Requests
            .Where(r => r.State == RequestState.Pended)
            .OrderBy(r => r.DecisionDeadline)
            .ToListAsync();
}
