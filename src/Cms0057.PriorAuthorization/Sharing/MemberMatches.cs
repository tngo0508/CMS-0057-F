using Cms0057.PriorAuthorization.Data;
using Microsoft.EntityFrameworkCore;

namespace Cms0057.PriorAuthorization.Sharing;

/// <summary>
/// The plan's side of the Payer-to-Payer API's opt-in and matching (42 CFR 422.121(b)(1)): which Members have
/// asked that their information follow them to another payer, and which Members each other payer has identified
/// as someone it covers. A match is made when the other payer's $member-match names one Member here, with the
/// Member's Consent, and the Member has opted in; it lets that payer see the Member's information until the
/// Consent's period ends or the Member withdraws the opt-in. Withdrawing keeps the matches, so opting in again
/// restores them.
/// </summary>
public sealed class MemberMatches(PriorAuthorizationDbContext db, TimeProvider clock)
{
    /// <summary>
    /// Finds the one Member the other payer describes: the identifier it believes this plan knows the person by,
    /// together with the person's name. Null when nobody, or more than one Member, fits.
    /// </summary>
    public async Task<string?> Identify(IReadOnlyCollection<string> identifiers, string? familyName, IReadOnlyCollection<string> givenNames)
    {
        if (identifiers.Count == 0) return null;
        var candidates = await db.Members.Where(m => identifiers.Contains(m.Identifier)).ToListAsync();
        var matched = candidates
            .Where(m => familyName is null || string.Equals(m.FamilyName, familyName, StringComparison.OrdinalIgnoreCase))
            .Where(m => givenNames.Count == 0 || givenNames.Any(g => string.Equals(m.GivenName, g, StringComparison.OrdinalIgnoreCase)))
            .ToList();
        return matched.Count == 1 ? matched[0].Identifier : null;
    }

    /// <summary>The Member, for the Patient resource that $everything about them starts with. Null when there is none.</summary>
    public Task<Member?> Find(string memberIdentifier) =>
        db.Members.SingleOrDefaultAsync(m => m.Identifier == memberIdentifier);

    /// <summary>Whether the Member has opted in to the Payer-to-Payer exchange.</summary>
    public Task<bool> HasOptedIn(string memberIdentifier) =>
        db.Members.AnyAsync(m => m.Identifier == memberIdentifier && m.PayerToPayerOptedIn);

    /// <summary>Records that the payer has matched the Member, or renews the match with the latest Consent.</summary>
    public async Task Record(string payerIdentifier, string memberIdentifier, DateTimeOffset consentEnds)
    {
        var existing = await db.MemberMatches.SingleOrDefaultAsync(m => m.PayerIdentifier == payerIdentifier && m.MemberIdentifier == memberIdentifier);
        if (existing is null)
            db.MemberMatches.Add(new MemberMatch
            {
                PayerIdentifier = payerIdentifier,
                MemberIdentifier = memberIdentifier,
                MatchedAt = clock.GetUtcNow(),
                ConsentEnds = consentEnds,
            });
        else
            existing.ConsentEnds = consentEnds;
        await db.SaveChangesAsync();
    }

    /// <summary>Whether the payer has matched anyone at all, ever.</summary>
    public Task<bool> HasMatches(string payerIdentifier) =>
        db.MemberMatches.AnyAsync(m => m.PayerIdentifier == payerIdentifier);

    /// <summary>When the payer first matched a Member here; null when it never has.</summary>
    public async Task<DateTimeOffset?> FirstMatchOf(string payerIdentifier) =>
        await db.MemberMatches.Where(m => m.PayerIdentifier == payerIdentifier).Select(m => (DateTimeOffset?)m.MatchedAt).MinAsync();

    /// <summary>The Members the payer has matched and may see now: opted in, with Consent still in its period.</summary>
    public async Task<IReadOnlyList<string>> ListOf(string payerIdentifier) =>
        await Shared(payerIdentifier)
            .OrderBy(m => m.MatchedAt).ThenBy(m => m.Id)
            .Select(m => m.MemberIdentifier)
            .ToListAsync();

    /// <summary>Whether the Member's information may be disclosed to the payer: matched, opted in, Consent in its period.</summary>
    public Task<bool> MayDiscloseTo(string payerIdentifier, string memberIdentifier) =>
        Shared(payerIdentifier).AnyAsync(m => m.MemberIdentifier == memberIdentifier);

    private IQueryable<MemberMatch> Shared(string payerIdentifier)
    {
        var now = clock.GetUtcNow();
        return db.MemberMatches
            .Where(m => m.PayerIdentifier == payerIdentifier && m.ConsentEnds >= now)
            .Where(m => db.Members.Any(member => member.Identifier == m.MemberIdentifier && member.PayerToPayerOptedIn));
    }

    /// <summary>Records the Member's choice: opted in to the Payer-to-Payer exchange, or withdrawn. False when there is no such Member.</summary>
    public async Task<bool> SetOptIn(string memberIdentifier, bool optedIn)
    {
        var member = await db.Members.SingleOrDefaultAsync(m => m.Identifier == memberIdentifier);
        if (member is null) return false;
        if (member.PayerToPayerOptedIn != optedIn)
        {
            member.PayerToPayerOptedIn = optedIn;
            member.PayerToPayerChangedAt = clock.GetUtcNow();
            await db.SaveChangesAsync();
        }
        return true;
    }
}
