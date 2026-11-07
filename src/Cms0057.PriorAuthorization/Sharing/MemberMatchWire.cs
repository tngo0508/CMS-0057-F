using Cms0057.PriorAuthorization.Pas;
using Hl7.Fhir.Model;

namespace Cms0057.PriorAuthorization.Sharing;

/// <summary>
/// HRex's $member-match on the wire: the Parameters another payer sends (the person as it knows them, the
/// Coverage it believes this plan gave them, their Consent) and the Parameters this plan answers with (the Member's
/// identifier here). Operation: http://hl7.org/fhir/us/davinci-hrex/OperationDefinition/member-match.
/// </summary>
public static class MemberMatchWire
{
    public const string OperationDefinition = "http://hl7.org/fhir/us/davinci-hrex/OperationDefinition/member-match";

    /// <summary>The namespace of the plan's member identifiers, the same one the Coverage in a provider's request carries.</summary>
    public const string MemberIdentifierSystem = "http://example.org/fhir/sid/member-identifier";
    private const string IdentifierTypeSystem = "http://terminology.hl7.org/CodeSystem/v2-0203";
    private const string HrexTempCodeSystem = "http://hl7.org/fhir/us/davinci-hrex/CodeSystem/hrex-temp";

    /// <summary>
    /// What the other payer told us about the person: every identifier the Coverage and Patient carry that could be
    /// this plan's member identifier, the person's name, and the Consent's period end when a Consent came along.
    /// </summary>
    public sealed record Request(IReadOnlyCollection<string> Identifiers, string? FamilyName, IReadOnlyCollection<string> GivenNames, DateTimeOffset? ConsentEnds);

    /// <exception cref="RequestRejected">The body is not the operation's Parameters, or lacks a required one.</exception>
    public static Request Read(string json)
    {
        var parameters = FhirJson.Read<Parameters>(json);
        var patient = Resource<Patient>(parameters, "MemberPatient")
            ?? throw new RequestRejected("The MemberPatient parameter is required: the person as the requesting payer knows them");
        var coverage = Resource<Coverage>(parameters, "CoverageToMatch")
            ?? throw new RequestRejected("The CoverageToMatch parameter is required: the coverage the requesting payer believes this plan gave the person");
        var consent = Resource<Consent>(parameters, "Consent");

        var identifiers = coverage.Identifier.Select(i => i.Value)
            .Append(coverage.SubscriberId)
            .Concat(patient.Identifier.Select(i => i.Value))
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => v!.Trim())
            .Distinct()
            .ToList();
        var name = patient.Name.FirstOrDefault(n => n.Use is null or HumanName.NameUse.Official or HumanName.NameUse.Usual) ?? patient.Name.FirstOrDefault();
        return new Request(identifiers, name?.Family, [.. name?.Given ?? []], ConsentEnd(consent));
    }

    /// <summary>The Member found, as HRex has the operation answer: the identifier the payer quotes from now on.</summary>
    public static Parameters Found(string memberIdentifier, string memberIdentifierSystem) => new()
    {
        Parameter =
        [
            new Parameters.ParameterComponent
            {
                Name = "MemberIdentifier",
                Value = new Identifier(memberIdentifierSystem, memberIdentifier)
                {
                    Type = new CodeableConcept(HrexTempCodeSystem, "UMB", "Member Number", null),
                },
            },
            new Parameters.ParameterComponent
            {
                Name = "MemberId",
                Value = new ResourceReference("Patient/" + memberIdentifier),
            },
        ],
    };

    /// <summary>The Member as a Patient resource: what the plan knows them by, and their name.</summary>
    public static Patient PatientOf(Data.Member member, string memberIdentifierSystem) => new()
    {
        Id = member.Identifier,
        Identifier =
        [
            new Identifier(memberIdentifierSystem, member.Identifier)
            {
                Type = new CodeableConcept(IdentifierTypeSystem, "MB", "Member Number", null),
            },
        ],
        Active = true,
        Name = [new HumanName { Family = member.FamilyName, Given = [member.GivenName] }],
    };

    /// <summary>Why there is no match, as HRex has it answered: an OperationOutcome with status 422.</summary>
    public static OperationOutcome NoMatch(string reason) => new()
    {
        Issue =
        [
            new OperationOutcome.IssueComponent
            {
                Severity = OperationOutcome.IssueSeverity.Error,
                Code = OperationOutcome.IssueType.NotFound,
                Diagnostics = reason,
            },
        ],
    };

    private static T? Resource<T>(Parameters parameters, string name) where T : Resource =>
        parameters.Parameter.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))?.Resource as T;

    /// <summary>
    /// The end of the Consent's permitted period: HRex's Consent profile requires one, and the exchange ends with it.
    /// Null when no Consent came, or when it does not permit disclosure.
    /// </summary>
    private static DateTimeOffset? ConsentEnd(Consent? consent)
    {
        if (consent is null) return null;
        if (consent.Status is not (Consent.ConsentState.Active or null)) return null;
        var provision = consent.Provision;
        if (provision is null || provision.Type is not (Consent.ConsentProvisionType.Permit or null)) return null;
        if (provision.Period?.EndElement?.ToDateTimeOffset(TimeSpan.Zero) is { } end) return end;
        throw new RequestRejected("The Consent's provision needs a period with an end: how long the Member permits the exchange for");
    }
}
