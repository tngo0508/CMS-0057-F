using Cms0057.PriorAuthorization.Notifications;
using Microsoft.EntityFrameworkCore;

namespace Cms0057.PriorAuthorization.Data;

public sealed class PriorAuthorizationDbContext(DbContextOptions<PriorAuthorizationDbContext> options)
    : DbContext(options)
{
    public DbSet<PriorAuthorizationRequest> Requests => Set<PriorAuthorizationRequest>();
    public DbSet<Member> Members => Set<Member>();
    public DbSet<DecisionSubscription> Subscriptions => Set<DecisionSubscription>();
    public DbSet<Attribution> Attributions => Set<Attribution>();
    public DbSet<MemberMatch> MemberMatches => Set<MemberMatch>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<PriorAuthorizationRequest>(request =>
        {
            request.HasKey(r => r.Id);
            request.Property(r => r.Id).ValueGeneratedNever();
            request.Property(r => r.SubmissionIdentifier).HasMaxLength(400);
            request.HasIndex(r => r.SubmissionIdentifier).IsUnique();
            request.Property(r => r.ClaimIdentifier).HasMaxLength(400);
            request.HasIndex(r => r.ClaimIdentifier);
            request.Property(r => r.MemberIdentifier).HasMaxLength(100);
            request.Property(r => r.ProviderIdentifier).HasMaxLength(100);
            request.Property(r => r.Expedited).HasDefaultValue(false);
            request.Property(r => r.State).HasConversion<string>().HasMaxLength(20);
            request.Property(r => r.AuthorizationNumber).HasMaxLength(50);
            request.Property(r => r.DenialReason).HasMaxLength(4000);
            request.Property(r => r.DenialReasonCode).HasMaxLength(20);
            request.Property(r => r.DenialReasonDisplay).HasMaxLength(200);
            request.Ignore(r => r.ReferenceNumber);
            request.Ignore(r => r.ServiceCodesUnderReview);
            request.OwnsMany(r => r.Items, item =>
            {
                item.ToTable("RequestItems");
                item.WithOwner().HasForeignKey("RequestId");
                item.Property<int>("Id");
                item.HasKey("Id");
                item.Property(i => i.ServiceCode).HasMaxLength(50);
                item.Property(i => i.Modifiers).HasMaxLength(200);
            });
            request.Navigation(r => r.Items).UsePropertyAccessMode(PropertyAccessMode.Field);
        });

        model.Entity<Member>(member =>
        {
            member.HasKey(m => m.Id);
            member.Property(m => m.Identifier).HasMaxLength(100);
            member.HasIndex(m => m.Identifier).IsUnique();
            member.Property(m => m.FamilyName).HasMaxLength(200);
            member.Property(m => m.GivenName).HasMaxLength(200);
            member.Property(m => m.ProviderAccessOptedOut).HasDefaultValue(false);
            member.Property(m => m.PayerToPayerOptedIn).HasDefaultValue(false);
        });

        model.Entity<MemberMatch>(match =>
        {
            match.HasKey(m => m.Id);
            match.Property(m => m.PayerIdentifier).HasMaxLength(100);
            match.Property(m => m.MemberIdentifier).HasMaxLength(100);
            match.HasIndex(m => new { m.PayerIdentifier, m.MemberIdentifier }).IsUnique();
        });

        model.Entity<Attribution>(attribution =>
        {
            attribution.HasKey(a => a.Id);
            attribution.Property(a => a.ProviderIdentifier).HasMaxLength(100);
            attribution.Property(a => a.MemberIdentifier).HasMaxLength(100);
            attribution.HasIndex(a => new { a.ProviderIdentifier, a.MemberIdentifier }).IsUnique();
        });

        model.Entity<DecisionSubscription>(subscription =>
        {
            subscription.HasKey(s => s.Id);
            subscription.Property(s => s.Id).ValueGeneratedNever();
            subscription.Property(s => s.OrganizationIdentifier).HasMaxLength(100);
            subscription.HasIndex(s => s.OrganizationIdentifier);
            subscription.Property(s => s.Endpoint).HasMaxLength(2000);
        });
    }
}

/// <summary>A person covered by the Behavioral Health Plan.</summary>
public sealed class Member
{
    public int Id { get; set; }
    public required string Identifier { get; set; }
    public required string FamilyName { get; set; }
    public required string GivenName { get; set; }

    /// <summary>
    /// The Member's Opt-Out: true when they have asked that their information not be shared with their providers
    /// through the Provider Access API. Shared by default, as the rule has it; changeable at any time.
    /// </summary>
    public bool ProviderAccessOptedOut { get; set; }

    /// <summary>When the Member last changed their Opt-Out, for the plan's records.</summary>
    public DateTimeOffset? ProviderAccessChangedAt { get; set; }

    /// <summary>
    /// The Member's Opt-In: true when they have asked that their information be shared with another payer they
    /// have moved to or also hold coverage with, through the Payer-to-Payer API. Not shared until they do, as the
    /// rule has it; withdrawable at any time.
    /// </summary>
    public bool PayerToPayerOptedIn { get; set; }

    /// <summary>When the Member last changed their Opt-In, for the plan's records.</summary>
    public DateTimeOffset? PayerToPayerChangedAt { get; set; }
}

/// <summary>
/// A Member Match: the plan's record that another payer has identified a Member as someone it covers, with the
/// Member's consent, which is what lets that payer see the Member's information through the Payer-to-Payer API.
/// </summary>
public sealed class MemberMatch
{
    public int Id { get; set; }

    /// <summary>The identifier the other payer is registered under (an NPI, usually).</summary>
    public required string PayerIdentifier { get; set; }
    public required string MemberIdentifier { get; set; }
    public DateTimeOffset MatchedAt { get; set; }

    /// <summary>The end of the period the Member's Consent permits the exchange for.</summary>
    public DateTimeOffset ConsentEnds { get; set; }
}

/// <summary>
/// An Attribution: the plan's record that a Member is a patient of an in-network provider, which is what lets the
/// provider see the Member's information through the Provider Access API. Recorded when the provider sends a
/// Prior Authorization Request for the Member, or by the plan's staff.
/// </summary>
public sealed class Attribution
{
    public int Id { get; set; }
    public required string ProviderIdentifier { get; set; }
    public required string MemberIdentifier { get; set; }
    public DateTimeOffset AttributedAt { get; set; }
}
