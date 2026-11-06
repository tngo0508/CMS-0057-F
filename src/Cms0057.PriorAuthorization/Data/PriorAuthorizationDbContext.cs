using Microsoft.EntityFrameworkCore;

namespace Cms0057.PriorAuthorization.Data;

public sealed class PriorAuthorizationDbContext(DbContextOptions<PriorAuthorizationDbContext> options)
    : DbContext(options)
{
    public DbSet<PriorAuthorizationRequest> Requests => Set<PriorAuthorizationRequest>();
    public DbSet<Member> Members => Set<Member>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<PriorAuthorizationRequest>(request =>
        {
            request.HasKey(r => r.Id);
            request.Property(r => r.Id).ValueGeneratedNever();
            request.Property(r => r.SubmissionIdentifier).HasMaxLength(400);
            request.HasIndex(r => r.SubmissionIdentifier).IsUnique();
            request.Property(r => r.MemberIdentifier).HasMaxLength(100);
            request.Property(r => r.ServiceCode).HasMaxLength(50);
            request.Property(r => r.State).HasConversion<string>().HasMaxLength(20);
            request.Property(r => r.AuthorizationNumber).HasMaxLength(50);
            request.Property(r => r.DenialReason).HasMaxLength(4000);
            request.Ignore(r => r.ReferenceNumber);
        });

        model.Entity<Member>(member =>
        {
            member.HasKey(m => m.Id);
            member.Property(m => m.Identifier).HasMaxLength(100);
            member.HasIndex(m => m.Identifier).IsUnique();
            member.Property(m => m.FamilyName).HasMaxLength(200);
            member.Property(m => m.GivenName).HasMaxLength(200);
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
