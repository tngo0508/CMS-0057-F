using Microsoft.Extensions.Options;

namespace Cms0057.PriorAuthorization;

/// <summary>What the Behavioral Health Plan requires before a requested service can be delivered.</summary>
public abstract record AuthorizationRule
{
    private AuthorizationRule() { }

    /// <summary>The plan does not prior-authorize this service.</summary>
    public sealed record NotRequired : AuthorizationRule;

    /// <summary>A Reviewer must decide, within the given time of receipt.</summary>
    public sealed record RequiresReview(TimeSpan DecideWithin) : AuthorizationRule;
}

public interface IAuthorizationRules
{
    AuthorizationRule For(RequestedService service);
}

public sealed class PriorAuthorizationOptions
{
    /// <summary>
    /// Billing codes that identify residential substance use disorder treatment. A request matches when it carries
    /// the code and, if any modifiers are listed, at least one of them.
    /// </summary>
    public List<ResidentialService> ResidentialServices { get; set; } = [];
}

public sealed class ResidentialService
{
    public string Code { get; set; } = "";
    public List<string> Modifiers { get; set; } = [];
}

/// <summary>
/// The first slice's only rule: residential substance use disorder treatment must be decided by a Reviewer within
/// 24 hours of receipt (DHCS BHIN 24-001). Everything else is Not Required.
/// </summary>
public sealed class ResidentialTreatmentRules(IOptions<PriorAuthorizationOptions> options) : IAuthorizationRules
{
    private static readonly TimeSpan ResidentialDecisionTime = TimeSpan.FromHours(24);

    public AuthorizationRule For(RequestedService service)
    {
        var residential = options.Value.ResidentialServices.Any(configured =>
            string.Equals(configured.Code, service.Code, StringComparison.OrdinalIgnoreCase)
            && (configured.Modifiers.Count == 0
                || configured.Modifiers.Intersect(service.Modifiers, StringComparer.OrdinalIgnoreCase).Any()));

        return residential
            ? new AuthorizationRule.RequiresReview(ResidentialDecisionTime)
            : new AuthorizationRule.NotRequired();
    }
}
