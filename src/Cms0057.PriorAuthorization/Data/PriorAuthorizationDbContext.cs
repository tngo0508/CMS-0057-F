using Cms0057.PriorAuthorization.Notifications;
using Microsoft.EntityFrameworkCore;

namespace Cms0057.PriorAuthorization.Data;

public sealed class PriorAuthorizationDbContext(DbContextOptions<PriorAuthorizationDbContext> options)
    : DbContext(options)
{
    public DbSet<PriorAuthorizationRequest> Requests => Set<PriorAuthorizationRequest>();
    public DbSet<Member> Members => Set<Member>();
    public DbSet<DecisionSubscription> Subscriptions => Set<DecisionSubscription>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<PriorAuthorizationRequest>(request =>
        {
            request.HasKey(r => r.Id);
            request.Property(r => r.Id).ValueGeneratedNever();
            request.Property(r => r.SubmissionIdentifier).HasMaxLength(400);
            request.HasIndex(r => r.SubmissionIdentifier).IsUnique();
            request.Property(r => r.MemberIdentifier).HasMaxLength(100);
            request.Property(r => r.ProviderIdentifier).HasMaxLength(100);
            request.Property(r => r.State).HasConversion<string>().HasMaxLength(20);
            request.Property(r => r.AuthorizationNumber).HasMaxLength(50);
            request.Property(r => r.DenialReason).HasMaxLength(4000);
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
}
