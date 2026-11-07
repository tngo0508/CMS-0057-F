using Cms0057.PriorAuthorization.PatientAccess;

namespace Cms0057.PriorAuthorization.Sharing;

/// <summary>
/// What of a Member's prior authorization information goes to another payer (42 CFR 422.121(b)(1)(i), as Da Vinci
/// PDex scopes it): requests from the last five years, leaving out denied ones, which the rule excludes, and ones
/// that are no longer active and whose status last changed more than a year ago.
/// </summary>
public static class PayerToPayer
{
    public static IEnumerable<PriorAuthorizationRequest> Shareable(IEnumerable<PriorAuthorizationRequest> requests, DateTimeOffset now)
    {
        var fiveYearsAgo = now.AddYears(-5);
        var aYearAgo = now.AddYears(-1);
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        return requests
            .Where(r => r.State != RequestState.Denied)
            .Where(r => r.ReceivedAt >= fiveYearsAgo)
            .Where(r => IsActive(r, today) || PriorAuthorizationInformation.LastChanged(r) >= aYearAgo);
    }

    /// <summary>Active: awaiting a decision, or approved for a period that has not ended.</summary>
    private static bool IsActive(PriorAuthorizationRequest request, DateOnly today) => request.State switch
    {
        RequestState.Pended => true,
        RequestState.Approved => request.AuthorizationEnds is null || request.AuthorizationEnds >= today,
        _ => false,
    };
}
