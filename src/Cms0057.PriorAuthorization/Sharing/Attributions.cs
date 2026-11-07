using Cms0057.PriorAuthorization.Data;
using Microsoft.EntityFrameworkCore;

namespace Cms0057.PriorAuthorization.Sharing;

/// <summary>
/// The plan's attribution process for the Provider Access API (42 CFR 422.121(a)(3)) and the Members' Opt-Out
/// (422.121(a)(4)): which Members each in-network provider may see information about. A provider is attributed a
/// Member when it sends a Prior Authorization Request for them, the treatment relationship the Member's own care
/// shows, or when the plan's staff record it. An Opt-Out keeps a Member off every provider's list without
/// forgetting the Attributions, so opting back in restores them.
/// </summary>
public sealed class Attributions(PriorAuthorizationDbContext db, TimeProvider clock)
{
    /// <summary>Records that the Member is a patient of the provider, once; nothing changes if it is already known.</summary>
    /// <exception cref="RequestRejected">There is no such Member.</exception>
    public async Task Record(string providerIdentifier, string memberIdentifier)
    {
        if (await db.Attributions.AnyAsync(a => a.ProviderIdentifier == providerIdentifier && a.MemberIdentifier == memberIdentifier)) return;
        db.Attributions.Add(new Attribution
        {
            ProviderIdentifier = providerIdentifier,
            MemberIdentifier = memberIdentifier,
            AttributedAt = clock.GetUtcNow(),
        });
    }

    /// <summary>The plan's staff attribute a Member to a provider. False when there is no such Member.</summary>
    public async Task<bool> Attribute(string providerIdentifier, string memberIdentifier)
    {
        if (!await db.Members.AnyAsync(m => m.Identifier == memberIdentifier)) return false;
        await Record(providerIdentifier, memberIdentifier);
        await db.SaveChangesAsync();
        return true;
    }

    /// <summary>Whether the provider has an Attribution List at all: at least one Member ever attributed.</summary>
    public Task<bool> HasList(string providerIdentifier) =>
        db.Attributions.AnyAsync(a => a.ProviderIdentifier == providerIdentifier);

    /// <summary>
    /// The provider's Attribution List as it may see it: the Members attributed to it who have not opted out, in
    /// the order attributed.
    /// </summary>
    public async Task<IReadOnlyList<string>> ListOf(string providerIdentifier) =>
        await db.Attributions
            .Where(a => a.ProviderIdentifier == providerIdentifier)
            .Where(a => db.Members.Any(m => m.Identifier == a.MemberIdentifier && !m.ProviderAccessOptedOut))
            .OrderBy(a => a.AttributedAt).ThenBy(a => a.Id)
            .Select(a => a.MemberIdentifier)
            .ToListAsync();

    /// <summary>Whether the Member's information may be disclosed to the provider: attributed, and not opted out.</summary>
    public Task<bool> MayDiscloseTo(string providerIdentifier, string memberIdentifier) =>
        db.Attributions.AnyAsync(a => a.ProviderIdentifier == providerIdentifier && a.MemberIdentifier == memberIdentifier
            && db.Members.Any(m => m.Identifier == memberIdentifier && !m.ProviderAccessOptedOut));

    /// <summary>Records the Member's choice: opted out of Provider Access, or back in. False when there is no such Member.</summary>
    public async Task<bool> SetProviderAccessOptOut(string memberIdentifier, bool optedOut)
    {
        var member = await db.Members.SingleOrDefaultAsync(m => m.Identifier == memberIdentifier);
        if (member is null) return false;
        if (member.ProviderAccessOptedOut != optedOut)
        {
            member.ProviderAccessOptedOut = optedOut;
            member.ProviderAccessChangedAt = clock.GetUtcNow();
            await db.SaveChangesAsync();
        }
        return true;
    }
}
