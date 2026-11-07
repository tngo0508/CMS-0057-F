namespace Cms0057.PriorAuthorization.PatientAccess;

/// <summary>
/// A FHIR date search parameter value with a comparison prefix, as a Patient Access API sends
/// <c>_lastUpdated=ge2026-11-06T19:00:00Z</c> to ask for what changed since it last looked (the rule has it updated
/// within one business day of a status change). The prefixes that make sense for that are supported: ge, gt, le, lt.
/// </summary>
public readonly record struct DateSearch(string Prefix, DateTimeOffset Value)
{
    private static readonly string[] Prefixes = ["ge", "gt", "le", "lt"];

    /// <summary>Reads the parameter; null when the request does not carry it.</summary>
    /// <exception cref="RequestRejected">The value has no supported prefix or is not a date or instant.</exception>
    public static DateSearch? Parse(string? parameter)
    {
        if (string.IsNullOrWhiteSpace(parameter)) return null;
        var prefix = Prefixes.FirstOrDefault(p => parameter.StartsWith(p, StringComparison.Ordinal))
                     ?? throw new RequestRejected($"_lastUpdated takes a prefix of ge, gt, le or lt, as in ge2026-11-06T19:00:00Z; got: {parameter}");
        if (!DateTimeOffset.TryParse(parameter[prefix.Length..], null, System.Globalization.DateTimeStyles.AssumeUniversal, out var value))
            throw new RequestRejected($"_lastUpdated must be a date or instant; got: {parameter}");
        return new DateSearch(prefix, value);
    }

    public bool Matches(DateTimeOffset when) => Prefix switch
    {
        "ge" => when >= Value,
        "gt" => when > Value,
        "le" => when <= Value,
        _ => when < Value,
    };
}
