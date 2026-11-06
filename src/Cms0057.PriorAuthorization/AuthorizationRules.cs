using Microsoft.Extensions.Options;

namespace Cms0057.PriorAuthorization;

/// <summary>What the Behavioral Health Plan requires before a requested service can be delivered.</summary>
public abstract record AuthorizationRule
{
    private AuthorizationRule() { }

    /// <summary>The plan does not prior-authorize this service.</summary>
    public sealed record NotRequired : AuthorizationRule;

    /// <summary>
    /// A Reviewer must decide, by the Decision Deadline the time frame gives, on the strength of the Documentation
    /// Questionnaires named (by canonical URL; none when the plan has not said what it asks for).
    /// </summary>
    public sealed record RequiresReview(DecisionTimeframe Timeframe, IReadOnlyList<string> Questionnaires) : AuthorizationRule;
}

/// <summary>
/// How long the plan has to decide, counted from receipt: a span of calendar time, optionally tightened to a number
/// of business days (Monday to Friday), and a shorter span when the request is expedited.
/// </summary>
public sealed record DecisionTimeframe(TimeSpan Standard, int? StandardBusinessDays, TimeSpan Expedited)
{
    public DateTimeOffset DeadlineFrom(DateTimeOffset received, bool expedited)
    {
        if (expedited) return received + Expedited;

        var byCalendar = received + Standard;
        if (StandardBusinessDays is not { } days) return byCalendar;
        var byBusinessDays = AddBusinessDays(received, days);
        return byBusinessDays < byCalendar ? byBusinessDays : byCalendar;
    }

    private static DateTimeOffset AddBusinessDays(DateTimeOffset from, int days)
    {
        var at = from;
        while (days > 0)
        {
            at = at.AddDays(1);
            if (at.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday)) days--;
        }
        return at;
    }
}

public interface IAuthorizationRules
{
    AuthorizationRule For(RequestedService service);
}

public sealed class PriorAuthorizationOptions
{
    /// <summary>The services the plan prior-authorizes, grouped by the rule that governs them.</summary>
    public List<AuthorizationRequirement> Requirements { get; set; } = [];

    /// <summary>
    /// The coded Denial Reasons this plan uses, from the X12 886 Review Decision Reason Codes. X12 licenses those
    /// codes, so none ship with the service; the plan enters the ones it has licensed.
    /// </summary>
    public List<DenialReasonCode> DenialReasonCodes { get; set; } = [];

    /// <summary>
    /// Validate request Bundles against the Da Vinci PAS 2.2.1 profiles, after the structural checks. On by
    /// default; off makes the service accept whatever the structural checks accept.
    /// </summary>
    public bool ValidateProfiles { get; set; } = true;

    /// <summary>The FHIR package server the PAS package and its dependencies are fetched from on first start.</summary>
    public string FhirPackageServer { get; set; } = "https://packages.fhir.org";

    /// <summary>
    /// The folder the fetched packages (.tgz files) are kept in, relative to the content root. Null means
    /// "fhir-packages". A copy of the folder is all an offline machine needs.
    /// </summary>
    public string? FhirPackageFolder { get; set; }

    /// <summary>
    /// The folder holding the plan's Documentation Questionnaires, one FHIR Questionnaire per JSON file, relative
    /// to the content root. Null means "questionnaires".
    /// </summary>
    public string? QuestionnaireFolder { get; set; }
}

/// <summary>One rule: which billing codes it covers and how long the plan has to decide.</summary>
public sealed class AuthorizationRequirement
{
    /// <summary>What the rule is and where it comes from, for people reading the configuration.</summary>
    public string Name { get; set; } = "";

    /// <summary>A request matches when it carries the code and, if any modifiers are listed, at least one of them.</summary>
    public List<AuthorizedService> Services { get; set; } = [];

    /// <summary>Calendar time to decide a standard request, e.g. "7.00:00:00".</summary>
    public TimeSpan StandardDecisionTime { get; set; }

    /// <summary>When set, a standard request must also be decided within this many business days.</summary>
    public int? StandardBusinessDays { get; set; }

    /// <summary>Calendar time to decide an expedited request, e.g. "3.00:00:00".</summary>
    public TimeSpan ExpeditedDecisionTime { get; set; }

    /// <summary>
    /// The canonical URL of the Documentation Questionnaire the plan asks a provider to complete with a request
    /// under this rule; one of the questionnaires in the questionnaire folder. Null when the plan has not decided.
    /// </summary>
    public string? Questionnaire { get; set; }

    public bool Covers(RequestedService service) => Services.Any(configured =>
        string.Equals(configured.Code, service.Code, StringComparison.OrdinalIgnoreCase)
        && (configured.Modifiers.Count == 0
            || configured.Modifiers.Intersect(service.Modifiers, StringComparer.OrdinalIgnoreCase).Any()));

    public DecisionTimeframe Timeframe => new(StandardDecisionTime, StandardBusinessDays, ExpeditedDecisionTime);

    /// <summary>The services as the public metrics report lists them: code, or code and modifier.</summary>
    public IEnumerable<string> ServiceNames => Services.SelectMany(s =>
        s.Modifiers.Count == 0 ? [s.Code] : s.Modifiers.Select(m => $"{s.Code} {m}"));
}

public sealed class AuthorizedService
{
    public string Code { get; set; } = "";
    public List<string> Modifiers { get; set; } = [];
}

public sealed class DenialReasonCode
{
    public string Code { get; set; } = "";
    public string Display { get; set; } = "";
}

/// <summary>
/// The plan's rules as configured. A service no requirement covers is Not Required. Where several requirements
/// cover one service, the request takes all of them and the stricter deadline wins at receipt.
/// </summary>
public sealed class ConfiguredAuthorizationRules(IOptions<PriorAuthorizationOptions> options) : IAuthorizationRules
{
    public AuthorizationRule For(RequestedService service)
    {
        var covering = options.Value.Requirements.Where(r => r.Covers(service)).ToList();
        return covering.Count == 0
            ? new AuthorizationRule.NotRequired()
            : new AuthorizationRule.RequiresReview(
                Strictest(covering.Select(r => r.Timeframe)),
                [.. covering.Select(r => r.Questionnaire).OfType<string>().Distinct()]);
    }

    /// <summary>Of several time frames, the one that gives the plan the least time on every count.</summary>
    private static DecisionTimeframe Strictest(IEnumerable<DecisionTimeframe> timeframes)
    {
        var all = timeframes.ToList();
        var businessDays = all.Select(t => t.StandardBusinessDays).OfType<int>().ToList();
        return new DecisionTimeframe(
            all.Min(t => t.Standard),
            businessDays.Count == 0 ? null : businessDays.Min(),
            all.Min(t => t.Expedited));
    }
}
