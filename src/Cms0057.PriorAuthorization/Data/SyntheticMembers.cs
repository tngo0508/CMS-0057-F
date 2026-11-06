using Microsoft.EntityFrameworkCore;

namespace Cms0057.PriorAuthorization.Data;

/// <summary>Three made-up Members for development. None corresponds to a real person.</summary>
public static class SyntheticMembers
{
    public static async Task Seed(PriorAuthorizationDbContext db)
    {
        if (await db.Members.AnyAsync()) return;

        db.Members.AddRange(
            new Member { Identifier = "90000001A", FamilyName = "EXAMPLE", GivenName = "ALEX" },
            new Member { Identifier = "90000002B", FamilyName = "SAMPLE", GivenName = "JORDAN" },
            new Member { Identifier = "90000003C", FamilyName = "TESTCASE", GivenName = "RILEY" });
        await db.SaveChangesAsync();
    }
}
