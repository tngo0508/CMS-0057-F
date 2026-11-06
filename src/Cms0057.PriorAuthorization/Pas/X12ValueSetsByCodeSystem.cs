using Hl7.Fhir.Model;
using Hl7.Fhir.Rest;
using Hl7.Fhir.Specification.Terminology;
using Task = System.Threading.Tasks.Task;

namespace Cms0057.PriorAuthorization.Pas;

/// <summary>
/// Answers for the X12 value sets PAS binds to. X12 licenses them, no FHIR package carries them, and so the
/// terminology service cannot say whether a code is in one; but a code says which code system it is from. Where
/// the value set is not held, a code from the X12 code system of the same code list (the last segment of both
/// URLs: 1069, 1365, 886...) is taken as a member and a code from any other system is not. That is enough to tell
/// PAS's X12 coding slices from the FHIR codings beside them (Coverage.relationship carries both) and to accept
/// the X12 codes a request carries without checking them against a list this service may not hold.
/// </summary>
internal sealed class X12ValueSetsByCodeSystem(ITerminologyService inner) : ITerminologyService
{
    private const string ValueSets = "https://valueset.x12.org/";
    private const string CodeSystems = "https://codesystem.x12.org/";

    public async Task<Parameters> ValueSetValidateCode(Parameters parameters, string? id = null, bool useGet = false)
    {
        var request = new ValidateCodeParameters(parameters);
        var valueSet = request.Url?.Value;
        if (valueSet is null || !valueSet.StartsWith(ValueSets, StringComparison.Ordinal))
            return await inner.ValueSetValidateCode(parameters, id, useGet);
        try
        {
            return await inner.ValueSetValidateCode(parameters, id, useGet);
        }
        catch (FhirOperationException)
        {
            var systems = SystemsOf(request).ToList();
            var member = systems.Any(system => system is not null && SameCodeList(valueSet, system));
            return new Parameters()
                .Add("result", new FhirBoolean(member))
                .Add("message", new FhirString(member
                    ? $"The X12 value set {valueSet} is licensed and not held here; the code is accepted as it comes from the X12 code system of that list"
                    : $"The X12 value set {valueSet} holds X12 codes only, and the code is from {string.Join(", ", systems.Select(s => s ?? "no system"))}"));
        }
    }

    private static IEnumerable<string?> SystemsOf(ValidateCodeParameters request)
    {
        if (request.CodeableConcept is { } concept) return concept.Coding.Select(c => c.System);
        if (request.Coding is { } coding) return [coding.System];
        return [request.System?.Value];
    }

    private static bool SameCodeList(string valueSet, string system) =>
        system.StartsWith(CodeSystems, StringComparison.Ordinal)
        && LastSegment(valueSet) == LastSegment(system);

    private static string LastSegment(string url) => url.TrimEnd('/')[(url.TrimEnd('/').LastIndexOf('/') + 1)..];

    public Task<Parameters> CodeSystemValidateCode(Parameters parameters, string? id = null, bool useGet = false) =>
        inner.CodeSystemValidateCode(parameters, id, useGet);

    public Task<Resource> Expand(Parameters parameters, string? id = null, bool useGet = false) =>
        inner.Expand(parameters, id, useGet);

    public Task<Parameters> Lookup(Parameters parameters, bool useGet = false) => inner.Lookup(parameters, useGet);

    public Task<Parameters> Translate(Parameters parameters, string? id = null, bool useGet = false) =>
        inner.Translate(parameters, id, useGet);

    public Task<Parameters> Subsumes(Parameters parameters, string? id = null, bool useGet = false) =>
        inner.Subsumes(parameters, id, useGet);

    public Task<Resource> Closure(Parameters parameters, bool useGet = false) => inner.Closure(parameters, useGet);
}
